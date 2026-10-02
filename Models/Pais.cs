using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    /// <summary>
    /// País según ISO 3166-1 (249 filas). Decisión D12: esta entidad usa la tabla catalogo_paises, la
    /// que tienen como destino las claves foráneas de empresas, personas y documentos, en lugar de la
    /// tabla paises (193 filas, sin claves foráneas), que queda sin uso y se retira más adelante.
    /// Se conservan los nombres de propiedad CodigoIso y Nombre para no tocar las pantallas actuales.
    /// </summary>
    [Table("catalogo_paises")]
    public class Pais
    {
        /// <summary>Código ISO 3166-1 alfa-2 (2 letras).</summary>
        [Key]
        [StringLength(2)]
        [Column("pais_iso", TypeName = "char(2)")]
        public string CodigoIso { get; set; } = string.Empty;

        /// <summary>Código ISO 3166-1 alfa-3 (3 letras). Único.</summary>
        [Required]
        [StringLength(3)]
        [Column("pais_iso3", TypeName = "char(3)")]
        public string CodigoIso3 { get; set; } = string.Empty;

        /// <summary>Código numérico ISO 3166-1 (3 dígitos). Único.</summary>
        [Required]
        [StringLength(3)]
        [Column("pais_num", TypeName = "char(3)")]
        public string CodigoNumerico { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("nombre_espanol")]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("nombre_ingles")]
        public string NombreIngles { get; set; } = string.Empty;

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }
}
