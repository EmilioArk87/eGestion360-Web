using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Services;

/// <summary>
/// Detecta nuevos boletines de precios de combustibles publicados por el SEN
/// (Secretaría de Energía de Honduras) a través de la API de WordPress.
///
/// El SEN publica los precios cada lunes como imágenes (WhatsApp screenshots).
/// Este servicio NO puede extraer los valores numéricos automáticamente.
/// Guarda el URL de la imagen en sen_boletines para que el admin los ingrese
/// manualmente desde la interfaz de administración.
/// </summary>
public class SenCombustibleService
{
    private readonly IHttpClientFactory _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SenCombustibleService> _logger;
    private readonly IConfiguration _config;

    private const string WpMediaApiUrl =
        "https://sen.hn/wp-json/wp/v2/media?per_page=10&orderby=date&order=desc&media_type=image";

    public SenCombustibleService(
        IHttpClientFactory http,
        IServiceScopeFactory scopes,
        ILogger<SenCombustibleService> logger,
        IConfiguration config)
    {
        _http = http;
        _scopes = scopes;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Consulta la API del SEN y registra boletines nuevos (aún no guardados) en sen_boletines.
    /// Devuelve el número de boletines nuevos detectados.
    /// </summary>
    public async Task<int> SyncAsync(int idEmpresa, CancellationToken ct = default)
    {
        _logger.LogInformation("SEN sync iniciado para empresa {IdEmpresa}.", idEmpresa);

        var apiUrl = _config["KpiSync:SenApiUrl"] ?? WpMediaApiUrl;
        var items = await FetchMediaItemsAsync(apiUrl, ct);

        if (items.Count == 0)
        {
            _logger.LogWarning("SEN: la API no devolvió elementos de media.");
            return 0;
        }

        // Filtra sólo imágenes publicadas en lunes (día de publicación semanal del SEN)
        var mondays = items
            .Where(i => i.Date.DayOfWeek == DayOfWeek.Monday)
            .ToList();

        if (mondays.Count == 0)
        {
            // Si no hay lunes exacto (publicaron en otro día), toma los últimos 3 items
            mondays = items.Take(3).ToList();
        }

        int nuevos = 0;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        foreach (var item in mondays)
        {
            // La fecha de vigencia es el lunes de esa semana
            var fechaVigencia = GetMonday(item.Date);

            // ¿Ya existe este boletín?
            var existsCmd = conn.CreateCommand();
            existsCmd.CommandText = """
                SELECT COUNT(1) FROM dbo.sen_boletines
                WHERE id_empresa = @id AND fecha_vigencia = @fv
                """;
            AddParam(existsCmd, "@id", idEmpresa);
            AddParam(existsCmd, "@fv", fechaVigencia);
            var exists = (int)await existsCmd.ExecuteScalarAsync(ct)! > 0;

            if (exists) continue;

            // Insertar boletín pendiente
            var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO dbo.sen_boletines
                    (id_empresa, fecha_vigencia, url_imagen, fecha_publicacion, wp_media_id, creado_por)
                VALUES
                    (@id_empresa, @fecha_vigencia, @url_imagen, @fecha_publicacion, @wp_media_id, 'KpiSync')
                """;
            AddParam(insertCmd, "@id_empresa", idEmpresa);
            AddParam(insertCmd, "@fecha_vigencia", fechaVigencia);
            AddParam(insertCmd, "@url_imagen", item.SourceUrl);
            AddParam(insertCmd, "@fecha_publicacion", item.Date);
            AddParam(insertCmd, "@wp_media_id", item.Id);
            await insertCmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "SEN: nuevo boletín detectado. Vigencia: {Fecha}. URL: {Url}",
                fechaVigencia, item.SourceUrl);
            nuevos++;
        }

        if (nuevos > 0)
        {
            _logger.LogInformation("SEN sync: {Count} boletín(es) nuevo(s) pendiente(s) de ingreso manual.", nuevos);
        }
        else
        {
            _logger.LogInformation("SEN sync: sin boletines nuevos.");
        }

        return nuevos;
    }

    // -------------------------------------------------------------------------
    // Llamada a la API de WordPress del SEN
    // -------------------------------------------------------------------------

    private async Task<List<WpMediaItem>> FetchMediaItemsAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("SEN");
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var items = await JsonSerializer.DeserializeAsync<List<WpMediaItem>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            return items ?? new List<WpMediaItem>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SEN: error al consultar la API de WordPress.");
            return new List<WpMediaItem>();
        }
    }

    // -------------------------------------------------------------------------
    // Utilidades
    // -------------------------------------------------------------------------

    private static DateOnly GetMonday(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        int diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    // -------------------------------------------------------------------------
    // DTOs para deserializar la API de WordPress
    // -------------------------------------------------------------------------

    private sealed class WpMediaItem
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Slug { get; set; } = string.Empty;

        // WordPress serializa source_url con guión bajo
        [System.Text.Json.Serialization.JsonPropertyName("source_url")]
        public string SourceUrl { get; set; } = string.Empty;
    }
}
