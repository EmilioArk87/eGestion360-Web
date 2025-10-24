using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages.Mantenimientos
{
    public class UsuariosModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public UsuariosModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<User> Users { get; set; } = new List<User>();

        public async Task<IActionResult> OnGetAsync()
        {
            // Verificar si el usuario ha iniciado sesión
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            Users = await _context.usuarios.OrderBy(u => u.Id).ToListAsync();
            return Page();
        }
    }
}
