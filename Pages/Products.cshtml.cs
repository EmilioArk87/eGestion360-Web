using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace eGestion360Web.Pages
{
    public class ProductsModel : PageModel
    {
        // Vista "Otros Productos" oculta temporalmente: cualquier acceso directo
        // a /Products redirige al Inicio. Para reactivarla, devolver Page().
        public IActionResult OnGet()
        {
            return RedirectToPage("/Index");
        }
    }
}
