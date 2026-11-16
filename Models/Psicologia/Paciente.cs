using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>
    /// Ficha de paciente de una persona en una empresa (psi.pacientes, script 042), como <see cref="Empleado"/>: cuelga de un
    /// vínculo <see cref="PersonaEmpresa"/> de tipo 'paciente'. En la BD, la clave foránea compuesta (id_tenant, id_persona_empresa,
    /// id_empresa, tipo_vinculo) obliga a que IdEmpresa sea la del vínculo y a que TipoVinculo sea 'paciente'; el servicio que
    /// guarda pone ambos valores.
    /// </summary>
    [Table("pacientes", Schema = "psi")]
    public class Paciente : ITenantEntity
    {
        [Key]
        [Column("id_paciente")]
        public int IdPaciente { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_persona_empresa")]
        public int IdPersonaEmpresa { get; set; }

        [Required]
        [StringLength(20)]
        [Column("tipo_vinculo", TypeName = "varchar(20)")]
        public string TipoVinculo { get; set; } = TiposVinculo.Paciente;

        /// <summary>Número de expediente PSI-AAAA-NNNNN (decisión D5): único en la empresa y nunca se reutiliza.</summary>
        [Required]
        [StringLength(20)]
        [Display(Name = "Expediente")]
        [Column("numero_expediente", TypeName = "varchar(20)")]
        public string NumeroExpediente { get; set; } = string.Empty;

        [Display(Name = "Apertura del expediente")]
        [Column("fecha_apertura")]
        public DateOnly FechaApertura { get; set; }

        [StringLength(1000)]
        [Display(Name = "Observaciones")]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

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

        public PersonaEmpresa Vinculo { get; set; } = null!;
        public ICollection<Atencion> Atenciones { get; set; } = new List<Atencion>();
    }
}
