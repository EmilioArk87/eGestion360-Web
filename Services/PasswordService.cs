using BCrypt.Net;

namespace eGestion360Web.Services
{
    public interface IPasswordService
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
    }

    public class PasswordService : IPasswordService
    {
        public string HashPassword(string password)
        {
            // Usar BCrypt con factor de trabajo 12 (recomendado para producción)
            return BCrypt.Net.BCrypt.HashPassword(password, 12);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch
            {
                // Si hay error en la verificación, retornar false
                return false;
            }
        }
    }
}