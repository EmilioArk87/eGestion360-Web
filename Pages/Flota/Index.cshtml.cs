using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota
{
    public class IndexModel : PageModel
    {
        public bool IsAdmin { get; set; }

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            IsAdmin = AuthHelper.IsAdmin(HttpContext);
            return Page();
        }
    }
}
