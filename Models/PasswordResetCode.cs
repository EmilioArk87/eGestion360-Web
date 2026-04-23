using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Models
{
    public class PasswordResetCode
    {
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        [StringLength(6)]
        public string Code { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string Email { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
        
        public bool IsUsed { get; set; } = false;
        
        public DateTime? UsedAt { get; set; }
        
        [StringLength(100)]
        public string? IpAddress { get; set; }
        
        // Navigation property
        public User User { get; set; } = null!;
    }
}