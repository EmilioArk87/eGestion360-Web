using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;

        public RegisterModel(ApplicationDbContext context, IPasswordService passwordService)
        {
            _context = context;
            _passwordService = passwordService;
        }

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [StringLength(50, ErrorMessage = "El nombre de usuario no puede tener más de 50 caracteres")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [StringLength(100, ErrorMessage = "El email no puede tener más de 100 caracteres")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debe confirmar la contraseña")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden")]
        [Display(Name = "Confirmar Contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            // Si el usuario ya ha iniciado sesión, redirigir al menú principal
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
                // Verificar si el username ya existe
                if (await _context.Users.AnyAsync(u => u.Username == Username))
                {
                    ModelState.AddModelError("Username", "Este nombre de usuario ya está en uso");
                    return Page();
                }

                // Verificar si el email ya existe
                if (await _context.Users.AnyAsync(u => u.Email == Email))
                {
                    ModelState.AddModelError("Email", "Este email ya está registrado");
                    return Page();
                }

                // Crear nuevo usuario con contraseña hasheada
                var user = new User
                {
                    Username = Username,
                    Email = Email,
                    Password = _passwordService.HashPassword(Password),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Redirigir al login con mensaje de éxito
                TempData["SuccessMessage"] = "Usuario registrado exitosamente. Puede iniciar sesión.";
                return RedirectToPage("/Login");
            }

            return Page();
        }
    }
}