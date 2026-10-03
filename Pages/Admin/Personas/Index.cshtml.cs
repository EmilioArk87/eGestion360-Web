using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Personas
{
    public class IndexModel : PersonaAdminPaginaBase
    {
        public const int TamanoPagina = 25;

        private readonly IPersonaAdminConsultaService _consulta;

        public IndexModel(IPersonaAdminConsultaService consulta, IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _consulta = consulta;
        }

        [BindProperty(SupportsGet = true)] public string? Texto { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdEmpresa { get; set; }
        [BindProperty(SupportsGet = true)] public string? Rol { get; set; }
        [BindProperty(SupportsGet = true)] public EstadoVinculoFiltro Estado { get; set; }
        [BindProperty(SupportsGet = true)] public FiltroPerfilPersona Perfil { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public PersonaAdminPagina Resultado { get; private set; } = new(Array.Empty<PersonaAdminFila>(), 0, 1, TamanoPagina);
        public IReadOnlyList<OpcionCatalogo> Empresas { get; private set; } = Array.Empty<OpcionCatalogo>();

        public bool HayFiltros =>
            !string.IsNullOrWhiteSpace(Texto) || IdEmpresa != null || !string.IsNullOrWhiteSpace(Rol)
            || Estado != EstadoVinculoFiltro.Todos || Perfil != FiltroPerfilPersona.Todos;

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var bloqueo = Entrar();
            if (bloqueo != null) return bloqueo;

            Empresas = await _consulta.EmpresasAsync(ct);

            var filtro = new PersonaAdminFiltro
            {
                Texto = Texto, IdEmpresa = IdEmpresa, TipoVinculo = Rol, Estado = Estado, Perfil = Perfil
            };
            Resultado = await _consulta.ListarAsync(filtro, Pagina, TamanoPagina, ct);

            // Una página que ya no existe (se borró un filtro, o la dirección está vieja) lleva a la última.
            if (Resultado.Filas.Count == 0 && Resultado.Total > 0 && Resultado.TotalPaginas > 0)
            {
                Pagina = Resultado.TotalPaginas;
                Resultado = await _consulta.ListarAsync(filtro, Pagina, TamanoPagina, ct);
            }

            return Page();
        }
    }
}
