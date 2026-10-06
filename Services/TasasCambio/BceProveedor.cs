using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Tipo de referencia EUR/USD del Banco Central Europeo, para derivar el euro en lempiras. Devuelve lecturas
    /// 1 EUR = x USD de tipo REFERENCIA; no se guardan como tasa, solo entran en la fórmula del derivado.
    ///
    /// XML: &lt;Cube time="yyyy-MM-dd"&gt;&lt;Cube currency="USD" rate="1.0876"/&gt;…&lt;/Cube&gt;. Si se pide un
    /// solo día se usa el archivo diario (solo trae la última fecha publicada); si no, el de 90 días.
    /// </summary>
    public sealed class BceProveedor : ITasaCambioProveedor
    {
        private readonly ClienteHttpTasas _http;
        private readonly TasasCambioOptions _opt;

        public BceProveedor(ClienteHttpTasas http, IOptions<TasasCambioOptions> opciones)
        {
            _http = http;
            _opt = opciones.Value;
        }

        public string Fuente => TasasCambioCatalogo.Fuente.Bce;

        public bool Habilitado => !string.IsNullOrWhiteSpace(_opt.Bce.UrlDiaria) || !string.IsNullOrWhiteSpace(_opt.Bce.UrlHistorica90d);

        public async Task<ResultadoProveedor> ObtenerAsync(SolicitudLectura solicitud, CancellationToken ct = default)
        {
            var url = solicitud.Desde >= solicitud.Hasta && !string.IsNullOrWhiteSpace(_opt.Bce.UrlDiaria)
                ? _opt.Bce.UrlDiaria
                : (string.IsNullOrWhiteSpace(_opt.Bce.UrlHistorica90d) ? _opt.Bce.UrlDiaria : _opt.Bce.UrlHistorica90d);

            RespuestaFuente? respuesta = null;
            try
            {
                respuesta = await _http.ObtenerAsync(url, url, ct: ct);
                var tipos = LeerXml(respuesta.Contenido);

                // Se devuelven también fechas anteriores al rango: el derivado usa la anterior más cercana.
                var lecturas = tipos
                    .Where(t => t.Fecha <= solicitud.Hasta)
                    .Select(t => new LecturaTasa("EUR", "USD", TasasCambioCatalogo.TipoTasa.Referencia, t.Fecha, t.Tasa,
                        Fuente, TextoTasas.Cortar($"BCE eurofxref {t.Fecha:yyyy-MM-dd}", 400)))
                    .ToList();

                return new ResultadoProveedor
                {
                    Fuente = Fuente, Lecturas = lecturas, Endpoint = url,
                    HttpStatus = respuesta.HttpStatus, HashContenido = respuesta.HashContenido
                };
            }
            catch (ErrorFuenteException ex)
            {
                return new ResultadoProveedor
                {
                    Fuente = Fuente, Error = ex.Error, Endpoint = url,
                    HttpStatus = respuesta?.HttpStatus ?? ex.Error.HttpStatus, HashContenido = respuesta?.HashContenido
                };
            }
        }

        /// <summary>EUR/USD por fecha, en cultura invariante. Lanza <see cref="ErrorFuenteException"/> si el XML no sirve.</summary>
        public static IReadOnlyList<(DateOnly Fecha, decimal Tasa)> LeerXml(byte[] contenido)
        {
            XDocument documento;
            try
            {
                using var flujo = new MemoryStream(contenido);
                documento = XDocument.Load(flujo, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                throw ErrorFuenteException.FormatoInesperado("el XML del BCE no es válido", ex);
            }

            var resultado = new List<(DateOnly, decimal)>();
            foreach (var dia in documento.Descendants().Where(e => e.Name.LocalName == "Cube" && e.Attribute("time") != null))
            {
                if (!DateOnly.TryParseExact(dia.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var fecha))
                    continue;

                var usd = dia.Elements().FirstOrDefault(e => e.Name.LocalName == "Cube"
                    && string.Equals(e.Attribute("currency")?.Value, "USD", StringComparison.OrdinalIgnoreCase));
                if (usd == null) continue;

                if (decimal.TryParse(usd.Attribute("rate")?.Value, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var tasa) && tasa > 0)
                    resultado.Add((fecha, tasa));
            }

            if (resultado.Count == 0)
                throw ErrorFuenteException.FormatoInesperado("el XML del BCE no trae el tipo EUR/USD");

            return resultado.OrderBy(r => r.Item1).ToList();
        }
    }
}
