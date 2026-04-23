using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Extensión del servicio de configuración de email con métodos específicos para Hostinger.es
    /// </summary>
    public static class HostingerEmailConfigurationExtensions
    {
        /// <summary>
        /// Configurar email de Hostinger.es usando procedimiento almacenado
        /// </summary>
        /// <param name="service">Servicio de configuración de email</param>
        /// <param name="emailUsuario">Email de la cuenta de Hostinger</param>
        /// <param name="contraseña">Contraseña de la cuenta</param>
        /// <param name="nombreRemitente">Nombre que aparecerá como remitente</param>
        /// <param name="nombrePerfil">Nombre del perfil de configuración</param>
        /// <param name="establecerPorDefecto">Si establecer como configuración por defecto</param>
        /// <param name="puerto">Puerto SMTP (587 o 465)</param>
        /// <param name="usarSSL">Usar SSL/TLS</param>
        /// <param name="creadoPor">Usuario que crea la configuración</param>
        /// <returns>Resultado de la configuración</returns>
        public static async Task<HostingerConfigResult> ConfigurarHostingerEmailAsync(
            this IEmailConfigurationService service,
            string emailUsuario,
            string contraseña,
            string nombreRemitente,
            string nombrePerfil = "Hostinger Principal",
            bool establecerPorDefecto = true,
            int puerto = 587,
            bool usarSSL = true,
            string creadoPor = "Sistema")
        {
            // Necesitamos acceso al contexto de la base de datos
            // Para esto necesitamos que el servicio implemente una interfaz extendida
            if (service is EmailConfigurationService concreteService)
            {
                return await concreteService.ExecuteHostingerStoredProcedureAsync(
                    emailUsuario, contraseña, nombreRemitente, nombrePerfil,
                    establecerPorDefecto, puerto, usarSSL, creadoPor);
            }
            
            throw new InvalidOperationException("El servicio no soporta configuración de Hostinger via procedimiento almacenado");
        }

        /// <summary>
        /// Verificar si hay configuraciones de Hostinger activas
        /// </summary>
        public static async Task<List<EmailConfiguration>> GetHostingerConfigurationsAsync(
            this IEmailConfigurationService service)
        {
            var allConfigs = await service.GetAllConfigurationsAsync();
            return allConfigs.Where(c => c.SmtpHost.Contains("hostinger", StringComparison.OrdinalIgnoreCase))
                           .ToList();
        }

        /// <summary>
        /// Crear configuración de Hostinger con validaciones específicas
        /// </summary>
        public static EmailConfiguration CreateHostingerConfiguration(
            string emailUsuario,
            string nombreRemitente,
            string nombrePerfil = "Hostinger Principal",
            int puerto = 587,
            bool usarSSL = true,
            string creadoPor = "Sistema")
        {
            // Validaciones específicas de Hostinger
            if (string.IsNullOrWhiteSpace(emailUsuario) || !emailUsuario.Contains("@"))
                throw new ArgumentException("Email de usuario requerido y debe tener formato válido");

            if (string.IsNullOrWhiteSpace(nombreRemitente))
                nombreRemitente = emailUsuario;

            if (puerto != 587 && puerto != 465 && puerto != 25)
                throw new ArgumentException("Puerto debe ser 587 (STARTTLS), 465 (SSL) o 25 (no recomendado)");

            return new EmailConfiguration
            {
                ProfileName = nombrePerfil,
                Provider = "SMTP",
                FromEmail = emailUsuario,
                FromName = nombreRemitente,
                SmtpHost = "smtp.hostinger.com",  // Servidor oficial de Hostinger
                SmtpPort = puerto,
                UseSsl = usarSSL,
                Username = emailUsuario,  // En Hostinger, username = email
                PasswordHash = "CONFIGURAR_CONTRASEÑA_ENCRIPTADA", // Placeholder
                IsActive = true,
                IsDefault = false, // Se establece después
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = creadoPor,
                TestEmailsSent = 0
            };
        }

        /// <summary>
        /// Configuraciones predefinidas para diferentes tipos de cuenta de Hostinger
        /// </summary>
        public static class HostingerPresets
        {
            /// <summary>
            /// Configuración para cuenta corporativa (puerto 587, STARTTLS)
            /// </summary>
            public static EmailConfiguration Corporativa(string email, string nombre, string perfil = "Hostinger Corporativo")
            {
                return CreateHostingerConfiguration(email, nombre, perfil, puerto: 587, usarSSL: true);
            }

            /// <summary>
            /// Configuración para cuenta con SSL estricto (puerto 465)
            /// </summary>
            public static EmailConfiguration SSLEstricto(string email, string nombre, string perfil = "Hostinger SSL")
            {  
                return CreateHostingerConfiguration(email, nombre, perfil, puerto: 465, usarSSL: true);
            }

            /// <summary>
            /// Configuración para desarrollo/pruebas
            /// </summary>
            public static EmailConfiguration Desarrollo(string email, string nombre)
            {
                return CreateHostingerConfiguration(email, nombre, "Hostinger Desarrollo", puerto: 587, usarSSL: true, creadoPor: "Desarrollo");
            }

            /// <summary>
            /// Configuración para producción con nombre personalizado
            /// </summary>
            public static EmailConfiguration Produccion(string email, string nombreEmpresa)
            {
                return CreateHostingerConfiguration(email, $"{nombreEmpresa} - Sistema", "Hostinger Producción", creadoPor: "Admin");
            }
        }
    }

    /// <summary>
    /// Resultado de la configuración de Hostinger
    /// </summary>
    public class HostingerConfigResult
    {
        public bool Success { get; set; }
        public int ConfigurationId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> ValidationErrors { get; set; } = new();
        
        public static HostingerConfigResult FromSuccess(int configId, string message = "Configuración creada exitosamente")
        {
            return new HostingerConfigResult
            {
                Success = true,
                ConfigurationId = configId,
                Message = message,
                Status = "SUCCESS"
            };
        }
        
        public static HostingerConfigResult FromError(string message, List<string>? errors = null)
        {
            return new HostingerConfigResult
            {
                Success = false,
                Message = message,
                Status = "ERROR",
                ValidationErrors = errors ?? new List<string>()
            };
        }
    }
}