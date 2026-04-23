using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace eGestion360Web.Pages
{
    public class UserManagementModel : PageModel
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            Username = HttpContext.Session.GetString("Username") ?? "Usuario";
            Email = HttpContext.Session.GetString("Email") ?? "";

            return Page();
        }
    }
}
