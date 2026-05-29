using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Facturacion
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<Factura> Facturas { get; set; } = new();
        public Dictionary<string, int> ConteoPorEstado { get; set; } = new();

        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
        [BindProperty(SupportsGet = true)] public string? TipoVenta { get; set; }
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? Hasta { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var baseQ = _db.Facturas.Where(f => f.IdEmpresa == idEmpresa && !f.Eliminado);

            ConteoPorEstado = await baseQ
                .GroupBy(f => f.Estado)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            IQueryable<Factura> q = baseQ.Include(f => f.Cliente).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Estado))    q = q.Where(f => f.Estado == Estado);
            if (!string.IsNullOrWhiteSpace(TipoVenta)) q = q.Where(f => f.TipoVenta == TipoVenta);
            if (Desde.HasValue) q = q.Where(f => f.FechaEmision >= Desde.Value);
            if (Hasta.HasValue) q = q.Where(f => f.FechaEmision <= Hasta.Value);

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                q = q.Where(f =>
                    (f.Numero.HasValue && (f.Serie + "-" + f.Numero).ToLower().Contains(s)) ||
                    f.Cliente.RazonSocial.ToLower().Contains(s) ||
                    f.Cliente.Codigo.ToLower().Contains(s) ||
                    (f.Cliente.IdentificadorFiscal != null && f.Cliente.IdentificadorFiscal.ToLower().Contains(s)));
            }

            Facturas = await q
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.IdFactura)
                .Take(200)
                .ToListAsync();

            return Page();
        }
    }
}
