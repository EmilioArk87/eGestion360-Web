using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Empresa Empresa { get; set; } = new Empresa
        {
            Activa = true,
            PaisIso = "PY",
            MonedaIso = "PYG",
            ZonaHoraria = "America/Guatemala"
        };

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

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

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            Empresa.PaisIso = Empresa.PaisIso.ToUpperInvariant();
            Empresa.MonedaIso = Empresa.MonedaIso.ToUpperInvariant();
            Empresa.Eliminado = false;
            Empresa.FechaEliminado = null;
            Empresa.FechaBaja = Empresa.Activa ? null : now;
            Empresa.FechaActivacion = now;
            Empresa.CreadoPor = user;
            Empresa.FechaCreacion = now;
            Empresa.ModificadoPor = null;
            Empresa.FechaModificacion = null;

            _context.Empresas.Add(Empresa);
            await _context.SaveChangesAsync();

            TempData["EmpresasMessage"] = "Empresa creada correctamente.";
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
