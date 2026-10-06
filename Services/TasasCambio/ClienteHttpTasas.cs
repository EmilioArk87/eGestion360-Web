using System.Net;
using System.Security.Cryptography;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>Respuesta exitosa de una fuente: el contenido tal cual y los datos para la bitácora.</summary>
    public sealed record RespuestaFuente(byte[] Contenido, int HttpStatus, string HashContenido, string Endpoint);

    /// <summary>
    /// Descarga de las fuentes de tasas con el cliente con nombre <see cref="NombreCliente"/> (tiempo máximo 30 s,
    /// registrado en Program.cs). Reintenta hasta 3 veces los errores transitorios (tiempo agotado, red, 408, 429 y
    /// 5xx) con esperas de 2, 4 y 8 s más hasta 1 s al azar, y respeta Retry-After si no pasa de 30 s. Los errores
    /// permanentes (401/403, 404, otros 4xx) no se reintentan.
    ///
    /// No se usa Microsoft.Extensions.Http.Resilience para no sumar un paquete por una política tan corta.
    /// </summary>
    public sealed class ClienteHttpTasas
    {
        public const string NombreCliente = "TasasCambio";
        public static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan[] Esperas = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8) };
        private static readonly TimeSpan RetryAfterMaximo = TimeSpan.FromSeconds(30);

        private readonly IHttpClientFactory _fabrica;
        private readonly ILogger<ClienteHttpTasas> _log;
        private readonly Func<TimeSpan, CancellationToken, Task> _esperar;

        /// <param name="esperar">Solo para pruebas: reemplaza la espera entre reintentos (por omisión, Task.Delay).</param>
        public ClienteHttpTasas(IHttpClientFactory fabrica, ILogger<ClienteHttpTasas> log,
            Func<TimeSpan, CancellationToken, Task>? esperar = null)
        {
            _fabrica = fabrica;
            _log = log;
            _esperar = esperar ?? ((espera, ct) => Task.Delay(espera, ct));
        }

        /// <summary>
        /// GET a <paramref name="url"/>. Devuelve el contenido o lanza <see cref="ErrorFuenteException"/> ya clasificada.
        /// </summary>
        /// <param name="endpoint">La URL tal como puede quedar en logs y bitácora: sin la clave.</param>
        /// <param name="encabezados">Encabezados extra (por ejemplo la clave del API); nunca se registran.</param>
        public async Task<RespuestaFuente> ObtenerAsync(string url, string endpoint,
            IReadOnlyDictionary<string, string>? encabezados = null, CancellationToken ct = default)
        {
            var cliente = _fabrica.CreateClient(NombreCliente);

            for (var intento = 1; ; intento++)
            {
                ErrorFuente error;
                TimeSpan? retryAfter = null;
                Exception? interna = null;

                try
                {
                    using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
                    if (encabezados != null)
                        foreach (var (nombre, valor) in encabezados)
                            peticion.Headers.TryAddWithoutValidation(nombre, valor);

                    using var respuesta = await cliente.SendAsync(peticion, ct);
                    var status = (int)respuesta.StatusCode;

                    if (respuesta.IsSuccessStatusCode)
                    {
                        var contenido = await respuesta.Content.ReadAsByteArrayAsync(ct);
                        return new RespuestaFuente(contenido, status, Hash(contenido), endpoint);
                    }

                    error = Clasificar(respuesta.StatusCode, respuesta.ReasonPhrase);
                    retryAfter = LeerRetryAfter(respuesta);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    // HttpClient.Timeout se manifiesta como TaskCanceledException sin que se haya pedido cancelar.
                    error = new ErrorFuente(TipoErrorFuente.Transitorio,
                        $"Tiempo de espera agotado ({TiempoMaximo.TotalSeconds:0} s)");
                    interna = ex;
                }
                catch (HttpRequestException ex)
                {
                    error = new ErrorFuente(TipoErrorFuente.Transitorio, "Error de red: " + ex.Message,
                        ex.StatusCode is { } s ? (int)s : null);
                    interna = ex;
                }

                if (!error.EsTransitorio || intento > Esperas.Length)
                    throw new ErrorFuenteException(error, interna);

                var espera = Esperas[intento - 1] + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
                if (retryAfter is { } pedido)
                {
                    // Un Retry-After largo no se espera aquí: el job ya tiene sus propios reintentos por fecha.
                    if (pedido > RetryAfterMaximo) throw new ErrorFuenteException(error, interna);
                    if (pedido > espera) espera = pedido;
                }

                _log.LogWarning("Tasas de cambio: {Endpoint} falló ({Error}); reintento {Intento} de {Maximo} en {Espera:0.0} s.",
                    endpoint, error.Mensaje, intento, Esperas.Length, espera.TotalSeconds);
                await _esperar(espera, ct);
            }
        }

        /// <summary>408, 429 y 5xx son transitorios; cualquier otro código de error es permanente.</summary>
        public static ErrorFuente Clasificar(HttpStatusCode status, string? razon = null)
        {
            var codigo = (int)status;
            var mensaje = $"HTTP {codigo} {razon}".TrimEnd();
            var transitorio = codigo is 408 or 429 || codigo >= 500;
            return new ErrorFuente(transitorio ? TipoErrorFuente.Transitorio : TipoErrorFuente.Permanente, mensaje, codigo);
        }

        public static string Hash(byte[] contenido) =>
            Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();

        private static TimeSpan? LeerRetryAfter(HttpResponseMessage respuesta)
        {
            var encabezado = respuesta.Headers.RetryAfter;
            if (encabezado == null) return null;
            if (encabezado.Delta is { } delta) return delta;
            if (encabezado.Date is { } fecha)
            {
                var falta = fecha - DateTimeOffset.UtcNow;
                return falta > TimeSpan.Zero ? falta : TimeSpan.Zero;
            }
            return null;
        }
    }
}
