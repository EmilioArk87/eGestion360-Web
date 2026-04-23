using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("personas")]
    public class Persona
    {
        [Key]
        [Column("id_persona")]
        public int IdPersona { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El documento es requerido")]
        [StringLength(30)]
        [Display(Name = "Documento")]
        [Column("documento")]
        public string Documento { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Tipo documento")]
        [Column("tipo_documento")]
        public string TipoDocumento { get; set; } = "DNI";

        [Required(ErrorMessage = "Los nombres son requeridos")]
        [StringLength(100)]
        [Display(Name = "Nombres")]
        [Column("nombres")]
        public string Nombres { get; set; } = string.Empty;

        [Required(ErrorMessage = "Los apellidos son requeridos")]
        [StringLength(100)]
        [Display(Name = "Apellidos")]
        [Column("apellidos")]
        public string Apellidos { get; set; } = string.Empty;

        [Required(ErrorMessage = "El cargo es requerido")]
        [StringLength(30)]
        [Display(Name = "Cargo")]
        [Column("cargo")]
        public string Cargo { get; set; } = "CONDUCTOR";

        [Range(0, double.MaxValue)]
        [Display(Name = "Tarifa diaria")]
        [Column("tarifa_diaria")]
        public decimal? TarifaDiaria { get; set; }

        [StringLength(3)]
        [Display(Name = "Moneda tarifa")]
        [Column("moneda_tarifa")]
        public string? MonedaTarifa { get; set; }

        [StringLength(30)]
        [Display(Name = "Teléfono")]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [StringLength(150)]
        [Display(Name = "Email")]
        [Column("email")]
        public string? Email { get; set; }

        [Display(Name = "Fecha de ingreso")]
        [Column("fecha_ingreso")]
        public DateOnly? FechaIngreso { get; set; }

        [Display(Name = "Fecha de baja")]
        [Column("fecha_baja")]
        public DateOnly? FechaBaja { get; set; }

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

        [NotMapped]
        public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
    }
}
