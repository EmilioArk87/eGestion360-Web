using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class HistorialModel : PersonaPaginaBase
    {
        private readonly IPersonaConsultaService _consulta;

        public HistorialModel(IPersonaConsultaService consulta, IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _consulta = consulta;
        }

        public int IdPersona { get; private set; }
        public HistorialPersona? Historial { get; private set; }

        public bool PuedeEditar => AuthHelper.PuedeEditar(HttpContext, "flota");

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Ver);
            if (bloqueo != null) return bloqueo;

            IdPersona = id;
            Historial = await _consulta.HistorialAsync(IdEmpresa, id, ct: ct);
            if (Historial == null) return NotFound();

            return Page();
        }
    }
}
