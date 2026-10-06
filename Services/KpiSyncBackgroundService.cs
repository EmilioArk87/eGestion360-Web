namespace eGestion360Web.Services;

/// <summary>
/// Servicio en segundo plano que ejecuta los jobs de sincronización de datos externos:
///
///   - SEN (Secretaría de Energía de Honduras): todos los domingos a las 20:00 hora Honduras.
///     Detecta nuevos boletines de precios de combustibles y los registra en sen_boletines
///     para ingreso manual desde la UI.
///
/// El servicio despierta cada 10 minutos y evalúa si algún job debe correr.
/// Para evitar duplicar ejecuciones usa un "último run" en memoria por día/semana.
///
/// Las tasas de cambio del BCH ya no van aquí: las obtiene TasasCambio.TasasCambioBackgroundService.
/// </summary>
public class KpiSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KpiSyncBackgroundService> _logger;
    private readonly IConfiguration _config;

    // Última ejecución exitosa (en memoria; se reinicia si el proceso se reinicia)
    private DateOnly _senLastRun = DateOnly.MinValue;

    private static readonly TimeZoneInfo HondurasZone = GetHondurasZone();

    public KpiSyncBackgroundService(
        IServiceScopeFactory scopes,
        ILogger<KpiSyncBackgroundService> logger,
        IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KpiSyncBackgroundService iniciado.");

        // Espera inicial de 1 minuto para que la app termine de arrancar
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluarJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KpiSync: error inesperado en el loop principal.");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }

        _logger.LogInformation("KpiSyncBackgroundService detenido.");
    }

    private async Task EvaluarJobsAsync(CancellationToken ct)
    {
        var ahoraHn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HondurasZone);
        var hoyHn   = DateOnly.FromDateTime(ahoraHn);

        // -----------------------------------------------------------------
        // Job SEN: domingos a las 20:00 Honduras
        // -----------------------------------------------------------------
        if (ahoraHn.DayOfWeek == DayOfWeek.Sunday
            && ahoraHn.Hour >= 20
            && _senLastRun < hoyHn)
        {
            _logger.LogInformation("KpiSync: ejecutando job SEN ({Hora})", ahoraHn.ToString("HH:mm"));
            await RunSenAsync(ct);
            _senLastRun = hoyHn;
        }
    }

    // -------------------------------------------------------------------------
    // Delegación a los servicios de dominio
    // -------------------------------------------------------------------------

    private async Task RunSenAsync(CancellationToken ct)
    {
        var empresaIds = GetEmpresaIds();
        await using var scope = _scopes.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<SenCombustibleService>();

        foreach (var id in empresaIds)
        {
            try
            {
                int nuevos = await svc.SyncAsync(id, ct);
                _logger.LogInformation("SEN empresa {Id}: {Nuevos} boletín(es) nuevo(s).", id, nuevos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SEN empresa {Id}: error.", id);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Utilidades
    // -------------------------------------------------------------------------

    private List<int> GetEmpresaIds()
    {
        var raw = _config["KpiSync:EmpresaIds"] ?? "1";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                  .Where(n => n > 0)
                  .ToList();
    }

    // Windows "Central America Standard Time", Linux/macOS "America/Tegucigalpa" o UTC-6 fijo: ver ZonaHorariaHonduras.
    private static TimeZoneInfo GetHondurasZone() => ZonaHorariaHonduras.Obtener();
}
