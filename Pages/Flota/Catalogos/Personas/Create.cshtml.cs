using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            if (!ModelState.IsValid) return Page();

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
    }
}
