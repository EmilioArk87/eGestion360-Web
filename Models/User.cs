using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Models
{
    public class User
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Password { get; set; } = string.Empty;
        
        public byte[]? PasswordHash { get; set; }

        public byte[]? PasswordSalt { get; set; }

        [StringLength(20)]
        public string? PasswordAlgorithm { get; set; }
        
        public bool IsActive { get; set; } = true;

        public bool RequirePasswordChange { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}