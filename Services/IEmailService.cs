namespace eGestion360Web.Services
{
    public interface IEmailService
    {
        Task<bool> SendPasswordResetCodeAsync(string email, string username, string code);
        Task<bool> SendPasswordResetConfirmationAsync(string email, string username);
        Task<bool> SendTestEmailAsync(string toEmail, string subject, string message);

        /// <summary>
        /// Envío genérico: <paramref name="htmlContent"/> va dentro de la plantilla de eGestion360, con la misma
        /// configuración SMTP que el resto de correos. Lo usan las alertas de procesos (tasas de cambio).
        /// </summary>
        Task<bool> SendHtmlEmailAsync(string toEmail, string subject, string htmlContent);
    }
}
