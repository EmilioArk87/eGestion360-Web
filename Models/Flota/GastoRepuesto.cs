using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("gastos_repuestos")]
    public class GastoRepuesto
    {
        [Key]
        [Column("id_gasto_repuesto")]
        public int IdGastoRepuesto { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Required(ErrorMessage = "La categoría es requerida")]
        [Display(Name = "Categoría")]
        [Column("id_categoria_repuesto")]
        public int IdCategoriaRepuesto { get; set; }

        [Required(ErrorMessage = "La fecha es requerida")]
        [Display(Name = "Fecha")]
        [Column("fecha")]
        public DateOnly Fecha { get; set; }

        [StringLength(50)]
        [Display(Name = "Nº Factura")]
        [Column("no_factura")]
        public string? NoFactura { get; set; }

        [StringLength(150)]
        [Display(Name = "Proveedor")]
        [Column("proveedor")]
        public string? Proveedor { get; set; }

        [Required(ErrorMessage = "La descripción es requerida")]
        [StringLength(250)]
        [Display(Name = "Descripción")]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Range(0.001, double.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        [Display(Name = "Cantidad")]
        [Column("cantidad")]
        public decimal Cantidad { get; set; } = 1;

        [Required(ErrorMessage = "El precio unitario es requerido")]
        [Range(0, double.MaxValue)]
        [Display(Name = "Precio unitario")]
        [Column("precio_unitario")]
        public decimal PrecioUnitario { get; set; }

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda")]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Display(Name = "Subtotal")]
        [Column("subtotal")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal Subtotal { get; set; }

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
        public CategoriaRepuesto? CategoriaRepuesto { get; set; }
    }
}
