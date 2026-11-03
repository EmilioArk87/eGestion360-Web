using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Quién pregunta por sus permisos: lo que sale de la identidad de la petición (cookie, F1.5) y de los derechos de uso (F1.7). El
    /// evaluador no lee <c>HttpContext</c>: así se prueba sin servidor web y lo puede usar un proceso de fondo.
    /// </summary>
    /// <param name="EsOperador">Personal de la plataforma (hoy el administrador general). Recibe todos los permisos, como hoy <c>IsAdmin</c> pasa cualquier guarda.</param>
    /// <param name="IdTenant">Tenant de la identidad; nulo si no hay (entonces solo el operador tiene permisos).</param>
    /// <param name="IdRol">Rol de la empresa (<c>empresa_roles</c>) con el que entró; nulo si no tiene (un administrador de empresa).</param>
    /// <param name="RolLegado">Rol de texto del acceso: <c>empresa_admin</c> o <c>empresa_user</c> (<c>AuthHelper.EmpresaAdminRole</c>).</param>
    /// <param name="ModulosHabilitados">Códigos de módulo que el plan del tenant concede Y la empresa tiene activados (<c>DerechosDeLaPeticion.TieneModulo</c>).</param>
    public sealed record ContextoDePermisos(
        bool EsOperador,
        int? IdTenant,
        int? IdRol,
        string RolLegado,
        IReadOnlySet<string> ModulosHabilitados);

    /// <summary>Los permisos que tiene quien preguntó. Una sola pregunta, <see cref="Puede"/>, para páginas, menú y botones (ADR-008).</summary>
    public sealed class PermisosEfectivos
    {
        /// <summary>No tiene ningún permiso.</summary>
        public static readonly PermisosEfectivos Ninguno = new(new HashSet<string>(StringComparer.Ordinal));

        private readonly IReadOnlySet<string> _codigos;

        public PermisosEfectivos(IReadOnlySet<string> codigos) => _codigos = codigos;

        /// <summary>Los códigos de todos los permisos que tiene.</summary>
        public IReadOnlyCollection<string> Codigos => _codigos;

        /// <summary>True si tiene el permiso (por ejemplo <c>flota.vehiculos.editar</c>).</summary>
        public bool Puede(string codigo) => _codigos.Contains(codigo);

        /// <summary>True si tiene al menos uno de los permisos.</summary>
        public bool PuedeAlguno(params string[] codigos) => codigos.Any(_codigos.Contains);
    }

    public interface IEvaluadorDePermisos
    {
        /// <summary>
        /// Los permisos efectivos de quien preguntó. Debe llamarse con el tenant de la petición ya fijado (el filtro de EF y la política
        /// RLS lo exigen). Lo leído se guarda unos segundos en memoria (D61).
        /// </summary>
        Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default);

        /// <summary>Olvida lo guardado de los roles de un tenant: llamar después de guardar los permisos de un rol o el acceso de alguien.</summary>
        void Invalidar(int idTenant);

        /// <summary>Olvida el catálogo guardado: llamar después de que un script o el operador cambie <c>saas.permisos</c>.</summary>
        void InvalidarCatalogo();
    }

    /// <summary>
    /// Calcula los permisos efectivos (paso F1.6a, ADR-029; decisiones D60 y D61). Las reglas son las de hoy, para que activarlo no cambie lo
    /// que nadie puede hacer:
    /// <list type="bullet">
    /// <item><description>El <b>operador</b> de la plataforma tiene todos los permisos (como <c>AuthHelper.IsAdmin</c>).</description></item>
    /// <item><description>El <b>administrador de empresa</b> (rol de texto <c>empresa_admin</c>) tiene los permisos de administración de la
    /// empresa y todos los de los módulos que su empresa puede usar (como <c>IsAnyAdmin</c> y <c>PuedeX</c> con <c>HasModulo</c>). Se decide por el
    /// texto del rol y NO por la casilla <c>es_admin</c> del rol de empresa: en producción los usuarios de un rol <c>es_admin</c> entran como
    /// <c>empresa_user</c>, y tratarlos como administradores les daría poderes que hoy no tienen.</description></item>
    /// <item><description>Un <b>usuario con rol</b> tiene los permisos que su rol concede (<c>rol_permisos</c>); los de módulo solo valen si el
    /// módulo está habilitado para su empresa.</description></item>
    /// <item><description>Cualquier otro caso (sin rol, sin tenant) no tiene permisos.</description></item>
    /// </list>
    /// Los permisos de ámbito <c>plataforma</c> son solo del operador, aunque la base tuviera una fila que los diera a un rol (no puede: lo impide una restricción).
    /// </summary>
    public sealed class EvaluadorDePermisos : IEvaluadorDePermisos
    {
        /// <summary>Cuánto se recuerda lo leído de la base (D61, igual que los derechos de uso).</summary>
        public const int SegundosEnMemoria = 30;

        private const string ClaveCatalogo = "permisos:catalogo";

        private static readonly IReadOnlySet<string> SinCodigos = new HashSet<string>(StringComparer.Ordinal);

        // Número que cambia cada vez que se invalida un tenant: las claves de los roles lo llevan y las anteriores quedan huérfanas
        // (vencen solas a los 30 s). Evita tener que recorrer la memoria para borrar por prefijo.
        private static long _contadorDeVersion;

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;

        public EvaluadorDePermisos(ApplicationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        private sealed record EntradaDelCatalogo(string Codigo, string Modulo, string Ambito);

        private static string ClaveVersion(int idTenant) => $"permisos:version:{idTenant}";

        private long Version(int idTenant) => _cache.TryGetValue(ClaveVersion(idTenant), out long v) ? v : 0;

        public async Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default)
        {
            var catalogo = await CatalogoAsync(ct);

            if (contexto.EsOperador)
                return new PermisosEfectivos(catalogo.Select(p => p.Codigo).ToHashSet(StringComparer.Ordinal));

            if (contexto.IdTenant is not > 0) return PermisosEfectivos.Ninguno;

            bool Vale(EntradaDelCatalogo p)
                => p.Ambito == AmbitoDePermiso.Empresa
                   || (p.Ambito == AmbitoDePermiso.Modulo && contexto.ModulosHabilitados.Contains(p.Modulo));

            if (string.Equals(contexto.RolLegado, AuthHelper.EmpresaAdminRole, StringComparison.OrdinalIgnoreCase))
                return new PermisosEfectivos(catalogo.Where(Vale).Select(p => p.Codigo).ToHashSet(StringComparer.Ordinal));

            if (contexto.IdRol is not > 0) return PermisosEfectivos.Ninguno;

            var concedidos = await ConcedidosAlRolAsync(contexto.IdTenant.Value, contexto.IdRol.Value, ct);
            return new PermisosEfectivos(catalogo.Where(p => concedidos.Contains(p.Codigo) && Vale(p))
                                                 .Select(p => p.Codigo).ToHashSet(StringComparer.Ordinal));
        }

        private async Task<IReadOnlyList<EntradaDelCatalogo>> CatalogoAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(ClaveCatalogo, out IReadOnlyList<EntradaDelCatalogo>? guardado) && guardado != null) return guardado;

            IReadOnlyList<EntradaDelCatalogo> catalogo = await _db.Permisos.AsNoTracking()
                .Where(p => p.Activo)
                .OrderBy(p => p.Orden)
                .Select(p => new EntradaDelCatalogo(p.Codigo, p.Modulo, p.Ambito))
                .ToListAsync(ct);
            _cache.Set(ClaveCatalogo, catalogo, TimeSpan.FromSeconds(SegundosEnMemoria));
            return catalogo;
        }

        /// <summary>
        /// Códigos de los permisos que el rol tiene en <c>rol_permisos</c>. Se piden con el tenant de la identidad además del filtro de EF y
        /// de la política RLS: tres barreras, y la clave de la memoria lleva el tenant, así que un rol de otro tenant nunca se mezcla.
        /// </summary>
        private async Task<IReadOnlySet<string>> ConcedidosAlRolAsync(int idTenant, int idRol, CancellationToken ct)
        {
            // Si la petición es de otro tenant, el filtro de EF devolvería vacío y ese vacío se quedaría 30 s en la memoria bajo la clave del tenant
            // preguntado, negándole los permisos a su gente. Una pregunta por un tenant que no es el de la petición no lee ni guarda nada.
            if (_db.FiltraPorTenant && _db.IdTenantFiltro != idTenant) return SinCodigos;

            var clave = $"permisos:rol:{idTenant}:{idRol}:v{Version(idTenant)}";
            if (_cache.TryGetValue(clave, out IReadOnlySet<string>? guardado) && guardado != null) return guardado;

            var codigos = await (from rp in _db.RolPermisos.AsNoTracking()
                                 where rp.IdTenant == idTenant && rp.IdRol == idRol
                                 join p in _db.Permisos.AsNoTracking() on rp.IdPermiso equals p.IdPermiso
                                 where p.Activo
                                 select p.Codigo).ToListAsync(ct);

            IReadOnlySet<string> resultado = codigos.ToHashSet(StringComparer.Ordinal);
            _cache.Set(clave, resultado, TimeSpan.FromSeconds(SegundosEnMemoria));
            return resultado;
        }

        public void Invalidar(int idTenant)
            => _cache.Set(ClaveVersion(idTenant), Interlocked.Increment(ref _contadorDeVersion));

        public void InvalidarCatalogo() => _cache.Remove(ClaveCatalogo);
    }
}
