using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EditModel(ApplicationDbContext context) => _context = context;

        [BindProperty]
        public int Id { get; set; }

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
        [Display(Name = "Activo")]
        public bool IsActive { get; set; }

        [BindProperty]
        [Display(Name = "Solicitar cambio de contraseña al ingresar")]
        public bool RequirePasswordChange { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            Id                    = user.Id;
            Username              = user.Username;
            Email                 = user.Email;
            IsActive              = user.IsActive;
            RequirePasswordChange = user.RequirePasswordChange;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))        return RedirectToPage("/MainMenu");

            if (!ModelState.IsValid) return Page();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == Id);
            if (user == null) return NotFound();

            // Verificar unicidad de username y email (excluyendo el registro actual)
            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Username == Username))
            {
                ModelState.AddModelError(nameof(Username), "Ese nombre de usuario ya está en uso.");
                return Page();
            }
            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Email == Email))
            {
                ModelState.AddModelError(nameof(Email), "Ese email ya está registrado.");
                return Page();
            }

            user.Username              = Username.Trim();
            user.Email                 = Email.Trim().ToLowerInvariant();
            user.IsActive              = IsActive;
            user.RequirePasswordChange = RequirePasswordChange;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' actualizado correctamente.";
            return RedirectToPage("/UserManagement");
        }
    }
}
