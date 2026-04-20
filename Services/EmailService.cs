using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace eGestion360Web.Services
{
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
    }

    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _settings = configuration.GetSection("EmailSettings").Get<EmailSettings>()
                        ?? throw new InvalidOperationException("EmailSettings no configurado en appsettings.json");
            _logger = logger;
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string username, string tempPassword)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            message.To.Add(new MailboxAddress(username, toEmail));
            message.Subject = "eGestion360 - Recuperación de contraseña";

            message.Body = new TextPart("html")
            {
                Text = $"""
                    <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;">
                        <div style="background-color: #0d6efd; padding: 20px; text-align: center;">
                            <h2 style="color: white; margin: 0;">eGestion360</h2>
                            <p style="color: #cfe2ff; margin: 5px 0 0;">Sistema de Gestión Web</p>
                        </div>
                        <div style="padding: 30px; border: 1px solid #dee2e6; border-top: none;">
                            <h3>Recuperación de contraseña</h3>
                            <p>Hola <strong>{username}</strong>,</p>
                            <p>Recibimos una solicitud para restablecer tu contraseña. Tu contraseña temporal es:</p>
                            <div style="background-color: #f8f9fa; border: 1px solid #dee2e6; border-radius: 4px;
                                        padding: 15px; text-align: center; margin: 20px 0;">
                                <span style="font-size: 20px; font-weight: bold; letter-spacing: 2px;
                                             font-family: monospace;">{tempPassword}</span>
                            </div>
                            <p>Por seguridad, <strong>cambia esta contraseña</strong> en tu próximo inicio de sesión.</p>
                            <p>Si no solicitaste este cambio, puedes ignorar este correo.</p>
                            <hr style="border: none; border-top: 1px solid #dee2e6; margin: 20px 0;">
                            <p style="color: #6c757d; font-size: 12px; margin: 0;">
                                &copy; {DateTime.Now.Year} SIP Tecnología - Todos los derechos reservados
                            </p>
                        </div>
                    </div>
                    """
            };

            using var client = new SmtpClient();
            try
            {
                // Puerto 465 = SSL directo; puerto 587 = STARTTLS
                var socketOptions = _settings.SmtpPort == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;
                await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, socketOptions);
                await client.AuthenticateAsync(_settings.SmtpUser, _settings.SmtpPassword);
                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}
