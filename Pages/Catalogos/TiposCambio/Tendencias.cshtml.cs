using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>
    /// Tendencias: la compra y la venta de una moneda a lo largo del tiempo (script 020), con una gráfica de líneas,
    /// contadores y la tabla de los mismos puntos. Se abre desde el botón "Tendencias" de Tipos de cambio. Misma regla de
    /// acceso que esa pantalla (sesión y módulo "catalogos"); la empresa sale SIEMPRE de la sesión, nunca de la URL.
    /// Todo se elige con enlaces (moneda, serie, período y fecha): no necesita JavaScript.
    /// </summary>
    public class TendenciasModel : TasasCambioPaginaBase
    {
        public const string RangoSieteDias = "7";
        public const string RangoTreintaDias = "30";
        public const string RangoNoventaDias = "90";
        public const string RangoTodo = "TODO";
        public static readonly string[] Rangos = { RangoSieteDias, RangoTreintaDias, RangoNoventaDias, RangoTodo };

        private readonly ITasaCambioConsultaService _consultas;
        private readonly ILogger<TendenciasModel> _log;

        public TendenciasModel(ITasaCambioConsultaService consultas, IOptions<TasasCambioOptions> opciones, TimeProvider reloj,
            ILogger<TendenciasModel> log) : base(opciones, reloj)
        {
            _consultas = consultas;
            _log = log;
        }

        [BindProperty(SupportsGet = true)] public string? Moneda { get; set; }
        [BindProperty(SupportsGet = true)] public string? Serie { get; set; }
        [BindProperty(SupportsGet = true)] public string? Rango { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Fecha { get; set; }

        public TendenciaTasas Tendencia { get; private set; } = new("", Array.Empty<PuntoTendencia>(), null);
        public GraficaTendencia Grafica { get; private set; } = GraficaTendencia.Calcular(Array.Empty<PuntoTendencia>(), SerieTendencia.Ambas, null);

        /// <summary>El punto resaltado (la fecha elegida o, si no hay, el último); nulo si no hay datos.</summary>
        public PuntoTendencia? Elegido { get; private set; }

        public bool HayDatos => Tendencia.Puntos.Count > 0;
        public bool VerCompra => Serie != SerieTendencia.Venta;
        public bool VerVenta => Serie != SerieTendencia.Compra;

        public async Task<IActionResult> OnGetAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            Moneda = Elegir(Moneda, Monedas) ?? Monedas[0];
            Serie = Elegir(Serie, SerieTendencia.Todas) ?? SerieTendencia.Ambas;
            Rango = Elegir(Rango, Rangos) ?? RangoTodo;
            Tendencia = new TendenciaTasas(Moneda, Array.Empty<PuntoTendencia>(), null);

            try
            {
                Tendencia = await _consultas.TendenciaAsync(Moneda, Dias, IdEmpresaSesion, Ct);
            }
            catch (Exception ex) when (IndexModel.EsErrorDeBaseDeDatos(ex))
            {
                AvisarErrorBaseDatos(ex, _log, "las tendencias");
            }

            Grafica = GraficaTendencia.Calcular(Tendencia.Puntos, Serie, Fecha);
            Elegido = HayDatos
                ? Tendencia.Puntos.FirstOrDefault(p => p.Fecha == Fecha) ?? Tendencia.Puntos[^1]
                : null;
            return Page();
        }

        /// <summary>Días hacia atrás del período elegido; 0 = todo lo guardado.</summary>
        public int Dias => Rango switch
        {
            RangoSieteDias => 7,
            RangoTreintaDias => 30,
            RangoNoventaDias => 90,
            _ => 0
        };

        public static string NombreRango(string rango) => rango switch
        {
            RangoSieteDias => "7 días",
            RangoTreintaDias => "30 días",
            RangoNoventaDias => "90 días",
            _ => "Todo"
        };

        public static string NombreSerie(string serie) => serie switch
        {
            SerieTendencia.Compra => "Compra",
            SerieTendencia.Venta => "Venta",
            _ => "Compra y venta"
        };

        // ── Para la vista ─────────────────────────────────────────────────────

        public PuntoTendencia? Primero => Tendencia.Puntos.FirstOrDefault();
        public PuntoTendencia? Ultimo => Tendencia.Puntos.LastOrDefault();

        /// <summary>El día publicado justo antes de <paramref name="punto"/> (para el primero, el anterior a la ventana).</summary>
        public PuntoTendencia? Previo(PuntoTendencia punto)
        {
            var i = Tendencia.Puntos.ToList().FindIndex(p => p.Fecha == punto.Fecha);
            return i > 0 ? Tendencia.Puntos[i - 1] : i == 0 ? Tendencia.Anterior : null;
        }

        public static string Tasa4(decimal? valor) => valor is { } v ? v.ToString("0.0000", CultureInfo.InvariantCulture) : "—";

        public static string Diferencia(PuntoTendencia punto) =>
            punto is { Compra: { } c, Venta: { } v } ? (v - c).ToString("0.0000", CultureInfo.InvariantCulture) : "—";

        /// <summary>Cambio de <paramref name="actual"/> contra <paramref name="anterior"/> con signo; "—" si falta alguno.</summary>
        public static string Cambio(decimal? actual, decimal? anterior, bool conPorcentaje = false)
        {
            if (actual is not { } a || anterior is not { } b) return "—";
            var delta = a - b;
            var texto = Signo(delta) + Math.Abs(delta).ToString("0.0000", CultureInfo.InvariantCulture);
            if (!conPorcentaje || b == 0m) return texto;
            var pct = delta / b * 100m;
            return $"{texto} ({Signo(pct)}{Math.Abs(pct).ToString("0.000", CultureInfo.InvariantCulture)} %)";
        }

        private static string Signo(decimal valor) => valor > 0m ? "+" : valor < 0m ? "−" : string.Empty;

        public string Descripcion => HayDatos
            ? $"Línea de {NombreSerie(Serie ?? SerieTendencia.Ambas).ToLowerInvariant()} de {Moneda} entre {FormatoTasas.Fecha(Primero!.Fecha)} y {FormatoTasas.Fecha(Ultimo!.Fecha)}"
            : "Sin datos";
    }
}
