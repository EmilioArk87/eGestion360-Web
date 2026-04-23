using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Gastos.Seguros
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<PolizaSeguro> Polizas { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            int id = GetIdEmpresa();
            Polizas = await _db.PolizasSeguros
                .Include(p => p.Vehiculo)
                .Where(p => p.IdEmpresa == id)
                .OrderByDescending(p => p.FechaFin).ThenBy(p => p.Vehiculo!.Placa)
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
