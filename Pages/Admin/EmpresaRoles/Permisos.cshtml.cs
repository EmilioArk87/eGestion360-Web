using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.EmpresaRoles
{
    public class PermisoItem
    {
        public Modulo Modulo { get; set; } = null!;
        public bool PuedeVer      { get; set; }
        public bool PuedeCrear    { get; set; }
        public bool PuedeEditar   { get; set; }
        public bool PuedeEliminar { get; set; }
    }

    public class PermisosModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public PermisosModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)]
        public int RolId { get; set; }

        public EmpresaRol? Rol     { get; set; }
        public Empresa?    Empresa { get; set; }
        public List<PermisoItem> Items { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");
            return await CargarAsync();
        }

        public async Task<IActionResult> OnPostAsync(int rolId, List<int> modulos, IFormCollection form)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            RolId = rolId;

            var existentes = await _context.EmpresaRolPermisos
                .Where(p => p.IdRol == rolId)
                .ToListAsync();

            foreach (var idModulo in modulos)
            {
                bool ver      = form.ContainsKey($"ver_{idModulo}");
                bool crear    = form.ContainsKey($"crear_{idModulo}");
                bool editar   = form.ContainsKey($"editar_{idModulo}");
                bool eliminar = form.ContainsKey($"eliminar_{idModulo}");

                var existente = existentes.FirstOrDefault(p => p.IdModulo == idModulo);
                if (existente == null)
                {
                    _context.EmpresaRolPermisos.Add(new EmpresaRolPermiso
                    {
                        IdRol       = rolId,
                        IdModulo    = idModulo,
                        PuedeVer      = ver,
                        PuedeCrear    = crear,
                        PuedeEditar   = editar,
                        PuedeEliminar = eliminar
                    });
                }
                else
                {
                    existente.PuedeVer      = ver;
                    existente.PuedeCrear    = crear;
                    existente.PuedeEditar   = editar;
                    existente.PuedeEliminar = eliminar;
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Permisos guardados correctamente.";
            return RedirectToPage(new { rolId });
        }

        private async Task<IActionResult> CargarAsync()
        {
            Rol = await _context.EmpresaRoles.FindAsync(RolId);
            if (Rol == null) return NotFound();

            Empresa = await _context.Empresas.FindAsync(Rol.IdEmpresa);

            var modulosEmpresa = await _context.EmpresaModulos
                .Where(em => em.IdEmpresa == Rol.IdEmpresa && em.Activo)
                .Include(em => em.Modulo)
                .Where(em => em.Modulo.Activo)
                .Select(em => em.Modulo)
                .OrderBy(m => m.Orden)
                .ToListAsync();

            var permisos = await _context.EmpresaRolPermisos
                .Where(p => p.IdRol == RolId)
                .ToListAsync();

            Items = modulosEmpresa.Select(m =>
            {
                var p = permisos.FirstOrDefault(x => x.IdModulo == m.IdModulo);
                return new PermisoItem
                {
                    Modulo        = m,
                    PuedeVer      = p?.PuedeVer      ?? false,
                    PuedeCrear    = p?.PuedeCrear    ?? false,
                    PuedeEditar   = p?.PuedeEditar   ?? false,
                    PuedeEliminar = p?.PuedeEliminar ?? false
                };
            }).ToList();

            return Page();
        }
    }
}
