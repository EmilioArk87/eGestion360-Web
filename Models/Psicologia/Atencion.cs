using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>
    /// Una evaluación o consulta de un paciente (psi.atenciones, script 042): el historial clínico. Puede ser parte de una jornada.
    /// Una atención cerrada ya no se edita (la aplicación lo hace cumplir; la BD exige quién y cuándo la cerró). Su contenido clínico
    /// es el mínimo del examen para licencia de conducir (decisión D17): el resultado de cada prueba aplicada, la conclusión apto o
    /// no apto y las observaciones.
    /// </summary>
    [Table("atenciones", Schema = "psi")]
    public class Atencion : ITenantEntity
    {
        [Key]
        [Column("id_atencion")]
        public int IdAtencion { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_paciente")]
        public int IdPaciente { get; set; }

        [Column("id_jornada")]
        public int? IdJornada { get; set; }

        [Display(Name = "Fecha de la atención")]
        [Column("fecha_atencion")]
        public DateOnly FechaAtencion { get; set; }

        /// <summary>Edad que declaró o que trae la fuente al momento de la atención (decisión D3); no se deduce la fecha de nacimiento.</summary>
        [Range(0, 120)]
        [Display(Name = "Edad")]
        [Column("edad_declarada")]
        public byte? EdadDeclarada { get; set; }

        [Column("id_persona_profesional")]
        public int? IdPersonaProfesional { get; set; }

        /// <summary>Nombre del profesional tal como sale impreso (se conserva aunque la persona cambie después).</summary>
        [StringLength(200)]
        [Display(Name = "Profesional")]
        [Column("nombre_profesional")]
        public string? NombreProfesional { get; set; }

        [Required]
        [StringLength(10)]
        [Column("estado", TypeName = "varchar(10)")]
        public string Estado { get; set; } = EstadosAtencion.Borrador;

        /// <summary>Conclusión del examen (decisión D17): <see cref="ResultadosConstancia.Apto"/> o <see cref="ResultadosConstancia.NoApto"/>; nula mientras no se define.</summary>
        [StringLength(10)]
        [Display(Name = "Conclusión")]
        [Column("conclusion", TypeName = "varchar(10)")]
        public string? Conclusion { get; set; }

        [StringLength(4000)]
        [Display(Name = "Observaciones")]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

        [Column("fecha_cierre")]
        public DateTime? FechaCierre { get; set; }

        [StringLength(100)]
        [Column("cerrado_por")]
        public string? CerradoPor { get; set; }

        [StringLength(300)]
        [Display(Name = "Motivo de anulación")]
        [Column("motivo_anulacion")]
        public string? MotivoAnulacion { get; set; }

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

        public Paciente Paciente { get; set; } = null!;
        public Jornada? Jornada { get; set; }
        public Persona? Profesional { get; set; }

        /// <summary>Pruebas psicológicas aplicadas en la atención.</summary>
        public ICollection<AtencionPrueba> Pruebas { get; set; } = new List<AtencionPrueba>();
    }
}
