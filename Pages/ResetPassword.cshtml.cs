using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Models;
using eGestion360Web.Data;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    public class ResetPasswordModel : PageModel
    {
        private readonly IPasswordResetService _passwordResetService;
        private readonly IPasswordService _passwordService;
        private readonly IEmailService _emailService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ResetPasswordModel> _logger;

        public ResetPasswordModel(
            IPasswordResetService passwordResetService,
            IPasswordService passwordService,
            IEmailService emailService,
            ApplicationDbContext context,
            ILogger<ResetPasswordModel> logger)
        {
            _passwordResetService = passwordResetService;
            _passwordService = passwordService;
            _emailService = emailService;
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe tener 6 dígitos")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "El código debe contener solo números")]
        [Display(Name = "Código de Verificación")]
        public string VerificationCode { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La nueva contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva Contraseña")]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debe confirmar la contraseña")]
        [Compare("NewPassword", ErrorMessage = "Las contraseñas no coinciden")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar Nueva Contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public bool PasswordReset { get; set; } = false;
        public string? UserUsername { get; set; }

        public IActionResult OnGet(string? email)
        {
            // Si ya está logueado, redirigir al menu
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToPage("/MainMenu");
            }

            if (!string.IsNullOrEmpty(email))
            {
                Email = email;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Validar el código
                    var isValidCode = await _passwordResetService.ValidateResetCodeAsync(Email, VerificationCode);

                    if (!isValidCode)
                    {
                        ModelState.AddModelError("VerificationCode", "Código inválido, expirado o ya utilizado.");
                        return Page();
                    }

                    // Obtener usuario por código
                    var user = await _passwordResetService.GetUserByResetCodeAsync(Email, VerificationCode);

                    if (user == null)
                    {
                        ModelState.AddModelError("", "Error interno. Solicita un nuevo código.");
                        return Page();
                    }

                    // Actualizar contraseña
                    user.Password = _passwordService.HashPassword(NewPassword);
                    _context.Users.Update(user);

                    // Marcar código como usado
                    await _passwordResetService.MarkCodeAsUsedAsync(Email, VerificationCode);

                    // Guardar cambios
                    await _context.SaveChangesAsync();

                    // Enviar email de confirmación
                    await _emailService.SendPasswordResetConfirmationAsync(Email, user.Username);

                    UserUsername = user.Username;
                    PasswordReset = true;

                    _logger.LogInformation("Contraseña reseteada exitosamente para usuario {UserId} ({Username})", 
                        user.Id, user.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reseteando contraseña para {Email}", Email);
                    ModelState.AddModelError("", "Error interno del servidor. Intenta más tarde.");
                }
            }

            return Page();
        }
    }
}