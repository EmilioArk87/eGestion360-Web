using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IVinculoService _vinculos;

        public DeleteModel(ApplicationDbContext db, IVinculoService vinculos)
        {
            _db = db;
            _vinculos = vinculos;
        }

        public Cliente? Cliente { get; set; }

        /// <summary>El cliente está enlazado a una persona: eliminarlo también cierra su relación como cliente.</summary>
        public bool Enlazado => Cliente?.IdPersonaEmpresa != null;

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

            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            // Un cliente enlazado a una persona deja de serlo: se cierra su vínculo antes de eliminarlo. La persona y sus
            // otros roles (por ejemplo, empleado) no se tocan. Si ya estaba dado de baja, no hay nada que cerrar.
            if (cliente.IdPersonaEmpresa != null)
            {
                var baja = await _vinculos.TerminarClienteAsync(new TerminarClienteInput
                {
                    IdEmpresa = idEmpresa, IdCliente = id, Motivo = "Eliminado desde Clientes", Usuario = usuario
                });

                if (!baja.Ok && !baja.SinCambios)
                {
                    TempData["ClientesError"] = baja.Mensaje;
                    return RedirectToPage("Index");
                }
            }

            cliente.Eliminado          = true;
            cliente.FechaEliminado     = DateTime.UtcNow;
            cliente.Activo             = false;
            cliente.ModificadoPor      = usuario;
            cliente.FechaModificacion  = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["ClientesMessage"] = "Cliente eliminado correctamente.";
            return RedirectToPage("Index");
        }
    }
}
