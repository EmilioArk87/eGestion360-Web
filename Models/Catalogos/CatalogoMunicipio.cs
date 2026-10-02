using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Municipio de un departamento (catálogo global, sin id_empresa). El código de 4 dígitos es
    /// departamento (2) + municipio (2), el mismo esquema que usa el DNI. Los nombres de municipio
    /// NO son únicos: la clave natural es (departamento, código).
    /// </summary>
    [Table("catalogo_municipios")]
    public class CatalogoMunicipio
    {
        [Key]
        [Column("id_municipio")]
        public int IdMunicipio { get; set; }

        [Column("id_departamento")]
        public int IdDepartamento { get; set; }

        [Required]
        [StringLength(4)]
        [Display(Name = "Código")]
        [Column("codigo", TypeName = "char(4)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Municipio")]
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

        public CatalogoDepartamento Departamento { get; set; } = null!;
    }
}
