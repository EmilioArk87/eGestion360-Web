using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;

namespace eGestion360Web.Services.Eventos
{
    /// <summary>
    /// Worker que procesa el outbox de eventos.
    ///
    /// Flujo por iteración:
    ///   1. Reclama un lote de eventos pendientes con UPDATE atómico (race-safe entre workers).
    ///   2. Para cada evento, invoca todos los handlers cuyo CanHandle() devuelva true.
    ///   3. Si todos los handlers tienen éxito → status='processed'.
    ///   4. Si alguno falla → status='failed', programa backoff exponencial.
    ///   5. Si supera max_intentos → status='dead' (requiere reset manual desde /Admin/Outbox).
    ///
    /// Múltiples instancias del proceso pueden correr en paralelo: el UPDATE con
    /// locked_until evita que dos workers tomen el mismo evento.
    /// </summary>
    public sealed class OutboxDispatcherBackgroundService : BackgroundService
    {
        private const int BatchSize = 25;
        private const int LockTimeoutSeconds = 60;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        // Tabla de backoff por número de intento: intento N → espera N
        private static readonly TimeSpan[] BackoffTable =
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12),
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxDispatcherBackgroundService> _log;
        private readonly string _workerId;

        public OutboxDispatcherBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<OutboxDispatcherBackgroundService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
            var prefix = Environment.MachineName;
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            _workerId = $"{prefix}-{suffix}";
            if (_workerId.Length > 100) _workerId = _workerId.Substring(0, 100);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("OutboxDispatcher iniciado. worker_id={WorkerId}", _workerId);

            // Pequeño delay para dejar que la app termine de inicializar
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                int processed = 0;
                try
                {
                    processed = await ProcesarLoteAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error inesperado en OutboxDispatcher");
                }

                if (processed == 0)
                {
                    try { await Task.Delay(PollInterval, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _log.LogInformation("OutboxDispatcher detenido. worker_id={WorkerId}", _workerId);
        }

        private async Task<int> ProcesarLoteAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler>().ToList();

            var now = DateTime.UtcNow;
            var lockUntil = now.AddSeconds(LockTimeoutSeconds);

            // Paso 1 — Reclamar lote con UPDATE atómico (SQL Server).
            // El OUTPUT nos da los ids ya marcados como 'processing' para este worker.
            // Solo agarra:
            //   - status pending o failed
            //   - no bloqueado (locked_until expirado o nulo)
            //   - sin backoff pendiente
            //   - bajo el límite de intentos
            var sql = @"
UPDATE TOP (@BatchSize) domain_events
SET status        = 'processing',
    locked_until  = @LockUntil,
    worker_id     = @WorkerId,
    intentos      = intentos + 1
OUTPUT INSERTED.id_evento
WHERE status IN ('pending', 'failed')
  AND (locked_until      IS NULL OR locked_until      < @Now)
  AND (proximo_intento_en IS NULL OR proximo_intento_en <= @Now)
  AND intentos < max_intentos;";

            List<long> claimedIds;
            try
            {
                claimedIds = await db.Database
                    .SqlQueryRaw<long>(
                        sql,
                        new Microsoft.Data.SqlClient.SqlParameter("@BatchSize", BatchSize),
                        new Microsoft.Data.SqlClient.SqlParameter("@LockUntil", lockUntil),
                        new Microsoft.Data.SqlClient.SqlParameter("@WorkerId", _workerId),
                        new Microsoft.Data.SqlClient.SqlParameter("@Now", now))
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falló reclamo de lote desde domain_events");
                return 0;
            }

            if (claimedIds.Count == 0) return 0;

            // Paso 2 — Cargar los eventos reclamados (entidades trackeadas para actualizar luego)
            var eventos = await db.Set<DomainEvent>()
                .Where(e => claimedIds.Contains(e.IdEvento))
                .ToListAsync(ct);

            int ok = 0, fallidos = 0;
            foreach (var evt in eventos)
            {
                if (ct.IsCancellationRequested) break;

                var dispatch = new DomainEventDispatch(
                    evt.IdEvento, evt.IdEmpresa, evt.EventType, evt.AggregateType,
                    evt.AggregateId, evt.EventVersion, evt.Payload, evt.OccurredAt);

                var matching = handlers.Where(h => h.CanHandle(evt.EventType)).ToList();

                bool todosOk = true;
                string? primerError = null;

                foreach (var handler in matching)
                {
                    try
                    {
                        await handler.HandleAsync(dispatch, ct);
                    }
                    catch (Exception ex)
                    {
                        todosOk = false;
                        primerError ??= $"[{handler.Name}] {ex.GetType().Name}: {ex.Message}";
                        _log.LogError(ex, "Handler {Handler} falló procesando evento {IdEvento} ({EventType})",
                            handler.Name, evt.IdEvento, evt.EventType);
                    }
                }

                var ahora = DateTime.UtcNow;

                if (todosOk)
                {
                    evt.Status         = DomainEventStatus.Processed;
                    evt.ProcessedAt    = ahora;
                    evt.LockedUntil    = null;
                    evt.WorkerId       = null;
                    evt.UltimoError    = null;
                    evt.ProximoIntentoEn = null;
                    ok++;
                }
                else
                {
                    fallidos++;
                    if (evt.Intentos >= evt.MaxIntentos)
                    {
                        evt.Status         = DomainEventStatus.Dead;
                        evt.LockedUntil    = null;
                        evt.WorkerId       = null;
                        evt.ProximoIntentoEn = null;
                        evt.UltimoError    = $"DEAD tras {evt.Intentos} intentos. Último error: {primerError}";
                        _log.LogWarning("Evento {IdEvento} marcado DEAD ({EventType})", evt.IdEvento, evt.EventType);
                    }
                    else
                    {
                        var idx = Math.Min(evt.Intentos - 1, BackoffTable.Length - 1);
                        idx = Math.Max(idx, 0);
                        var espera = BackoffTable[idx];
                        evt.Status         = DomainEventStatus.Failed;
                        evt.LockedUntil    = null;
                        evt.WorkerId       = null;
                        evt.ProximoIntentoEn = ahora.Add(espera);
                        evt.UltimoError    = primerError;
                    }
                }
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falló persistencia de estado de eventos procesados");
            }

            if (ok > 0 || fallidos > 0)
                _log.LogInformation("Outbox lote: {Ok} ok, {Fail} fail (worker={Worker})", ok, fallidos, _workerId);

            return eventos.Count;
        }
    }
}
