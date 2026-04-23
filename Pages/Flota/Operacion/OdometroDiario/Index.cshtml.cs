using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Operacion.OdometroDiario
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        public List<Models.Flota.OdometroDiario> Registros { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            Desde ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            Hasta ??= DateOnly.FromDateTime(DateTime.Today);

            int idEmpresa = GetIdEmpresa();
            Registros = await _db.OdometrosDiarios
                .Include(o => o.Vehiculo)
                .Include(o => o.Ruta)
                .Where(o => o.IdEmpresa == idEmpresa && o.Fecha >= Desde && o.Fecha <= Hasta)
                .OrderByDescending(o => o.Fecha).ThenBy(o => o.Vehiculo!.Placa)
                .ToListAsync();

            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
