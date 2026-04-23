namespace eGestion360Web.Services
{
    public interface IEmailService
    {
        Task<bool> SendPasswordResetCodeAsync(string email, string username, string code);
        Task<bool> SendPasswordResetConfirmationAsync(string email, string username);
        Task<bool> SendTestEmailAsync(string toEmail, string subject, string message);
    }
}
