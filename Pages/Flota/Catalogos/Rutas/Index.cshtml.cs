using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Rutas
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<Ruta> Lista { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = GetIdEmpresa();

            var query = _db.Rutas
                .Where(r => r.IdEmpresa == idEmpresa && !r.Eliminado)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(r =>
                    r.Codigo.ToLower().Contains(s) ||
                    r.Nombre.ToLower().Contains(s) ||
                    (r.Descripcion != null && r.Descripcion.ToLower().Contains(s)));
            }

            Lista = await query.OrderBy(r => r.Nombre).ToListAsync();
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
