using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion.Pagos
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPagoService _pagos;
        public CreateModel(ApplicationDbContext db, IPagoService pagos)
        {
            _db = db;
            _pagos = pagos;
        }

        [BindProperty] public PagoInput Input { get; set; } = new();

        public class PagoInput
        {
            public int IdCliente { get; set; }
            public DateTime Fecha { get; set; } = DateTime.Today;
            public int IdFormaPago { get; set; }
            public decimal Monto { get; set; }
            public string Moneda { get; set; } = "HNL";
            public decimal TipoCambio { get; set; } = 1m;
            public string Serie { get; set; } = "RC-01";
            public string? Referencia { get; set; }
            public string? Observaciones { get; set; }
            public List<AplicacionItem> Aplicaciones { get; set; } = new();
        }

        public class AplicacionItem
        {
            public int IdFactura { get; set; }
            public decimal Monto { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeCrear(HttpContext, "facturacion"))
                return RedirectToPage("/MainMenu");

            await CargarListasAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeCrear(HttpContext, "facturacion"))
                return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            var aplicaciones = (Input.Aplicaciones ?? new())
                .Where(a => a.IdFactura > 0 && a.Monto > 0)
                .Select(a => new AplicacionInput { IdFactura = a.IdFactura, Monto = a.Monto })
                .ToList();

            var result = await _pagos.RegistrarPagoAsync(new RegistrarPagoInput
            {
                IdEmpresa     = idEmpresa,
                IdCliente     = Input.IdCliente,
                Fecha         = Input.Fecha,
                IdFormaPago   = Input.IdFormaPago,
                Monto         = Input.Monto,
                Moneda        = Input.Moneda,
                TipoCambio    = Input.TipoCambio,
                Serie         = Input.Serie,
                Referencia    = Input.Referencia,
                Observaciones = Input.Observaciones,
                Aplicaciones  = aplicaciones,
                Usuario       = usuario
            });

            if (!result.Ok)
            {
                foreach (var e in result.Errores) ModelState.AddModelError(string.Empty, e);
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            TempData["PagosMessage"] = $"Pago {result.Serie}-{result.Numero:D6} registrado. Saldo a favor: {result.SaldoFavor:N2}.";
            return RedirectToPage("Details", new { id = result.IdPago });
        }

        // Endpoint AJAX: facturas pendientes del cliente seleccionado
        public async Task<IActionResult> OnGetFacturasPendientesAsync(int idCliente)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return new JsonResult(Array.Empty<object>());
            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            var facturas = await _db.Facturas
                .AsNoTracking()
                .Where(f => f.IdEmpresa == idEmpresa
                         && f.IdCliente == idCliente
                         && !f.Eliminado
                         && (f.Estado == "emitida" || f.Estado == "parcialmente_pagada")
                         && f.SaldoPendiente > 0
                         && f.TipoVenta == "credito")
                .OrderBy(f => f.FechaEmision)
                .Select(f => new
                {
                    id     = f.IdFactura,
                    doc    = f.Serie + "-" + (f.Numero ?? 0).ToString("D6"),
                    fecha  = f.FechaEmision,
                    vence  = f.FechaVencimiento,
                    total  = f.Total,
                    saldo  = f.SaldoPendiente,
                    moneda = f.Moneda,
                    estado = f.Estado
                })
                .ToListAsync();

            return new JsonResult(facturas);
        }

        private async Task CargarListasAsync(int idEmpresa)
        {
            ViewData["Clientes"] = new SelectList(
                await _db.Clientes
                    .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                    .OrderBy(c => c.RazonSocial)
                    .Select(c => new { c.IdCliente, Display = c.RazonSocial + " (" + c.Codigo + ")" })
                    .ToListAsync(),
                "IdCliente", "Display");

            ViewData["FormasPago"] = new SelectList(
                await _db.FormasPago
                    .Where(f => f.IdEmpresa == idEmpresa && f.Activo && !f.Eliminado)
                    .OrderBy(f => f.Nombre)
                    .Select(f => new { f.IdFormaPago, f.Nombre })
                    .ToListAsync(),
                "IdFormaPago", "Nombre");
        }
    }
}
