using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Vehiculos
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public DeleteModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Vehiculo Vehiculo { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var vehiculo = await _db.Vehiculos
                .Include(v => v.TipoVehiculo)
                .FirstOrDefaultAsync(v => v.IdVehiculo == id);

            if (vehiculo is null || vehiculo.IdEmpresa != GetIdEmpresa())
                return NotFound();

            Vehiculo = vehiculo;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var vehiculo = await _db.Vehiculos.FindAsync(Vehiculo.IdVehiculo);
            if (vehiculo is null || vehiculo.IdEmpresa != GetIdEmpresa())
                return NotFound();

            // Eliminación lógica (soft delete)
            vehiculo.Eliminado = true;
            vehiculo.FechaEliminado = DateTime.UtcNow;
            vehiculo.Activo = false;
            vehiculo.ModificadoPor = HttpContext.Session.GetString("Username") ?? "sistema";
            vehiculo.FechaModificacion = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Mensaje"] = $"Vehículo {vehiculo.Placa} eliminado correctamente.";
            return RedirectToPage("Index");
        }

        private int GetIdEmpresa()
        {
            var sessionVal = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(sessionVal, out int id) && id > 0) return id;
            return 1;
        }
    }
}
