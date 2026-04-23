using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    public class EmailConfiguration
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        [Display(Name = "Nombre del Perfil")]
        public string ProfileName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(20)]
        [Display(Name = "Proveedor")]
        public string Provider { get; set; } = "SMTP";
        
        [Required]
        [StringLength(100)]
        [EmailAddress]
        [Display(Name = "Email Remitente")]
        public string FromEmail { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        [Display(Name = "Nombre Remitente")]
        public string FromName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        [Display(Name = "Servidor SMTP")]
        public string SmtpHost { get; set; } = string.Empty;
        
        [Range(25, 65535)]
        [Display(Name = "Puerto SMTP")]
        public int SmtpPort { get; set; } = 587;
        
        [Display(Name = "Usar SSL/TLS")]
        public bool UseSsl { get; set; } = true;
        
        [Required]
        [StringLength(100)]
        [Display(Name = "Usuario SMTP")]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [StringLength(500)]
        [Display(Name = "Contraseña")]
        [DataType(DataType.Password)]
        public string PasswordHash { get; set; } = string.Empty;
        
        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;
        
        [Display(Name = "Por Defecto")]
        public bool IsDefault { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [StringLength(50)]
        public string? CreatedBy { get; set; }
        
        public DateTime? LastTestedAt { get; set; }
        
        public int TestEmailsSent { get; set; } = 0;
        
        // Property not mapped - para mostrar en formularios
        [NotMapped]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña (Texto Plano)")]
        public string? PlainPassword { get; set; }
        
        // Helper method para verificar si la configuración está lista
        [NotMapped]
        public bool IsConfigured => !string.IsNullOrEmpty(PasswordHash) && 
                                   PasswordHash != "CONFIGURAR_CONTRASEÑA_ENCRIPTADA";
    }
}