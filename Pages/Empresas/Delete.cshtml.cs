using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Pages.Empresas
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly TenantContext? _tenant;

        public DeleteModel(ApplicationDbContext context, TenantContext? tenant = null)
        {
            _context = context;
            _tenant = tenant;
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

            // La base solo deja modificar una empresa dentro del contexto de su tenant (ADR-023; F1.4c, ADR-025).
            _tenant?.Fijar(empresa.IdTenant, null);
            await _context.SaveChangesAsync();

            TempData["EmpresasMessage"] = "Empresa eliminada correctamente.";
            return RedirectToPage("Index");
        }
    }
}
