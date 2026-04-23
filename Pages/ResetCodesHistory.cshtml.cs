using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages
{
    public class ResetCodesHistoryModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ResetCodesHistoryModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<PasswordResetCode> ResetCodes { get; set; } = new List<PasswordResetCode>();
        public int ActiveCodes { get; set; }
        public int ExpiredCodes { get; set; }
        public int UsedCodes { get; set; }

        public async Task OnGetAsync()
        {
            // Obtener todos los códigos con información del usuario
            ResetCodes = await _context.PasswordResetCodes
                .Include(rc => rc.User)
                .OrderByDescending(rc => rc.CreatedAt)
                .Take(50) // Últimos 50 registros
                .ToListAsync();

            // Calcular estadísticas
            var now = DateTime.UtcNow;
            ActiveCodes = ResetCodes.Count(rc => !rc.IsUsed && rc.ExpiresAt > now);
            ExpiredCodes = ResetCodes.Count(rc => !rc.IsUsed && rc.ExpiresAt <= now);
            UsedCodes = ResetCodes.Count(rc => rc.IsUsed);
        }
    }
}