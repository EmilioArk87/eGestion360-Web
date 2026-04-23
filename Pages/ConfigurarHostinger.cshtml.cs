using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Models;

namespace eGestion360Web.Pages
{
    public class ConfigurarHostingerModel : PageModel
    {
        private readonly IEmailConfigurationService _emailConfigService;
        private readonly ILogger<ConfigurarHostingerModel> _logger;

        public ConfigurarHostingerModel(IEmailConfigurationService emailConfigService, ILogger<ConfigurarHostingerModel> logger)
        {
            _emailConfigService = emailConfigService;
            _logger = logger;
        }

        [BindProperty]
        public HostingerConfigForm FormData { get; set; } = new HostingerConfigForm();
        
        public List<EmailConfiguration> ConfiguracionesExistentes { get; set; } = new();
        public string? MensajeResultado { get; set; }
        public bool ConfiguracionExitosa { get; set; }
        public int? NuevaConfiguracionId { get; set; }

        public async Task OnGetAsync()
        {
            await CargarConfiguracionesExistentesAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarConfiguracionesExistentesAsync();
                return Page();
            }

            try
            {
                _logger.LogInformation("Iniciando configuración de Hostinger para email: {Email}", FormData.EmailUsuario);

                // Usar la extensión para configurar Hostinger
                var result = await _emailConfigService.ConfigurarHostingerEmailAsync(
                    emailUsuario: FormData.EmailUsuario,
                    contraseña: FormData.Contraseña,
                    nombreRemitente: FormData.NombreRemitente,
                    nombrePerfil: FormData.NombrePerfil,
                    establecerPorDefecto: FormData.EstablecerPorDefecto,
                    puerto: FormData.Puerto,
                    usarSSL: FormData.UsarSSL,
                    creadoPor: "WebAdmin");

                if (result.Success)
                {
                    ConfiguracionExitosa = true;
                    NuevaConfiguracionId = result.ConfigurationId;
                    MensajeResultado = $"✅ {result.Message}";
                    
                    _logger.LogInformation("Configuración de Hostinger creada exitosamente. ID: {ConfigId}", result.ConfigurationId);
                    
                    // Limpiar el formulario después del éxito
                    FormData = new HostingerConfigForm();
                }
                else
                {
                    ConfiguracionExitosa = false;
                    MensajeResultado = $"❌ {result.Message}";
                    
                    if (result.ValidationErrors.Any())
                    {
                        MensajeResultado += "<br><strong>Errores:</strong><ul>";
                        foreach (var error in result.ValidationErrors)
                        {
                            MensajeResultado += $"<li>{error}</li>";
                        }
                        MensajeResultado += "</ul>";
                    }
                    
                    _logger.LogError("Error configurando Hostinger: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                ConfiguracionExitosa = false;
                MensajeResultado = $"❌ Error interno: {ex.Message}";
                _logger.LogError(ex, "Error configurando email de Hostinger");
            }

            await CargarConfiguracionesExistentesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostEliminarConfiguracionAsync(int configId)
        {
            try
            {
                var eliminada = await _emailConfigService.DeleteConfigurationAsync(configId);
                if (eliminada)
                {
                    MensajeResultado = "✅ Configuración eliminada exitosamente";
                    ConfiguracionExitosa = true;
                }
                else
                {
                    MensajeResultado = "❌ No se pudo eliminar la configuración";
                    ConfiguracionExitosa = false;
                }
            }
            catch (Exception ex)
            {
                MensajeResultado = $"❌ Error eliminando configuración: {ex.Message}";
                ConfiguracionExitosa = false;
                _logger.LogError(ex, "Error eliminando configuración {ConfigId}", configId);
            }

            await CargarConfiguracionesExistentesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostEstablecerPorDefectoAsync(int configId)
        {
            try
            {
                var establecida = await _emailConfigService.SetDefaultConfigurationAsync(configId);
                if (establecida)
                {
                    MensajeResultado = "✅ Configuración establecida como por defecto";
                    ConfiguracionExitosa = true;
                }
                else
                {
                    MensajeResultado = "❌ No se pudo establecer como por defecto";
                    ConfiguracionExitosa = false;
                }
            }
            catch (Exception ex)
            {
                MensajeResultado = $"❌ Error estableciendo por defecto: {ex.Message}";
                ConfiguracionExitosa = false;
                _logger.LogError(ex, "Error estableciendo configuración por defecto {ConfigId}", configId);
            }

            await CargarConfiguracionesExistentesAsync();
            return Page();
        }

        private async Task CargarConfiguracionesExistentesAsync()
        {
            try
            {
                ConfiguracionesExistentes = await _emailConfigService.GetHostingerConfigurationsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cargando configuraciones existentes");
                ConfiguracionesExistentes = new List<EmailConfiguration>();
            }
        }
    }

    public class HostingerConfigForm
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Email de usuario requerido")]
        [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Formato de email inválido")]
        [System.ComponentModel.DataAnnotations.Display(Name = "Email de Usuario")]
        public string EmailUsuario { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Contraseña requerida")]
        [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
        [System.ComponentModel.DataAnnotations.Display(Name = "Contraseña")]
        public string Contraseña { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Nombre del remitente requerido")]
        [System.ComponentModel.DataAnnotations.Display(Name = "Nombre del Remitente")]
        public string NombreRemitente { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Display(Name = "Nombre del Perfil")]
        public string NombrePerfil { get; set; } = "Hostinger Principal";

        [System.ComponentModel.DataAnnotations.Display(Name = "Establecer como Por Defecto")]
        public bool EstablecerPorDefecto { get; set; } = true;

        [System.ComponentModel.DataAnnotations.Range(25, 65535, ErrorMessage = "Puerto debe estar entre 25 y 65535")]
        [System.ComponentModel.DataAnnotations.Display(Name = "Puerto SMTP")]
        public int Puerto { get; set; } = 587;

        [System.ComponentModel.DataAnnotations.Display(Name = "Usar SSL/TLS")]
        public bool UsarSSL { get; set; } = true;
    }
}