using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class IndexModel : PersonaPaginaBase
    {
        private readonly IPersonaConsultaService _consulta;
        private readonly IPersonaService _personas;

        public IndexModel(
            IPersonaConsultaService consulta, IPersonaService personas,
            IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _consulta = consulta;
            _personas = personas;
        }

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroCargo { get; set; }
        [BindProperty(SupportsGet = true)] public string? FiltroEstado { get; set; }

        /// <summary>"incompleto" o "nombres": ver <see cref="FiltroPerfilPersona"/>.</summary>
        [BindProperty(SupportsGet = true)] public string? FiltroPerfil { get; set; }

        public IReadOnlyList<PersonaFila> Personas { get; private set; } = Array.Empty<PersonaFila>();
        public IReadOnlyList<OpcionCatalogo> Cargos { get; private set; } = Array.Empty<OpcionCatalogo>();

        /// <summary>Personas con el nombre por revisar (sin importar los filtros), para el aviso de arriba.</summary>
        public int NombresPorRevisar { get; private set; }

        public bool PuedeCrear => AuthHelper.PuedeCrear(HttpContext, "flota");
        public bool PuedeEditar => AuthHelper.PuedeEditar(HttpContext, "flota");

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Ver);
            if (bloqueo != null) return bloqueo;

            var filtro = new PersonaFiltro
            {
                Texto = Search,
                Cargo = FiltroCargo,
                Activo = FiltroEstado switch { "1" => true, "0" => false, _ => null },
                Perfil = FiltroPerfil switch
                {
                    "incompleto" => FiltroPerfilPersona.Incompleto,
                    "nombres" => FiltroPerfilPersona.NombresPorRevisar,
                    _ => FiltroPerfilPersona.Todos
                }
            };

            Personas = await _consulta.ListarAsync(IdEmpresa, filtro, ct);
            Cargos = await _consulta.CargosAsync(IdEmpresa, ct);
            NombresPorRevisar = (await _consulta.ListarAsync(
                IdEmpresa, new PersonaFiltro { Perfil = FiltroPerfilPersona.NombresPorRevisar }, ct)).Count;

            return Page();
        }

        public async Task<IActionResult> OnPostToggleAsync(int id, bool activo, CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Editar);
            if (bloqueo != null) return bloqueo;

            var resultado = await _personas.CambiarEstadoAsync(new CambiarEstadoPersonaInput
            {
                IdEmpresa = IdEmpresa, IdPersona = id, Activo = activo, Usuario = Usuario
            }, ct);

            if (!resultado.Encontrada)
                TempData["Error"] = "No se encontró a la persona.";
            else if (!resultado.Ok)
                TempData["Error"] = "No se pudo cambiar el estado. Intenta de nuevo.";
            else
                TempData["Mensaje"] = $"{resultado.NombreCompleto} {(activo ? "activado" : "desactivado")} correctamente.";

            return RedirectToPage(new { Search, FiltroCargo, FiltroEstado, FiltroPerfil });
        }
    }
}
