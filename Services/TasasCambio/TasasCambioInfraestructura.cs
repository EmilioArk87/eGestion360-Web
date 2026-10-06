using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Crea contextos nuevos de EF. El job usa uno para las tasas y otro para la bitácora: cada uno con su conexión y
    /// su transacción, para que un error al guardar una tasa no deshaga el registro del error.
    /// </summary>
    public interface ITasasCambioContextos
    {
        /// <summary>Un contexto nuevo; quien lo pide lo desecha.</summary>
        ApplicationDbContext Crear();
    }

    /// <summary>Contextos con las mismas opciones que el de la petición (cadena de conexión e interceptores).</summary>
    public sealed class TasasCambioContextos : ITasasCambioContextos
    {
        private readonly DbContextOptions<ApplicationDbContext> _opciones;

        public TasasCambioContextos(DbContextOptions<ApplicationDbContext> opciones) => _opciones = opciones;

        public ApplicationDbContext Crear() => new(_opciones);
    }

    /// <summary>Alertas del job (FALLIDA, EN_REVISION, días hábiles sin tasa).</summary>
    public interface ITasasCambioNotificador
    {
        /// <summary>
        /// Verdadero si la alerta quedó atendida: enviada al menos a un destinatario o, si no hay destinatarios
        /// configurados, registrada en el log. Falso si había destinatarios y no salió ningún correo (se reintenta en
        /// la próxima ejecución).
        /// </summary>
        Task<bool> NotificarAsync(string asunto, string contenidoHtml, CancellationToken ct = default);
    }

    /// <summary>
    /// Alertas por correo a <see cref="AlertasOptions.Destinatarios"/> con <see cref="IEmailService.SendHtmlEmailAsync"/>.
    /// Siempre deja la alerta en el log; con la lista vacía (por omisión), solo en el log.
    /// </summary>
    public sealed class TasasCambioNotificadorCorreo : ITasasCambioNotificador
    {
        private readonly IEmailService _correo;
        private readonly TasasCambioOptions _opt;
        private readonly ILogger<TasasCambioNotificadorCorreo> _log;

        public TasasCambioNotificadorCorreo(IEmailService correo, IOptions<TasasCambioOptions> opciones,
            ILogger<TasasCambioNotificadorCorreo> log)
        {
            _correo = correo;
            _opt = opciones.Value;
            _log = log;
        }

        public async Task<bool> NotificarAsync(string asunto, string contenidoHtml, CancellationToken ct = default)
        {
            _log.LogWarning("Alerta de tasas de cambio: {Asunto}", asunto);

            var destinatarios = _opt.Alertas.Destinatarios
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (destinatarios.Count == 0) return true;

            var enviados = 0;
            foreach (var destinatario in destinatarios)
            {
                ct.ThrowIfCancellationRequested();
                if (await _correo.SendHtmlEmailAsync(destinatario, asunto, contenidoHtml)) enviados++;
            }

            if (enviados == 0)
                _log.LogError("No se pudo enviar a nadie la alerta de tasas de cambio '{Asunto}'.", asunto);
            return enviados > 0;
        }
    }
}
