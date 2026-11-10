using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Portada pública")]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
