using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Gastos.Repuestos
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        public List<GastoRepuesto> Registros { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            Desde ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-90));
            Hasta ??= DateOnly.FromDateTime(DateTime.Today);
            int id = GetIdEmpresa();
            Registros = await _db.GastosRepuesto
                .Include(g => g.Vehiculo).Include(g => g.CategoriaRepuesto)
                .Where(g => g.IdEmpresa == id && g.Fecha >= Desde && g.Fecha <= Hasta)
                .OrderByDescending(g => g.Fecha).ToListAsync();
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
