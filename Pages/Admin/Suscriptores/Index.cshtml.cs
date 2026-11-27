using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Plataforma;

namespace eGestion360Web.Pages.Admin.Suscriptores
{
    /// <summary>
    /// Los suscriptores de la plataforma (en el código, tenants) con su estado, su plan y cuánto usan de sus topes (F1.9a, ADR-032).
    /// Solo del operador: la ruta corre en ámbito de plataforma.
    /// </summary>
    [RequierePermiso("plataforma.suscriptores.ver")]
    public class IndexModel : PageModel
    {
        private readonly IServicioDeSuscriptores _suscriptores;

        public IndexModel(IServicioDeSuscriptores suscriptores) => _suscriptores = suscriptores;

        public IReadOnlyList<ResumenDeSuscriptor> Suscriptores { get; private set; } = Array.Empty<ResumenDeSuscriptor>();

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            Suscriptores = await _suscriptores.ListarAsync(ct);
            return Page();
        }
    }
}
