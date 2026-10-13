using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>
    /// Bitácora del job de tasas de cambio (script 020): las últimas ejecuciones con su detalle por moneda. Se abre
    /// desde el botón "Bitácora del job" de Tipos de cambio. Quien no es administrador del sistema recibe las
    /// ejecuciones SIN endpoint ni detalle del error: no basta con no pintarlos en la vista.
    /// </summary>
    public class BitacoraModel : TasasCambioPaginaBase
    {
        public const int MaxEjecuciones = 20;

        private readonly ITasaCambioConsultaService _consultas;
        private readonly ILogger<BitacoraModel> _log;

        public BitacoraModel(ITasaCambioConsultaService consultas, IOptions<TasasCambioOptions> opciones, TimeProvider reloj,
            ILogger<BitacoraModel> log) : base(opciones, reloj)
        {
            _consultas = consultas;
            _log = log;
        }

        public IReadOnlyList<EjecucionTasasResumen> Ejecuciones { get; private set; } = Array.Empty<EjecucionTasasResumen>();

        public async Task<IActionResult> OnGetAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            try
            {
                var ejecuciones = await _consultas.UltimasEjecucionesAsync(MaxEjecuciones, Ct);
                Ejecuciones = EsAdminSistema
                    ? ejecuciones
                    : ejecuciones.Select(e => e with { Endpoint = null, DetalleError = null }).ToList();
            }
            catch (Exception ex) when (IndexModel.EsErrorDeBaseDeDatos(ex))
            {
                AvisarErrorBaseDatos(ex, _log, "la bitácora");
                Ejecuciones = Array.Empty<EjecucionTasasResumen>();
            }

            return Page();
        }
    }
}
