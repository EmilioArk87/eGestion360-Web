using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Facturacion
{
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DetailsModel(ApplicationDbContext db) => _db = db;

        public Factura? Factura { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, "facturacion")) return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            Factura = await _db.Facturas
                .AsNoTracking()
                .Include(f => f.Cliente)
                .Include(f => f.FormaPago)
                .Include(f => f.CondicionPago)
                .Include(f => f.Detalle).ThenInclude(d => d.Producto)
                .Include(f => f.Detalle).ThenInclude(d => d.Impuesto)
                .FirstOrDefaultAsync(f => f.IdFactura == id && f.IdEmpresa == idEmpresa && !f.Eliminado);

            if (Factura == null) return RedirectToPage("Index");
            return Page();
        }
    }
}
