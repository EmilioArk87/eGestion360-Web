using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages.Mantenimientos
{
    public class UsuariosDeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public UsuariosDeleteModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public User UserModel { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.usuarios.FirstOrDefaultAsync(m => m.Id == id);

            if (user == null)
            {
                return NotFound();
            }
            else
            {
                UserModel = user;
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int? id)
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.usuarios.FindAsync(id);
            if (user != null)
            {
                UserModel = user;
                _context.usuarios.Remove(UserModel);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Usuario eliminado exitosamente.";
            }

            return RedirectToPage("./Usuarios");
        }
    }
}
