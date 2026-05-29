using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Cliente Cliente { get; set; } = new Cliente
        {
            Activo = true,
            Tipo = "natural",
            MonedaIsoDefault = "HNL"
        };

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeCrear(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            await CargarListasAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeCrear(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            // Validar unicidad de código dentro del tenant
            var existeCodigo = await _db.Clientes
                .AnyAsync(c => c.IdEmpresa == idEmpresa && !c.Eliminado && c.Codigo == Cliente.Codigo);

            if (existeCodigo)
                ModelState.AddModelError("Cliente.Codigo", "Ya existe un cliente con este código en la empresa.");

            if (!ModelState.IsValid)
            {
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            Cliente.IdEmpresa = idEmpresa;
            Cliente.MonedaIsoDefault = (Cliente.MonedaIsoDefault ?? "HNL").ToUpperInvariant();
            Cliente.Eliminado = false;
            Cliente.FechaEliminado = null;
            Cliente.CreadoPor = user;
            Cliente.FechaCreacion = now;
            Cliente.ModificadoPor = null;
            Cliente.FechaModificacion = null;

            _db.Clientes.Add(Cliente);
            await _db.SaveChangesAsync();

            TempData["ClientesMessage"] = "Cliente creado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarListasAsync(int idEmpresa)
        {
            var monedas = await _db.Monedas
                .Where(m => m.Activo)
                .OrderBy(m => m.CodigoIso)
                .Select(m => new SelectListItem { Value = m.CodigoIso, Text = $"{m.CodigoIso} - {m.Nombre}" })
                .ToListAsync();
            ViewData["Monedas"] = new SelectList(monedas, "Value", "Text");

            var condiciones = await _db.CondicionesPago
                .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                .OrderBy(c => c.Nombre)
                .Select(c => new SelectListItem { Value = c.IdCondicionPago.ToString(), Text = c.Nombre })
                .ToListAsync();
            ViewData["CondicionesPago"] = new SelectList(condiciones, "Value", "Text");
        }
    }
}
