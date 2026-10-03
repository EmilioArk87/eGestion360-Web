using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Personas
{
    public class HistorialModel : PersonaAdminPaginaBase
    {
        private readonly IPersonaAdminConsultaService _consulta;

        public HistorialModel(IPersonaAdminConsultaService consulta, IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _consulta = consulta;
        }

        public int IdPersona { get; private set; }
        public HistorialPersona? Historial { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar();
            if (bloqueo != null) return bloqueo;

            IdPersona = id;
            Historial = await _consulta.HistorialAsync(id, ct: ct);
            if (Historial == null) return NotFound();

            return Page();
        }
    }
}
