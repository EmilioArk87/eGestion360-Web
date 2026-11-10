using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Información pública (hoy oculta: sin ruta)")]
    public class ProductsModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
