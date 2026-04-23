using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("vehiculos")]
    public class Vehiculo
    {
        [Key]
        [Column("id_vehiculo")]
        public int IdVehiculo { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El tipo de vehículo es requerido")]
        [Display(Name = "Tipo de vehículo")]
        [Column("id_tipo_vehiculo")]
        public int IdTipoVehiculo { get; set; }

        [Display(Name = "Ruta asignada")]
        [Column("id_ruta")]
        public int? IdRuta { get; set; }

        [Required(ErrorMessage = "La placa es requerida")]
        [StringLength(20)]
        [Display(Name = "Placa")]
        [Column("placa")]
        public string Placa { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Número interno")]
        [Column("numero_interno")]
        public string? NumeroInterno { get; set; }

        [StringLength(50)]
        [Display(Name = "Marca")]
        [Column("marca")]
        public string? Marca { get; set; }

        [StringLength(50)]
        [Display(Name = "Modelo")]
        [Column("modelo")]
        public string? Modelo { get; set; }

        [Range(1950, 2100, ErrorMessage = "Año fuera de rango")]
        [Display(Name = "Año")]
        [Column("anio")]
        public short? Anio { get; set; }

        [StringLength(30)]
        [Display(Name = "VIN / Chasis")]
        [Column("vin")]
        public string? Vin { get; set; }

        [StringLength(30)]
        [Display(Name = "Color")]
        [Column("color")]
        public string? Color { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "La capacidad debe ser mayor a 0")]
        [Display(Name = "Capacidad")]
        [Column("capacidad")]
        public int? Capacidad { get; set; }

        [Required(ErrorMessage = "El tipo de combustible es requerido")]
        [StringLength(30)]
        [Display(Name = "Tipo de combustible")]
        [Column("tipo_combustible")]
        public string TipoCombustible { get; set; } = "DIESEL";

        [Range(0, double.MaxValue, ErrorMessage = "El KM inicial no puede ser negativo")]
        [Display(Name = "KM inicial")]
        [Column("km_inicial")]
        public decimal KmInicial { get; set; }

        [Display(Name = "Fecha de alta")]
        [Column("fecha_alta")]
        public DateOnly? FechaAlta { get; set; }

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

        public TipoVehiculo? TipoVehiculo { get; set; }
        public Ruta? Ruta { get; set; }
    }
}
