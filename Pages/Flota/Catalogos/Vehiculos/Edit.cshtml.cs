using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Vehiculos
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Vehiculo Vehiculo { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var vehiculo = await _db.Vehiculos.FindAsync(id);
            if (vehiculo is null || vehiculo.IdEmpresa != GetIdEmpresa())
                return NotFound();

            Vehiculo = vehiculo;
            await CargarSelectsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Vehiculo.CreadoPor");

            if (!ModelState.IsValid)
            {
                await CargarSelectsAsync();
                return Page();
            }

            var existente = await _db.Vehiculos.FindAsync(Vehiculo.IdVehiculo);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa())
                return NotFound();

            existente.IdTipoVehiculo  = Vehiculo.IdTipoVehiculo;
            existente.IdRuta          = Vehiculo.IdRuta;
            existente.Placa           = Vehiculo.Placa.Trim().ToUpper();
            existente.NumeroInterno   = Vehiculo.NumeroInterno?.Trim();
            existente.Marca           = Vehiculo.Marca?.Trim();
            existente.Modelo          = Vehiculo.Modelo?.Trim();
            existente.Anio            = Vehiculo.Anio;
            existente.Vin             = Vehiculo.Vin?.Trim().ToUpper();
            existente.Color           = Vehiculo.Color?.Trim();
            existente.Capacidad       = Vehiculo.Capacidad;
            existente.TipoCombustible = Vehiculo.TipoCombustible;
            existente.KmInicial       = Vehiculo.KmInicial;
            existente.FechaAlta       = Vehiculo.FechaAlta;
            existente.Activo          = Vehiculo.Activo;
            existente.ModificadoPor   = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Vehículo {existente.Placa} actualizado correctamente.";
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

        private async Task CargarSelectsAsync()
        {
            int idEmpresa = GetIdEmpresa();

            ViewData["TiposVehiculo"] = new SelectList(
                await _db.TiposVehiculo
                    .Where(t => t.IdEmpresa == idEmpresa && t.Activo)
                    .OrderBy(t => t.Nombre)
                    .ToListAsync(),
                "IdTipoVehiculo", "Nombre", Vehiculo.IdTipoVehiculo);

            ViewData["Rutas"] = new SelectList(
                await _db.Rutas
                    .Where(r => r.IdEmpresa == idEmpresa && r.Activo)
                    .OrderBy(r => r.Nombre)
                    .ToListAsync(),
                "IdRuta", "Nombre", Vehiculo.IdRuta);
        }

        private int GetIdEmpresa()
        {
            var sessionVal = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(sessionVal, out int id) && id > 0) return id;
            return 1;
        }
    }
}
