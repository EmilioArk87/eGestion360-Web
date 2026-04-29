using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;

        public CreateModel(ApplicationDbContext context, IPasswordService passwordService)
        {
            _context         = context;
            _passwordService = passwordService;
        }

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [StringLength(50)]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [StringLength(100)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [BindProperty]
        [Display(Name = "Solicitar cambio de contraseña al ingresar")]
        public bool RequirePasswordChange { get; set; } = false;

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            if (!ModelState.IsValid) return Page();

            if (await _context.Users.AnyAsync(u => u.Username == Username.Trim()))
            {
                ModelState.AddModelError(nameof(Username), "Ese nombre de usuario ya está en uso.");
                return Page();
            }
            if (await _context.Users.AnyAsync(u => u.Email == Email.Trim().ToLowerInvariant()))
            {
                ModelState.AddModelError(nameof(Email), "Ese email ya está registrado.");
                return Page();
            }

            var user = new User
            {
                Username              = Username.Trim(),
                Email                 = Email.Trim().ToLowerInvariant(),
                Password              = _passwordService.HashPassword(Password),
                IsActive              = IsActive,
                RequirePasswordChange = RequirePasswordChange,
                CreatedAt             = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' creado correctamente.";
            return RedirectToPage("/UserManagement");
        }
    }
}
