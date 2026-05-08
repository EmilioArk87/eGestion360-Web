using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.EmpresaRoles
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)]
        public int EmpresaId { get; set; }

        public Empresa? Empresa { get; set; }
        public List<EmpresaRol> Roles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            // empresa_admin solo puede ver roles de su propia empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext) && AuthHelper.GetEmpresaId(HttpContext) != EmpresaId)
                return RedirectToPage("/MainMenu");

            return await CargarAsync();
        }

        public async Task<IActionResult> OnPostCrearAsync(int empresaId, string nombre, string? descripcion, bool esAdmin)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            EmpresaId = empresaId;

            _context.EmpresaRoles.Add(new EmpresaRol
            {
                IdEmpresa   = empresaId,
                Nombre      = nombre.Trim(),
                Descripcion = descripcion?.Trim(),
                EsAdmin     = esAdmin,
                Activo      = true
            });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Rol '{nombre}' creado correctamente.";
            return RedirectToPage(new { empresaId });
        }

        public async Task<IActionResult> OnPostToggleAsync(int rolId, bool activo, int empresaId)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            EmpresaId = empresaId;
            var rol = await _context.EmpresaRoles.FindAsync(rolId);
            if (rol != null)
            {
                rol.Activo = activo;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Rol '{rol.Nombre}' actualizado.";
            }
            return RedirectToPage(new { empresaId });
        }

        private async Task<IActionResult> CargarAsync()
        {
            Empresa = await _context.Empresas.FindAsync(EmpresaId);
            if (Empresa == null) return NotFound();
            Roles = await _context.EmpresaRoles
                .Where(r => r.IdEmpresa == EmpresaId)
                .OrderBy(r => r.Nombre)
                .ToListAsync();
            return Page();
        }
    }
}
