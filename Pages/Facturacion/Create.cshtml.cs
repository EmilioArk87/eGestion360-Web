using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IFacturacionService _facturas;
        public CreateModel(ApplicationDbContext db, IFacturacionService facturas)
        {
            _db = db;
            _facturas = facturas;
        }

        [BindProperty] public FacturaInput Input { get; set; } = new();

        public class FacturaInput
        {
            public int IdCliente { get; set; }
            public string TipoVenta { get; set; } = "contado";
            public DateTime FechaEmision { get; set; } = DateTime.Today;
            public int? IdFormaPago { get; set; }
            public int? IdCondicionPago { get; set; }
            public string Moneda { get; set; } = "HNL";
            public decimal TipoCambio { get; set; } = 1m;
            public string Serie { get; set; } = "F-01";
            public string? Observaciones { get; set; }
            public decimal DescuentoGlobal { get; set; }
            public decimal Retencion { get; set; }
            public List<LineaInput> Lineas { get; set; } = new();
        }

        public class LineaInput
        {
            public int? IdProducto { get; set; }
            public string Descripcion { get; set; } = string.Empty;
            public decimal Cantidad { get; set; }
            public decimal PrecioUnitario { get; set; }
            public decimal DescuentoPorc { get; set; }
            public int? IdImpuesto { get; set; }
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

            // Limpiar líneas vacías (las que el usuario añadió y dejó sin tocar)
            Input.Lineas = (Input.Lineas ?? new List<LineaInput>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Descripcion) || l.Cantidad > 0 || l.PrecioUnitario > 0)
                .ToList();

            var serviceInput = new EmitirFacturaInput
            {
                IdEmpresa       = idEmpresa,
                IdCliente       = Input.IdCliente,
                TipoVenta       = Input.TipoVenta,
                FechaEmision    = Input.FechaEmision,
                IdFormaPago     = Input.IdFormaPago,
                IdCondicionPago = Input.IdCondicionPago,
                Moneda          = Input.Moneda,
                TipoCambio      = Input.TipoCambio,
                Serie           = Input.Serie,
                Observaciones   = Input.Observaciones,
                DescuentoGlobal = Input.DescuentoGlobal,
                Retencion       = Input.Retencion,
                Usuario         = usuario,
                Lineas          = Input.Lineas.Select(l => new EmitirFacturaLinea
                {
                    IdProducto     = l.IdProducto,
                    Descripcion    = l.Descripcion,
                    Cantidad       = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    DescuentoPorc  = l.DescuentoPorc,
                    IdImpuesto     = l.IdImpuesto
                }).ToList()
            };

            var result = await _facturas.EmitirAsync(serviceInput);

            if (!result.Ok)
            {
                foreach (var err in result.Errores) ModelState.AddModelError(string.Empty, err);
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            TempData["FacturasMessage"] = $"Factura {result.Serie}-{result.Numero:D6} emitida correctamente.";
            return RedirectToPage("Details", new { id = result.IdFactura });
        }

        // ── Helpers de UI ────────────────────────────────────────────────

        public class ProductoOption
        {
            public int Id { get; set; }
            public string Codigo { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public decimal Precio { get; set; }
            public int? IdImpuestoDefault { get; set; }
        }

        public List<ProductoOption> Productos { get; private set; } = new();

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

            ViewData["CondicionesPago"] = new SelectList(
                await _db.CondicionesPago
                    .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                    .OrderBy(c => c.Nombre)
                    .Select(c => new { c.IdCondicionPago, c.Nombre })
                    .ToListAsync(),
                "IdCondicionPago", "Nombre");

            ViewData["Impuestos"] = new SelectList(
                await _db.Impuestos
                    .Where(i => i.IdEmpresa == idEmpresa && i.Activo && !i.Eliminado && !i.EsRetencion)
                    .OrderBy(i => i.Tasa)
                    .Select(i => new { i.IdImpuesto, Display = i.Nombre + " (" + i.Tasa + "%)" })
                    .ToListAsync(),
                "IdImpuesto", "Display");

            Productos = await _db.ProductosServicios
                .Where(p => p.IdEmpresa == idEmpresa && p.Activo && !p.Eliminado)
                .OrderBy(p => p.Codigo)
                .Select(p => new ProductoOption
                {
                    Id = p.IdProducto,
                    Codigo = p.Codigo,
                    Descripcion = p.Descripcion,
                    Precio = p.PrecioDefault,
                    IdImpuestoDefault = p.IdImpuestoDefault
                })
                .ToListAsync();
        }
    }
}
