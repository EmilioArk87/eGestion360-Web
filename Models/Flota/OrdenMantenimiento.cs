using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("ordenes_mantenimiento")]
    public class OrdenMantenimiento
    {
        [Key]
        [Column("id_orden_mantenimiento")]
        public int IdOrdenMantenimiento { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Required(ErrorMessage = "El taller es requerido")]
        [Display(Name = "Taller")]
        [Column("id_taller")]
        public int IdTaller { get; set; }

        [Required(ErrorMessage = "La fecha es requerida")]
        [Display(Name = "Fecha")]
        [Column("fecha")]
        public DateOnly Fecha { get; set; }

        [Required(ErrorMessage = "El número de factura es requerido")]
        [StringLength(50)]
        [Display(Name = "Nº Factura")]
        [Column("no_factura")]
        public string NoFactura { get; set; } = string.Empty;

        [Required(ErrorMessage = "El tipo de mantenimiento es requerido")]
        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo_mantenimiento")]
        public string TipoMantenimiento { get; set; } = "PREVENTIVO";

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(500)]
        [Display(Name = "Descripción del trabajo")]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        [Display(Name = "Mano de obra")]
        [Column("monto_mano_obra")]
        public decimal MontoManoObra { get; set; }

        [Range(0, double.MaxValue)]
        [Display(Name = "Repuestos")]
        [Column("monto_repuestos")]
        public decimal MontoRepuestos { get; set; }

        [Range(0, double.MaxValue)]
        [Display(Name = "Otros")]
        [Column("monto_otros")]
        public decimal MontoOtros { get; set; }

        [Display(Name = "Total")]
        [Column("total")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal Total { get; set; }

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda")]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Display(Name = "KM odómetro")]
        [Column("km_odometro")]
        public decimal? KmOdometro { get; set; }

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
        public Taller? Taller { get; set; }
    }
}
