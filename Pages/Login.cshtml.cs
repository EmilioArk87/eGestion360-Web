using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Pages
{
    public class LoginModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public LoginModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Recordarme")]
        public bool RememberMe { get; set; }

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
                var submittedUsername = Username.Trim();
                var normalizedUsername = submittedUsername.ToUpperInvariant();

                var user = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        u.IsActive &&
                        (u.Username == submittedUsername || u.Username.ToUpper() == normalizedUsername));

                if (user != null && VerifyPassword(user, Password))
                {
                    // Almacenar información del usuario en la sesión
                    HttpContext.Session.SetString("UserId", user.Id.ToString());
                    HttpContext.Session.SetString("Username", user.Username);
                    HttpContext.Session.SetString("Email", user.Email);

                    if (user.RequirePasswordChange)
                    {
                        return RedirectToPage("/ChangePassword");
                    }

                    return RedirectToPage("/MainMenu");
                }
                else
                {
                    ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
                }
            }

            return Page();
        }

        private static bool VerifyPassword(User user, string plainPassword)
        {
            // Users table stores bcrypt hashes ($2a$, $2b$, $2y$) in Password.
            if (!string.IsNullOrEmpty(user.Password))
            {
                if (user.Password.StartsWith("$2", StringComparison.Ordinal))
                {
                    try
                    {
                        return BCrypt.Net.BCrypt.Verify(plainPassword, user.Password);
                    }
                    catch
                    {
                        return false;
                    }
                }

                return string.Equals(user.Password, plainPassword, StringComparison.Ordinal);
            }

            // Keep hashed-password support in case data source changes.
            if (user.PasswordHash == null || user.PasswordHash.Length == 0)
            {
                return false;
            }

            var algorithm = (user.PasswordAlgorithm ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(algorithm))
            {
                return false;
            }

            // Support common PBKDF2 variants such as PBKDF2, PBKDF2-SHA256, or PBKDF2-SHA512-100000.
            if (algorithm.Contains("PBKDF2", StringComparison.Ordinal))
            {
                return VerifyPbkdf2(user.PasswordHash, user.PasswordSalt, plainPassword, algorithm);
            }

            if (algorithm.Contains("SHA512", StringComparison.Ordinal))
            {
                return VerifySha(plainPassword, user.PasswordSalt, user.PasswordHash, SHA512.HashData);
            }

            if (algorithm.Contains("SHA256", StringComparison.Ordinal))
            {
                return VerifySha(plainPassword, user.PasswordSalt, user.PasswordHash, SHA256.HashData);
            }

            return false;
        }

        private static bool VerifyPbkdf2(byte[] storedHash, byte[]? salt, string plainPassword, string algorithm)
        {
            if (salt == null || salt.Length == 0)
            {
                return false;
            }

            var iterations = 100000;
            var match = Regex.Match(algorithm, @"(\d{4,7})");
            if (match.Success && int.TryParse(match.Value, out var parsedIterations))
            {
                iterations = parsedIterations;
            }

            var hashAlgorithm = HashAlgorithmName.SHA256;
            if (algorithm.Contains("SHA512", StringComparison.Ordinal))
            {
                hashAlgorithm = HashAlgorithmName.SHA512;
            }
            else if (algorithm.Contains("SHA1", StringComparison.Ordinal))
            {
                hashAlgorithm = HashAlgorithmName.SHA1;
            }

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainPassword),
                salt,
                iterations,
                hashAlgorithm,
                storedHash.Length);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }

        private static bool VerifySha(
            string plainPassword,
            byte[]? salt,
            byte[] storedHash,
            Func<byte[], byte[]> hashFunc)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(plainPassword);

            // Accept either password+salt or salt+password to tolerate legacy implementations.
            if (salt != null && salt.Length > 0)
            {
                var passwordThenSalt = new byte[passwordBytes.Length + salt.Length];
                Buffer.BlockCopy(passwordBytes, 0, passwordThenSalt, 0, passwordBytes.Length);
                Buffer.BlockCopy(salt, 0, passwordThenSalt, passwordBytes.Length, salt.Length);

                var saltThenPassword = new byte[salt.Length + passwordBytes.Length];
                Buffer.BlockCopy(salt, 0, saltThenPassword, 0, salt.Length);
                Buffer.BlockCopy(passwordBytes, 0, saltThenPassword, salt.Length, passwordBytes.Length);

                var hash1 = hashFunc(passwordThenSalt);
                if (hash1.Length == storedHash.Length && CryptographicOperations.FixedTimeEquals(hash1, storedHash))
                {
                    return true;
                }

                var hash2 = hashFunc(saltThenPassword);
                if (hash2.Length == storedHash.Length && CryptographicOperations.FixedTimeEquals(hash2, storedHash))
                {
                    return true;
                }
            }

            var plainHash = hashFunc(passwordBytes);
            return plainHash.Length == storedHash.Length && CryptographicOperations.FixedTimeEquals(plainHash, storedHash);
        }
    }
}
