using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace eGestion360Web.Pages
{
    public class MainMenuModel : PageModel
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                // Si no ha iniciado sesión, redirigir al login
                return RedirectToPage("/Login");
            }

            // Obtener información del usuario de la sesión
            Username = HttpContext.Session.GetString("Username") ?? "Usuario";
            Email = HttpContext.Session.GetString("Email") ?? "";

            return Page();
        }
    }
}
