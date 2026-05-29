using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Facturacion;

namespace eGestion360Web.Pages.Facturacion.Notas
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly INotaService _notas;
        public CreateModel(ApplicationDbContext db, INotaService notas)
        {
            _db = db;
            _notas = notas;
        }

        [BindProperty(SupportsGet = true)] public string Tipo { get; set; } = "credito";
        [BindProperty] public NotaInput Input { get; set; } = new();

        public class NotaInput
        {
            public int IdFacturaOrigen { get; set; }
            public DateTime FechaEmision { get; set; } = DateTime.Today;
            public decimal Monto { get; set; }
            public string Motivo { get; set; } = string.Empty;
            public string? Observaciones { get; set; }
            public string Serie { get; set; } = "NC-01";
        }

        public List<FacturaOption> FacturasDisponibles { get; private set; } = new();
        public class FacturaOption
        {
            public int Id { get; set; }
            public string Display { get; set; } = "";
            public decimal Saldo { get; set; }
            public decimal Total { get; set; }
            public string Moneda { get; set; } = "";
            public string Cliente { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeCrear(HttpContext, "facturacion"))
                return RedirectToPage("/MainMenu");

            Tipo = (Tipo ?? "credito").ToLowerInvariant();
            if (Tipo != "credito" && Tipo != "debito") Tipo = "credito";
            Input.Serie = Tipo == "credito" ? "NC-01" : "ND-01";

            await CargarAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion") || !AuthHelper.PuedeCrear(HttpContext, "facturacion"))
                return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            Tipo = (Tipo ?? "credito").ToLowerInvariant();
            if (Tipo != "credito" && Tipo != "debito") Tipo = "credito";

            var r = await _notas.EmitirAsync(new EmitirNotaInput
            {
                IdEmpresa       = idEmpresa,
                Tipo            = Tipo,
                Serie           = Input.Serie,
                IdFacturaOrigen = Input.IdFacturaOrigen,
                FechaEmision    = Input.FechaEmision,
                Monto           = Input.Monto,
                Motivo          = Input.Motivo,
                Observaciones   = Input.Observaciones,
                Usuario         = usuario
            });

            if (!r.Ok)
            {
                foreach (var e in r.Errores) ModelState.AddModelError(string.Empty, e);
                await CargarAsync(idEmpresa);
                return Page();
            }

            TempData["NotasMessage"] = $"Nota de {Tipo} {r.Serie}-{r.Numero:D6} emitida.";
            return RedirectToPage("Index", new { tipo = Tipo });
        }

        private async Task CargarAsync(int idEmpresa)
        {
            // Para NC mostramos facturas con saldo > 0; para ND también permitimos.
            FacturasDisponibles = await _db.Facturas.AsNoTracking()
                .Include(f => f.Cliente)
                .Where(f => f.IdEmpresa == idEmpresa
                         && !f.Eliminado
                         && f.Estado != "anulada"
                         && (Tipo == "debito" || f.SaldoPendiente > 0))
                .OrderByDescending(f => f.FechaEmision)
                .Take(500)
                .Select(f => new FacturaOption
                {
                    Id      = f.IdFactura,
                    Display = f.Serie + "-" + (f.Numero ?? 0).ToString("D6") + " · " + f.Cliente.RazonSocial,
                    Saldo   = f.SaldoPendiente,
                    Total   = f.Total,
                    Moneda  = f.Moneda,
                    Cliente = f.Cliente.RazonSocial
                })
                .ToListAsync();
        }
    }
}
