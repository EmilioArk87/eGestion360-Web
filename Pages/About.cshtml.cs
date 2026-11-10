using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Información pública del producto")]
    public class AboutModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
