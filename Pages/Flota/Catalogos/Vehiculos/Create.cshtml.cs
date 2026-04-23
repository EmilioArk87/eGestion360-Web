using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Vehiculos
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Vehiculo Vehiculo { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            await CargarSelectsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Vehiculo.TokenConcurrencia");

            if (!ModelState.IsValid)
            {
                await CargarSelectsAsync();
                return Page();
            }

            var usuario = HttpContext.Session.GetString("Username") ?? "sistema";
            int idEmpresa = GetIdEmpresa();

            Vehiculo.IdEmpresa = idEmpresa;
            Vehiculo.Placa = Vehiculo.Placa.Trim().ToUpper();
            Vehiculo.Vin = Vehiculo.Vin?.Trim().ToUpper();
            Vehiculo.CreadoPor = usuario;
            Vehiculo.FechaCreacion = DateTime.UtcNow;

            _db.Vehiculos.Add(Vehiculo);
            await _db.SaveChangesAsync();

            TempData["Mensaje"] = $"Vehículo {Vehiculo.Placa} registrado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarSelectsAsync()
        {
            int idEmpresa = GetIdEmpresa();

            ViewData["TiposVehiculo"] = new SelectList(
                await _db.TiposVehiculo
                    .Where(t => t.IdEmpresa == idEmpresa && t.Activo)
                    .OrderBy(t => t.Nombre)
                    .ToListAsync(),
                "IdTipoVehiculo", "Nombre");

            ViewData["Rutas"] = new SelectList(
                await _db.Rutas
                    .Where(r => r.IdEmpresa == idEmpresa && r.Activo)
                    .OrderBy(r => r.Nombre)
                    .ToListAsync(),
                "IdRuta", "Nombre");
        }

        private int GetIdEmpresa()
        {
            var sessionVal = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(sessionVal, out int id) && id > 0) return id;
            return 1;
        }
    }
}
