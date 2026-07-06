using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public CreateModel(ApplicationDbContext context) => _context = context;

        [BindProperty]
        public Persona Persona { get; set; } = new()
        {
            TipoDocumento = "DNI",
            Cargo         = "CONDUCTOR",
            MonedaTarifa  = "HNL",
            Activo        = true
        };

        public List<Cargo> Cargos { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarCargosAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            if (!ModelState.IsValid)
            {
                await CargarCargosAsync();
                return Page();
            }

            var empresaId = AuthHelper.IsAdmin(HttpContext)
                ? 1
                : (AuthHelper.GetEmpresaId(HttpContext) ?? 1);

            Persona.IdEmpresa      = empresaId;
            Persona.CreadoPor      = HttpContext.Session.GetString("Username") ?? "sistema";
            Persona.FechaCreacion  = DateTime.UtcNow;

            _context.Personas.Add(Persona);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = $"{Persona.NombreCompleto} registrado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarCargosAsync()
        {
            var empresaId = AuthHelper.IsAdmin(HttpContext)
                ? 1
                : (AuthHelper.GetEmpresaId(HttpContext) ?? 1);

            Cargos = await _context.Cargos
                .Where(c => c.IdEmpresa == empresaId && c.Activo && !c.Eliminado)
                .OrderBy(c => c.Nombre)
                .ToListAsync();
        }
    }
}
