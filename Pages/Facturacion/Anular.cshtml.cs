using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion
{
    public class AnularModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IFacturacionService _facturas;

        public AnularModel(ApplicationDbContext db, IFacturacionService facturas)
        {
            _db = db;
            _facturas = facturas;
        }

        public Factura? Factura { get; set; }

        [BindProperty] public string Motivo { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeEliminar(HttpContext, "facturacion"))
                return RedirectToPage("Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Factura = await _db.Facturas.AsNoTracking()
                .Include(f => f.Cliente)
                .FirstOrDefaultAsync(f => f.IdFactura == id && f.IdEmpresa == idEmpresa && !f.Eliminado);

            if (Factura == null || Factura.Estado != "emitida") return RedirectToPage("Index");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeEliminar(HttpContext, "facturacion"))
                return RedirectToPage("Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            if (string.IsNullOrWhiteSpace(Motivo) || Motivo.Trim().Length < 5)
                ModelState.AddModelError(nameof(Motivo), "El motivo debe tener al menos 5 caracteres.");

            if (!ModelState.IsValid)
            {
                Factura = await _db.Facturas.AsNoTracking()
                    .Include(f => f.Cliente)
                    .FirstOrDefaultAsync(f => f.IdFactura == id && f.IdEmpresa == idEmpresa && !f.Eliminado);
                return Page();
            }

            var result = await _facturas.AnularAsync(new AnularFacturaInput
            {
                IdEmpresa = idEmpresa,
                IdFactura = id,
                Motivo    = Motivo,
                Usuario   = usuario
            });

            if (!result.Ok)
            {
                foreach (var e in result.Errores) ModelState.AddModelError(string.Empty, e);
                Factura = await _db.Facturas.AsNoTracking()
                    .Include(f => f.Cliente)
                    .FirstOrDefaultAsync(f => f.IdFactura == id && f.IdEmpresa == idEmpresa && !f.Eliminado);
                return Page();
            }

            TempData["FacturasMessage"] = "Factura anulada correctamente.";
            return RedirectToPage("Details", new { id });
        }
    }
}
