using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DeleteModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Empresa Empresa { get; set; } = new Empresa();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            var empresa = await _context.Empresas.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdEmpresa == id && !e.Eliminado);
            if (empresa == null)
            {
                return NotFound();
            }

            Empresa = empresa;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            var empresa = await _context.Empresas.FirstOrDefaultAsync(e => e.IdEmpresa == id && !e.Eliminado);
            if (empresa == null)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            empresa.Eliminado = true;
            empresa.FechaEliminado = now;
            empresa.Activa = false;
            empresa.FechaBaja ??= now;
            empresa.ModificadoPor = user;
            empresa.FechaModificacion = now;

            await _context.SaveChangesAsync();

            TempData["EmpresasMessage"] = "Empresa eliminada correctamente.";
            return RedirectToPage("Index");
        }
    }
}
