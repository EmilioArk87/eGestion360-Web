using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Información pública de contacto")]
    public class ContactModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
