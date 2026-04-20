using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<ForgotPasswordModel> _logger;

        public ForgotPasswordModel(ApplicationDbContext context, IEmailService emailService, ILogger<ForgotPasswordModel> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        [BindProperty]
        [Required(ErrorMessage = "El correo electrónico es requerido")]
        [EmailAddress(ErrorMessage = "Ingresa un correo electrónico válido")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = string.Empty;

        public bool EmailSent { get; set; }
        public string? ErrorMessage { get; set; }

        public IActionResult OnGet()
        {
            if (HttpContext.Session.GetString("UserId") != null)
                return RedirectToPage("/MainMenu");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.IsActive && u.Email == Email.Trim());

            if (user != null)
            {
                var tempPassword = GenerateTempPassword();
                user.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);
                user.RequirePasswordChange = true;

                try
                {
                    await _context.SaveChangesAsync();
                    await _emailService.SendPasswordResetEmailAsync(user.Email, user.Username, tempPassword);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar correo de recuperación a {Email}", Email);
                    ErrorMessage = "Ocurrió un error al enviar el correo. Por favor intenta nuevamente.";
                    return Page();
                }
            }

            // Siempre mostrar mensaje de éxito para no revelar si el email existe
            EmailSent = true;
            return Page();
        }

        private static string GenerateTempPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
            var random = new Random();
            return new string(Enumerable.Range(0, 10).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }
    }
}
