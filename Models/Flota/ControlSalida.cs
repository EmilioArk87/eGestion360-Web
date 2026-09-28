using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("control_salidas")]
    public class ControlSalida
    {
        [Key]
        [Column("id_control_salida")]
        public int IdControlSalida { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El vehículo es requerido")]
        [Display(Name = "Vehículo")]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Display(Name = "Conductor")]
        [Column("id_conductor")]
        public int? IdConductor { get; set; }

        [Display(Name = "Ruta")]
        [Column("id_ruta")]
        public int? IdRuta { get; set; }

        [StringLength(200)]
        [Display(Name = "Destino / Ruta")]
        [Column("destino")]
        public string? Destino { get; set; }

        [Required(ErrorMessage = "La fecha y hora de salida es requerida")]
        [Display(Name = "Fecha/Hora Salida")]
        [Column("fecha_hora_salida")]
        public DateTime FechaHoraSalida { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "El odómetro de salida es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El odómetro de salida no puede ser negativo")]
        [Display(Name = "Odómetro de salida")]
        [Column("odometro_salida")]
        public decimal OdometroSalida { get; set; }

        [StringLength(500)]
        [Display(Name = "Observaciones salida")]
        [Column("observaciones_salida")]
        public string? ObservacionesSalida { get; set; }

        [Display(Name = "Fecha/Hora Entrada")]
        [Column("fecha_hora_entrada")]
        public DateTime? FechaHoraEntrada { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "El odómetro de entrada no puede ser negativo")]
        [Display(Name = "Odómetro de entrada")]
        [Column("odometro_entrada")]
        public decimal? OdometroEntrada { get; set; }

        [Display(Name = "KM Recorridos")]
        [Column("km_recorridos")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal? KmRecorridos { get; set; }

        [StringLength(500)]
        [Display(Name = "Observaciones entrada")]
        [Column("observaciones_entrada")]
        public string? ObservacionesEntrada { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Estado")]
        [Column("estado")]
        public string Estado { get; set; } = "ABIERTO"; // ABIERTO (fuera), CERRADO (en sitio), ANULADO

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

        // Propiedades de navegación
        public Vehiculo? Vehiculo { get; set; }
        public Persona? Conductor { get; set; }
        public Ruta? Ruta { get; set; }

        // Propiedad calculada en memoria para mostrar tiempo fuera
        [NotMapped]
        public string TiempoFuera
        {
            get
            {
                var fin = FechaHoraEntrada ?? DateTime.Now;
                var diff = fin - FechaHoraSalida;
                if (diff.TotalMinutes < 1) return "recién";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} h {diff.Minutes} min";
                return $"{(int)diff.TotalDays} d {diff.Hours} h";
            }
        }
    }
}
