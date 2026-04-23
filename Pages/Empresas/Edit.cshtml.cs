using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EditModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
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
            CargarPaises();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            if (!ModelState.IsValid)
            {
                CargarPaises();
                return Page();
            }

            var existing = await _context.Empresas.FirstOrDefaultAsync(e => e.IdEmpresa == Empresa.IdEmpresa && !e.Eliminado);
            if (existing == null)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            existing.Codigo = Empresa.Codigo;
            existing.RazonSocial = Empresa.RazonSocial;
            existing.NombreComercial = Empresa.NombreComercial;
            existing.IdentificadorFiscal = Empresa.IdentificadorFiscal;
            existing.PaisIso = Empresa.PaisIso.ToUpperInvariant();
            existing.MonedaIso = Empresa.MonedaIso.ToUpperInvariant();
            existing.ZonaHoraria = Empresa.ZonaHoraria;

            if (existing.Activa != Empresa.Activa)
            {
                existing.Activa = Empresa.Activa;
                existing.FechaBaja = Empresa.Activa ? null : now;
            }

            existing.ModificadoPor = user;
            existing.FechaModificacion = now;

            await _context.SaveChangesAsync();

            TempData["EmpresasMessage"] = "Empresa actualizada correctamente.";
            return RedirectToPage("Index");
        }

        private void CargarPaises()
        {
            var paises = _context.Paises
                .Where(p => p.Activo)
                .OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.CodigoIso, Text = $"{p.CodigoIso} - {p.Nombre}" })
                .ToList();
            ViewData["Paises"] = new SelectList(paises, "Value", "Text");

            var monedas = _context.Monedas
                .Where(m => m.Activo)
                .OrderBy(m => m.Nombre)
                .Select(m => new SelectListItem { Value = m.CodigoIso, Text = $"{m.CodigoIso} - {m.Nombre} ({m.Simbolo})" })
                .ToList();
            ViewData["Monedas"] = new SelectList(monedas, "Value", "Text");
        }
    }
}
