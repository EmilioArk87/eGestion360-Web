using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin
{
    public class ResetPasswordModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;
        private readonly ILogger<ResetPasswordModel> _logger;

        public ResetPasswordModel(ApplicationDbContext context, IPasswordService passwordService, ILogger<ResetPasswordModel> logger)
        {
            _context = context;
            _passwordService = passwordService;
            _logger = logger;
        }

        public List<User> Users { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "La nueva contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        public int TargetUserId { get; set; }

        [BindProperty]
        public bool RequireChange { get; set; } = true;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToPage("/MainMenu");

            await CargarUsuarios();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToPage("/MainMenu");

            if (!ModelState.IsValid)
            {
                await CargarUsuarios();
                return Page();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == TargetUserId && u.IsActive);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Usuario no encontrado.";
                await CargarUsuarios();
                return Page();
            }

            user.Password = _passwordService.HashPassword(NewPassword);
            user.RequirePasswordChange = RequireChange;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            var adminUser = HttpContext.Session.GetString("Username") ?? "admin";
            _logger.LogInformation("Admin {Admin} reseteó la contraseña del usuario {Username} (ID:{UserId}). RequireChange={RequireChange}",
                adminUser, user.Username, user.Id, RequireChange);

            TempData["SuccessMessage"] = $"Contraseña de '{user.Username}' actualizada correctamente.";
            return RedirectToPage();
        }

        private async Task CargarUsuarios()
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
                query = query.Where(u => u.Username.Contains(Search) || u.Email.Contains(Search));

            Users = await query.OrderBy(u => u.Username).ToListAsync();
        }
    }
}
