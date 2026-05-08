using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)] public string? Search       { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroCargo  { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroEstado { get; set; }

        public List<Persona> Personas { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var empresaId = AuthHelper.IsAdmin(HttpContext)
                ? (int?)null
                : AuthHelper.GetEmpresaId(HttpContext);

            var q = _context.Personas.Where(p => !p.Eliminado).AsQueryable();

            if (empresaId.HasValue)
                q = q.Where(p => p.IdEmpresa == empresaId.Value);

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLower();
                q = q.Where(p => p.Nombres.ToLower().Contains(s)
                               || p.Apellidos.ToLower().Contains(s)
                               || p.Documento.ToLower().Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(FiltroCargo))
                q = q.Where(p => p.Cargo == FiltroCargo);

            if (!string.IsNullOrWhiteSpace(FiltroEstado))
            {
                bool activo = FiltroEstado == "1";
                q = q.Where(p => p.Activo == activo);
            }

            Personas = await q.OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres).ToListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostToggleAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var persona = await _context.Personas.FindAsync(id);
            if (persona != null)
            {
                persona.Activo           = activo;
                persona.ModificadoPor    = HttpContext.Session.GetString("Username");
                persona.FechaModificacion = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = $"{persona.NombreCompleto} {(activo ? "activado" : "desactivado")} correctamente.";
            }
            return RedirectToPage(new { Search, FiltroCargo, FiltroEstado });
        }
    }
}
