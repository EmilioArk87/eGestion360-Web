using System.Globalization;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Configuración del job de tasas de cambio (sección "TasasCambio"). Los valores por omisión son los de
    /// producción, salvo <see cref="Habilitado"/>, que arranca en falso: el worker no hace nada hasta que se
    /// aplique el script 020 y se active a propósito.
    ///
    /// Las dos claves secretas (<see cref="BchOptions.ApiKey"/> y <see cref="DisparadorOptions.Token"/>) NO van en
    /// appsettings: vienen de las variables de entorno TasasCambio__Bch__ApiKey y TasasCambio__Disparador__Token o
    /// de user-secrets (ver 1 - Documetacion/CONFIGURACION_SECRETOS.md).
    ///
    /// Las listas se dejan vacías en el inicializador y el valor por omisión se aplica al leerlas: el enlazador de
    /// configuración agrega los elementos a una lista que ya tiene valores en vez de reemplazarlos, y
    /// ["USD","EUR"] en appsettings terminaría como USD, EUR, USD, EUR.
    /// </summary>
    public sealed class TasasCambioOptions
    {
        public const string Seccion = "TasasCambio";

        private static readonly string[] MonedasPorOmision = { "USD", "EUR" };
        private static readonly int[] EsperasPorOmision = { 15, 30, 60, 120 };

        /// <summary>Falso: el worker solo registra una línea y el endpoint responde 404.</summary>
        public bool Habilitado { get; set; }

        /// <summary>Monedas que se buscan contra la moneda local. Vacía = USD y EUR.</summary>
        public List<string> Monedas { get; set; } = new();

        public string MonedaLocal { get; set; } = "HNL";

        /// <summary>Id de Windows o IANA; si no existe en el servidor se prueba el otro y, al final, UTC-6 fijo.</summary>
        public string ZonaHoraria { get; set; } = "Central America Standard Time";

        /// <summary>Hora de Honduras (HH:mm) desde la que se busca la tasa del día.</summary>
        public string PrimerIntento { get; set; } = "17:00";

        /// <summary>Hora de Honduras (HH:mm) después de la que ya no se programan reintentos del día.</summary>
        public string UltimoIntento { get; set; } = "23:30";

        /// <summary>Hora de Honduras (HH:mm) del barrido que revisa que esté la tasa del día hábil anterior.</summary>
        public string BarridoMatutino { get; set; } = "07:00";

        /// <summary>Cuántos días antes de la última tasa vigente se vuelven a leer para rellenar huecos y ver correcciones.</summary>
        public int DiasPuestaAlDia { get; set; } = 3;

        public ReintentosOptions Reintentos { get; set; } = new();
        public ValidacionOptions Validacion { get; set; } = new();
        public BchOptions Bch { get; set; } = new();
        public BceOptions Bce { get; set; } = new();
        public DisparadorOptions Disparador { get; set; } = new();
        public AlertasOptions Alertas { get; set; } = new();

        /// <summary><see cref="Monedas"/> en mayúsculas y sin repetir; USD y EUR si la lista viene vacía.</summary>
        public IReadOnlyList<string> MonedasEfectivas
        {
            get
            {
                var lista = Monedas
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();
                return lista.Count > 0 ? lista : MonedasPorOmision;
            }
        }

        public string MonedaLocalEfectiva =>
            string.IsNullOrWhiteSpace(MonedaLocal) ? "HNL" : MonedaLocal.Trim().ToUpperInvariant();

        public TimeOnly HoraPrimerIntento => LeerHora(PrimerIntento, new TimeOnly(17, 0));
        public TimeOnly HoraUltimoIntento => LeerHora(UltimoIntento, new TimeOnly(23, 30));
        public TimeOnly HoraBarridoMatutino => LeerHora(BarridoMatutino, new TimeOnly(7, 0));

        /// <summary>Esperas entre reintentos de una misma fecha; vacía = 15, 30, 60 y 120 minutos.</summary>
        public IReadOnlyList<int> EsperasEfectivas =>
            Reintentos.EsperasMinutos.Count > 0 ? Reintentos.EsperasMinutos : EsperasPorOmision;

        private static TimeOnly LeerHora(string? texto, TimeOnly porOmision) =>
            TimeOnly.TryParseExact(texto?.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var hora)
                ? hora
                : porOmision;
    }

    public sealed class ReintentosOptions
    {
        /// <summary>Intentos como máximo para una misma fecha objetivo (el primero cuenta).</summary>
        public int MaxPorFecha { get; set; } = 6;

        /// <summary>Minutos de espera tras el intento 1, 2, 3…; del último en adelante se repite el último valor.</summary>
        public List<int> EsperasMinutos { get; set; } = new();
    }

    public sealed class ValidacionOptions
    {
        /// <summary>Rango aceptable por moneda (en moneda local). Fuera de rango, la tasa se guarda EN_REVISION.</summary>
        public Dictionary<string, RangoTasa> Rangos { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = new RangoTasa { Min = 15m, Max = 45m },
            ["EUR"] = new RangoTasa { Min = 15m, Max = 60m },
        };

        /// <summary>Variación máxima (%) contra la última tasa vigente anterior; más que esto va a revisión.</summary>
        public decimal VariacionDiariaMaxPct { get; set; } = 3.0m;

        /// <summary>Diferencia máxima (%) entre compra y venta de la misma fecha; más que esto va a revisión.</summary>
        public decimal DiferenciaCompraVentaMaxPct { get; set; } = 2.0m;
    }

    public sealed class RangoTasa
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
    }

    /// <summary>
    /// Banco Central de Honduras. El API de indicadores no está verificado (formato de la respuesta, nombre y lugar de
    /// la clave, nombres de los parámetros de fecha): por eso todo es configurable y, mientras falten la clave o los
    /// indicadores, el proveedor API queda deshabilitado y se usa el Excel.
    /// </summary>
    public sealed class BchOptions
    {
        /// <summary>Raíz del API, sin la barra final. Vacía = API deshabilitado.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>SOLO por variable de entorno (TasasCambio__Bch__ApiKey) o user-secrets. Nunca se registra en logs ni en la bitácora.</summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Nombre del encabezado o del parámetro de la URL que lleva la clave (no la clave). Se llama así, y no
        /// "NombreClave", para que el revisor de secretos no confunda "nombre": "valor" con una clave escrita en appsettings.
        /// </summary>
        public string ParametroAutenticacion { get; set; } = "clave";

        /// <summary>"header" (encabezado) o "query" (parámetro de la URL).</summary>
        public string ClaveEn { get; set; } = "header";

        /// <summary>Nombre del parámetro de la fecha inicial del rango (no verificado).</summary>
        public string ParametroDesde { get; set; } = "fechaInicio";

        /// <summary>Nombre del parámetro de la fecha final del rango (no verificado).</summary>
        public string ParametroHasta { get; set; } = "fechaFinal";

        /// <summary>Id del indicador; 0 = no configurado (el API queda deshabilitado si falta el de compra o el de venta del dólar).</summary>
        public int IndicadorUsdCompra { get; set; }
        public int IndicadorUsdVenta { get; set; }

        /// <summary>Indicador oficial del euro. En 0 (lo normal), el euro se deriva con el BCE.</summary>
        public int IndicadorEurCompra { get; set; }
        public int IndicadorEurVenta { get; set; }

        /// <summary>Excel "Precio Promedio Diario del Dólar" (hoja "Tipo de Cambio Diario": Fecha / Compra / Venta).</summary>
        public string UrlExcel { get; set; } =
            "https://www.bch.hn/estadisticos/GIE/LIBTipo%20de%20cambio/Precio%20Promedio%20Diario%20del%20D%C3%B3lar.xlsx";

        public bool ApiHabilitado =>
            !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey)
            && IndicadorUsdCompra > 0 && IndicadorUsdVenta > 0;

        public bool EuroOficialHabilitado => ApiHabilitado && IndicadorEurCompra > 0 && IndicadorEurVenta > 0;

        public bool ClaveEnQuery => string.Equals(ClaveEn?.Trim(), "query", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Banco Central Europeo: tipo de referencia EUR/USD para derivar el euro.</summary>
    public sealed class BceOptions
    {
        public string UrlDiaria { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
        public string UrlHistorica90d { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist-90d.xml";
    }

    public sealed class DisparadorOptions
    {
        /// <summary>
        /// Token del endpoint externo (encabezado X-Job-Token). SOLO por variable de entorno
        /// (TasasCambio__Disparador__Token) o user-secrets. Vacío = el endpoint responde 404.
        /// </summary>
        public string? Token { get; set; }
    }

    public sealed class AlertasOptions
    {
        /// <summary>Correos que reciben las alertas. Vacía (por omisión) = las alertas solo quedan en el log.</summary>
        public List<string> Destinatarios { get; set; } = new();
    }
}
