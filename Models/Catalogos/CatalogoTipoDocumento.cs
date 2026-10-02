using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Tipo de documento de una persona (catálogo global): DNI, RTN, pasaporte, carné de residente.
    /// Es la única fuente de las reglas de formato: el servicio de validación aplica <see cref="Patron"/>
    /// y los largos a la forma normalizada del número (sin guiones, puntos ni espacios).
    /// </summary>
    [Table("catalogo_tipos_documento")]
    public class CatalogoTipoDocumento
    {
        [Key]
        [Required]
        [StringLength(20)]
        [Display(Name = "Código")]
        [Column("codigo", TypeName = "varchar(20)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Tipo de documento")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>País al que aplica; nulo si no depende de un país (por ejemplo, el pasaporte).</summary>
        [StringLength(2)]
        [Column("pais_iso", TypeName = "char(2)")]
        public string? PaisIso { get; set; }

        /// <summary>Expresión regular .NET sobre el número normalizado; nulo si no hay formato verificado.</summary>
        [StringLength(200)]
        [Column("patron")]
        public string? Patron { get; set; }

        [Column("largo_min")]
        public byte? LargoMin { get; set; }

        [Column("largo_max")]
        public byte? LargoMax { get; set; }

        /// <summary>Si es un documento de identidad (marca la identidad de la persona como verificada).</summary>
        [Column("es_identidad")]
        public bool EsIdentidad { get; set; } = true;

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
