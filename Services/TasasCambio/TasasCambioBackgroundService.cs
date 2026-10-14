using Microsoft.Extensions.Options;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Worker del job de tasas de cambio.
    ///
    ///   * Con TasasCambio:Habilitado = false (por omisión) registra una línea y espera, sin tocar la base, a que se
    ///     habilite (el interruptor se cambia desde la pantalla de configuración, script 024, sin reiniciar).
    ///   * Al arrancar, cada 10 minutos y cada vez que llega el disparador externo, pregunta a
    ///     <see cref="TasasCambioPlanificador"/> si toca ejecutar (primer intento del día, reintento vencido, barrido
    ///     matutino pendiente o hueco de puesta al día). Si no toca, no descarga nada ni escribe en la bitácora.
    ///   * Lo forzado (botón "Ejecutar ahora", carga de un rango) llega por <see cref="ColaEjecucionTasasCambio"/> y se
    ///     ejecuta siempre.
    ///
    /// En IIS (Somee) el proceso se detiene cuando no hay visitas y se recicla seguido: por eso el disparador externo
    /// solo despierta la aplicación y hace evaluar la programación ya, y nada depende de la memoria del proceso. El
    /// bloqueo del job evita que dos procesos ejecuten a la vez.
    /// Patrón de referencia: <see cref="Eventos.OutboxDispatcherBackgroundService"/>.
    /// </summary>
    public sealed class TasasCambioBackgroundService : BackgroundService
    {
        public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan EsperaDeshabilitadoPorOmision = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopes;
        private readonly TasasCambioOptions _opt;
        private readonly ColaEjecucionTasasCambio _cola;
        private readonly TimeProvider _reloj;
        private readonly ILogger<TasasCambioBackgroundService> _log;
        private readonly ITasasCambioConfiguracionService? _configuracion;

        /// <summary>Cada cuánto se revisa el interruptor mientras el job está apagado (cambia solo en memoria, sin costo).</summary>
        public TimeSpan EsperaDeshabilitado { get; init; } = EsperaDeshabilitadoPorOmision;

        public TasasCambioBackgroundService(
            IServiceScopeFactory scopes,
            IOptions<TasasCambioOptions> opciones,
            ColaEjecucionTasasCambio cola,
            TimeProvider reloj,
            ILogger<TasasCambioBackgroundService> log,
            ITasasCambioConfiguracionService? configuracion = null)
        {
            _configuracion = configuracion;
            _scopes = scopes;
            _opt = opciones.Value;
            _cola = cola;
            _reloj = reloj;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Lo guardado desde la pantalla (script 024) rige sobre appsettings; sin la tabla se queda appsettings.
            if (_configuracion != null) await _configuracion.AplicarAsync(stoppingToken);

            if (!await EsperarHabilitadoAsync(stoppingToken)) return;

            _log.LogInformation("Tasas de cambio: worker iniciado.");

            // Pequeña espera para dejar que la app termine de inicializar
            try { await Task.Delay(EsperaInicial, _reloj, stoppingToken); }
            catch (OperationCanceledException) { return; }

            await ProtegidoAsync(() => ArrancarAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Si lo apagan desde la pantalla, el worker queda en espera (sin tocar la base) hasta que lo vuelvan a encender.
                if (!_opt.Habilitado && !await EsperarHabilitadoAsync(stoppingToken)) break;

                SolicitudEjecucionTasas? solicitud;
                try { solicitud = await EsperarAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }

                if (solicitud != null)
                    await ProtegidoAsync(() => AtenderAsync(solicitud, stoppingToken), stoppingToken);
                else
                    await ProtegidoAsync(() => EvaluarProgramacionAsync(null, stoppingToken), stoppingToken);
            }

            _log.LogInformation("Tasas de cambio: worker detenido.");
        }

        /// <summary>
        /// Espera, sin tocar la base, a que el interruptor esté encendido (por la pantalla de configuración). Revisa
        /// cada <see cref="EsperaDeshabilitado"/> porque el cambio es solo en memoria. Falso si la aplicación se detiene.
        /// </summary>
        private async Task<bool> EsperarHabilitadoAsync(CancellationToken ct)
        {
            if (_opt.Habilitado) return true;

            _log.LogInformation("Tasas de cambio: job deshabilitado (TasasCambio:Habilitado = false); el worker espera a que se habilite.");
            while (!_opt.Habilitado)
            {
                try { await Task.Delay(EsperaDeshabilitado, _reloj, ct); }
                catch (OperationCanceledException) { return false; }
            }

            _log.LogInformation("Tasas de cambio: job habilitado; el worker continúa.");
            return !ct.IsCancellationRequested;
        }

        /// <summary>Al arrancar el proceso: la misma evaluación que la vuelta de 10 minutos (pone al día solo si hay huecos).</summary>
        public Task<DecisionProgramada?> ArrancarAsync(CancellationToken ct = default) => EvaluarProgramacionAsync(null, ct);

        /// <summary>
        /// Pregunta al planificador y, si toca, ejecuta. Devuelve la decisión o nulo si no tocaba nada (en ese caso no
        /// hubo descarga ni fila en la bitácora).
        /// </summary>
        /// <param name="disparador">Con qué disparador queda la ejecución (EXTERNO); nulo = el que decida el planificador.</param>
        public async Task<DecisionProgramada?> EvaluarProgramacionAsync(string? disparador = null, CancellationToken ct = default)
        {
            await using var scope = _scopes.CreateAsyncScope();
            FijarPlataforma(scope);
            var planificador = scope.ServiceProvider.GetRequiredService<TasasCambioPlanificador>();
            var decision = await planificador.DecidirAsync(ct);
            if (decision == null)
            {
                if (disparador != null)
                    _log.LogInformation("Tasas de cambio: llamada {Disparador} sin nada que hacer.", disparador);
                return null;
            }

            _log.LogInformation("Tasas de cambio: {Motivo}", decision.Motivo);
            var sync = scope.ServiceProvider.GetRequiredService<ITasaCambioSyncService>();
            await sync.EjecutarAsync(disparador ?? decision.Disparador, TasaCambioSyncService.UsuarioJob, ct);
            return decision;
        }

        /// <summary>Atiende una solicitud de la cola: la forzada se ejecuta siempre; la del disparador externo solo evalúa.</summary>
        public async Task AtenderAsync(SolicitudEjecucionTasas solicitud, CancellationToken ct = default)
        {
            if (!solicitud.Forzar)
            {
                await EvaluarProgramacionAsync(solicitud.Disparador, ct);
                return;
            }

            await using var scope = _scopes.CreateAsyncScope();
            FijarPlataforma(scope);
            var sync = scope.ServiceProvider.GetRequiredService<ITasaCambioSyncService>();
            var resultado = solicitud is { Desde: { } desde, Hasta: { } hasta }
                ? await sync.EjecutarAsync(solicitud.Disparador, solicitud.EjecutadoPor, desde, hasta, ct)
                : await sync.EjecutarAsync(solicitud.Disparador, solicitud.EjecutadoPor, ct);
            if (!resultado.Ejecutada)
                _log.LogInformation("Tasas de cambio: la solicitud {Disparador} no se ejecutó: {Mensaje}", solicitud.Disparador, resultado.Mensaje);
        }

        /// <summary>Atiende todo lo que haya en la cola en este momento (el ciclo lo hace de a una; público para las pruebas).</summary>
        public async Task<int> ProcesarColaAsync(CancellationToken ct = default)
        {
            var atendidas = 0;
            while (_cola.Lector.TryRead(out var solicitud))
            {
                await AtenderAsync(solicitud, ct);
                atendidas++;
            }
            return atendidas;
        }

        /// <summary>Espera hasta <see cref="Intervalo"/> o hasta que llegue una solicitud; devuelve la solicitud o nulo.</summary>
        private async Task<SolicitudEjecucionTasas?> EsperarAsync(CancellationToken ct)
        {
            if (_cola.Lector.TryRead(out var pendiente)) return pendiente;

            using var plazo = new CancellationTokenSource(Intervalo, _reloj);
            using var combinado = CancellationTokenSource.CreateLinkedTokenSource(ct, plazo.Token);
            try
            {
                await _cola.Lector.WaitToReadAsync(combinado.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;   // se cumplió el intervalo
            }
            return _cola.Lector.TryRead(out var solicitud) ? solicitud : null;
        }

        private async Task ProtegidoAsync(Func<Task> accion, CancellationToken ct)
        {
            try
            {
                await accion();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // la aplicación se está deteniendo
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Tasas de cambio: error inesperado en el worker.");
            }
        }

        /// <summary>
        /// El job escribe las filas globales de la tasa oficial (<c>id_tenant</c> nulo): la política RLS solo lo permite en ámbito de
        /// plataforma (ADR-023). El resto de sus tablas son de la plataforma y no llevan política.
        /// </summary>
        private static void FijarPlataforma(IServiceScope scope)
            => scope.ServiceProvider.GetService<eGestion360Web.Services.Tenancy.TenantContext>()?.FijarPlataforma();
    }
}
