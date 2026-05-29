using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Cliente Cliente { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEditar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            var cliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (cliente == null) return RedirectToPage("Index");

            Cliente = cliente;
            await CargarListasAsync(idEmpresa);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEditar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            var dbCliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (dbCliente == null) return RedirectToPage("Index");

            // Validar unicidad de código (excluyendo el propio registro)
            var existeCodigo = await _db.Clientes
                .AnyAsync(c => c.IdEmpresa == idEmpresa && !c.Eliminado
                            && c.IdCliente != id && c.Codigo == Cliente.Codigo);

            if (existeCodigo)
                ModelState.AddModelError("Cliente.Codigo", "Ya existe otro cliente con este código en la empresa.");

            if (!ModelState.IsValid)
            {
                Cliente.IdCliente = id;
                Cliente.IdEmpresa = idEmpresa;
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            dbCliente.Codigo                = Cliente.Codigo;
            dbCliente.RazonSocial           = Cliente.RazonSocial;
            dbCliente.NombreComercial       = Cliente.NombreComercial;
            dbCliente.Tipo                  = Cliente.Tipo;
            dbCliente.IdentificadorFiscal   = Cliente.IdentificadorFiscal;
            dbCliente.Email                 = Cliente.Email;
            dbCliente.Telefono              = Cliente.Telefono;
            dbCliente.Direccion             = Cliente.Direccion;
            dbCliente.Ciudad                = Cliente.Ciudad;
            dbCliente.MonedaIsoDefault      = (Cliente.MonedaIsoDefault ?? "HNL").ToUpperInvariant();
            dbCliente.IdCondicionPagoDefault= Cliente.IdCondicionPagoDefault;
            dbCliente.LimiteCredito         = Cliente.LimiteCredito;
            dbCliente.Activo                = Cliente.Activo;
            dbCliente.ModificadoPor         = HttpContext.Session.GetString("Username") ?? "system";
            dbCliente.FechaModificacion     = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "El registro fue modificado por otro usuario. Recargue e intente de nuevo.");
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            TempData["ClientesMessage"] = "Cliente actualizado correctamente.";
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
