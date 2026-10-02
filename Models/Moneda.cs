using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    /// <summary>
    /// Moneda según ISO 4217. La clave es CHAR(3) porque doce columnas de moneda de otras tablas
    /// tienen clave foránea hacia ella (script 017) y una clave foránea exige el mismo tipo.
    /// </summary>
    [Table("monedas")]
    public class Moneda
    {
        [Key]
        [StringLength(3)]
        [Column("codigo_iso", TypeName = "char(3)")]
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

        /// <summary>Código numérico ISO 4217 (3 dígitos). Nulo en las monedas que ya no figuran en la lista vigente.</summary>
        [StringLength(3)]
        [Column("codigo_numerico", TypeName = "char(3)")]
        public string? CodigoNumerico { get; set; }

        /// <summary>Decimales de la unidad menor según ISO 4217 (por ejemplo 2 para HNL, 0 para JPY, 3 para KWD).</summary>
        [Column("decimales")]
        public byte? Decimales { get; set; }

        /// <summary>Origen verificable del dato (lista ISO 4217 de SIX).</summary>
        [StringLength(300)]
        [Column("fuente")]
        public string? Fuente { get; set; }
    }
}
