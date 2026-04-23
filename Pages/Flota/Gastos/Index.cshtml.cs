using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Gastos
{
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
