using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages.Mantenimientos
{
    public class UsuariosEditModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public UsuariosEditModel(ApplicationDbContext context)
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
            UserModel = user;
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
                _context.Attach(UserModel).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                TempData["Message"] = "Usuario actualizado exitosamente.";
                return RedirectToPage("./Usuarios");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(UserModel.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al actualizar el usuario: " + ex.Message);
                return Page();
            }
        }

        private bool UserExists(int id)
        {
            return _context.usuarios.Any(e => e.Id == id);
        }
    }
}
