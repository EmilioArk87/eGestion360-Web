using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion.Pagos
{
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPagoService _pagos;
        public DetailsModel(ApplicationDbContext db, IPagoService pagos)
        {
            _db = db;
            _pagos = pagos;
        }

        public Pago? Pago { get; set; }
        public List<(PagoAplicacion Apl, Factura Fac)> Aplicaciones { get; set; } = new();

        [BindProperty] public string? MotivoAnulacion { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Pago = await _db.Pagos.AsNoTracking()
                .Include(p => p.Cliente)
                .Include(p => p.FormaPago)
                .FirstOrDefaultAsync(p => p.IdPago == id && p.IdEmpresa == idEmpresa && !p.Eliminado);

            if (Pago == null) return RedirectToPage("Index");

            var apl = await _db.PagoAplicaciones.AsNoTracking()
                .Where(a => a.IdPago == id)
                .ToListAsync();
            var ids = apl.Select(a => a.IdFactura).ToList();
            var facs = await _db.Facturas.AsNoTracking()
                .Where(f => ids.Contains(f.IdFactura))
                .ToDictionaryAsync(f => f.IdFactura);
            Aplicaciones = apl.Select(a => (a, facs[a.IdFactura])).ToList();
            return Page();
        }

        public async Task<IActionResult> OnPostAnularAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeEliminar(HttpContext, "facturacion"))
                return RedirectToPage("Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (string.IsNullOrWhiteSpace(MotivoAnulacion) || MotivoAnulacion.Trim().Length < 5)
            {
                TempData["PagosError"] = "El motivo de anulación debe tener al menos 5 caracteres.";
                return RedirectToPage("Details", new { id });
            }

            var r = await _pagos.AnularPagoAsync(new AnularPagoInput
            {
                IdEmpresa = idEmpresa,
                IdPago    = id,
                Motivo    = MotivoAnulacion,
                Usuario   = HttpContext.Session.GetString("Username") ?? "system"
            });

            if (!r.Ok) TempData["PagosError"] = string.Join("; ", r.Errores);
            else TempData["PagosMessage"] = "Pago anulado y aplicaciones revertidas.";

            return RedirectToPage("Details", new { id });
        }
    }
}
