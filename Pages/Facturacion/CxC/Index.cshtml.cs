using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Facturacion.CxC
{
    /// <summary>
    /// Estado de cuenta y antigüedad de saldos por cliente.
    /// Muestra facturas con saldo pendiente agrupadas por rango de días vencidos.
    /// </summary>
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<EstadoCuentaCliente> Estado { get; set; } = new();
        public Totales TotalGeneral { get; set; } = new();

        [BindProperty(SupportsGet = true)] public int? IdCliente { get; set; }
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }

        public class Totales
        {
            public decimal Vigente { get; set; }
            public decimal V1_30   { get; set; }
            public decimal V31_60  { get; set; }
            public decimal V61_90  { get; set; }
            public decimal V91Plus { get; set; }
            public decimal SaldoFavor { get; set; }
            public decimal Total =>  Vigente + V1_30 + V31_60 + V61_90 + V91Plus;
            public decimal Neto  =>  Total - SaldoFavor;
        }

        public class EstadoCuentaCliente
        {
            public int IdCliente { get; set; }
            public string Codigo { get; set; } = "";
            public string RazonSocial { get; set; } = "";
            public string? Rtn { get; set; }
            public Totales Totales { get; set; } = new();
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var hoy = DateTime.Today;

            // Facturas con saldo pendiente (CxC)
            var facturasQ = _db.Facturas.AsNoTracking()
                .Where(f => f.IdEmpresa == idEmpresa
                         && !f.Eliminado
                         && f.TipoVenta == "credito"
                         && (f.Estado == "emitida" || f.Estado == "parcialmente_pagada")
                         && f.SaldoPendiente > 0);

            if (IdCliente.HasValue) facturasQ = facturasQ.Where(f => f.IdCliente == IdCliente.Value);

            var facturas = await facturasQ
                .Select(f => new { f.IdCliente, f.FechaVencimiento, f.FechaEmision, f.SaldoPendiente })
                .ToListAsync();

            // Saldos a favor (anticipos)
            var anticipos = await _db.Pagos.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && !p.Eliminado && p.SaldoFavor > 0 && p.Estado != "anulado")
                .GroupBy(p => p.IdCliente)
                .Select(g => new { IdCliente = g.Key, Saldo = g.Sum(x => x.SaldoFavor) })
                .ToDictionaryAsync(x => x.IdCliente, x => x.Saldo);

            // Clientes referenciados (más filtro opcional)
            var idsCli = facturas.Select(f => f.IdCliente).Concat(anticipos.Keys).Distinct().ToList();
            var clientesQ = _db.Clientes.AsNoTracking()
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado && idsCli.Contains(c.IdCliente));
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                clientesQ = clientesQ.Where(c =>
                    c.RazonSocial.ToLower().Contains(s) ||
                    c.Codigo.ToLower().Contains(s) ||
                    (c.IdentificadorFiscal != null && c.IdentificadorFiscal.ToLower().Contains(s)));
            }
            var clientes = await clientesQ.ToListAsync();

            foreach (var c in clientes.OrderBy(c => c.RazonSocial))
            {
                var ec = new EstadoCuentaCliente
                {
                    IdCliente = c.IdCliente, Codigo = c.Codigo, RazonSocial = c.RazonSocial, Rtn = c.IdentificadorFiscal
                };

                foreach (var f in facturas.Where(x => x.IdCliente == c.IdCliente))
                {
                    var vence = f.FechaVencimiento ?? f.FechaEmision;
                    var dias = (hoy - vence.Date).Days;
                    if (dias <= 0)            ec.Totales.Vigente  += f.SaldoPendiente;
                    else if (dias <= 30)      ec.Totales.V1_30    += f.SaldoPendiente;
                    else if (dias <= 60)      ec.Totales.V31_60   += f.SaldoPendiente;
                    else if (dias <= 90)      ec.Totales.V61_90   += f.SaldoPendiente;
                    else                      ec.Totales.V91Plus  += f.SaldoPendiente;
                }
                if (anticipos.TryGetValue(c.IdCliente, out var sf)) ec.Totales.SaldoFavor = sf;

                // Solo incluir si tiene algo en CxC o saldo a favor
                if (ec.Totales.Total > 0 || ec.Totales.SaldoFavor > 0) Estado.Add(ec);
            }

            // Totales generales
            TotalGeneral.Vigente    = Estado.Sum(e => e.Totales.Vigente);
            TotalGeneral.V1_30      = Estado.Sum(e => e.Totales.V1_30);
            TotalGeneral.V31_60     = Estado.Sum(e => e.Totales.V31_60);
            TotalGeneral.V61_90     = Estado.Sum(e => e.Totales.V61_90);
            TotalGeneral.V91Plus    = Estado.Sum(e => e.Totales.V91Plus);
            TotalGeneral.SaldoFavor = Estado.Sum(e => e.Totales.SaldoFavor);

            return Page();
        }
    }
}
