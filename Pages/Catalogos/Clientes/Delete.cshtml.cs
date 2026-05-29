using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DeleteModel(ApplicationDbContext db) => _db = db;

        public Cliente? Cliente { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEliminar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            Cliente = await _db.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (Cliente == null) return RedirectToPage("Index");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEliminar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            var cliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (cliente == null) return RedirectToPage("Index");

            cliente.Eliminado          = true;
            cliente.FechaEliminado     = DateTime.UtcNow;
            cliente.Activo             = false;
            cliente.ModificadoPor      = HttpContext.Session.GetString("Username") ?? "system";
            cliente.FechaModificacion  = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["ClientesMessage"] = "Cliente eliminado correctamente.";
            return RedirectToPage("Index");
        }
    }
}
