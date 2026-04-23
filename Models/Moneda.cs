using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    [Table("monedas")]
    public class Moneda
    {
        [Key]
        [StringLength(3)]
        [Column("codigo_iso")]
        public string CodigoIso { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        [Column("simbolo")]
        public string Simbolo { get; set; } = string.Empty;

        [Column("activo")]
        public bool Activo { get; set; } = true;
    }
}
