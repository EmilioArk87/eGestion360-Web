using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.EmpresaRoles
{
    /// <summary>
    /// Los permisos de un rol de empresa. Desde F1.6e edita el catálogo nuevo (<c>rol_permisos</c>, la tabla que el sistema consulta para decidir quién
    /// puede hacer qué) y avisa al evaluador para que el cambio rija en la petición siguiente; hasta F1.6d guardaba solo las cuatro casillas viejas
    /// de <c>empresa_rol_permisos</c>, que ya no deciden nada.
    /// </summary>
    [RequierePermiso("administracion.roles.ver")]
    public class PermisosModel : PageModel
    {
        private readonly IPermisosDeRol _permisosDeRol;
        private readonly ApplicationDbContext _context;

        public PermisosModel(IPermisosDeRol permisosDeRol, ApplicationDbContext context)
        {
            _permisosDeRol = permisosDeRol;
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int RolId { get; set; }

        public PermisosDelRol? Datos { get; set; }
        public Empresa? Empresa { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            return await CargarAsync();
        }

        [RequierePermiso("administracion.roles.editar")]
        public async Task<IActionResult> OnPostAsync(int rolId, List<string>? codigos)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            RolId = rolId;
            var datos = await _permisosDeRol.ObtenerAsync(rolId);
            if (datos == null || !EsDeLaEmpresaDeLaSesion(datos.IdEmpresa)) return NotFound();

            var usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "sistema";
            var resultado = await _permisosDeRol.GuardarAsync(rolId, codigos ?? new List<string>(), usuario);
            if (resultado == null) return NotFound();

            TempData["SuccessMessage"] = resultado.HuboCambios
                ? $"Permisos guardados: {resultado.Agregados} concedidos y {resultado.Quitados} retirados. El cambio rige en la próxima petición de cada persona."
                : "No había cambios que guardar.";
            return RedirectToPage(new { rolId });
        }

        /// <summary>Quien trabaja dentro de una empresa solo toca los roles de esa empresa; el operador sin empresa en la sesión ve los de todo el tenant que eligió.</summary>
        private bool EsDeLaEmpresaDeLaSesion(int idEmpresaDelRol)
        {
            var empresaDeLaSesion = AuthHelper.GetEmpresaId(HttpContext);
            return empresaDeLaSesion is not > 0 || empresaDeLaSesion == idEmpresaDelRol;
        }

        private async Task<IActionResult> CargarAsync()
        {
            Datos = await _permisosDeRol.ObtenerAsync(RolId);
            if (Datos == null || !EsDeLaEmpresaDeLaSesion(Datos.IdEmpresa)) return NotFound();

            Empresa = await _context.Empresas.FindAsync(Datos.IdEmpresa);
            return Page();
        }
    }
}
