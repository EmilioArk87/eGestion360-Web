using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>Averigua a qué tenant pertenece una empresa o una persona.</summary>
    public interface IResolvedorDeTenant
    {
        /// <summary>Tenant de la empresa, o nulo si la empresa no existe, está eliminada o aún no tiene tenant.</summary>
        Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default);

        /// <summary>Tenant de la persona, o nulo si la persona no existe o aún no tiene tenant.</summary>
        Task<int?> ResolverPorPersonaAsync(int idPersona, CancellationToken ct = default);

        /// <summary>
        /// Tenant interno: el de la propia plataforma (<c>saas.tenants.tipo = 'interno'</c>, ADR-006), al que pertenece el personal
        /// de la plataforma. Nulo si no hay.
        /// </summary>
        Task<int?> ResolverInternoAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Resuelve el tenant desde <c>empresas.id_tenant</c> (o <c>personas.id_tenant</c>) y recuerda el resultado unos minutos,
    /// para no consultar la base en cada petición. Una empresa o una persona no cambia de tenant (el interceptor de guardado
    /// lo impide y la pantalla de edición no lo permite), así que el desfase posible es de pocos minutos y solo si alguien lo
    /// cambia directo en la base. Los resultados nulos no se guardan: una empresa sin tenant es un error que hay que ver, no
    /// una respuesta que repetir.
    ///
    /// Consulta con su propio <see cref="ApplicationDbContext"/>, en un ámbito aparte: así no se mezcla con el contexto que
    /// está guardando (lo llama <see cref="TenantAlGuardarInterceptor"/> en pleno <c>SaveChanges</c>, y el contexto no admite
    /// otra consulta en paralelo) y no hay dependencia circular entre el contexto, sus interceptores y este servicio.
    /// </summary>
    public sealed class ResolvedorDeTenantEf : IResolvedorDeTenant
    {
        private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _ambitos;
        private readonly IMemoryCache _cache;
        private readonly ITenantContext? _llamador;

        /// <param name="llamador">El contexto de quien pide: la consulta, que corre en un ámbito aparte, trabaja con el mismo tenant
        /// (con la RLS encendida una persona solo se ve desde su tenant).</param>
        public ResolvedorDeTenantEf(IServiceScopeFactory ambitos, IMemoryCache cache, ITenantContext? llamador = null)
        {
            _ambitos = ambitos;
            _cache = cache;
            _llamador = llamador;
        }

        public async Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default)
        {
            if (idEmpresa <= 0) return null;

            // Consulta escalar en lugar de una proyección de la entidad: mientras la columna admite nulos (scripts 025 a 029,
            // hasta F1.3), una fila creada por la versión anterior de la aplicación trae id_tenant nulo, y EF no puede leer un
            // nulo en la propiedad obligatoria del modelo. Así devuelve nulo y quien llama lo avisa.
            return await ResolverAsync($"tenant-de-empresa:{idEmpresa}", ct, db => db.Database
                .SqlQuery<int?>($"SELECT id_tenant AS [Value] FROM empresas WHERE id_empresa = {idEmpresa} AND eliminado = 0"));
        }

        public async Task<int?> ResolverPorPersonaAsync(int idPersona, CancellationToken ct = default)
        {
            if (idPersona <= 0) return null;

            return await ResolverAsync($"tenant-de-persona:{idPersona}", ct, db => db.Database
                .SqlQuery<int?>($"SELECT id_tenant AS [Value] FROM personas WHERE id_persona = {idPersona}"));
        }

        public async Task<int?> ResolverInternoAsync(CancellationToken ct = default)
            => await ResolverAsync("tenant-interno", ct, db => db.Tenants.AsNoTracking().Where(t => t.Tipo == TipoTenant.Interno).Select(t => (int?)t.IdTenant));

        private async Task<int?> ResolverAsync(string clave, CancellationToken ct, Func<ApplicationDbContext, IQueryable<int?>> consulta)
        {
            if (_cache.TryGetValue(clave, out int enCache)) return enCache;

            int? idTenant;
            using (var ambito = _ambitos.CreateScope())
            {
                HeredarContexto(ambito.ServiceProvider);
                var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                idTenant = await consulta(db).FirstOrDefaultAsync(ct);
            }

            if (idTenant is not > 0) return null;

            _cache.Set(clave, idTenant.Value, Vigencia);
            return idTenant;
        }

        /// <summary>El ámbito de la consulta trabaja con el mismo tenant y ámbito que quien la pide.</summary>
        private void HeredarContexto(IServiceProvider servicios)
        {
            if (_llamador == null) return;

            servicios.GetService<TenantContext>()?.Fijar(_llamador.IdTenant, _llamador.IdEmpresa, _llamador.EsPlataforma);
        }
    }
}
