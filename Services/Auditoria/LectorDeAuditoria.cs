using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>Una página de resultados de la auditoría.</summary>
    public sealed record PaginaDeAuditoria(IReadOnlyList<FilaDeAuditoria> Filas, int Total, int Pagina, int TotalDePaginas);

    /// <summary>Lo que sale para exportar: como mucho <see cref="ConsultaDeAuditoria.MaximoParaExportar"/> filas, las más recientes.</summary>
    public sealed record ExportacionDeAuditoria(IReadOnlyList<FilaDeAuditoria> Filas, int Total)
    {
        public bool Recortada => Total > Filas.Count;
    }

    /// <summary>
    /// Lee las dos tablas de auditoría para las pantallas de F1.8c (ADR-030 D74), sin escribir nada.
    /// <list type="bullet">
    /// <item><description>La del tenant pide SIEMPRE un tenant y filtra por él a mano, además del filtro global de EF y de la política
    /// RLS: un operador sin tenant (filtro apagado) nunca ve la auditoría de todos los tenants por esta pantalla.</description></item>
    /// <item><description>La de la plataforma no tiene RLS (plano de control): la abre solo el personal de la plataforma.</description></item>
    /// </list>
    /// </summary>
    public sealed class LectorDeAuditoria
    {
        private readonly ApplicationDbContext _db;

        public LectorDeAuditoria(ApplicationDbContext db) => _db = db;

        /// <summary>La zona en la que se leen y se muestran las fechas.</summary>
        public static TimeZoneInfo Zona { get; } = ZonaHorariaHonduras.Obtener();

        /// <summary>Hoy en Honduras.</summary>
        public static DateOnly Hoy(DateTime? utcAhora = null)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcAhora ?? DateTime.UtcNow, Zona));

        private IQueryable<AuditoriaSeguridad> DelTenant(int idTenant, FiltroDeAuditoria filtro)
            => ConsultaDeAuditoria.Filtrar(_db.AuditoriaSeguridad.AsNoTracking().Where(e => e.IdTenant == idTenant), filtro, Zona);

        private IQueryable<AuditoriaPlataforma> DeLaPlataforma(FiltroDeAuditoria filtro)
        {
            var consulta = _db.AuditoriaPlataforma.AsNoTracking();
            if (filtro.IdTenant is { } idTenant) consulta = consulta.Where(e => e.IdTenant == idTenant);
            return ConsultaDeAuditoria.Filtrar(consulta, filtro, Zona);
        }

        public async Task<PaginaDeAuditoria> PaginaDelTenantAsync(int idTenant, FiltroDeAuditoria filtro, int pagina, CancellationToken ct = default)
        {
            var consulta = DelTenant(idTenant, filtro);
            var (total, numero, paginas) = await ContarAsync(consulta, pagina, ct);
            var eventos = await ConsultaDeAuditoria.Ordenar(consulta).Skip((numero - 1) * ConsultaDeAuditoria.PorPagina).Take(ConsultaDeAuditoria.PorPagina).ToListAsync(ct);
            return new PaginaDeAuditoria(eventos.Select(e => ConsultaDeAuditoria.AFila(e, e.IdTenant, null, Zona)).ToList(), total, numero, paginas);
        }

        public async Task<ExportacionDeAuditoria> ExportarDelTenantAsync(int idTenant, FiltroDeAuditoria filtro, CancellationToken ct = default)
        {
            var consulta = DelTenant(idTenant, filtro);
            var total = await consulta.CountAsync(ct);
            var eventos = await ConsultaDeAuditoria.Ordenar(consulta).Take(ConsultaDeAuditoria.MaximoParaExportar).ToListAsync(ct);
            return new ExportacionDeAuditoria(eventos.Select(e => ConsultaDeAuditoria.AFila(e, e.IdTenant, null, Zona)).ToList(), total);
        }

        public async Task<PaginaDeAuditoria> PaginaDeLaPlataformaAsync(FiltroDeAuditoria filtro, int pagina, CancellationToken ct = default)
        {
            var consulta = DeLaPlataforma(filtro);
            var (total, numero, paginas) = await ContarAsync(consulta, pagina, ct);
            var eventos = await ConsultaDeAuditoria.Ordenar(consulta).Skip((numero - 1) * ConsultaDeAuditoria.PorPagina).Take(ConsultaDeAuditoria.PorPagina).ToListAsync(ct);
            var tenants = await TenantsAsync(ct);
            return new PaginaDeAuditoria(eventos.Select(e => AFila(e, tenants)).ToList(), total, numero, paginas);
        }

        public async Task<ExportacionDeAuditoria> ExportarDeLaPlataformaAsync(FiltroDeAuditoria filtro, CancellationToken ct = default)
        {
            var consulta = DeLaPlataforma(filtro);
            var total = await consulta.CountAsync(ct);
            var eventos = await ConsultaDeAuditoria.Ordenar(consulta).Take(ConsultaDeAuditoria.MaximoParaExportar).ToListAsync(ct);
            var tenants = await TenantsAsync(ct);
            return new ExportacionDeAuditoria(eventos.Select(e => AFila(e, tenants)).ToList(), total);
        }

        /// <summary>Código de cada tenant, para mostrarlo en la auditoría de la plataforma y para su filtro.</summary>
        public async Task<IReadOnlyDictionary<int, string>> TenantsAsync(CancellationToken ct = default)
            => await _db.Tenants.AsNoTracking().OrderBy(t => t.Codigo).ToDictionaryAsync(t => t.IdTenant, t => t.Codigo, ct);

        private static FilaDeAuditoria AFila(AuditoriaPlataforma e, IReadOnlyDictionary<int, string> tenants)
            => ConsultaDeAuditoria.AFila(e, e.IdTenant, e.IdTenant is { } id && tenants.TryGetValue(id, out var codigo) ? codigo : null, Zona);

        private static async Task<(int Total, int Pagina, int Paginas)> ContarAsync<T>(IQueryable<T> consulta, int pagina, CancellationToken ct)
        {
            var total = await consulta.CountAsync(ct);
            var paginas = (int)Math.Ceiling(total / (double)ConsultaDeAuditoria.PorPagina);
            var numero = Math.Clamp(pagina, 1, Math.Max(paginas, 1));
            return (total, numero, paginas);
        }

        /// <summary>Los tipos que puede tener cada tabla, para el filtro: los de la plataforma o los del tenant según el catálogo (D70).</summary>
        public static IReadOnlyList<string> TiposDe(bool plataforma)
            => TiposDeEvento.Todos
                .Where(t => TiposDeEvento.DestinoDe(t) switch
                {
                    DestinoDeEvento.Plataforma => plataforma,
                    DestinoDeEvento.Tenant => true,          // sin tenant (el personal de la plataforma, el catálogo global) quedan en la plataforma
                    _ => true,
                })
                .Order(StringComparer.Ordinal)
                .ToList();
    }
}
