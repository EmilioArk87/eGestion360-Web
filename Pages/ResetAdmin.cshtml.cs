using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Pages
{
    public class ResetAdminModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ResetAdminModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public string Message { get; set; } = "";

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
                
                if (adminUser != null)
                {
                    // Resetear contraseña a texto plano
                    adminUser.Password = "admin123";
                    _context.Users.Update(adminUser);
                    await _context.SaveChangesAsync();
                    
                    Message = "✅ Contraseña del admin reseteada exitosamente a 'admin123' en texto plano. Ahora puedes hacer login y se convertirá automáticamente a hash.";
                }
                else
                {
                    Message = "❌ No se encontró el usuario admin.";
                }
            }
            catch (Exception ex)
            {
                Message = $"❌ Error: {ex.Message}";
            }

            return Page();
        }
    }
}