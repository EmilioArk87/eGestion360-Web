using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// API de indicadores del BCH: GET {BaseUrl}/api/v1/indicadores/{id}/cifras con el rango de fechas, un
    /// indicador por llamada (compra y venta del dólar y, si se configuran, del euro).
    ///
    /// FORMATO NO VERIFICADO: no se ha probado contra el API real. Se lee de forma tolerante: la lista de cifras es
    /// la raíz o la primera propiedad que sea un arreglo, y de cada objeto se toman "Fecha" y "Valor" sin distinguir
    /// mayúsculas. El nombre y el lugar de la clave (encabezado o parámetro) y los nombres de los parámetros de fecha
    /// son configurables. Sin clave o sin los indicadores del dólar, el proveedor queda deshabilitado y el job usa el
    /// Excel. La clave nunca se escribe en logs, mensajes ni bitácora.
    /// </summary>
    public sealed class BchApiProveedor : ITasaCambioProveedor
    {
        private readonly ClienteHttpTasas _http;
        private readonly TasasCambioOptions _opt;

        public BchApiProveedor(ClienteHttpTasas http, IOptions<TasasCambioOptions> opciones)
        {
            _http = http;
            _opt = opciones.Value;
        }

        public string Fuente => TasasCambioCatalogo.Fuente.BchApi;

        public bool Habilitado => _opt.Bch.ApiHabilitado;

        /// <summary>Verdadero si además hay indicadores oficiales del euro: entonces el euro no se deriva.</summary>
        public bool EuroHabilitado => _opt.Bch.EuroOficialHabilitado;

        public async Task<ResultadoProveedor> ObtenerAsync(SolicitudLectura solicitud, CancellationToken ct = default)
        {
            var bch = _opt.Bch;
            var raiz = bch.BaseUrl.TrimEnd('/');

            var indicadores = new List<(string Moneda, string Tipo, int Id)>
            {
                ("USD", TasasCambioCatalogo.TipoTasa.Compra, bch.IndicadorUsdCompra),
                ("USD", TasasCambioCatalogo.TipoTasa.Venta, bch.IndicadorUsdVenta),
            };
            if (EuroHabilitado)
            {
                indicadores.Add(("EUR", TasasCambioCatalogo.TipoTasa.Compra, bch.IndicadorEurCompra));
                indicadores.Add(("EUR", TasasCambioCatalogo.TipoTasa.Venta, bch.IndicadorEurVenta));
            }

            var endpointResumen = TextoTasas.Cortar(
                $"{raiz}/api/v1/indicadores/{string.Join(",", indicadores.Select(i => i.Id))}/cifras", 400);

            if (!Habilitado)
                return ResultadoProveedor.Fallido(Fuente,
                    new ErrorFuente(TipoErrorFuente.Permanente, "API del BCH deshabilitado: faltan la URL, la clave o los indicadores del dólar."),
                    null);

            var lecturas = new List<LecturaTasa>();
            var hashes = new List<string>();
            int? status = null;
            var local = _opt.MonedaLocalEfectiva;

            try
            {
                foreach (var (moneda, tipo, id) in indicadores)
                {
                    var endpoint = $"{raiz}/api/v1/indicadores/{id}/cifras" +
                                   $"?{Uri.EscapeDataString(bch.ParametroDesde)}={solicitud.Desde:yyyy-MM-dd}" +
                                   $"&{Uri.EscapeDataString(bch.ParametroHasta)}={solicitud.Hasta:yyyy-MM-dd}";

                    var url = endpoint;
                    Dictionary<string, string>? encabezados = null;
                    if (bch.ClaveEnQuery)
                        url += $"&{Uri.EscapeDataString(bch.ParametroAutenticacion)}={Uri.EscapeDataString(bch.ApiKey!)}";
                    else
                        encabezados = new Dictionary<string, string> { [bch.ParametroAutenticacion] = bch.ApiKey! };

                    var respuesta = await _http.ObtenerAsync(url, endpoint, encabezados, ct);
                    status = respuesta.HttpStatus;
                    hashes.Add(respuesta.HashContenido);

                    foreach (var (fecha, valor) in LeerJson(respuesta.Contenido))
                    {
                        if (fecha < solicitud.Desde) continue;
                        lecturas.Add(new LecturaTasa(moneda, local, tipo, fecha, valor, Fuente, $"BCH API indicador {id}"));
                    }
                }
            }
            catch (ErrorFuenteException ex)
            {
                var error = ex.Error with { Mensaje = Sanear(ex.Error.Mensaje)!, Detalle = Sanear(ex.Error.Detalle) };
                return ResultadoProveedor.Fallido(Fuente, error, endpointResumen);
            }

            var hash = hashes.Count == 1
                ? hashes[0]
                : ClienteHttpTasas.Hash(Encoding.ASCII.GetBytes(string.Concat(hashes)));

            return new ResultadoProveedor
            {
                Fuente = Fuente, Lecturas = lecturas, Endpoint = endpointResumen, HttpStatus = status, HashContenido = hash
            };
        }

        /// <summary>
        /// Cifras (fecha, valor) de una respuesta del API. Lista vacía si la respuesta trae una lista vacía; lanza
        /// <see cref="ErrorFuenteException"/> (formato inesperado) si no es JSON, no trae una lista o ninguna cifra
        /// tiene "Fecha" y "Valor" legibles.
        /// </summary>
        public static IReadOnlyList<(DateOnly Fecha, decimal Valor)> LeerJson(byte[] contenido)
        {
            JsonDocument documento;
            try
            {
                documento = JsonDocument.Parse(contenido);
            }
            catch (JsonException ex)
            {
                throw ErrorFuenteException.FormatoInesperado("la respuesta del API del BCH no es JSON", ex);
            }

            using (documento)
            {
                var lista = BuscarArreglo(documento.RootElement, profundidad: 0)
                    ?? throw ErrorFuenteException.FormatoInesperado("la respuesta del API del BCH no trae una lista de cifras");

                var resultado = new Dictionary<DateOnly, decimal>();
                var objetos = 0;
                foreach (var cifra in lista.EnumerateArray())
                {
                    if (cifra.ValueKind != JsonValueKind.Object) continue;
                    objetos++;
                    if (!TryLeerFecha(Propiedad(cifra, "fecha"), out var fecha)) continue;
                    // Un valor fuera de toda escala (más de un millón) no es una tasa y desbordaría decimal(18,8).
                    if (!TryLeerValor(Propiedad(cifra, "valor"), out var valor) || valor <= 0 || valor > 1_000_000m) continue;
                    resultado[fecha] = valor;
                }

                if (objetos > 0 && resultado.Count == 0)
                    throw ErrorFuenteException.FormatoInesperado("las cifras del API del BCH no traen Fecha y Valor legibles");

                return resultado.OrderBy(r => r.Key).Select(r => (r.Key, r.Value)).ToList();
            }
        }

        private static JsonElement? BuscarArreglo(JsonElement elemento, int profundidad)
        {
            if (elemento.ValueKind == JsonValueKind.Array) return elemento;
            if (elemento.ValueKind != JsonValueKind.Object || profundidad > 2) return null;

            foreach (var propiedad in elemento.EnumerateObject())
                if (propiedad.Value.ValueKind == JsonValueKind.Array)
                    return propiedad.Value;

            foreach (var propiedad in elemento.EnumerateObject())
                if (propiedad.Value.ValueKind == JsonValueKind.Object
                    && BuscarArreglo(propiedad.Value, profundidad + 1) is { } anidado)
                    return anidado;

            return null;
        }

        private static JsonElement? Propiedad(JsonElement objeto, string nombre)
        {
            foreach (var propiedad in objeto.EnumerateObject())
                if (string.Equals(propiedad.Name, nombre, StringComparison.OrdinalIgnoreCase))
                    return propiedad.Value;
            return null;
        }

        private static bool TryLeerFecha(JsonElement? elemento, out DateOnly fecha)
        {
            fecha = default;
            return elemento is { ValueKind: JsonValueKind.String } e && TextoTasas.TryLeerFecha(e.GetString(), out fecha);
        }

        private static bool TryLeerValor(JsonElement? elemento, out decimal valor)
        {
            valor = 0m;
            if (elemento is not { } e) return false;
            if (e.ValueKind == JsonValueKind.Number) return e.TryGetDecimal(out valor);
            return e.ValueKind == JsonValueKind.String && TextoTasas.TryLeerDecimal(e.GetString(), out valor);
        }

        /// <summary>Quita la clave del API de un texto, por si alguna capa la incluyera en un mensaje.</summary>
        private string? Sanear(string? texto)
        {
            var clave = _opt.Bch.ApiKey;
            if (texto == null || string.IsNullOrEmpty(clave)) return texto;
            return texto.Replace(clave, "***", StringComparison.Ordinal)
                        .Replace(Uri.EscapeDataString(clave), "***", StringComparison.Ordinal);
        }
    }
}
