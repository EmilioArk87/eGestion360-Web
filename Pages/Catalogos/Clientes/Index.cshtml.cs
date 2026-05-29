using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<Cliente> Clientes { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos"))
                return RedirectToPage("/MainMenu");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            var query = _db.Clientes
                .Include(c => c.CondicionPagoDefault)
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                query = query.Where(c =>
                    c.Codigo.ToLower().Contains(s) ||
                    c.RazonSocial.ToLower().Contains(s) ||
                    (c.NombreComercial != null && c.NombreComercial.ToLower().Contains(s)) ||
                    (c.IdentificadorFiscal != null && c.IdentificadorFiscal.ToLower().Contains(s)));
            }

            Clientes = await query.OrderBy(c => c.RazonSocial).ToListAsync();
            return Page();
        }
    }
}
