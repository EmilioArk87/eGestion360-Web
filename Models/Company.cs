using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Models
{
    public class Company
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(100)]
        public string? LegalName { get; set; }
        
        [Required]
        [StringLength(50)]
        public string TaxId { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Address { get; set; }
        
        [StringLength(100)]
        public string? City { get; set; }
        
        [StringLength(100)]
        public string? State { get; set; }
        
        [StringLength(20)]
        public string? PostalCode { get; set; }
        
        [Required]
        [StringLength(2)]
        public string Country { get; set; } = string.Empty;
        
        [StringLength(20)]
        public string? Phone { get; set; }
        
        [StringLength(100)]
        public string? Email { get; set; }
        
        [StringLength(200)]
        public string? Website { get; set; }
        
        [Required]
        [StringLength(10)]
        public string Currency { get; set; } = "USD";
        
        [Required]
        [StringLength(10)]
        public string TimeZone { get; set; } = "UTC";
        
        [StringLength(10)]
        public string? Language { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
