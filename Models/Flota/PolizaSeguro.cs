using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("polizas_seguros")]
    public class PolizaSeguro
    {
        [Key]
        [Column("id_poliza_seguro")]
        public int IdPolizaSeguro { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Required(ErrorMessage = "El número de póliza es requerido")]
        [StringLength(50)]
        [Display(Name = "Nº Póliza")]
        [Column("no_poliza")]
        public string NoPoliza { get; set; } = string.Empty;

        [Required(ErrorMessage = "La aseguradora es requerida")]
        [StringLength(150)]
        [Display(Name = "Aseguradora")]
        [Column("aseguradora")]
        public string Aseguradora { get; set; } = string.Empty;

        [Required(ErrorMessage = "El tipo de cobertura es requerido")]
        [StringLength(30)]
        [Display(Name = "Tipo de cobertura")]
        [Column("tipo_cobertura")]
        public string TipoCobertura { get; set; } = "AMPLIA";

        [Required(ErrorMessage = "La fecha de inicio es requerida")]
        [Display(Name = "Fecha inicio")]
        [Column("fecha_inicio")]
        public DateOnly FechaInicio { get; set; }

        [Required(ErrorMessage = "La fecha de fin es requerida")]
        [Display(Name = "Fecha fin")]
        [Column("fecha_fin")]
        public DateOnly FechaFin { get; set; }

        [Required(ErrorMessage = "La prima total es requerida")]
        [Range(0, double.MaxValue)]
        [Display(Name = "Prima total")]
        [Column("prima_total")]
        public decimal PrimaTotal { get; set; }

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda")]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Display(Name = "Costo diario")]
        [Column("costo_diario")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal? CostoDiario { get; set; }

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
    }
}
