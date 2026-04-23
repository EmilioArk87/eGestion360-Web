using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Services
{
    public interface IEmailConfigurationService
    {
        Task<EmailConfiguration?> GetActiveConfigurationAsync(string? profileName = null);
        Task<EmailConfiguration?> GetDefaultConfigurationAsync();
        Task<EmailConfiguration?> GetConfigurationByIdAsync(int id);
        Task<List<EmailConfiguration>> GetAllConfigurationsAsync();
        Task<bool> SaveConfigurationAsync(EmailConfiguration configuration, string plainPassword);
        Task<bool> SetDefaultConfigurationAsync(int configurationId);
        Task<bool> ToggleActiveStatusAsync(int configurationId);
        Task<bool> DeleteConfigurationAsync(int id);
        Task<bool> TestConfigurationAsync(int configurationId);
        Task<string> DecryptPasswordAsync(string encryptedPassword);
        Task UpdateTestStatsAsync(int configurationId, bool success = true);
    }

    public class EmailConfigurationService : IEmailConfigurationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<EmailConfigurationService> _logger;

        public EmailConfigurationService(
            ApplicationDbContext context,
            IEncryptionService encryptionService,
            ILogger<EmailConfigurationService> logger)
        {
            _context = context;
            _encryptionService = encryptionService;
            _logger = logger;
        }

        public async Task<EmailConfiguration?> GetActiveConfigurationAsync(string? profileName = null)
        {
            try
            {
                EmailConfiguration? config;

                if (!string.IsNullOrEmpty(profileName))
                {
                    // Buscar perfil específico
                    config = await _context.EmailConfigurations
                        .FirstOrDefaultAsync(c => c.ProfileName == profileName && c.IsActive);
                }
                else
                {
                    // Buscar configuración por defecto o la primera activa
                    config = await _context.EmailConfigurations
                        .Where(c => c.IsActive)
                        .OrderByDescending(c => c.IsDefault)
                        .ThenBy(c => c.Id)
                        .FirstOrDefaultAsync();
                }

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo configuración de email");
                return null;
            }
        }

        public async Task<List<EmailConfiguration>> GetAllConfigurationsAsync()
        {
            try
            {
                return await _context.EmailConfigurations
                    .OrderByDescending(c => c.IsDefault)
                    .ThenByDescending(c => c.IsActive)
                    .ThenBy(c => c.ProfileName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo todas las configuraciones de email");
                return new List<EmailConfiguration>();
            }
        }

        public async Task<bool> SaveConfigurationAsync(EmailConfiguration configuration, string plainPassword)
        {
            try
            {
                // Encriptar la contraseña (AES reversible, no BCrypt)
                configuration.PasswordHash = _encryptionService.Encrypt(plainPassword);
                configuration.UpdatedAt = DateTime.UtcNow;

                if (configuration.Id == 0)
                {
                    // Nueva configuración
                    configuration.CreatedAt = DateTime.UtcNow;
                    
                    // Si es la primera configuración, hacerla por defecto
                    if (!await _context.EmailConfigurations.AnyAsync())
                    {
                        configuration.IsDefault = true;
                    }
                    
                    _context.EmailConfigurations.Add(configuration);
                }
                else
                {
                    // Actualizar configuración existente
                    _context.EmailConfigurations.Update(configuration);
                }

                // Si se marca como default, quitar el default anterior
                if (configuration.IsDefault && configuration.IsActive)
                {
                    await _context.EmailConfigurations
                        .Where(c => c.Id != configuration.Id && c.IsDefault)
                        .ForEachAsync(c => c.IsDefault = false);
                }

                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Configuración de email guardada: {ProfileName}", configuration.ProfileName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando configuración de email");
                return false;
            }
        }

        public async Task<bool> SetDefaultConfigurationAsync(int configurationId)
        {
            try
            {
                // Quitar default a todas las configuraciones
                await _context.EmailConfigurations
                    .ForEachAsync(c => c.IsDefault = false);

                // Establecer nueva por defecto
                var config = await _context.EmailConfigurations.FindAsync(configurationId);
                if (config != null && config.IsActive)
                {
                    config.IsDefault = true;
                    config.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Configuración por defecto cambiada a: {ProfileName}", config.ProfileName);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error estableciendo configuración por defecto");
                return false;
            }
        }

        public async Task<bool> ToggleActiveStatusAsync(int configurationId)
        {
            try
            {
                var config = await _context.EmailConfigurations.FindAsync(configurationId);
                if (config != null)
                {
                    config.IsActive = !config.IsActive;
                    config.UpdatedAt = DateTime.UtcNow;
                    
                    // Si se desactiva y era default, buscar otro para ser default
                    if (!config.IsActive && config.IsDefault)
                    {
                        config.IsDefault = false;
                        var newDefault = await _context.EmailConfigurations
                            .FirstOrDefaultAsync(c => c.Id != configurationId && c.IsActive);
                        
                        if (newDefault != null)
                        {
                            newDefault.IsDefault = true;
                        }
                    }

                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Estado activo cambiado para {ProfileName}: {IsActive}", 
                        config.ProfileName, config.IsActive);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cambiando estado activo de configuración");
                return false;
            }
        }

        public async Task<bool> TestConfigurationAsync(int configurationId)
        {
            try
            {
                var config = await _context.EmailConfigurations.FindAsync(configurationId);
                if (config == null) return false;

                // Crear un email de prueba
                var testSubject = "Prueba de Configuración SMTP - eGestion360";
                var testContent = GenerateTestEmailContent(config);

                // Intentar enviar (esto requerirá implementar el envío en EmailService)
                // Por ahora simularemos que funciona si la configuración está completa
                var success = config.IsConfigured;

                await UpdateTestStatsAsync(configurationId, success);

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error probando configuración de email");
                await UpdateTestStatsAsync(configurationId, false);
                return false;
            }
        }

        public async Task<string> DecryptPasswordAsync(string encryptedPassword)
        {
            try
            {
                await Task.CompletedTask; // Para mantener la signatura async
                return _encryptionService.Decrypt(encryptedPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error desencriptando contraseña");
                return string.Empty;
            }
        }

        public async Task UpdateTestStatsAsync(int configurationId, bool success = true)
        {
            try
            {
                var config = await _context.EmailConfigurations.FindAsync(configurationId);
                if (config != null)
                {
                    config.TestEmailsSent++;
                    if (success)
                    {
                        config.LastTestedAt = DateTime.UtcNow;
                    }
                    config.UpdatedAt = DateTime.UtcNow;
                    
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error actualizando estadísticas de prueba");
            }
        }

        public async Task<EmailConfiguration?> GetDefaultConfigurationAsync()
        {
            try
            {
                return await _context.EmailConfigurations
                    .FirstOrDefaultAsync(c => c.IsDefault && c.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting default email configuration");
                return null;
            }
        }

        public async Task<EmailConfiguration?> GetConfigurationByIdAsync(int id)
        {
            try
            {
                return await _context.EmailConfigurations
                    .FirstOrDefaultAsync(c => c.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting email configuration by ID {ConfigId}", id);
                return null;
            }
        }

        public async Task<bool> DeleteConfigurationAsync(int id)
        {
            try
            {
                var config = await _context.EmailConfigurations.FindAsync(id);
                if (config == null)
                {
                    _logger.LogWarning("Email configuration {ConfigId} not found for deletion", id);
                    return false;
                }

                // No permitir eliminar la configuración predeterminada
                if (config.IsDefault)
                {
                    _logger.LogWarning("Cannot delete default email configuration {ConfigId}", id);
                    return false;
                }

                _context.EmailConfigurations.Remove(config);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Email configuration {ConfigId} deleted successfully", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting email configuration {ConfigId}", id);
                return false;
            }
        }

        /// <summary>
        /// Ejecuta el procedimiento almacenado para configurar email de Hostinger.es
        /// </summary>
        public async Task<HostingerConfigResult> ExecuteHostingerStoredProcedureAsync(
            string emailUsuario,
            string contraseñaPlana,
            string nombreRemitente,
            string nombrePerfil = "Hostinger Principal",
            bool establecerPorDefecto = true,
            int puerto = 587,
            bool usarSSL = true,
            string creadoPor = "Sistema")
        {
            try
            {
                // Validaciones de entrada
                var validationErrors = new List<string>();
                
                if (string.IsNullOrWhiteSpace(emailUsuario) || !emailUsuario.Contains("@"))
                    validationErrors.Add("Email de usuario requerido y debe tener formato válido");
                
                if (string.IsNullOrWhiteSpace(contraseñaPlana))
                    validationErrors.Add("Contraseña requerida");
                
                if (string.IsNullOrWhiteSpace(nombreRemitente))
                    nombreRemitente = emailUsuario;
                
                if (puerto != 25 && puerto != 465 && puerto != 587 && puerto != 2525)
                    validationErrors.Add("Puerto debe ser uno de: 25, 465, 587, 2525");
                
                if (validationErrors.Any())
                {
                    return HostingerConfigResult.FromError("Errores de validación", validationErrors);
                }

                // Ejecutar el procedimiento almacenado
                using var command = _context.Database.GetDbConnection().CreateCommand();
                
                command.CommandText = "SP_ConfigurarHostingerEmail";
                command.CommandType = System.Data.CommandType.StoredProcedure;
                
                // Agregar parámetros
                command.Parameters.Add(CreateParameter("@EmailUsuario", emailUsuario));
                command.Parameters.Add(CreateParameter("@ContraseñaPlana", contraseñaPlana));
                command.Parameters.Add(CreateParameter("@NombreRemitente", nombreRemitente));
                command.Parameters.Add(CreateParameter("@NombrePerfil", nombrePerfil));
                command.Parameters.Add(CreateParameter("@EstablecerPorDefecto", establecerPorDefecto));
                command.Parameters.Add(CreateParameter("@Puerto", puerto));
                command.Parameters.Add(CreateParameter("@UsarSSL", usarSSL));
                command.Parameters.Add(CreateParameter("@CreadoPor", creadoPor));

                await _context.Database.OpenConnectionAsync();
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var configId = reader.GetInt32("ConfigurationId");
                    var status = reader.GetString("Status");
                    var message = reader.GetString("Message");
                    
                    if (status == "SUCCESS")
                    {
                        _logger.LogInformation("Configuración de Hostinger creada exitosamente. ID: {ConfigId}", configId);
                        return HostingerConfigResult.FromSuccess(configId, message);
                    }
                    else
                    {
                        _logger.LogError("Error ejecutando procedimiento de Hostinger: {Message}", message);
                        return HostingerConfigResult.FromError(message);
                    }
                }
                
                return HostingerConfigResult.FromError("No se recibió respuesta del procedimiento almacenado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ejecutando procedimiento almacenado de Hostinger");
                return HostingerConfigResult.FromError($"Error interno: {ex.Message}");
            }
            finally
            {
                if (_context.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }

        /// <summary>
        /// Método auxiliar para crear parámetros de SQL Server
        /// </summary>
        private System.Data.Common.DbParameter CreateParameter(string name, object value)
        {
            var parameter = _context.Database.GetDbConnection().CreateCommand().CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            return parameter;
        }

        private string GenerateTestEmailContent(EmailConfiguration config)
        {
            return $@"
<h3>✅ Prueba de Configuración SMTP</h3>

<p>Esta es una prueba de la configuración de email para <strong>{config.ProfileName}</strong>.</p>

<div style='background: #e3f2fd; padding: 15px; border-left: 4px solid #2196f3; margin: 20px 0;'>
    <strong>📧 Detalles de la configuración:</strong>
    <ul>
        <li><strong>Perfil:</strong> {config.ProfileName}</li>
        <li><strong>Proveedor:</strong> {config.Provider}</li>
        <li><strong>Servidor:</strong> {config.SmtpHost}:{config.SmtpPort}</li>
        <li><strong>SSL/TLS:</strong> {(config.UseSsl ? "Habilitado" : "Deshabilitado")}</li>
        <li><strong>Usuario:</strong> {config.Username}</li>
        <li><strong>Remitente:</strong> {config.FromName} ({config.FromEmail})</li>
    </ul>
</div>

<p><strong>🎉 Si recibes este email, la configuración está funcionando correctamente.</strong></p>

<p>Fecha de prueba: {DateTime.Now:dd/MM/yyyy HH:mm:ss}</p>

<p>Saludos,<br>
<strong>Sistema eGestion360</strong></p>";
        }
    }
}