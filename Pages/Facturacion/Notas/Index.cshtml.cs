using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Facturacion.Notas
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<Nota> Notas { get; set; } = new();
        [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            IQueryable<Nota> q = _db.Notas.AsNoTracking()
                .Include(n => n.Cliente)
                .Include(n => n.FacturaOrigen)
                .Where(n => n.IdEmpresa == idEmpresa && !n.Eliminado);

            if (!string.IsNullOrWhiteSpace(Tipo)) q = q.Where(n => n.Tipo == Tipo);

            Notas = await q.OrderByDescending(n => n.FechaEmision).ThenByDescending(n => n.IdNota).Take(200).ToListAsync();
            return Page();
        }
    }
}
