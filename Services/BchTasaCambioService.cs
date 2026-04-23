using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Services;

/// <summary>
/// Descarga el Excel "Precio Promedio Diario del Dólar" del Banco Central de Honduras
/// y persiste las tasas de cambio HNL→USD en la tabla tasas_cambio.
/// </summary>
public class BchTasaCambioService
{
    private readonly IHttpClientFactory _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BchTasaCambioService> _logger;
    private readonly IConfiguration _config;

    public BchTasaCambioService(
        IHttpClientFactory http,
        IServiceScopeFactory scopes,
        ILogger<BchTasaCambioService> logger,
        IConfiguration config)
    {
        _http = http;
        _scopes = scopes;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Sincroniza las tasas de los últimos <paramref name="diasAtras"/> días.
    /// Devuelve la cantidad de filas insertadas/actualizadas.
    /// </summary>
    public async Task<int> SyncAsync(int idEmpresa, int diasAtras = 7, CancellationToken ct = default)
    {
        var url = _config["KpiSync:BchUrl"]
                  ?? "https://www.bch.hn/estadisticos/GIE/LIBTipo%20de%20cambio/Precio%20Promedio%20Diario%20del%20D%C3%B3lar.xlsx";

        _logger.LogInformation("BCH sync iniciado para empresa {IdEmpresa}. URL: {Url}", idEmpresa, url);

        var rates = await DownloadAndParseAsync(url, diasAtras, ct);
        if (rates.Count == 0)
        {
            _logger.LogWarning("BCH: no se encontraron tasas en el Excel.");
            return 0;
        }

        int upserted = await UpsertAsync(idEmpresa, rates, ct);
        _logger.LogInformation("BCH sync completado. Filas upserted: {Count}", upserted);
        return upserted;
    }

    // -------------------------------------------------------------------------
    // Descarga y parseo del Excel BCH
    // -------------------------------------------------------------------------

    private async Task<List<(DateOnly Fecha, decimal Tasa)>> DownloadAndParseAsync(
        string url, int diasAtras, CancellationToken ct)
    {
        var client = _http.CreateClient("BCH");
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var wb = new XLWorkbook(stream);

        return ParseWorkbook(wb, diasAtras);
    }

    private List<(DateOnly, decimal)> ParseWorkbook(XLWorkbook wb, int diasAtras)
    {
        var result = new List<(DateOnly, decimal)>();
        var ws = wb.Worksheets.First();
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-diasAtras));

        int? dateCol = null;
        int? rateCol = null;

        foreach (var row in ws.RowsUsed())
        {
            // Detectar la fila de encabezados buscando "Fecha" en las primeras 20 filas
            if (dateCol is null && row.RowNumber() <= 20)
            {
                foreach (var cell in row.CellsUsed())
                {
                    var val = cell.Value.ToString().Trim().ToUpperInvariant();
                    if (val.Contains("FECHA")) dateCol = cell.Address.ColumnNumber;
                    if (val.Contains("PRECIO") || val.Contains("TASA") || val.Contains("TIPO"))
                        rateCol = cell.Address.ColumnNumber;
                }
                continue;
            }

            if (dateCol is null) continue;

            var dateCell = row.Cell(dateCol.Value);
            var rateColNum = rateCol ?? dateCol.Value + 1;
            var rateCell = row.Cell(rateColNum);

            if (!dateCell.Value.IsDateTime && !dateCell.Value.IsText) continue;

            DateTime fecha;
            if (dateCell.Value.IsDateTime)
            {
                fecha = dateCell.GetDateTime();
            }
            else if (!DateTime.TryParse(dateCell.Value.ToString(), out fecha))
            {
                continue;
            }

            var fechaOnly = DateOnly.FromDateTime(fecha);
            if (fechaOnly < cutoff) continue;

            if (!decimal.TryParse(
                    rateCell.Value.ToString().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal tasa) || tasa <= 0)
                continue;

            result.Add((fechaOnly, tasa));
        }

        if (dateCol is null)
        {
            // Fallback: sin encabezado detectado, asumir col 1 = fecha, col 2 = tasa
            _logger.LogWarning("BCH: no se detectó encabezado 'Fecha'. Usando columna 1 y 2 como fallback.");
            return ParseFallback(wb.Worksheets.First(), cutoff);
        }

        _logger.LogInformation("BCH: {Count} tasas parseadas desde el Excel.", result.Count);
        return result;
    }

    private List<(DateOnly, decimal)> ParseFallback(IXLWorksheet ws, DateOnly cutoff)
    {
        var result = new List<(DateOnly, decimal)>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var c1 = row.Cell(1);
            var c2 = row.Cell(2);
            if (!c1.Value.IsDateTime) continue;

            var fecha = DateOnly.FromDateTime(c1.GetDateTime());
            if (fecha < cutoff) continue;

            if (decimal.TryParse(
                    c2.Value.ToString().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal tasa) && tasa > 0)
            {
                result.Add((fecha, tasa));
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Persistencia UPSERT en tasas_cambio
    // -------------------------------------------------------------------------

    private async Task<int> UpsertAsync(
        int idEmpresa,
        List<(DateOnly Fecha, decimal Tasa)> rates,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        int count = 0;
        foreach (var (fecha, tasa) in rates)
        {
            var sql = """
                MERGE dbo.tasas_cambio WITH (HOLDLOCK) AS target
                USING (VALUES (@id_empresa, @fecha, @moneda_origen, @moneda_destino, @tasa, @fuente, @creado_por))
                    AS source (id_empresa, fecha, moneda_origen, moneda_destino, tasa, fuente, creado_por)
                ON  target.id_empresa     = source.id_empresa
                AND target.fecha          = source.fecha
                AND target.moneda_origen  = source.moneda_origen
                AND target.moneda_destino = source.moneda_destino
                AND target.eliminado      = 0
                WHEN MATCHED THEN
                    UPDATE SET tasa = source.tasa,
                               fuente = source.fuente,
                               modificado_por = source.creado_por,
                               fecha_modificacion = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (id_empresa, fecha, moneda_origen, moneda_destino, tasa, fuente, creado_por)
                    VALUES (source.id_empresa, source.fecha, source.moneda_origen,
                            source.moneda_destino, source.tasa, source.fuente, source.creado_por);
                """;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            AddParam(cmd, "@id_empresa", idEmpresa);
            AddParam(cmd, "@fecha", fecha.ToDateTime(TimeOnly.MinValue));
            AddParam(cmd, "@moneda_origen", "HNL");
            AddParam(cmd, "@moneda_destino", "USD");
            AddParam(cmd, "@tasa", tasa);
            AddParam(cmd, "@fuente", "BCH");
            AddParam(cmd, "@creado_por", "KpiSync");
            count += await cmd.ExecuteNonQueryAsync(ct);
        }

        return count;
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
