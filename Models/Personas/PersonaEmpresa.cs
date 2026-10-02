using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Flota;

namespace eGestion360Web.Models.Personas
{
    /// <summary>
    /// Vínculo de una persona con una empresa, con su rol (script 014). Una fila por cada rol que una
    /// persona tiene en una empresa. Para un empleado, FechaInicio y FechaFin son el ingreso y la baja;
    /// para un cliente, el alta y la baja comercial. Los datos propios del rol viven en
    /// <see cref="Empleado"/> y en Cliente, que apuntan a este vínculo.
    /// </summary>
    [Table("persona_empresa")]
    public class PersonaEmpresa
    {
        [Key]
        [Column("id_persona_empresa")]
        public int IdPersonaEmpresa { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_persona")]
        public int IdPersona { get; set; }

        /// <summary>Uno de <see cref="TiposVinculo"/>.</summary>
        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo de vínculo")]
        [Column("tipo_vinculo", TypeName = "varchar(20)")]
        public string TipoVinculo { get; set; } = TiposVinculo.Empleado;

        [Display(Name = "Inicio")]
        [Column("fecha_inicio")]
        public DateOnly? FechaInicio { get; set; }

        [Display(Name = "Fin")]
        [Column("fecha_fin")]
        public DateOnly? FechaFin { get; set; }

        [StringLength(200)]
        [Display(Name = "Motivo del fin")]
        [Column("motivo_fin")]
        public string? MotivoFin { get; set; }

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

        public Empresa Empresa { get; set; } = null!;
        public Persona Persona { get; set; } = null!;

        /// <summary>Ficha de empleado, cuando <see cref="TipoVinculo"/> es 'empleado'.</summary>
        public Empleado? Empleado { get; set; }

        /// <summary>Vigente: no eliminado y sin fecha de fin (el índice UX_persona_empresa_vigente se apoya en esto).</summary>
        [NotMapped]
        public bool Vigente => !Eliminado && FechaFin == null;
    }
}
