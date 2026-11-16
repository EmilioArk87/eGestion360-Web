using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>Brigada o evento de atención grupal (psi.jornadas, script 042). Por ejemplo, "Brigada San Pedro Sula 15-nov-2026".</summary>
    [Table("jornadas", Schema = "psi")]
    public class Jornada : ITenantEntity
    {
        [Key]
        [Column("id_jornada")]
        public int IdJornada { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(150)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(150)]
        [Display(Name = "Lugar")]
        [Column("lugar")]
        public string? Lugar { get; set; }

        [StringLength(150)]
        [Display(Name = "Institución")]
        [Column("institucion")]
        public string? Institucion { get; set; }

        [Display(Name = "Fecha de inicio")]
        [Column("fecha_inicio")]
        public DateOnly FechaInicio { get; set; }

        [Display(Name = "Fecha de fin")]
        [Column("fecha_fin")]
        public DateOnly? FechaFin { get; set; }

        [Column("id_persona_responsable")]
        public int? IdPersonaResponsable { get; set; }

        /// <summary>Nombre del responsable tal como sale impreso (se conserva aunque la persona cambie después).</summary>
        [StringLength(200)]
        [Display(Name = "Responsable")]
        [Column("nombre_responsable")]
        public string? NombreResponsable { get; set; }

        [Required]
        [StringLength(10)]
        [Column("estado", TypeName = "varchar(10)")]
        public string Estado { get; set; } = EstadosJornada.Abierta;

        [StringLength(1000)]
        [Display(Name = "Observaciones")]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

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

        public Empresa? Empresa { get; set; }
        public Persona? Responsable { get; set; }
    }
}
