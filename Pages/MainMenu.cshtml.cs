using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;

namespace eGestion360Web.Pages
{
    public class MainMenuModel : PageModel
    {
        public string Username      { get; set; } = string.Empty;
        public string Email         { get; set; } = string.Empty;
        public bool   IsAdmin       { get; set; }
        public bool   IsEmpresaAdmin { get; set; }
        public int?   EmpresaId     { get; set; }

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            Username       = HttpContext.Session.GetString("Username") ?? "Usuario";
            Email          = HttpContext.Session.GetString("Email") ?? "";
            IsAdmin        = AuthHelper.IsAdmin(HttpContext);
            IsEmpresaAdmin = AuthHelper.IsEmpresaAdmin(HttpContext);
            EmpresaId      = AuthHelper.GetEmpresaId(HttpContext);

            return Page();
        }

        /// <summary>True si el usuario tiene acceso al módulo (superadmin siempre tiene acceso).</summary>
        public bool TieneModulo(string codigo) => AuthHelper.HasModulo(HttpContext, codigo);
    }
}
