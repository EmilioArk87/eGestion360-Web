namespace eGestion360Web.Services
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string toEmail, string username, string tempPassword);
    }
}
