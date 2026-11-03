using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin
{
    public class EmailConfigModel : PageModel
    {
        private readonly EmailManagerService _emailManager;
        private readonly ILogger<EmailConfigModel> _logger;

        public EmailConfigModel(EmailManagerService emailManager, ILogger<EmailConfigModel> logger)
        {
            _emailManager = emailManager;
            _logger = logger;
        }

        [BindProperty]
        public EmailConfiguration Configuration { get; set; } = new();

        [BindProperty]
        public int ConfigId { get; set; }

        /// <summary>Anulable a propósito: un string no anulable lo vuelve obligatorio ASP.NET, y al editar vacía significa «conservar la guardada».</summary>
        [BindProperty]
        public string? PlainPassword { get; set; }

        public List<EmailConfiguration>? Configurations { get; set; }
        public Dictionary<string, object>? Stats { get; set; }

        /// <summary>Verdadero cuando el guardado falló: la pantalla vuelve a abrir el formulario con lo escrito y los motivos.</summary>
        public bool ReabrirFormulario { get; private set; }

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // La contraseña viaja como PlainPassword y se cifra al guardar: el formulario nunca envía PasswordHash,
            // así que su [Required] no puede decidir si el formulario es válido.
            ModelState.Remove($"{nameof(Configuration)}.{nameof(EmailConfiguration.PasswordHash)}");

            // Al crear, la contraseña es obligatoria; al editar, vacía significa «conservar la que ya tiene».
            if (ConfigId == 0 && string.IsNullOrWhiteSpace(PlainPassword))
            {
                ModelState.AddModelError(nameof(PlainPassword), "Escribe la contraseña del buzón.");
            }

            if (ModelState.IsValid && !string.IsNullOrWhiteSpace(Configuration.ProfileName))
            {
                var perfiles = await _emailManager.GetAllConfigurationsAsync();
                var nombre = Configuration.ProfileName.Trim();
                if (perfiles.Any(c => c.Id != ConfigId && string.Equals(c.ProfileName, nombre, StringComparison.OrdinalIgnoreCase)))
                {
                    ModelState.AddModelError($"{nameof(Configuration)}.{nameof(EmailConfiguration.ProfileName)}",
                        $"Ya existe un perfil llamado «{nombre}». Usa otro nombre.");
                }
                Configuration.ProfileName = nombre;
            }

            if (!ModelState.IsValid)
            {
                ReabrirFormulario = true;
                await LoadDataAsync();
                return Page();
            }

            try
            {
                // Configuraciones específicas para SIP Tecnología
                if (Configuration.FromEmail == "egaray@siptecnologia.xyz")
                {
                    SetSipTecnologiaDefaults();
                }

                // Si es una nueva configuración
                if (ConfigId == 0)
                {
                    Configuration.CreatedAt = DateTime.Now;
                    Configuration.UpdatedAt = DateTime.Now;
                    Configuration.IsActive = true;
                    Configuration.TestEmailsSent = 0;
                    Configuration.CreatedBy = "Admin";
                }
                else
                {
                    // Es una actualización: el servicio conserva el estado, las pruebas y la fecha de alta de la existente
                    var existingConfig = await _emailManager.GetConfigurationByIdAsync(ConfigId);
                    if (existingConfig == null)
                    {
                        ModelState.AddModelError(string.Empty, "Esa configuración ya no existe. Cierra el formulario y revisa la lista.");
                        ReabrirFormulario = true;
                        await LoadDataAsync();
                        return Page();
                    }

                    Configuration.Id = ConfigId;
                }

                var result = await _emailManager.SaveConfigurationAsync(Configuration, PlainPassword ?? string.Empty);

                if (result)
                {
                    TempData["SuccessMessage"] = $"Configuración '{Configuration.ProfileName}' guardada exitosamente para {Configuration.FromEmail}";
                    return RedirectToPage();
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "No se pudo guardar la configuración. Revisa los datos e inténtalo de nuevo.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving email configuration");
                ModelState.AddModelError(string.Empty, "Error inesperado al guardar la configuración.");
            }

            ReabrirFormulario = true;
            await LoadDataAsync();
            return Page();
        }

        private void SetSipTecnologiaDefaults()
        {
            // Configuración predeterminada para SIP Tecnología
            Configuration.FromName = "eGestion360 - SIP Tecnología";
            Configuration.Provider = "SMTP";
            
            if (string.IsNullOrEmpty(Configuration.ProfileName))
            {
                Configuration.ProfileName = "SIP Tecnología - eGaray";
            }

            // Configuración SMTP típica para dominios personalizados
            if (string.IsNullOrEmpty(Configuration.SmtpHost))
            {
                // Intentar diferentes configuraciones comunes
                Configuration.SmtpHost = "mail.siptecnologia.xyz"; // Servidor de correo del dominio
                Configuration.SmtpPort = 587; // Puerto STARTTLS estándar
                Configuration.UseSsl = true;
                Configuration.Username = Configuration.FromEmail; // Usar el email completo como usuario
            }

            // Establecer como predeterminada si es la primera configuración
            Configuration.IsDefault = true;
            
            _logger.LogInformation("Configuración predeterminada aplicada para SIP Tecnología: {Email}", Configuration.FromEmail);
        }

        public async Task<IActionResult> OnPostTestAsync(int configId)
        {
            try
            {
                var result = await _emailManager.TestConfigurationAsync(configId);
                return new JsonResult(new
                {
                    success = result,
                    error = result ? null : "No se pudo enviar el correo de prueba con este perfil. Revisa servidor, puerto, SSL, usuario y contraseña; si la contraseña se guardó con otras claves de cifrado, vuelve a escribirla desde Editar."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing email configuration {ConfigId}", configId);
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostSetDefaultAsync(int configId)
        {
            try
            {
                var result = await _emailManager.MarkAsDefaultAsync(configId);
                return new JsonResult(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting default email configuration {ConfigId}", configId);
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostToggleActiveAsync(int configId)
        {
            try
            {
                var result = await _emailManager.ToggleActiveStatusAsync(configId);
                return new JsonResult(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling email configuration {ConfigId}", configId);
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int configId)
        {
            try
            {
                var result = await _emailManager.DeleteConfigurationAsync(configId);
                return new JsonResult(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting email configuration {ConfigId}", configId);
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                Configurations = await _emailManager.GetAllConfigurationsAsync();
                Stats = await _emailManager.GetSystemStatsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading email configurations");
                Configurations = new List<EmailConfiguration>();
                Stats = new Dictionary<string, object>();
            }
        }
    }
}