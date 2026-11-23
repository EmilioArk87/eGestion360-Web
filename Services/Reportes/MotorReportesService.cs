using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models.Reportes;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Reportes
{
    /// <summary>La versión activa que corresponde al tenant de la petición, ya compilada.</summary>
    /// <param name="EsPersonalizada">true si es la del tenant; false si es la global de la plataforma.</param>
    public sealed record VersionDeReporte(int IdReporte, string Codigo, string FuenteDatos, string TipoSalida, bool EsPersonalizada,
        int IdVersion, int NumeroVersion, string HashSha256, PlantillaCompilada Compilada);

    /// <summary>
    /// Motor de reportes versionados en BD (ADR-031, Fase 1). Resuelve la versión activa (tenant → global, RP4), la genera con Scriban y arma
    /// la fila de ejecución que el llamador guarda junto con su propio registro (RP8, RP9).
    /// </summary>
    public interface IMotorReportes
    {
        /// <summary>
        /// La versión activa del reporte para el tenant de la petición: la propia si existe y está activa, si no la global. Nulo si no hay
        /// ninguna o si las tablas del 047 todavía no existen: el llamador sigue con su formato de siempre.
        /// </summary>
        Task<VersionDeReporte?> ResolverAsync(string codigo, CancellationToken ct = default);

        /// <summary>Genera el HTML de la versión con las variables de la fuente de datos.</summary>
        string Renderizar(VersionDeReporte version, IReadOnlyDictionary<string, object?> modelo);

        /// <summary>
        /// La fila de <c>dbo.rep_ejecuciones</c> de una generación, sin guardar: el llamador la agrega a su contexto para que quede en la misma
        /// transacción que su propio registro (por ejemplo, <c>psi.impresiones</c>).
        /// </summary>
        ReporteEjecucion NuevaEjecucion(VersionDeReporte version, int idEmpresa, string? entidadOrigen, long? idRegistroOrigen,
            int? idCuenta, string usuario, string? parametrosJson = null);
    }

    public sealed class MotorReportesService : IMotorReportes
    {
        /// <summary>Una versión publicada no cambia nunca (RP5): su plantilla compilada se guarda por id_version.</summary>
        private static readonly TimeSpan VidaDeLaCompilada = TimeSpan.FromHours(6);

        private readonly ApplicationDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<MotorReportesService> _log;

        public MotorReportesService(ApplicationDbContext db, ITenantContext tenant, IMemoryCache cache, TimeProvider tiempo,
            ILogger<MotorReportesService> log)
        {
            _db = db;
            _tenant = tenant;
            _cache = cache;
            _tiempo = tiempo;
            _log = log;
        }

        public async Task<VersionDeReporte?> ResolverAsync(string codigo, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
            var idTenant = _tenant.IdTenant;

            List<Candidato> candidatos;
            try
            {
                // El filtro de tenant ya limita a las globales y las propias; se repite aquí para que en ámbito de plataforma (sin filtro)
                // nunca se use la versión de otro tenant.
                candidatos = await _db.Reportes.AsNoTracking()
                    .Where(r => r.Codigo == codigo && r.Activo && r.IdVersionActiva != null && (r.IdTenant == null || r.IdTenant == idTenant))
                    .Select(r => new Candidato(r.IdReporte, r.IdTenant, r.Codigo, r.FuenteDatos, r.TipoSalida, r.IdVersionActiva!.Value))
                    .ToListAsync(ct);
            }
            catch (DbException ex)
            {
                // Sin el script 047 la tabla no existe: el llamador sigue con su formato de siempre.
                _log.LogWarning(ex, "No se pudo leer el reporte {Codigo} del motor de reportes; se usa el formato en código.", codigo);
                return null;
            }

            var elegido = candidatos.OrderBy(c => c.IdTenant == null ? 1 : 0).FirstOrDefault();
            if (elegido == null) return null;

            var version = await _db.ReporteVersiones.AsNoTracking()
                .Where(v => v.IdVersion == elegido.IdVersionActiva && v.IdReporte == elegido.IdReporte && v.Estado == EstadosVersionReporte.Publicada)
                .Select(v => new { v.IdVersion, v.NumeroVersion, v.HashSha256, v.ContenidoPlantilla, v.ConfiguracionJson })
                .FirstOrDefaultAsync(ct);
            if (version == null)
            {
                _log.LogWarning("El reporte {Codigo} ({IdReporte}) apunta a una versión activa {IdVersion} que no está publicada.",
                    codigo, elegido.IdReporte, elegido.IdVersionActiva);
                return null;
            }

            PlantillaCompilada compilada;
            try
            {
                compilada = (await _cache.GetOrCreateAsync(("rep_version", version.IdVersion), e =>
                {
                    e.SlidingExpiration = VidaDeLaCompilada;
                    return Task.FromResult(ScribanReporteRenderer.Compilar(version.ContenidoPlantilla, version.ConfiguracionJson));
                }))!;
            }
            catch (PlantillaDeReporteInvalidaException ex)
            {
                _log.LogError(ex, "La versión {NumeroVersion} del reporte {Codigo} no compila; se usa el formato en código.", version.NumeroVersion, codigo);
                return null;
            }

            return new VersionDeReporte(elegido.IdReporte, elegido.Codigo, elegido.FuenteDatos, elegido.TipoSalida, elegido.IdTenant != null,
                version.IdVersion, version.NumeroVersion, version.HashSha256, compilada);
        }

        public string Renderizar(VersionDeReporte version, IReadOnlyDictionary<string, object?> modelo)
        {
            ArgumentNullException.ThrowIfNull(version);
            return ScribanReporteRenderer.Renderizar(version.Compilada, modelo);
        }

        public ReporteEjecucion NuevaEjecucion(VersionDeReporte version, int idEmpresa, string? entidadOrigen, long? idRegistroOrigen,
            int? idCuenta, string usuario, string? parametrosJson = null)
        {
            ArgumentNullException.ThrowIfNull(version);
            return new ReporteEjecucion
            {
                IdEmpresa = idEmpresa,
                IdReporte = version.IdReporte,
                IdVersion = version.IdVersion,
                CodigoReporte = version.Codigo,
                NumeroVersion = version.NumeroVersion,
                EntidadOrigen = entidadOrigen,
                IdRegistroOrigen = idRegistroOrigen,
                ParametrosJson = parametrosJson,
                TipoSalida = version.TipoSalida,
                IdCuenta = idCuenta,
                Usuario = Cortar(usuario, 100) ?? "system",
                IdCorrelacion = Cortar(_tenant.IdCorrelacion, 64),
                FechaHora = _tiempo.GetUtcNow().UtcDateTime
            };
        }

        private static string? Cortar(string? texto, int largo) => texto == null || texto.Length <= largo ? texto : texto[..largo];

        private sealed record Candidato(int IdReporte, int? IdTenant, string Codigo, string FuenteDatos, string TipoSalida, int IdVersionActiva);
    }
}
