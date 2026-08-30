using System.Security.Cryptography;
using System.Text;

namespace eGestion360Web.Services
{
    public interface IEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EncryptionService> _logger;
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            // Clave e IV vienen de las variables de entorno Encryption__Key / Encryption__IV.
            // NO poner un valor por defecto: con otra clave, Decrypt() devuelve basura en
            // silencio y las contraseñas SMTP ya guardadas en la BD quedan irrecuperables.
            // Mejor no arrancar que arrancar cifrando con la clave equivocada.
            var keyString = _configuration["Encryption:Key"];
            var ivString = _configuration["Encryption:IV"];

            if (string.IsNullOrWhiteSpace(keyString) || string.IsNullOrWhiteSpace(ivString))
            {
                throw new InvalidOperationException(
                    "Faltan las variables de entorno Encryption__Key y/o Encryption__IV. " +
                    "Ver 1 - Documetacion/CONFIGURACION_SECRETOS.md. Deben coincidir exactamente " +
                    "con las que se usaron para cifrar los datos existentes.");
            }


            // Asegurar que la clave tenga 32 bytes (256 bits) y IV tenga 16 bytes
            _key = PadOrTruncate(Encoding.UTF8.GetBytes(keyString), 32);
            _iv = PadOrTruncate(Encoding.UTF8.GetBytes(ivString), 16);
        }

        public string Encrypt(string plainText)
        {
            try
            {
                if (string.IsNullOrEmpty(plainText))
                    return string.Empty;

                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = _iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var encryptor = aes.CreateEncryptor())
                    using (var msEncrypt = new MemoryStream())
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (var swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                        swEncrypt.Close();
                        
                        var encrypted = msEncrypt.ToArray();
                        return Convert.ToBase64String(encrypted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encriptando texto");
                return string.Empty;
            }
        }

        public string Decrypt(string cipherText)
        {
            try
            {
                if (string.IsNullOrEmpty(cipherText))
                    return string.Empty;

                var cipherBytes = Convert.FromBase64String(cipherText);

                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = _iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var msDecrypt = new MemoryStream(cipherBytes))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error desencriptando texto");
                return string.Empty;
            }
        }

        private byte[] PadOrTruncate(byte[] source, int targetLength)
        {
            var result = new byte[targetLength];
            
            if (source.Length >= targetLength)
            {
                // Si es muy largo, truncar
                Array.Copy(source, 0, result, 0, targetLength);
            }
            else
            {
                // Si es muy corto, copiar y rellenar con ceros
                Array.Copy(source, 0, result, 0, source.Length);
                // El resto queda en ceros por defecto
            }
            
            return result;
        }
    }
}