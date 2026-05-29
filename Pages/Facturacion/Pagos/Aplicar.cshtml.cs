using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion.Pagos
{
    public class AplicarModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPagoService _pagos;
        public AplicarModel(ApplicationDbContext db, IPagoService pagos)
        {
            _db = db;
            _pagos = pagos;
        }

        public Pago? Pago { get; set; }
        public List<FacturaPendiente> FacturasPendientes { get; set; } = new();

        public class FacturaPendiente
        {
            public int IdFactura { get; set; }
            public string Doc { get; set; } = "";
            public DateTime FechaEmision { get; set; }
            public DateTime? FechaVencimiento { get; set; }
            public decimal Total { get; set; }
            public decimal Saldo { get; set; }
            public string Moneda { get; set; } = "";
        }

        [BindProperty] public List<AplicacionItem> Aplicaciones { get; set; } = new();
        public class AplicacionItem
        {
            public int IdFactura { get; set; }
            public decimal Monto { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!await CargarAsync(id)) return RedirectToPage("Index");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeEditar(HttpContext, "facturacion"))
                return RedirectToPage("Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            var apl = (Aplicaciones ?? new())
                .Where(a => a.IdFactura > 0 && a.Monto > 0)
                .Select(a => new AplicacionInput { IdFactura = a.IdFactura, Monto = a.Monto })
                .ToList();

            if (apl.Count == 0)
            {
                TempData["PagosError"] = "Indique al menos una aplicación con monto > 0.";
                return RedirectToPage("Aplicar", new { id });
            }

            var r = await _pagos.AplicarSaldoAFavorAsync(new AplicarPagoInput
            {
                IdEmpresa = idEmpresa, IdPago = id, Aplicaciones = apl, Usuario = usuario
            });

            if (!r.Ok)
            {
                foreach (var e in r.Errores) ModelState.AddModelError(string.Empty, e);
                await CargarAsync(id);
                return Page();
            }

            TempData["PagosMessage"] = $"Saldo aplicado. Restante: {r.SaldoFavorRestante:N2}.";
            return RedirectToPage("Details", new { id });
        }

        private async Task<bool> CargarAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return false;
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return false;
            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            Pago = await _db.Pagos.AsNoTracking()
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.IdPago == id && p.IdEmpresa == idEmpresa && !p.Eliminado);

            if (Pago == null || Pago.Estado == "anulado" || Pago.SaldoFavor <= 0) return false;

            FacturasPendientes = await _db.Facturas.AsNoTracking()
                .Where(f => f.IdEmpresa == idEmpresa
                         && f.IdCliente == Pago.IdCliente
                         && !f.Eliminado
                         && (f.Estado == "emitida" || f.Estado == "parcialmente_pagada")
                         && f.SaldoPendiente > 0
                         && f.Moneda == Pago.Moneda)
                .OrderBy(f => f.FechaEmision)
                .Select(f => new FacturaPendiente
                {
                    IdFactura = f.IdFactura,
                    Doc       = f.Serie + "-" + (f.Numero ?? 0).ToString("D6"),
                    FechaEmision = f.FechaEmision,
                    FechaVencimiento = f.FechaVencimiento,
                    Total = f.Total,
                    Saldo = f.SaldoPendiente,
                    Moneda = f.Moneda
                })
                .ToListAsync();
            return true;
        }
    }
}
