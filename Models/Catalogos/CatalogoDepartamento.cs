using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Departamento de un país (catálogo global, sin id_empresa). Honduras: 18 departamentos con el
    /// código de 2 dígitos del INE y del SINIT, cargados por el script 015.
    /// </summary>
    [Table("catalogo_departamentos")]
    public class CatalogoDepartamento
    {
        [Key]
        [Column("id_departamento")]
        public int IdDepartamento { get; set; }

        [Required]
        [StringLength(2)]
        [Column("pais_iso", TypeName = "char(2)")]
        public string PaisIso { get; set; } = "HN";

        [Required]
        [StringLength(2)]
        [Display(Name = "Código")]
        [Column("codigo", TypeName = "char(2)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Departamento")]
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

        public ICollection<CatalogoMunicipio> Municipios { get; set; } = new List<CatalogoMunicipio>();
    }
}
