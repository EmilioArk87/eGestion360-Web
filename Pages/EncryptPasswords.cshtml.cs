using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;

namespace eGestion360Web.Pages
{
    public class EncryptPasswordsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;

        public EncryptPasswordsModel(ApplicationDbContext context, IPasswordService passwordService)
        {
            _context = context;
            _passwordService = passwordService;
        }

        public List<string> Messages { get; set; } = new List<string>();
        public bool HasExecuted { get; set; } = false;

        public async Task<IActionResult> OnGetAsync()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                HasExecuted = true;
                var usersUpdated = 0;

                // Obtener todos los usuarios con contraseñas en texto plano
                var users = await _context.Users
                    .Where(u => !u.Password.StartsWith("$2"))
                    .ToListAsync();

                Messages.Add($"📊 Encontrados {users.Count} usuarios con contraseñas en texto plano");

                foreach (var user in users)
                {
                    var originalPassword = user.Password;
                    
                    // Hashear la contraseña
                    user.Password = _passwordService.HashPassword(originalPassword);
                    
                    Messages.Add($"🔐 Usuario '{user.Username}': Contraseña encriptada (era: '{originalPassword}')");
                    usersUpdated++;
                }

                if (usersUpdated > 0)
                {
                    await _context.SaveChangesAsync();
                    Messages.Add($"✅ {usersUpdated} contraseñas encriptadas exitosamente con BCrypt");
                }
                else
                {
                    Messages.Add("ℹ️ No se encontraron contraseñas en texto plano para encriptar");
                }

            }
            catch (Exception ex)
            {
                Messages.Add($"❌ Error durante la encriptación: {ex.Message}");
            }

            return Page();
        }
    }
}