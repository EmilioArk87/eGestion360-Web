using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Operacion.ControlSalidas
{
    public class HistorialModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public HistorialModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)] public DateTime? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Hasta { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdVehiculo { get; set; }
        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }

        public List<ControlSalida> Registros { get; set; } = new();
        public SelectList VehiculosList { get; set; } = null!;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            int idEmpresa = GetIdEmpresa();

            Desde ??= DateTime.Today.AddDays(-15);
            Hasta ??= DateTime.Today;

            var vehiculos = await _db.Vehiculos
                .Where(v => v.IdEmpresa == idEmpresa && v.Activo && !v.Eliminado)
                .OrderBy(v => v.Placa)
                .ToListAsync();

            VehiculosList = new SelectList(vehiculos, "IdVehiculo", "Placa", IdVehiculo);

            var finHasta = Hasta.Value.Date.AddDays(1);
            var query = _db.ControlSalidas
                .Include(c => c.Vehiculo)
                .Include(c => c.Conductor)
                .Include(c => c.Ruta)
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado && c.FechaHoraSalida >= Desde && c.FechaHoraSalida < finHasta);

            if (IdVehiculo.HasValue && IdVehiculo.Value > 0)
            {
                query = query.Where(c => c.IdVehiculo == IdVehiculo.Value);
            }

            if (!string.IsNullOrWhiteSpace(Estado) && Estado != "TODOS")
            {
                query = query.Where(c => c.Estado == Estado);
            }

            Registros = await query.OrderByDescending(c => c.FechaHoraSalida).ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostAnularAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            int idEmpresa = GetIdEmpresa();

            var reg = await _db.ControlSalidas
                .FirstOrDefaultAsync(c => c.IdControlSalida == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (reg == null)
            {
                TempData["Error"] = "Registro no encontrado.";
                return RedirectToPage();
            }

            reg.Estado = "ANULADO";
            reg.ModificadoPor = HttpContext.Session.GetString("Username") ?? "sistema";
            reg.FechaModificacion = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            TempData["Exito"] = "Movimiento anulado correctamente.";
            return RedirectToPage();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
