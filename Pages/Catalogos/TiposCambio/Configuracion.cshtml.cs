using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>
    /// Configuración del job de tasas de cambio (script 024): el interruptor del job automático, las monedas, las
    /// horas de los intentos, los reintentos, las reglas de validación y los destinatarios de las alertas.
    /// Solo el administrador del sistema (se revisa en el GET y en el POST). Las claves secretas (API del BCH, token
    /// del disparador) NO se editan aquí: siguen en variables de entorno o user-secrets.
    /// </summary>
    public class ConfiguracionModel : PageModel
    {
        public const string AvisoNoInstalado =
            "Falta aplicar el script 024 (tabla tasas_cambio_configuracion) en la base de datos: sin él no se puede guardar la configuración.";

        private readonly ITasasCambioConfiguracionService _config;
        private readonly TasasCambioOptions _opt;
        private readonly ILogger<ConfiguracionModel> _log;

        public ConfiguracionModel(ITasasCambioConfiguracionService config, IOptions<TasasCambioOptions> opciones,
            ILogger<ConfiguracionModel> log)
        {
            _config = config;
            _opt = opciones.Value;
            _log = log;
        }

        [BindProperty] public ConfiguracionTasasInput Entrada { get; set; } = new();

        /// <summary>Errores por nombre de campo de <see cref="ConfiguracionTasasInput"/>.</summary>
        public Dictionary<string, string> Errores { get; } = new();

        public string? AvisoBaseDatos { get; private set; }

        public bool EsAdminSistema => AuthHelper.IsAdmin(HttpContext);
        public string MonedaLocal => _opt.MonedaLocalEfectiva;
        public bool IgnoraInterruptorGuardado => _config.IgnoraInterruptorGuardado;

        /// <summary>¿Hay claves secretas del API del BCH y del disparador? Solo se informa sí/no, nunca el valor.</summary>
        public bool TieneClaveBch => !string.IsNullOrWhiteSpace(_opt.Bch.ApiKey);
        public bool TieneTokenDisparador => !string.IsNullOrWhiteSpace(_opt.Disparador.Token);
        public bool ApiBchHabilitado => _opt.Bch.ApiHabilitado;

        public DateTime? ActualizadoUtc { get; private set; }
        public string? ActualizadoPor { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            Entrada = ConfiguracionTasasInput.Desde(_opt);
            await CargarAuditoriaAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            var fila = ConfiguracionTasas.Validar(Entrada ?? new ConfiguracionTasasInput(), MonedaLocal, Errores);
            if (fila == null)
            {
                await CargarAuditoriaAsync();
                return Page();
            }

            try
            {
                await _config.GuardarAsync(fila, HttpContext.Session.GetString("Username") ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex) when (IndexModel.EsErrorDeBaseDeDatos(ex))
            {
                _log.LogError(ex, "Tasas de cambio: error de base de datos al guardar la configuración.");
                AvisoBaseDatos = IndexModel.FaltanTablas(ex)
                    ? AvisoNoInstalado
                    : "No se pudo guardar por un error de la base de datos. Intente de nuevo; si el problema sigue, avise al administrador.";
                await CargarAuditoriaAsync();
                return Page();
            }

            TempData[IndexModel.ClaveMensaje] = fila.Habilitado
                ? "Configuración guardada. El job automático de tasas de cambio está HABILITADO."
                : "Configuración guardada. El job automático de tasas de cambio está DESHABILITADO.";
            return RedirectToPage("Index");
        }

        /// <summary>Sin sesión al login; sin el módulo al menú; quien no es administrador del sistema vuelve a la pantalla de tasas.</summary>
        private IActionResult? Acceso()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, IndexModel.Modulo)) return RedirectToPage("/MainMenu");
            if (!EsAdminSistema)
            {
                _log.LogWarning("Tasas de cambio: {Usuario} (rol {Rol}) intentó abrir la configuración sin ser administrador del sistema.",
                    HttpContext.Session.GetString("Username"), HttpContext.Session.GetString("Role"));
                TempData[IndexModel.ClaveError] = "Solo el administrador del sistema puede configurar el job de tasas de cambio.";
                return RedirectToPage("Index");
            }
            return null;
        }

        private async Task CargarAuditoriaAsync()
        {
            try
            {
                var fila = await _config.LeerAsync(HttpContext.RequestAborted);
                ActualizadoPor = fila?.ActualizadoPor;
                ActualizadoUtc = fila?.ActualizadoUtc;
            }
            catch (Exception ex) when (IndexModel.EsErrorDeBaseDeDatos(ex))
            {
                AvisoBaseDatos ??= IndexModel.FaltanTablas(ex) ? AvisoNoInstalado : null;
            }
        }

        public string? Error(string campo) => Errores.TryGetValue(campo, out var mensaje) ? mensaje : null;
    }
}
