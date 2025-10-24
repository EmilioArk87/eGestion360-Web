using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages.Mantenimientos
{
    public class UsuariosCreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public UsuariosCreateModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public User UserModel { get; set; } = new User { IsActive = true };

        public IActionResult OnGet()
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                UserModel.CreatedAt = DateTime.UtcNow;
                _context.usuarios.Add(UserModel);
                await _context.SaveChangesAsync();

                TempData["Message"] = "Usuario creado exitosamente.";
                return RedirectToPage("./Usuarios");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al crear el usuario: " + ex.Message);
                return Page();
            }
        }
    }
}
