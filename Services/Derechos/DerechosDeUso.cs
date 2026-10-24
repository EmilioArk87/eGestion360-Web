using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Derechos
{
    /// <summary>
    /// Lee los derechos de uso y el estado de un tenant (paso F1.7). Los planes y los derechos son del plano de control (esquema
    /// <c>saas</c>, sin RLS); la activación de módulos por empresa (<c>empresa_modulos</c>) es una tabla de tenant y se lee con
    /// el tenant de la petición. Una suspensión o un cambio de plan se nota en segundos: lo leído se guarda
    /// <see cref="SegundosEnMemoria"/> segundos (D55) o hasta que alguien llame a <see cref="Invalidar"/>.
    /// </summary>
    public sealed class DerechosDeUso : IDerechosDeUso
    {
        /// <summary>Cuánto se recuerda lo leído de la base (D55).</summary>
        public const int SegundosEnMemoria = 30;

        private static readonly IReadOnlySet<string> SinCodigos = new HashSet<string>(StringComparer.Ordinal);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly TimeProvider _tiempo;

        public DerechosDeUso(ApplicationDbContext db, IMemoryCache cache, TimeProvider? tiempo = null)
        {
            _db = db;
            _cache = cache;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private static string ClaveTenant(int idTenant) => $"derechos:tenant:{idTenant}";

        private static string ClaveEmpresa(int idEmpresa) => $"derechos:empresa:{idEmpresa}";

        public async Task<DerechosDelTenant?> DelTenantAsync(int idTenant, CancellationToken ct = default)
        {
            if (_cache.TryGetValue(ClaveTenant(idTenant), out DerechosDelTenant? guardado)) return guardado;

            var tenant = await _db.Tenants.AsNoTracking()
                .Where(t => t.IdTenant == idTenant)
                .Select(t => new { t.IdTenant, t.Codigo, t.Tipo, t.Estado, t.ModoSuspension, t.MotivoEstado })
                .FirstOrDefaultAsync(ct);
            if (tenant == null) return null;

            // El plan vigente es la fila sin vigente_hasta (la base impide que haya dos).
            var planCodigo = await (from tp in _db.TenantPlanes
                                    where tp.IdTenant == idTenant && tp.VigenteHasta == null
                                    join pv in _db.PlanVersiones on tp.IdPlanVersion equals pv.IdPlanVersion
                                    join p in _db.Planes on pv.IdPlan equals p.IdPlan
                                    select p.Codigo).FirstOrDefaultAsync(ct);

            var concedidos = await (from tp in _db.TenantPlanes
                                    where tp.IdTenant == idTenant && tp.VigenteHasta == null
                                    join pvd in _db.PlanVersionDerechos on tp.IdPlanVersion equals pvd.IdPlanVersion
                                    join d in _db.Derechos on pvd.IdDerecho equals d.IdDerecho
                                    select new { d.Codigo, d.Tipo, pvd.ValorLimite }).ToListAsync(ct);

            var derechos = new HashSet<string>(concedidos.Select(c => c.Codigo), StringComparer.Ordinal);
            var limites = concedidos.Where(c => c.Tipo == TipoDerecho.Limite)
                                    .ToDictionary(c => c.Codigo, c => c.ValorLimite, StringComparer.Ordinal);

            var resultado = new DerechosDelTenant(tenant.IdTenant, tenant.Codigo, tenant.Tipo, tenant.Estado, tenant.ModoSuspension,
                tenant.MotivoEstado, planCodigo, derechos, limites);
            _cache.Set(ClaveTenant(idTenant), resultado, TimeSpan.FromSeconds(SegundosEnMemoria));
            return resultado;
        }

        public async Task<IReadOnlySet<string>> ModulosActivosDeLaEmpresaAsync(int idEmpresa, CancellationToken ct = default)
        {
            if (idEmpresa <= 0) return SinCodigos;
            if (_cache.TryGetValue(ClaveEmpresa(idEmpresa), out IReadOnlySet<string>? guardado) && guardado != null) return guardado;

            // Activo, módulo activo en el catálogo y sin vencer. Se cuenta como vigente el día del vencimiento.
            var hoy = _tiempo.GetUtcNow().UtcDateTime.Date;
            var codigos = await _db.EmpresaModulos.AsNoTracking()
                .Where(em => em.IdEmpresa == idEmpresa && em.Activo && em.Modulo.Activo
                             && (em.FechaVencimiento == null || em.FechaVencimiento.Value.Date >= hoy))
                .Select(em => em.Modulo.Codigo)
                .ToListAsync(ct);

            IReadOnlySet<string> resultado = new HashSet<string>(codigos.Select(c => c.ToLowerInvariant()), StringComparer.Ordinal);
            _cache.Set(ClaveEmpresa(idEmpresa), resultado, TimeSpan.FromSeconds(SegundosEnMemoria));
            return resultado;
        }

        public async Task<ResultadoDeLimite> PuedeAgregarAsync(int idTenant, string codigoDeLimite, CancellationToken ct = default)
        {
            var derechos = await DelTenantAsync(idTenant, ct);
            var tope = derechos?.Limite(codigoDeLimite);

            var enUso = codigoDeLimite switch
            {
                CodigosDeDerecho.LimiteUsuarios => await ContarUsuariosActivosAsync(idTenant, ct),
                CodigosDeDerecho.LimiteEmpresas => await ContarEmpresasAsync(idTenant, ct),
                _ => throw new ArgumentException($"Hoy solo se aplican {CodigosDeDerecho.LimiteUsuarios} y {CodigosDeDerecho.LimiteEmpresas} (D54).", nameof(codigoDeLimite))
            };

            return new ResultadoDeLimite(tope == null || enUso < tope, tope, enUso);
        }

        // Los dos conteos usan el filtro de tenant de siempre (y la RLS de la base): cuentan lo del tenant de la petición, que es
        // el que agrega el usuario o la empresa. El código de la aplicación no se salta los filtros (AmbitoDePlataformaTests).

        /// <summary>Usuarios activos cuya empresa (no eliminada) es del tenant.</summary>
        private Task<int> ContarUsuariosActivosAsync(int idTenant, CancellationToken ct)
            => (from u in _db.Users.AsNoTracking()
                where u.IsActive && u.EmpresaId != null
                join e in _db.Empresas.AsNoTracking() on u.EmpresaId equals e.IdEmpresa
                where e.IdTenant == idTenant && !e.Eliminado
                select u.Id).CountAsync(ct);

        /// <summary>Empresas no eliminadas del tenant.</summary>
        private Task<int> ContarEmpresasAsync(int idTenant, CancellationToken ct)
            => _db.Empresas.AsNoTracking().CountAsync(e => e.IdTenant == idTenant && !e.Eliminado, ct);

        public void Invalidar(int idTenant, int? idEmpresa = null)
        {
            _cache.Remove(ClaveTenant(idTenant));
            if (idEmpresa is > 0) _cache.Remove(ClaveEmpresa(idEmpresa.Value));
        }
    }
}
