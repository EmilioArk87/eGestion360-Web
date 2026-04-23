using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Models;

namespace eGestion360Web.Pages
{
    public class ValidarEmailsModel : PageModel
    {
        private readonly IEmailService _emailService;
        private readonly IEmailConfigurationService _emailConfigService;
        private readonly ILogger<ValidarEmailsModel> _logger;

        public ValidarEmailsModel(IEmailService emailService, IEmailConfigurationService emailConfigService, ILogger<ValidarEmailsModel> logger)
        {
            _emailService = emailService;
            _emailConfigService = emailConfigService;
            _logger = logger;
        }

        [BindProperty]
        public EmailTestForm TestForm { get; set; } = new EmailTestForm();
        
        public List<EmailConfiguration> ConfiguracionesDisponibles { get; set; } = new();
        public EmailConfiguration? ConfiguracionActual { get; set; }
        public List<EmailTestResult> ResultadosPruebas { get; set; } = new();
        public string? MensajeResultado { get; set; }
        public bool PruebaExitosa { get; set; }

        public async Task OnGetAsync()
        {
            await CargarDatosAsync();
        }

        public async Task<IActionResult> OnPostEnviarPruebaSimpleAsync()
        {
            await CargarDatosAsync();
            
            if (!ModelState.IsValid)
                return Page();

            try
            {
                _logger.LogInformation("Iniciando prueba simple de email a {Email}", TestForm.EmailDestino);

                var subject = $"Prueba Simple de Email - {DateTime.Now:dd/MM/yyyy HH:mm}";
                var message = GenerateSimpleTestEmail();

                var result = await _emailService.SendTestEmailAsync(TestForm.EmailDestino, subject, message);
                
                var testResult = new EmailTestResult
                {
                    TipoPrueba = "Prueba Simple",
                    EmailDestino = TestForm.EmailDestino,
                    Asunto = subject,
                    Exitoso = result,
                    FechaHora = DateTime.Now,
                    ConfiguracionUsada = ConfiguracionActual?.ProfileName ?? "Configuración por defecto"
                };

                ResultadosPruebas.Insert(0, testResult);

                if (result)
                {
                    MensajeResultado = $"✅ Email de prueba enviado exitosamente a {TestForm.EmailDestino}";
                    PruebaExitosa = true;
                    _logger.LogInformation("Prueba simple exitosa para {Email}", TestForm.EmailDestino);
                }
                else
                {
                    MensajeResultado = $"❌ Error enviando email de prueba a {TestForm.EmailDestino}";
                    PruebaExitosa = false;
                    _logger.LogError("Prueba simple falló para {Email}", TestForm.EmailDestino);
                }
            }
            catch (Exception ex)
            {
                MensajeResultado = $"❌ Excepción: {ex.Message}";
                PruebaExitosa = false;
                _logger.LogError(ex, "Error en prueba simple de email");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostPruebaCompleteAsync()
        {
            await CargarDatosAsync();
            
            if (!ModelState.IsValid)
                return Page();

            try
            {
                _logger.LogInformation("Iniciando prueba completa de email a {Email}", TestForm.EmailDestino);

                var tests = new List<(string tipo, string asunto, string mensaje)>
                {
                    ("Reset Password", "Código de Reset - Prueba", GeneratePasswordResetTestEmail()),
                    ("Confirmación", "Confirmación de Cambio - Prueba", GenerateConfirmationTestEmail()),
                    ("HTML Complejo", "Prueba HTML Avanzada", GenerateAdvancedHtmlTestEmail()),
                    ("Texto Simple", "Prueba Texto Simple", "Este es un mensaje de texto simple sin HTML.")
                };

                int exitosos = 0;
                int fallidos = 0;

                foreach (var (tipo, asunto, mensaje) in tests)
                {
                    try
                    {
                        var result = await _emailService.SendTestEmailAsync(TestForm.EmailDestino, asunto, mensaje);
                        
                        var testResult = new EmailTestResult
                        {
                            TipoPrueba = tipo,
                            EmailDestino = TestForm.EmailDestino,
                            Asunto = asunto,
                            Exitoso = result,
                            FechaHora = DateTime.Now,
                            ConfiguracionUsada = ConfiguracionActual?.ProfileName ?? "Configuración por defecto"
                        };

                        ResultadosPruebas.Insert(0, testResult);

                        if (result) exitosos++; else fallidos++;
                        
                        // Esperar entre emails para evitar spam
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error en tipo de prueba {TipoPrueba}", tipo);
                        fallidos++;
                    }
                }

                MensajeResultado = $"📊 Prueba completa: {exitosos} exitosos, {fallidos} fallidos";
                PruebaExitosa = exitosos > 0;

                _logger.LogInformation("Prueba completa terminada: {Exitosos} exitosos, {Fallidos} fallidos", exitosos, fallidos);
            }
            catch (Exception ex)
            {
                MensajeResultado = $"❌ Error en prueba completa: {ex.Message}";
                PruebaExitosa = false;
                _logger.LogError(ex, "Error en prueba completa de email");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostValidarConfiguracionAsync(int configId)
        {
            await CargarDatosAsync();
            
            try
            {
                var config = await _emailConfigService.GetConfigurationByIdAsync(configId);
                if (config == null)
                {
                    MensajeResultado = "❌ Configuración no encontrada";
                    return Page();
                }

                var success = await _emailConfigService.TestConfigurationAsync(configId);
                
                if (success)
                {
                    MensajeResultado = $"✅ Configuración '{config.ProfileName}' validada exitosamente";
                    PruebaExitosa = true;
                }
                else
                {
                    MensajeResultado = $"❌ Error validando configuración '{config.ProfileName}'";
                    PruebaExitosa = false;
                }
                
                await CargarDatosAsync(); // Recargar para ver estadísticas actualizadas
            }
            catch (Exception ex)
            {
                MensajeResultado = $"❌ Error validando configuración: {ex.Message}";
                PruebaExitosa = false;
                _logger.LogError(ex, "Error validando configuración {ConfigId}", configId);
            }

            return Page();
        }

        private async Task CargarDatosAsync()
        {
            try
            {
                ConfiguracionesDisponibles = await _emailConfigService.GetAllConfigurationsAsync();
                ConfiguracionActual = await _emailConfigService.GetActiveConfigurationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando configuraciones de email");
            }
        }

        private string GenerateSimpleTestEmail()
        {
            return $@"
<h2>✅ Prueba de Email - eGestion360</h2>
<p>Este es un email de prueba enviado el <strong>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</strong></p>
<p>Si recibes este mensaje, el sistema de email está funcionando correctamente.</p>
<hr>
<p><strong>Configuración utilizada:</strong> {ConfiguracionActual?.ProfileName ?? "Por defecto"}</p>
<p><strong>Servidor SMTP:</strong> {ConfiguracionActual?.SmtpHost ?? "No especificado"}:{ConfiguracionActual?.SmtpPort ?? 0}</p>
<p>¡Saludos del sistema eGestion360! 🚀</p>";
        }

        private string GeneratePasswordResetTestEmail()
        {
            return $@"
<h3>🔑 Prueba de Reset de Contraseña</h3>
<p>Este es un email de prueba que simula el envío de código de reset de contraseña.</p>
<div style='font-size: 24px; font-weight: bold; color: #007bff; text-align: center; padding: 15px; background: white; border: 2px dashed #007bff; margin: 20px 0;'>
    TEST123
</div>
<p>⏰ <strong>Código válido por:</strong> 15 minutos</p>
<p>🔒 <strong>Uso único</strong></p>";
        }

        private string GenerateConfirmationTestEmail()
        {
            return $@"
<h3>✅ Prueba de Confirmación</h3>
<p>Este email simula una confirmación de cambio exitoso.</p>
<div style='background: #d4edda; padding: 15px; border-left: 4px solid #28a745; margin: 20px 0;'>
    <strong>✅ Operación Completada</strong><br>
    Tu solicitud ha sido procesada exitosamente.
</div>
<p>📅 <strong>Fecha:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}<br>
🔐 <strong>Nivel de seguridad:</strong> Alto</p>";
        }

        private string GenerateAdvancedHtmlTestEmail()
        {
            return $@"
<h2 style='color: #0d6efd;'>🧪 Prueba de HTML Avanzado</h2>
<table style='width: 100%; border-collapse: collapse;'>
    <tr style='background: #f8f9fa;'>
        <th style='border: 1px solid #dee2e6; padding: 8px;'>Campo</th>
        <th style='border: 1px solid #dee2e6; padding: 8px;'>Valor</th>
    </tr>
    <tr>
        <td style='border: 1px solid #dee2e6; padding: 8px;'>Fecha de Prueba</td>
        <td style='border: 1px solid #dee2e6; padding: 8px;'>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</td>
    </tr>
    <tr style='background: #f8f9fa;'>
        <td style='border: 1px solid #dee2e6; padding: 8px;'>Estado del Sistema</td>
        <td style='border: 1px solid #dee2e6; padding: 8px;'><span style='color: green; font-weight: bold;'>✅ Operativo</span></td>
    </tr>
</table>
<p style='text-align: center; margin: 20px 0;'>
    <a href='#' style='background: #0d6efd; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>
        🔗 Botón de Prueba
    </a>
</p>";
        }
    }

    public class EmailTestForm
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Email destino requerido")]
        [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Formato de email inválido")]
        [System.ComponentModel.DataAnnotations.Display(Name = "Email de Destino")]
        public string EmailDestino { get; set; } = string.Empty;
    }

    public class EmailTestResult
    {
        public string TipoPrueba { get; set; } = string.Empty;
        public string EmailDestino { get; set; } = string.Empty;
        public string Asunto { get; set; } = string.Empty;
        public bool Exitoso { get; set; }
        public DateTime FechaHora { get; set; }
        public string ConfiguracionUsada { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
}