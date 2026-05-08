using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.EmpresaModulos
{
    public class ModuloItem
    {
        public Modulo Modulo { get; set; } = null!;
        public bool Activo { get; set; }
    }

    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)]
        public int EmpresaId { get; set; }

        public Empresa? Empresa { get; set; }
        public List<ModuloItem> Items { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            return await CargarAsync();
        }

        public async Task<IActionResult> OnPostAsync(int empresaId, List<int> modulosSeleccionados)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            EmpresaId = empresaId;

            var existentes = await _context.EmpresaModulos
                .Where(em => em.IdEmpresa == empresaId)
                .ToListAsync();

            var todosModulos = await _context.Modulos.Where(m => m.Activo).Select(m => m.IdModulo).ToListAsync();

            foreach (var idModulo in todosModulos)
            {
                var existente = existentes.FirstOrDefault(em => em.IdModulo == idModulo);
                var seleccionado = modulosSeleccionados.Contains(idModulo);

                if (seleccionado && existente == null)
                {
                    _context.EmpresaModulos.Add(new EmpresaModulo
                    {
                        IdEmpresa = empresaId,
                        IdModulo = idModulo,
                        FechaActivacion = DateTime.UtcNow,
                        Activo = true
                    });
                }
                else if (existente != null)
                {
                    existente.Activo = seleccionado;
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Módulos actualizados correctamente.";
            return RedirectToPage(new { empresaId });
        }

        private async Task<IActionResult> CargarAsync()
        {
            Empresa = await _context.Empresas.FindAsync(EmpresaId);
            if (Empresa == null) return NotFound();

            var modulos = await _context.Modulos.Where(m => m.Activo).OrderBy(m => m.Orden).ToListAsync();
            var activos = await _context.EmpresaModulos
                .Where(em => em.IdEmpresa == EmpresaId && em.Activo)
                .Select(em => em.IdModulo)
                .ToHashSetAsync();

            Items = modulos.Select(m => new ModuloItem { Modulo = m, Activo = activos.Contains(m.IdModulo) }).ToList();
            return Page();
        }
    }
}
