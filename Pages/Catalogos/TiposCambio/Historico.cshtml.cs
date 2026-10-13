using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.TasasCambio;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>
    /// Histórico de tasas (script 020): las oficiales y las propias de la empresa de la sesión, con filtros y
    /// paginación. Se abre desde el botón "Histórico" de Tipos de cambio. Misma regla de acceso que esa pantalla
    /// (sesión y módulo "catalogos"); la empresa sale SIEMPRE de la sesión, nunca de la URL.
    /// </summary>
    public class HistoricoModel : TasasCambioPaginaBase
    {
        public const int TamanoPagina = 25;

        private readonly ITasaCambioConsultaService _consultas;
        private readonly ILogger<HistoricoModel> _log;

        public HistoricoModel(ITasaCambioConsultaService consultas, IOptions<TasasCambioOptions> opciones, TimeProvider reloj,
            ILogger<HistoricoModel> log) : base(opciones, reloj)
        {
            _consultas = consultas;
            _log = log;
        }

        [BindProperty(SupportsGet = true)] public string? Moneda { get; set; }
        [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public PaginaTasas Historico { get; private set; } = HistoricoVacio(1);

        public bool HayFiltros => Moneda != null || Tipo != null || Estado != null || Desde != null || Hasta != null;

        public async Task<IActionResult> OnGetAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            NormalizarFiltros();
            var empresa = IdEmpresaSesion;

            try
            {
                Historico = await _consultas.HistoricoAsync(Filtro(empresa), Ct);
                if (Historico.Filas.Count == 0 && Historico.Total > 0 && Pagina > 1)
                {
                    Pagina = Math.Max(1, Historico.TotalPaginas);
                    Historico = await _consultas.HistoricoAsync(Filtro(empresa), Ct);
                }
            }
            catch (Exception ex) when (IndexModel.EsErrorDeBaseDeDatos(ex))
            {
                AvisarErrorBaseDatos(ex, _log, "el histórico");
                Historico = HistoricoVacio(Pagina);
            }

            return Page();
        }

        private FiltroHistoricoTasas Filtro(int? empresa) => new()
        {
            IdEmpresa = empresa,
            IncluirOficiales = true,
            MonedaOrigen = Moneda,
            TipoTasa = Tipo,
            Estado = Estado,
            Desde = Desde,
            Hasta = Hasta,
            Pagina = Pagina,
            TamanoPagina = TamanoPagina
        };

        private void NormalizarFiltros()
        {
            Moneda = Elegir(Moneda, Monedas);
            Tipo = Elegir(Tipo, Cat.TipoTasa.Todos);
            Estado = Elegir(Estado, Cat.EstadoTasa.Todos);
            if (Desde is { } d && Hasta is { } h && d > h) (Desde, Hasta) = (h, d);
            if (Pagina < 1) Pagina = 1;
        }

        private static PaginaTasas HistoricoVacio(int pagina) =>
            new(Array.Empty<TasaHistorialFila>(), 0, Math.Max(1, pagina), TamanoPagina);
    }
}
