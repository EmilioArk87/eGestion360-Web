using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>Averigua a qué tenant pertenece una empresa.</summary>
    public interface IResolvedorDeTenant
    {
        /// <summary>Tenant de la empresa, o nulo si la empresa no existe, está eliminada o aún no tiene tenant.</summary>
        Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default);
    }

    /// <summary>
    /// Resuelve el tenant desde <c>empresas.id_tenant</c> y recuerda el resultado unos minutos, para no consultar la base en
    /// cada petición. Una empresa no cambia de tenant en F1 (la pantalla de edición no lo permite), así que el desfase
    /// posible es de pocos minutos y solo si alguien lo cambia directo en la base. Los resultados nulos no se guardan:
    /// una empresa sin tenant es un error que hay que ver, no una respuesta que repetir.
    /// </summary>
    public sealed class ResolvedorDeTenantEf : IResolvedorDeTenant
    {
        private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;

        public ResolvedorDeTenantEf(ApplicationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default)
        {
            if (idEmpresa <= 0) return null;

            var clave = $"tenant-de-empresa:{idEmpresa}";
            if (_cache.TryGetValue(clave, out int enCache)) return enCache;

            // Consulta escalar en lugar de una proyección de la entidad: mientras la columna admite nulos (script 025, hasta
            // F1.3), una empresa creada por la versión anterior de la aplicación trae id_tenant nulo, y EF no puede leer un
            // nulo en la propiedad obligatoria del modelo. Así devuelve nulo y el middleware lo avisa en el log.
            var idTenant = await _db.Database
                .SqlQuery<int?>($"SELECT id_tenant AS [Value] FROM empresas WHERE id_empresa = {idEmpresa} AND eliminado = 0")
                .FirstOrDefaultAsync(ct);

            if (idTenant is not > 0) return null;

            _cache.Set(clave, idTenant.Value, Vigencia);
            return idTenant;
        }
    }
}
