using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Flota
{
    /// <summary>
    /// Aviso al que <see cref="EmpresaRequeridaPageFilter"/> desvía las sesiones sin empresa que intentan entrar a Flota.
    /// Si la sesión sí tiene empresa no hay nada que explicar y vuelve al menú de Flota.
    /// </summary>
    [PaginaSinPermiso("Explica por qué Flota no abre sin empresa")]
    public class SinEmpresaModel : PageModel
    {
        private readonly IPermisosDeLaPeticion _permisos;

        public SinEmpresaModel(IPermisosDeLaPeticion permisos) => _permisos = permisos;

        /// <summary>Quien puede administrar los usuarios del sistema: a esa persona se le indica dónde crear o revisar los usuarios de cada empresa.</summary>
        public bool PuedeAdministrarUsuarios { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (AuthHelper.GetEmpresaId(HttpContext) is > 0)
                return RedirectToPage("/Flota/Index");

            PuedeAdministrarUsuarios = (await _permisos.ObtenerAsync()).Puede("plataforma.usuarios.ver");
            return Page();
        }
    }
}
