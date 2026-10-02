using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Categoría de licencia de conducir (catálogo global). Honduras: A, B, B1, BE, C1, C, CE, D1 y D,
    /// según la tabla de la Policía Nacional (DNVT), cargadas por el script 015.
    /// </summary>
    [Table("catalogo_tipos_licencia")]
    public class CatalogoTipoLicencia
    {
        [Key]
        [Required]
        [StringLength(10)]
        [Display(Name = "Categoría")]
        [Column("codigo", TypeName = "varchar(10)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Descripción")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        /// <summary>Origen verificable del dato (fuente oficial).</summary>
        [StringLength(300)]
        [Column("fuente")]
        public string? Fuente { get; set; }
    }
}
