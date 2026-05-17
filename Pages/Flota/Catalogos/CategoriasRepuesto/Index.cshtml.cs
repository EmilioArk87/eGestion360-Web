using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.CategoriasRepuesto
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<CategoriaRepuesto> Categorias { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = GetIdEmpresa();

            var query = _db.CategoriasRepuesto
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(c =>
                    c.Nombre.ToLower().Contains(s) ||
                    (c.Descripcion != null && c.Descripcion.ToLower().Contains(s)));
            }

            Categorias = await query.OrderBy(c => c.Nombre).ToListAsync();
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
