using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Modulos
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) => _context = context;

        public List<Modulo> Modulos { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            Modulos = await _context.Modulos.OrderBy(m => m.Orden).ToListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            var modulo = await _context.Modulos.FindAsync(id);
            if (modulo != null)
            {
                modulo.Activo = activo;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Módulo '{modulo.Nombre}' actualizado.";
            }
            return RedirectToPage();
        }
    }
}
