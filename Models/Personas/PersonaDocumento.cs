using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;

namespace eGestion360Web.Models.Personas
{
    /// <summary>
    /// Documento de identidad de una persona (script 014). El índice único global
    /// UX_persona_documentos_identidad garantiza que un documento (tipo + país + número normalizado)
    /// pertenece a una sola persona, sin importar la empresa.
    /// El número de empleado NO va aquí: vive en <see cref="Empleado.CodigoInterno"/>.
    /// </summary>
    [Table("persona_documentos")]
    public class PersonaDocumento
    {
        [Key]
        [Column("id_persona_documento")]
        public int IdPersonaDocumento { get; set; }

        [Column("id_persona")]
        public int IdPersona { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo de documento")]
        [Column("tipo_documento", TypeName = "varchar(20)")]
        public string TipoDocumento { get; set; } = "DNI";

        [Required]
        [StringLength(2)]
        [Display(Name = "País emisor")]
        [Column("pais_emisor", TypeName = "char(2)")]
        public string PaisEmisor { get; set; } = "HN";

        /// <summary>Número tal como se guarda (para el DNI, solo dígitos).</summary>
        [Required]
        [StringLength(30)]
        [Display(Name = "Número")]
        [Column("numero")]
        public string Numero { get; set; } = string.Empty;

        /// <summary>Columna calculada y persistida por la BD (mayúsculas, sin guiones, espacios ni puntos). Solo lectura.</summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("numero_normalizado")]
        public string? NumeroNormalizado { get; set; }

        [Display(Name = "Principal")]
        [Column("es_principal")]
        public bool EsPrincipal { get; set; }

        [Display(Name = "Vencimiento")]
        [Column("fecha_vencimiento")]
        public DateOnly? FechaVencimiento { get; set; }

        [Display(Name = "Activo")]
        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("eliminado")]
        public bool Eliminado { get; set; }

        [Column("fecha_eliminado")]
        public DateTime? FechaEliminado { get; set; }

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();

        public Persona Persona { get; set; } = null!;
        public CatalogoTipoDocumento? Tipo { get; set; }
    }
}
