using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DetailsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Empresa Empresa { get; set; } = new Empresa();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            var empresa = await _context.Empresas.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdEmpresa == id && !e.Eliminado);
            if (empresa == null)
            {
                return NotFound();
            }

            Empresa = empresa;
            return Page();
        }
    }
}
