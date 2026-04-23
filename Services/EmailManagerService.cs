using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Services
{
    public class EmailManagerService
    {
        private readonly IEmailConfigurationService _configService;
        private readonly IEmailService _emailService;
        private readonly ILogger<EmailManagerService> _logger;

        public EmailManagerService(
            IEmailConfigurationService configService,
            IEmailService emailService,
            ILogger<EmailManagerService> logger)
        {
            _configService = configService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<List<EmailConfiguration>> GetAllConfigurationsAsync()
        {
            return await _configService.GetAllConfigurationsAsync();
        }

        public async Task<EmailConfiguration?> GetConfigurationByIdAsync(int id)
        {
            return await _configService.GetConfigurationByIdAsync(id);
        }

        public async Task<bool> SaveConfigurationAsync(EmailConfiguration config, string plainPassword)
        {
            try
            {
                return await _configService.SaveConfigurationAsync(config, plainPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving email configuration {ProfileName}", config.ProfileName);
                return false;
            }
        }

        public async Task<bool> TestConfigurationAsync(int configId)
        {
            try
            {
                var config = await _configService.GetConfigurationByIdAsync(configId);
                if (config == null)
                {
                    _logger.LogWarning("Configuration {ConfigId} not found for testing", configId);
                    return false;
                }

                // Generar correo de prueba
                var testResult = await _emailService.SendTestEmailAsync(
                    config.FromEmail,
                    "Prueba de Configuración de Email",
                    $"Esta es una prueba de la configuración '{config.ProfileName}'.\n\n" +
                    $"Enviado el: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                    $"Servidor SMTP: {config.SmtpHost}:{config.SmtpPort}\n" +
                    $"SSL: {(config.UseSsl ? "Habilitado" : "Deshabilitado")}"
                );

                if (testResult)
                {
                    // Actualizar estadísticas de prueba
                    await _configService.UpdateTestStatsAsync(configId);
                    _logger.LogInformation("Test email sent successfully for configuration {ConfigId}", configId);
                }

                return testResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing email configuration {ConfigId}", configId);
                return false;
            }
        }

        public async Task<bool> MarkAsDefaultAsync(int configId)
        {
            return await _configService.SetDefaultConfigurationAsync(configId);
        }

        public async Task<bool> ToggleActiveStatusAsync(int configId)
        {
            return await _configService.ToggleActiveStatusAsync(configId);
        }

        public async Task<bool> DeleteConfigurationAsync(int configId)
        {
            return await _configService.DeleteConfigurationAsync(configId);
        }

        public async Task<EmailConfiguration?> GetDefaultConfigurationAsync()
        {
            return await _configService.GetDefaultConfigurationAsync();
        }

        public async Task<Dictionary<string, object>> GetSystemStatsAsync()
        {
            try
            {
                var configs = await _configService.GetAllConfigurationsAsync();
                return new Dictionary<string, object>
                {
                    ["TotalConfigurations"] = configs.Count,
                    ["ActiveConfigurations"] = configs.Count(c => c.IsActive),
                    ["HasDefaultConfiguration"] = configs.Any(c => c.IsDefault && c.IsActive),
                    ["TotalTestEmailsSent"] = configs.Sum(c => c.TestEmailsSent),
                    ["LastTestDate"] = configs.Where(c => c.LastTestedAt.HasValue)
                        .Max(c => c.LastTestedAt) ?? DateTime.MinValue,
                    ["SystemStatus"] = configs.Any(c => c.IsActive) ? "Operativo" : "Sin configurar"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system stats");
                return new Dictionary<string, object>();
            }
        }
    }
}