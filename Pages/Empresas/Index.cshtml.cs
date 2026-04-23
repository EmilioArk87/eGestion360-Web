using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<Empresa> Empresas { get; set; } = new List<Empresa>();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            IQueryable<Empresa> query = _context.Empresas.AsNoTracking().Where(e => !e.Eliminado);

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var term = Search.Trim();
                query = query.Where(e =>
                    e.Codigo.Contains(term) ||
                    e.RazonSocial.Contains(term) ||
                    (e.NombreComercial != null && e.NombreComercial.Contains(term)) ||
                    (e.IdentificadorFiscal != null && e.IdentificadorFiscal.Contains(term)));
            }

            Empresas = await query.OrderBy(e => e.RazonSocial).ToListAsync();
            return Page();
        }
    }
}
