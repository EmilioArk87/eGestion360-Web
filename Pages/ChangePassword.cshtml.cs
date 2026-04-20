using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    public class ChangePasswordModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ChangePasswordModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        [Required(ErrorMessage = "La contraseña actual es requerida")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña actual")]
        public string CurrentPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La nueva contraseña es requerida")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debes confirmar la nueva contraseña")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar nueva contraseña")]
        [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return RedirectToPage("/Login");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
            {
                return RedirectToPage("/Login");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToPage("/Login");
            }

            if (!BCrypt.Net.BCrypt.Verify(CurrentPassword, user.Password))
            {
                ModelState.AddModelError(nameof(CurrentPassword), "La contraseña actual es incorrecta.");
                return Page();
            }

            if (BCrypt.Net.BCrypt.Verify(NewPassword, user.Password))
            {
                ModelState.AddModelError(nameof(NewPassword), "La nueva contraseña debe ser distinta a la actual.");
                return Page();
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.RequirePasswordChange = false;
            await _context.SaveChangesAsync();

            HttpContext.Session.Clear();
            TempData["PasswordChanged"] = "Contraseña actualizada correctamente. Inicia sesión con tu nueva contraseña.";
            return RedirectToPage("/Login");
        }
    }
}
