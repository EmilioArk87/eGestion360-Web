using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Operacion.SalariosDiarios
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        public List<SalarioDiario> Registros { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            Desde ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            Hasta ??= DateOnly.FromDateTime(DateTime.Today);

            int idEmpresa = GetIdEmpresa();
            Registros = await _db.SalariosDiarios
                .Include(s => s.Vehiculo)
                .Include(s => s.Persona)
                .Where(s => s.IdEmpresa == idEmpresa && s.Fecha >= Desde && s.Fecha <= Hasta)
                .OrderByDescending(s => s.Fecha).ThenBy(s => s.Vehiculo!.Placa)
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
