using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Identidad;

namespace eGestion360Web.Pages
{
    public class LogoutModel : PageModel
    {
        private readonly IAutenticador _autenticador;

        public LogoutModel(IAutenticador autenticador) => _autenticador = autenticador;

        public async Task<IActionResult> OnGetAsync()
        {
            await _autenticador.SalirAsync(HttpContext);
            return RedirectToPage("/Login");
        }

        public async Task<IActionResult> OnGetLogoutAsync()
        {
            await _autenticador.SalirAsync(HttpContext);
            return RedirectToPage("/Login");
        }
    }
}
