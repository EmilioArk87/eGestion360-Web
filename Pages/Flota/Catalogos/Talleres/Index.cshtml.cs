using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Talleres
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<Taller> Lista { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = GetIdEmpresa();

            var query = _db.Talleres
                .Where(t => t.IdEmpresa == idEmpresa && !t.Eliminado)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(t =>
                    t.Codigo.ToLower().Contains(s) ||
                    t.Nombre.ToLower().Contains(s) ||
                    (t.Rtn != null && t.Rtn.ToLower().Contains(s)) ||
                    (t.Contacto != null && t.Contacto.ToLower().Contains(s)) ||
                    (t.Telefono != null && t.Telefono.ToLower().Contains(s)));
            }

            Lista = await query.OrderBy(t => t.Nombre).ToListAsync();
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
