using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Facturacion.Pagos
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<Pago> Pagos { get; set; } = new();
        public Dictionary<string, int> ConteoPorEstado { get; set; } = new();

        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Hasta { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var baseQ = _db.Pagos.Where(p => p.IdEmpresa == idEmpresa && !p.Eliminado);

            ConteoPorEstado = await baseQ.GroupBy(p => p.Estado)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            IQueryable<Pago> q = baseQ
                .Include(p => p.Cliente)
                .Include(p => p.FormaPago)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Estado)) q = q.Where(p => p.Estado == Estado);
            if (Desde.HasValue) q = q.Where(p => p.Fecha >= Desde.Value);
            if (Hasta.HasValue) q = q.Where(p => p.Fecha <= Hasta.Value);

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                q = q.Where(p =>
                    (p.Serie + "-" + p.Numero).ToLower().Contains(s) ||
                    p.Cliente.RazonSocial.ToLower().Contains(s) ||
                    p.Cliente.Codigo.ToLower().Contains(s) ||
                    (p.Referencia != null && p.Referencia.ToLower().Contains(s)));
            }

            Pagos = await q.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.IdPago).Take(200).ToListAsync();
            return Page();
        }
    }
}
