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

        [BindProperty]
        public string PlainPassword { get; set; } = "";

        public List<EmailConfiguration>? Configurations { get; set; }
        public Dictionary<string, object>? Stats { get; set; }

        public async Task OnGetAsync()
        {
            await LoadDataAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
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
                    // Es una actualización
                    var existingConfig = await _emailManager.GetConfigurationByIdAsync(ConfigId);
                    if (existingConfig != null)
                    {
                        Configuration.Id = ConfigId;
                        Configuration.CreatedAt = existingConfig.CreatedAt;
                        Configuration.UpdatedAt = DateTime.Now;
                        Configuration.TestEmailsSent = existingConfig.TestEmailsSent;
                        Configuration.LastTestedAt = existingConfig.LastTestedAt;
                    }
                }

                var result = await _emailManager.SaveConfigurationAsync(Configuration, PlainPassword);

                if (result)
                {
                    TempData["SuccessMessage"] = $"Configuración '{Configuration.ProfileName}' guardada exitosamente para {Configuration.FromEmail}";
                    return RedirectToPage();
                }
                else
                {
                    TempData["ErrorMessage"] = "Error al guardar la configuración.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving email configuration");
                TempData["ErrorMessage"] = "Error inesperado al guardar la configuración.";
            }

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
                return new JsonResult(new { success = result });
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