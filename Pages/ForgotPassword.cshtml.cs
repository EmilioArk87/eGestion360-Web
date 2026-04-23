using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Data;
using eGestion360Web.Services;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly IPasswordResetService _passwordResetService;
        private readonly IEmailService _emailService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ForgotPasswordModel> _logger;

        public ForgotPasswordModel(
            IPasswordResetService passwordResetService,
            IEmailService emailService,
            ApplicationDbContext context,
            ILogger<ForgotPasswordModel> logger)
        {
            _passwordResetService = passwordResetService;
            _emailService = emailService;
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        public bool EmailSent { get; set; } = false;

        public IActionResult OnGet()
        {
            // Si ya está logueado, redirigir al menu
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToPage("/MainMenu");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Obtener IP del cliente para auditoría
                    var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

                    // Generar código de reseteo
                    var resetCode = await _passwordResetService.GenerateResetCodeAsync(Email, clientIp);

                    if (!string.IsNullOrEmpty(resetCode))
                    {
                        // Obtener nombre real del usuario para personalizar el email
                        var user = await _context.Users
                            .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);
                        var displayName = user?.Username ?? "Usuario";

                        // Enviar email con código
                        var emailSent = await _emailService.SendPasswordResetCodeAsync(Email, displayName, resetCode);

                        if (emailSent)
                        {
                            EmailSent = true;
                            _logger.LogInformation("Código de reset enviado exitosamente a {Email} desde {IP}", Email, clientIp);
                        }
                        else
                        {
                            ModelState.AddModelError("", "Error enviando el email. Intenta nuevamente.");
                        }
                    }
                    else
                    {
                        // Por seguridad, siempre mostramos el mismo mensaje
                        // No revelamos si el email existe o no
                        EmailSent = true;
                        _logger.LogWarning("Intento de reset para email inexistente {Email} desde {IP}", Email, clientIp);
                    }

                    // Limpiar códigos expirados en background
                    _ = Task.Run(async () => await _passwordResetService.CleanupExpiredCodesAsync());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en proceso de reset para {Email}", Email);
                    ModelState.AddModelError("", "Error interno del servidor. Intenta más tarde.");
                }
            }

            return Page();
        }
    }
}