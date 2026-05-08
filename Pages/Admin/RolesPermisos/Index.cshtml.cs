using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.RolesPermisos
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)]
        public int? EmpresaId { get; set; }

        public List<Empresa>    Empresas            { get; set; } = new();
        public Empresa?         EmpresaSeleccionada { get; set; }
        public List<EmpresaRol> Roles               { get; set; } = new();
        public List<Modulo>     ModulosEmpresa      { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            // empresa_admin solo ve su propia empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext))
                EmpresaId = AuthHelper.GetEmpresaId(HttpContext);

            await CargarEmpresasAsync();

            if (EmpresaId.HasValue)
                await CargarDatosEmpresaAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostCrearAsync(int empresaId, string nombre, string? descripcion, bool esAdmin)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

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

        public async Task<IActionResult> OnPostEditarAsync(int rolId, int empresaId, string nombre, string? descripcion, bool esAdmin)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var rol = await _context.EmpresaRoles.FindAsync(rolId);
            if (rol != null)
            {
                rol.Nombre      = nombre.Trim();
                rol.Descripcion = descripcion?.Trim();
                rol.EsAdmin     = esAdmin;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Rol '{nombre}' actualizado correctamente.";
            }
            return RedirectToPage(new { empresaId });
        }

        public async Task<IActionResult> OnPostToggleAsync(int rolId, bool activo, int empresaId)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var rol = await _context.EmpresaRoles.FindAsync(rolId);
            if (rol != null)
            {
                rol.Activo = activo;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Rol '{rol.Nombre}' {(activo ? "activado" : "desactivado")}.";
            }
            return RedirectToPage(new { empresaId });
        }

        private async Task CargarEmpresasAsync()
        {
            if (AuthHelper.IsAdmin(HttpContext))
            {
                Empresas = await _context.Empresas
                    .Where(e => !e.Eliminado)
                    .OrderBy(e => e.RazonSocial)
                    .ToListAsync();
            }
            else if (AuthHelper.IsEmpresaAdmin(HttpContext) && EmpresaId.HasValue)
            {
                var emp = await _context.Empresas.FindAsync(EmpresaId.Value);
                if (emp != null) Empresas.Add(emp);
            }
        }

        private async Task CargarDatosEmpresaAsync()
        {
            EmpresaSeleccionada = await _context.Empresas.FindAsync(EmpresaId!.Value);
            if (EmpresaSeleccionada == null) return;

            Roles = await _context.EmpresaRoles
                .Where(r => r.IdEmpresa == EmpresaId)
                .Include(r => r.Usuarios)
                .OrderBy(r => r.Nombre)
                .ToListAsync();

            ModulosEmpresa = await _context.EmpresaModulos
                .Where(em => em.IdEmpresa == EmpresaId && em.Activo)
                .Include(em => em.Modulo)
                .Where(em => em.Modulo.Activo)
                .Select(em => em.Modulo)
                .OrderBy(m => m.Orden)
                .ToListAsync();
        }
    }
}
