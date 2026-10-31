using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Módulos activos de una empresa y permisos por módulo del rol con el que entra una persona (lo que antes copiaba el Login a la sesión).</summary>
    public sealed record PermisosDeEmpresa(IReadOnlyList<string> Modulos, Dictionary<string, HashSet<string>> Permisos)
    {
        public static readonly PermisosDeEmpresa Vacios = new(Array.Empty<string>(), new Dictionary<string, HashSet<string>>());
    }

    public interface ICargadorDePermisos
    {
        /// <summary>
        /// Lee los módulos activos de la empresa y los permisos de su rol. Un administrador de empresa sin rol tiene todos los permisos
        /// sobre sus módulos. Debe correr con el tenant de la empresa ya fijado (el filtro de EF y la política RLS lo exigen).
        /// </summary>
        Task<PermisosDeEmpresa> CargarAsync(int idEmpresa, int? idRol, string rol, CancellationToken ct = default);
    }

    public sealed class CargadorDePermisos : ICargadorDePermisos
    {
        private readonly ApplicationDbContext _db;

        public CargadorDePermisos(ApplicationDbContext db) => _db = db;

        public async Task<PermisosDeEmpresa> CargarAsync(int idEmpresa, int? idRol, string rol, CancellationToken ct = default)
        {
            var modulos = await _db.EmpresaModulos.AsNoTracking()
                .Where(em => em.IdEmpresa == idEmpresa && em.Activo && em.Modulo.Activo)
                .Select(em => em.Modulo.Codigo)
                .ToListAsync(ct);

            var permisos = new Dictionary<string, HashSet<string>>();

            if (idRol is { } rolId)
            {
                var rolPermisos = await _db.EmpresaRolPermisos.AsNoTracking()
                    .Where(p => p.IdRol == rolId)
                    .Select(p => new { p.Modulo.Codigo, p.PuedeVer, p.PuedeCrear, p.PuedeEditar, p.PuedeEliminar })
                    .ToListAsync(ct);

                foreach (var p in rolPermisos)
                {
                    var set = new HashSet<string>();
                    if (p.PuedeVer) set.Add("ver");
                    if (p.PuedeCrear) set.Add("crear");
                    if (p.PuedeEditar) set.Add("editar");
                    if (p.PuedeEliminar) set.Add("eliminar");
                    permisos[p.Codigo] = set;
                }
            }
            else if (string.Equals(rol, AuthHelper.EmpresaAdminRole, StringComparison.OrdinalIgnoreCase))
            {
                // empresa_admin tiene todos los permisos sobre sus módulos
                foreach (var m in modulos)
                    permisos[m] = new HashSet<string> { "ver", "crear", "editar", "eliminar" };
            }

            return new PermisosDeEmpresa(modulos, permisos);
        }
    }
}
