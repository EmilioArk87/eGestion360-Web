using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Vehiculos
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<Vehiculo> Vehiculos { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = GetIdEmpresa();

            var query = _db.Vehiculos
                .Include(v => v.TipoVehiculo)
                .Include(v => v.Ruta)
                .Where(v => v.IdEmpresa == idEmpresa)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(v =>
                    v.Placa.ToLower().Contains(s) ||
                    (v.NumeroInterno != null && v.NumeroInterno.ToLower().Contains(s)) ||
                    (v.Marca != null && v.Marca.ToLower().Contains(s)) ||
                    (v.Modelo != null && v.Modelo.ToLower().Contains(s)));
            }

            Vehiculos = await query
                .OrderBy(v => v.Placa)
                .ToListAsync();

            return Page();
        }

        private int GetIdEmpresa()
        {
            var sessionVal = HttpContext.Session.GetString("EmpresaId");
            if (int.TryParse(sessionVal, out int id) && id > 0) return id;
            return 1;
        }
    }
}
