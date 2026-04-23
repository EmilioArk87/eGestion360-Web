using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("odometro_diario")]
    public class OdometroDiario
    {
        [Key]
        [Column("id_odometro_diario")]
        public int IdOdometroDiario { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Required(ErrorMessage = "La fecha es requerida")]
        [Display(Name = "Fecha")]
        [Column("fecha")]
        public DateOnly Fecha { get; set; }

        [Required(ErrorMessage = "El KM inicial es requerido")]
        [Range(0, double.MaxValue)]
        [Display(Name = "KM inicial")]
        [Column("km_inicial")]
        public decimal KmInicial { get; set; }

        [Required(ErrorMessage = "El KM final es requerido")]
        [Range(0, double.MaxValue)]
        [Display(Name = "KM final")]
        [Column("km_final")]
        public decimal KmFinal { get; set; }

        [Display(Name = "KM recorridos")]
        [Column("km_recorridos")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal KmRecorridos { get; set; }

        [Display(Name = "Ruta")]
        [Column("id_ruta")]
        public int? IdRuta { get; set; }

        [Display(Name = "Conductor")]
        [Column("id_conductor")]
        public int? IdConductor { get; set; }

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
        public Ruta? Ruta { get; set; }
        public Persona? Conductor { get; set; }
    }
}
