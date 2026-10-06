using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Services.TasasCambio
{
    public enum ResultadoDisparoExterno
    {
        /// <summary>Job deshabilitado o sin token configurado: el endpoint "no existe" (404).</summary>
        NoEncontrado,

        /// <summary>Falta el encabezado o el token no coincide (401).</summary>
        NoAutorizado,

        /// <summary>Se encoló una evaluación de la programación (202). Solo hay ejecución si en ese momento toca.</summary>
        Aceptado,

        /// <summary>Token correcto pero llegó a menos de 60 s de la anterior aceptada: no se encola nada (202).</summary>
        Ignorado
    }

    /// <summary>
    /// Disparador externo del job (singleton): lo llama un cron externo para despertar la aplicación en IIS. NO fuerza
    /// una ejecución: encola una evaluación de la programación (<see cref="SolicitudEjecucionTasas.Evaluar"/>), que el
    /// worker resuelve igual que su vuelta de 10 minutos; si no toca nada, no hay descarga ni fila en la bitácora.
    /// Compara el token en tiempo constante y no encola más de una evaluación por minuto.
    /// </summary>
    public sealed class DisparadorExternoTasasCambio
    {
        public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromSeconds(60);

        private readonly TasasCambioOptions _opt;
        private readonly ColaEjecucionTasasCambio _cola;
        private readonly TimeProvider _reloj;
        private readonly ILogger<DisparadorExternoTasasCambio> _log;
        private readonly object _candado = new();
        private DateTimeOffset? _ultimaAceptada;

        public DisparadorExternoTasasCambio(IOptions<TasasCambioOptions> opciones, ColaEjecucionTasasCambio cola, TimeProvider reloj,
            ILogger<DisparadorExternoTasasCambio> log)
        {
            _opt = opciones.Value;
            _cola = cola;
            _reloj = reloj;
            _log = log;
        }

        public ResultadoDisparoExterno Atender(string? tokenRecibido)
        {
            var configurado = _opt.Disparador.Token;
            if (!_opt.Habilitado || string.IsNullOrEmpty(configurado)) return ResultadoDisparoExterno.NoEncontrado;

            if (string.IsNullOrEmpty(tokenRecibido) || !TokenCoincide(tokenRecibido, configurado))
            {
                _log.LogWarning("Tasas de cambio: llamada al disparador externo con token ausente o incorrecto.");
                return ResultadoDisparoExterno.NoAutorizado;
            }

            lock (_candado)
            {
                var ahora = _reloj.GetUtcNow();
                if (_ultimaAceptada is { } ultima && ahora - ultima < IntervaloMinimo)
                {
                    _log.LogInformation("Tasas de cambio: disparador externo ignorado (menos de 60 s desde el anterior).");
                    return ResultadoDisparoExterno.Ignorado;
                }
                _ultimaAceptada = ahora;
            }

            if (!_cola.Solicitar(SolicitudEjecucionTasas.Evaluar(Cat.Disparador.Externo)))
                _log.LogInformation("Tasas de cambio: la cola ya tiene solicitudes pendientes; no se agregó otra.");
            else
                _log.LogInformation("Tasas de cambio: disparador externo recibido; se evalúa la programación.");
            return ResultadoDisparoExterno.Aceptado;
        }

        /// <summary>Compara los SHA-256 de ambos tokens con FixedTimeEquals: el tiempo no depende del contenido ni del largo.</summary>
        private static bool TokenCoincide(string recibido, string configurado) =>
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(recibido)),
                SHA256.HashData(Encoding.UTF8.GetBytes(configurado)));
    }

    /// <summary>
    /// POST /internal/jobs/tasas-cambio/ejecutar con el encabezado X-Job-Token. Minimal API: no pasa por los filtros de
    /// Razor Pages (AdminOnlyPageFilter, EmpresaRequeridaPageFilter) ni usa la sesión. Responde 404 si el job está
    /// deshabilitado o no hay token configurado, 401 si el token no coincide y 202 sin contenido si lo acepta (la
    /// evaluación, y la ejecución si toca, corren en el worker, no en la petición).
    /// </summary>
    public static class TasasCambioEndpoints
    {
        public const string Ruta = "/internal/jobs/tasas-cambio/ejecutar";
        public const string EncabezadoAutenticacion = "X-Job-Token";

        public static IEndpointRouteBuilder MapTasasCambioEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost(Ruta, Atender)
               .AllowAnonymous()
               .DisableAntiforgery()
               .ExcludeFromDescription();
            return app;
        }

        public static IResult Atender(HttpRequest request, DisparadorExternoTasasCambio disparador)
        {
            var token = request.Headers[EncabezadoAutenticacion].ToString();
            return disparador.Atender(token) switch
            {
                ResultadoDisparoExterno.NoEncontrado => Results.NotFound(),
                ResultadoDisparoExterno.NoAutorizado => Results.Unauthorized(),
                _ => Results.StatusCode(StatusCodes.Status202Accepted)
            };
        }
    }
}
