using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Operacion.Peajes
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty] public Peaje Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var peaje = await _db.Peajes.FindAsync(id);
            if (peaje is null || peaje.IdEmpresa != GetIdEmpresa() || peaje.Eliminado)
                return NotFound();

            Item = peaje;
            await CargarSelectsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            ModelState.Remove("Item.CreadoPor");
            if (!ModelState.IsValid) { await CargarSelectsAsync(); return Page(); }

            var existente = await _db.Peajes.FindAsync(Item.IdPeaje);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa() || existente.Eliminado)
                return NotFound();

            existente.IdVehiculo        = Item.IdVehiculo;
            existente.Fecha             = Item.Fecha;
            existente.Hora              = Item.Hora;
            existente.IdRuta            = Item.IdRuta;
            existente.IdConductor       = Item.IdConductor;
            existente.NombreCaseta      = Item.NombreCaseta?.Trim();
            existente.Monto             = Item.Monto;
            existente.Moneda            = Item.Moneda;
            existente.KmOdometro        = Item.KmOdometro;
            existente.NoComprobante     = Item.NoComprobante?.Trim();
            existente.Observaciones     = Item.Observaciones?.Trim();
            existente.ModificadoPor     = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Peaje del {existente.Fecha:dd/MM/yyyy} actualizado correctamente.";
                return RedirectToPage("Index");
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty,
                    "El registro fue modificado por otro usuario. Recargue la página e intente de nuevo.");
                await CargarSelectsAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var peaje = await _db.Peajes.FindAsync(id);
            if (peaje is null || peaje.IdEmpresa != GetIdEmpresa() || peaje.Eliminado)
                return NotFound();

            peaje.Eliminado        = true;
            peaje.FechaEliminado   = DateTime.UtcNow;
            peaje.ModificadoPor    = HttpContext.Session.GetString("Username") ?? "sistema";
            peaje.FechaModificacion = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Mensaje"] = "Peaje eliminado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarSelectsAsync()
        {
            int idEmpresa = GetIdEmpresa();
            ViewData["Vehiculos"] = new SelectList(
                await _db.Vehiculos.Where(v => v.IdEmpresa == idEmpresa && v.Activo).OrderBy(v => v.Placa).ToListAsync(),
                "IdVehiculo", "Placa");
            ViewData["Rutas"] = new SelectList(
                await _db.Rutas.Where(r => r.IdEmpresa == idEmpresa && r.Activo).OrderBy(r => r.Nombre).ToListAsync(),
                "IdRuta", "Nombre");
            ViewData["Conductores"] = new SelectList(
                await _db.Personas.Where(p => p.IdEmpresa == idEmpresa && p.Activo && p.Cargo == "CONDUCTOR").OrderBy(p => p.Apellidos).ToListAsync(),
                "IdPersona", "NombreCompleto");
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
