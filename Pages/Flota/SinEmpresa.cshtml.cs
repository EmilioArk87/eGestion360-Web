using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota
{
    /// <summary>
    /// Aviso al que <see cref="EmpresaRequeridaPageFilter"/> desvía las sesiones sin empresa que intentan entrar a Flota.
    /// Si la sesión sí tiene empresa no hay nada que explicar y vuelve al menú de Flota.
    /// </summary>
    public class SinEmpresaModel : PageModel
    {
        public bool IsAdmin { get; set; }

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (AuthHelper.GetEmpresaId(HttpContext) is > 0)
                return RedirectToPage("/Flota/Index");

            IsAdmin = AuthHelper.IsAdmin(HttpContext);
            return Page();
        }
    }
}
