using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Plataforma;

namespace eGestion360Web.Pages.Admin.Suscriptores
{
    /// <summary>
    /// Un suscriptor: suspender, reactivar o cancelar (D80) y cambiar su plan por uno que ya existe (D81), siempre con motivo y escribiendo su
    /// código para confirmar. El cambio de plan se revisa antes: qué se gana, qué se pierde y qué topes quedarían superados. Las reglas están en
    /// <see cref="ServicioDeSuscriptores"/>; la auditoría la deja el interceptor en la misma transacción.
    /// </summary>
    [RequierePermiso("plataforma.suscriptores.ver")]
    public class DetalleModel : PageModel
    {
        private readonly IServicioDeSuscriptores _suscriptores;

        public DetalleModel(IServicioDeSuscriptores suscriptores) => _suscriptores = suscriptores;

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        /// <summary>La versión de plan que se está revisando (llega por la dirección al pulsar «Revisar cambio»).</summary>
        [BindProperty(SupportsGet = true, Name = "plan")]
        public int? PlanARevisar { get; set; }

        public DetalleDeSuscriptor Detalle { get; private set; } = null!;

        public CambioPrevisto? Previsto { get; private set; }

        // Lo escrito en los formularios, para no perderlo si el cambio se rechaza.
        public AccionDeEstado? AccionElegida { get; private set; }
        public string? ModoElegido { get; private set; }
        public string? MotivoDeEstado { get; private set; }
        public string? MotivoDePlan { get; private set; }

        public string? ErrorDeEstado { get; private set; }
        public string? ErrorDePlan { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            return await CargarAsync(ct);
        }

        [RequierePermiso("plataforma.suscriptores.estado")]
        public async Task<IActionResult> OnPostEstadoAsync(AccionDeEstado accion, string? modo, string? motivo, string? confirmacion, string? token,
            CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var resultado = await _suscriptores.CambiarEstadoAsync(new CambioDeEstado(Id, accion, modo, motivo, confirmacion, token), Usuario(), ct);
            if (resultado.Ok)
            {
                TempData["SuscriptoresMensaje"] = "Estado cambiado. Se nota en menos de un minuto.";
                return RedirectToPage(new { id = Id });
            }

            (AccionElegida, ModoElegido, MotivoDeEstado, ErrorDeEstado) = (accion, modo, motivo, resultado.Error);
            PlanARevisar = null;
            return await CargarAsync(ct);
        }

        [RequierePermiso("plataforma.suscriptores.plan")]
        public async Task<IActionResult> OnPostPlanAsync(int idPlanVersion, int idPlanVersionActual, string? motivo, string? confirmacion,
            CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var resultado = await _suscriptores.CambiarPlanAsync(new CambioDePlan(Id, idPlanVersion, idPlanVersionActual, motivo, confirmacion), Usuario(), ct);
            if (resultado.Ok)
            {
                TempData["SuscriptoresMensaje"] = "Plan cambiado. Se nota en menos de un minuto.";
                return RedirectToPage(new { id = Id });
            }

            (MotivoDePlan, ErrorDePlan, PlanARevisar) = (motivo, resultado.Error, idPlanVersion);
            return await CargarAsync(ct);
        }

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "operador";

        private async Task<IActionResult> CargarAsync(CancellationToken ct)
        {
            var detalle = await _suscriptores.ObtenerAsync(Id, ct);
            if (detalle == null) return NotFound();

            Detalle = detalle;
            if (PlanARevisar is { } idPlan && idPlan != detalle.PlanActual?.IdPlanVersion)
                Previsto = await _suscriptores.PrevisualizarPlanAsync(Id, idPlan, ct);
            return Page();
        }
    }
}
