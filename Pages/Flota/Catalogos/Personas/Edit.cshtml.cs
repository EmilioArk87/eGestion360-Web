using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public EditModel(ApplicationDbContext context) => _context = context;

        [BindProperty]
        public Persona Persona { get; set; } = null!;

        public List<Cargo> Cargos { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            Persona = await _context.Personas.FirstOrDefaultAsync(p => p.IdPersona == id && !p.Eliminado)
                      ?? throw new InvalidOperationException();

            await CargarCargosAsync(Persona.IdEmpresa);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            if (!ModelState.IsValid)
            {
                await CargarCargosAsync(Persona.IdEmpresa);
                return Page();
            }

            var existente = await _context.Personas.FindAsync(Persona.IdPersona);
            if (existente == null) return NotFound();

            existente.Nombres           = Persona.Nombres.Trim();
            existente.Apellidos         = Persona.Apellidos.Trim();
            existente.TipoDocumento     = Persona.TipoDocumento;
            existente.Documento         = Persona.Documento.Trim();
            existente.Cargo             = Persona.Cargo;
            existente.Telefono          = Persona.Telefono?.Trim();
            existente.Email             = Persona.Email?.Trim();
            existente.FechaIngreso      = Persona.FechaIngreso;
            existente.FechaBaja         = Persona.FechaBaja;
            existente.TarifaDiaria      = Persona.TarifaDiaria;
            existente.MonedaTarifa      = Persona.MonedaTarifa;
            existente.Activo            = Persona.Activo;
            existente.ModificadoPor     = HttpContext.Session.GetString("Username");
            existente.FechaModificacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = $"{existente.NombreCompleto} actualizado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarCargosAsync(int idEmpresa)
        {
            Cargos = await _context.Cargos
                .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                .OrderBy(c => c.Nombre)
                .ToListAsync();
        }
    }
}
