using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Psicologia.Catalogos
{
    /// <summary>Catálogos del módulo: lleva a las pruebas psicológicas (D12) y a los tipos de constancia (D14), cada uno en su propia vista.</summary>
    [PaginaSinPermiso("Centro de navegación del módulo: cada opción pide su propio permiso")]
    public class IndexModel : PageModel
    {
        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            return Page();
        }
    }
}
