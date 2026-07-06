using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.TiposVehiculo
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<TipoVehiculo> Tipos { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = GetIdEmpresa();

            var query = _db.TiposVehiculo
                .Where(t => t.IdEmpresa == idEmpresa && !t.Eliminado)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(t =>
                    t.Codigo.ToLower().Contains(s) ||
                    t.Nombre.ToLower().Contains(s) ||
                    (t.Descripcion != null && t.Descripcion.ToLower().Contains(s)));
            }

            Tipos = await query.OrderBy(t => t.Nombre).ToListAsync();
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
