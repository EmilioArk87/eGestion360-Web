using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("salarios_diarios")]
    public class SalarioDiario
    {
        [Key]
        [Column("id_salario_diario")]
        public int IdSalarioDiario { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Required(ErrorMessage = "La persona es requerida")]
        [Display(Name = "Persona")]
        [Column("id_persona")]
        public int IdPersona { get; set; }

        [Required(ErrorMessage = "La fecha es requerida")]
        [Display(Name = "Fecha")]
        [Column("fecha")]
        public DateOnly Fecha { get; set; }

        [Required(ErrorMessage = "El cargo es requerido")]
        [StringLength(30)]
        [Display(Name = "Cargo")]
        [Column("cargo")]
        public string Cargo { get; set; } = "CONDUCTOR";

        [Required(ErrorMessage = "El monto es requerido")]
        [Range(0, double.MaxValue)]
        [Display(Name = "Monto")]
        [Column("monto")]
        public decimal Monto { get; set; }

        [Required(ErrorMessage = "La moneda es requerida")]
        [StringLength(3)]
        [Display(Name = "Moneda")]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [StringLength(500)]
        [Display(Name = "Observaciones")]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

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

        public Vehiculo? Vehiculo { get; set; }
        public Persona? Persona { get; set; }
    }
}
