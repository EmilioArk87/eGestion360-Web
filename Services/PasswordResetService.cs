using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Services
{
    public interface IPasswordResetService
    {
        Task<string> GenerateResetCodeAsync(string email, string ipAddress);
        Task<bool> ValidateResetCodeAsync(string email, string code);
        Task<User?> GetUserByResetCodeAsync(string email, string code);
        Task<bool> MarkCodeAsUsedAsync(string email, string code);
        Task CleanupExpiredCodesAsync();
        string GenerateNumericCode();
    }

    public class PasswordResetService : IPasswordResetService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PasswordResetService> _logger;
        private readonly Random _random;

        public PasswordResetService(ApplicationDbContext context, ILogger<PasswordResetService> logger)
        {
            _context = context;
            _logger = logger;
            _random = new Random();
        }

        public async Task<string> GenerateResetCodeAsync(string email, string ipAddress)
        {
            try
            {
                // Buscar usuario por email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

                if (user == null)
                {
                    // No revelar si el email existe o no por seguridad
                    return string.Empty;
                }

                // Invalidar códigos anteriores no usados del mismo usuario
                var previousCodes = await _context.PasswordResetCodes
                    .Where(c => c.UserId == user.Id && !c.IsUsed)
                    .ToListAsync();

                foreach (var oldCode in previousCodes)
                {
                    oldCode.IsUsed = true;
                    oldCode.UsedAt = DateTime.UtcNow;
                }

                // Generar nuevo código
                var newCode = GenerateNumericCode();
                var resetCode = new PasswordResetCode
                {
                    UserId = user.Id,
                    Code = newCode,
                    Email = email.ToLower(),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                    IsUsed = false,
                    IpAddress = ipAddress
                };

                _context.PasswordResetCodes.Add(resetCode);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Código de reset generado para usuario {UserId} desde IP {IpAddress}", 
                    user.Id, ipAddress);

                return newCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando código de reset para {Email}", email);
                return string.Empty;
            }
        }

        public async Task<bool> ValidateResetCodeAsync(string email, string code)
        {
            try
            {
                var resetCode = await _context.PasswordResetCodes
                    .Include(rc => rc.User)
                    .FirstOrDefaultAsync(rc => 
                        rc.Email.ToLower() == email.ToLower() &&
                        rc.Code == code &&
                        !rc.IsUsed &&
                        rc.ExpiresAt > DateTime.UtcNow);

                return resetCode != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validando código de reset");
                return false;
            }
        }

        public async Task<User?> GetUserByResetCodeAsync(string email, string code)
        {
            try
            {
                var resetCode = await _context.PasswordResetCodes
                    .Include(rc => rc.User)
                    .FirstOrDefaultAsync(rc => 
                        rc.Email.ToLower() == email.ToLower() &&
                        rc.Code == code &&
                        !rc.IsUsed &&
                        rc.ExpiresAt > DateTime.UtcNow);

                return resetCode?.User;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo usuario por código de reset");
                return null;
            }
        }

        public async Task<bool> MarkCodeAsUsedAsync(string email, string code)
        {
            try
            {
                var resetCode = await _context.PasswordResetCodes
                    .FirstOrDefaultAsync(rc => 
                        rc.Email.ToLower() == email.ToLower() &&
                        rc.Code == code &&
                        !rc.IsUsed);

                if (resetCode != null)
                {
                    resetCode.IsUsed = true;
                    resetCode.UsedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Código de reset marcado como usado para {Email}", email);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marcando código como usado");
                return false;
            }
        }

        public async Task CleanupExpiredCodesAsync()
        {
            try
            {
                var expiredCodes = await _context.PasswordResetCodes
                    .Where(rc => rc.ExpiresAt <= DateTime.UtcNow || rc.CreatedAt <= DateTime.UtcNow.AddDays(-7))
                    .ToListAsync();

                if (expiredCodes.Any())
                {
                    _context.PasswordResetCodes.RemoveRange(expiredCodes);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Eliminados {Count} códigos expirados", expiredCodes.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error limpiando códigos expirados");
            }
        }

        public string GenerateNumericCode()
        {
            // Generar código de 6 dígitos
            return _random.Next(100000, 999999).ToString();
        }
    }
}