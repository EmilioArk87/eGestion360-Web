using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace eGestion360Web.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IEmailConfigurationService _emailConfigService;

        public EmailService(ILogger<EmailService> logger, IConfiguration configuration, IEmailConfigurationService emailConfigService)
        {
            _logger = logger;
            _configuration = configuration;
            _emailConfigService = emailConfigService;
        }

        public async Task<bool> SendPasswordResetCodeAsync(string email, string username, string code)
        {
            try
            {
                var emailContent = GeneratePasswordResetEmail(username, code);
                var subject = "Código de Restablecimiento de Contraseña - eGestion360";
                
                var result = await SendEmailAsync(email, username, subject, emailContent);
                
                if (result)
                {
                    _logger.LogInformation("Código de reset enviado exitosamente a {Email}", email);
                }
                else
                {
                    _logger.LogError("Error enviando código de reset a {Email}", email);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email de reset a {Email}", email);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetConfirmationAsync(string email, string username)
        {
            try
            {
                var emailContent = GeneratePasswordResetConfirmationEmail(username);
                var subject = "Contraseña Actualizada - eGestion360";
                
                var result = await SendEmailAsync(email, username, subject, emailContent);
                
                if (result)
                {
                    _logger.LogInformation("Confirmación de reset enviada exitosamente a {Email}", email);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando confirmación a {Email}", email);
                return false;
            }
        }

        private async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            // Intentar obtener configuración desde base de datos primero
            var dbConfig = await _emailConfigService.GetActiveConfigurationAsync();
            
            if (dbConfig != null && dbConfig.IsConfigured)
            {
                return await SendEmailWithDbConfigAsync(dbConfig, toEmail, toName, subject, htmlContent);
            }
            
            // Fallback a configuración desde appsettings.json
            var emailProvider = _configuration["EmailSettings:Provider"];
            
            if (emailProvider == "Simulation" || string.IsNullOrEmpty(emailProvider))
            {
                return await SendSimulatedEmailAsync(toEmail, toName, subject, htmlContent);
            }
            
            return emailProvider.ToLower() switch
            {
                "gmail" => await SendGmailAsync(toEmail, toName, subject, htmlContent),
                "sendgrid" => await SendSendGridAsync(toEmail, toName, subject, htmlContent),
                "smtp" => await SendSmtpAsync(toEmail, toName, subject, htmlContent),
                _ => await SendSimulatedEmailAsync(toEmail, toName, subject, htmlContent)
            };
        }

        private async Task<bool> SendEmailWithDbConfigAsync(Models.EmailConfiguration config, string toEmail, string toName, string subject, string htmlContent)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(config.FromName, config.FromEmail));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = ConvertToHtml(htmlContent);
                bodyBuilder.TextBody = htmlContent;
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    // Puerto 465 = SSL implícito (SslOnConnect), puerto 587 = STARTTLS, resto sin SSL
                    SecureSocketOptions secureOptions;
                    if (config.SmtpPort == 465)
                        secureOptions = SecureSocketOptions.SslOnConnect;
                    else if (config.UseSsl)
                        secureOptions = SecureSocketOptions.StartTls;
                    else
                        secureOptions = SecureSocketOptions.None;

                    await client.ConnectAsync(config.SmtpHost, config.SmtpPort, secureOptions);
                    
                    // Desencriptar la contraseña para usar en SMTP
                    var plainPassword = await _emailConfigService.DecryptPasswordAsync(config.PasswordHash);
                    
                    if (string.IsNullOrEmpty(plainPassword))
                    {
                        _logger.LogError("No se pudo desencriptar la contraseña para {ProfileName}", config.ProfileName);
                        return false;
                    }
                    
                    await client.AuthenticateAsync(config.Username, plainPassword);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                    
                    _logger.LogInformation("Email enviado exitosamente usando configuración {ProfileName}", config.ProfileName);
                    await _emailConfigService.UpdateTestStatsAsync(config.Id, true);
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email con configuración de BD para {ProfileName}", config.ProfileName);
                await _emailConfigService.UpdateTestStatsAsync(config.Id, false);
                return false;
            }
        }

        private async Task<bool> SendSimulatedEmailAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            await Task.Delay(1000);
            
            Console.WriteLine($"📧 EMAIL SIMULADO 📧");
            Console.WriteLine($"Para: {toEmail} ({toName})");
            Console.WriteLine($"Asunto: {subject}");
            Console.WriteLine($"Contenido:");
            Console.WriteLine(htmlContent);
            Console.WriteLine($"────────────────────────────────────────");
            
            return true;
        }

        private async Task<bool> SendGmailAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(
                    _configuration["EmailSettings:FromName"], 
                    _configuration["EmailSettings:FromEmail"]
                ));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = ConvertToHtml(htmlContent);
                bodyBuilder.TextBody = htmlContent;
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                    await client.AuthenticateAsync(
                        _configuration["EmailSettings:Username"], 
                        _configuration["EmailSettings:Password"]
                    );
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email por Gmail");
                return false;
            }
        }

        private async Task<bool> SendSmtpAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(
                    _configuration["EmailSettings:FromName"], 
                    _configuration["EmailSettings:FromEmail"]
                ));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = ConvertToHtml(htmlContent);
                bodyBuilder.TextBody = htmlContent;
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    var smtpHost = _configuration["EmailSettings:SmtpHost"];
                    var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
                    var useSsl = bool.Parse(_configuration["EmailSettings:UseSsl"] ?? "true");

                    // Puerto 465 = SSL implícito (SslOnConnect), puerto 587 = STARTTLS, resto sin SSL
                    SecureSocketOptions secureOptions;
                    if (smtpPort == 465)
                        secureOptions = SecureSocketOptions.SslOnConnect;
                    else if (useSsl)
                        secureOptions = SecureSocketOptions.StartTls;
                    else
                        secureOptions = SecureSocketOptions.None;

                    await client.ConnectAsync(smtpHost, smtpPort, secureOptions);
                    await client.AuthenticateAsync(
                        _configuration["EmailSettings:Username"],
                        _configuration["EmailSettings:Password"]
                    );
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email por SMTP");
                return false;
            }
        }

        private async Task<bool> SendSendGridAsync(string toEmail, string toName, string subject, string htmlContent)
        {
            // TODO: Implementar SendGrid si se necesita
            _logger.LogWarning("SendGrid no implementado aún, usando simulación");
            return await SendSimulatedEmailAsync(toEmail, toName, subject, htmlContent);
        }

        private string ConvertToHtml(string textContent)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #007bff; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background: #f9f9f9; }}
        .code {{ font-size: 24px; font-weight: bold; color: #007bff; text-align: center; padding: 15px; background: white; border: 2px dashed #007bff; margin: 20px 0; }}
        .footer {{ background: #6c757d; color: white; padding: 15px; text-align: center; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>🏢 eGestion360</h2>
        </div>
        <div class='content'>
            {textContent.Replace("\n", "<br>")}
        </div>
        <div class='footer'>
            SIP Tecnología - Este es un email automático, no responder.
        </div>
    </div>
</body>
</html>";
        }

        private string GeneratePasswordResetEmail(string username, string code)
        {
            return $@"
<h3>Hola {username},</h3>

<p>Has solicitado restablecer tu contraseña en <strong>eGestion360</strong>.</p>

<div class='code'>{code}</div>

<div style='background: #fff3cd; padding: 15px; border-left: 4px solid #ffc107; margin: 20px 0;'>
    <strong>⚠️ Importante:</strong>
    <ul>
        <li>🔐 Este código es válido por <strong>15 minutos solamente</strong></li>
        <li>🔒 Solo se puede usar una vez</li>
        <li>❌ Si no solicitaste este cambio, puedes ignorar este email</li>
    </ul>
</div>

<p><strong>Para completar el proceso:</strong></p>
<ol>
    <li>Ingresa este código en la página de restablecimiento</li>
    <li>Crea tu nueva contraseña</li>
</ol>

<p>Saludos,<br>
<strong>Equipo eGestion360</strong><br>
SIP Tecnología</p>";
        }

        private string GeneratePasswordResetConfirmationEmail(string username)
        {
            return $@"
<h3>Hola {username},</h3>

<p>Tu contraseña ha sido <strong>actualizada exitosamente</strong> en eGestion360.</p>

<div style='background: #d4edda; padding: 15px; border-left: 4px solid #28a745; margin: 20px 0;'>
    <strong>✅ Cambio completado:</strong>
    <ul>
        <li>📅 Fecha: {DateTime.Now:dd/MM/yyyy HH:mm}</li>
        <li>🔒 Tu cuenta está segura</li>
        <li>✨ Ya puedes iniciar sesión con tu nueva contraseña</li>
    </ul>
</div>

<div style='background: #f8d7da; padding: 15px; border-left: 4px solid #dc3545; margin: 20px 0;'>
    <strong>⚠️ Si no realizaste este cambio:</strong>
    <p>Contacta inmediatamente a soporte técnico.</p>
</div>

<p>Saludos,<br>
<strong>Equipo eGestion360</strong><br>
SIP Tecnología</p>";
        }

        public async Task<bool> SendTestEmailAsync(string toEmail, string subject, string message)
        {
            try
            {
                var emailContent = GenerateTestEmail(message);
                var result = await SendEmailAsync(toEmail, "", subject, emailContent);
                
                if (result)
                {
                    _logger.LogInformation("Email de prueba enviado exitosamente a {Email}", toEmail);
                }
                else
                {
                    _logger.LogError("Error enviando email de prueba a {Email}", toEmail);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando email de prueba a {Email}", toEmail);
                return false;
            }
        }

        private string GenerateTestEmail(string message)
        {
            return $@"
<h2 style='color: #28a745; margin-bottom: 20px;'>✅ Prueba de Configuración de Email</h2>

<div style='background: #d4edda; padding: 20px; border-left: 4px solid #28a745; margin: 20px 0; border-radius: 5px;'>
    <h3>🎯 ¡Configuración exitosa!</h3>
    <p>Su configuración de email está funcionando correctamente.</p>
</div>

<div style='background: #f8f9fa; padding: 15px; border: 1px solid #dee2e6; margin: 20px 0; border-radius: 5px;'>
    <h4>📋 Detalles de la prueba:</h4>
    <pre style='background: white; padding: 10px; border-radius: 3px; font-family: monospace; white-space: pre-wrap;'>{message}</pre>
</div>

<p>Este es un email de prueba generado automáticamente por el sistema <strong>eGestion360</strong>.</p>
<p>Si está recibiendo este mensaje, significa que la configuración SMTP está funcionando correctamente.</p>

<p>Saludos,<br>
<strong>Sistema eGestion360</strong><br>
SIP Tecnología</p>";
        }
    }
}