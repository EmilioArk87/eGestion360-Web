using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Bitácora del job de tasas de cambio (script 020): una fila por ejecución o intento.
    /// Se escribe en una transacción separada de la de las tasas, para que un error al guardar
    /// no borre el registro del error.
    /// </summary>
    [Table("tasas_cambio_ejecuciones")]
    public class TasaCambioEjecucion
    {
        [Key]
        [Column("id_ejecucion")]
        public long IdEjecucion { get; set; }

        [Required]
        [StringLength(40)]
        [Column("job", TypeName = "varchar(40)")]
        public string Job { get; set; } = TasasCambioCatalogo.Job;

        /// <summary>PROGRAMADO, EXTERNO, MANUAL o REINTENTO (<see cref="TasasCambioCatalogo.Disparador"/>).</summary>
        [Required]
        [StringLength(12)]
        [Column("disparador", TypeName = "varchar(12)")]
        public string Disparador { get; set; } = string.Empty;

        /// <summary>Fecha de vigencia que se buscaba.</summary>
        [Column("fecha_objetivo")]
        public DateOnly FechaObjetivo { get; set; }

        [Column("intento")]
        public byte Intento { get; set; } = 1;

        /// <summary>Primera ejecución de la cadena de reintentos.</summary>
        [Column("id_ejecucion_origen")]
        public long? IdEjecucionOrigen { get; set; }

        /// <summary>Ver <see cref="TasasCambioCatalogo.EstadoEjecucion"/>.</summary>
        [Required]
        [StringLength(20)]
        [Column("estado", TypeName = "varchar(20)")]
        public string Estado { get; set; } = TasasCambioCatalogo.EstadoEjecucion.EnCurso;

        [StringLength(20)]
        [Column("fuente", TypeName = "varchar(20)")]
        public string? Fuente { get; set; }

        /// <summary>URL consultada, sin la clave del API.</summary>
        [StringLength(400)]
        [Column("endpoint")]
        public string? Endpoint { get; set; }

        [Column("http_status")]
        public short? HttpStatus { get; set; }

        [Column("leidos")]
        public int Leidos { get; set; }

        [Column("insertados")]
        public int Insertados { get; set; }

        [Column("reemplazados")]
        public int Reemplazados { get; set; }

        [Column("duplicados")]
        public int Duplicados { get; set; }

        [Column("invalidos")]
        public int Invalidos { get; set; }

        /// <summary>SHA-256 (hex, 64 caracteres) de la respuesta de la fuente.</summary>
        [StringLength(64)]
        [Column("hash_contenido", TypeName = "char(64)")]
        public string? HashContenido { get; set; }

        [StringLength(1000)]
        [Column("mensaje")]
        public string? Mensaje { get; set; }

        [Column("detalle_error")]
        public string? DetalleError { get; set; }

        [Column("proximo_intento_utc")]
        public DateTime? ProximoIntentoUtc { get; set; }

        [Column("notificado")]
        public bool Notificado { get; set; }

        [Required]
        [StringLength(100)]
        [Column("servidor")]
        public string Servidor { get; set; } = string.Empty;

        [StringLength(30)]
        [Column("version_app", TypeName = "varchar(30)")]
        public string? VersionApp { get; set; }

        /// <summary>"job" o el usuario que la disparó a mano.</summary>
        [Required]
        [StringLength(100)]
        [Column("ejecutado_por")]
        public string EjecutadoPor { get; set; } = string.Empty;

        [Column("inicio_utc")]
        public DateTime InicioUtc { get; set; }

        [Column("fin_utc")]
        public DateTime? FinUtc { get; set; }

        public TasaCambioEjecucion? EjecucionOrigen { get; set; }
        public List<TasaCambioEjecucionDetalle> Detalles { get; set; } = new();
    }

    /// <summary>Resultado por moneda y tipo dentro de una ejecución (script 020).</summary>
    [Table("tasas_cambio_ejecuciones_detalle")]
    public class TasaCambioEjecucionDetalle
    {
        [Key]
        [Column("id_detalle")]
        public long IdDetalle { get; set; }

        [Column("id_ejecucion")]
        public long IdEjecucion { get; set; }

        [Required]
        [StringLength(3)]
        [Column("moneda_origen", TypeName = "char(3)")]
        public string MonedaOrigen { get; set; } = string.Empty;

        [Required]
        [StringLength(3)]
        [Column("moneda_destino", TypeName = "char(3)")]
        public string MonedaDestino { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        [Column("tipo_tasa", TypeName = "varchar(10)")]
        public string TipoTasa { get; set; } = string.Empty;

        [Column("fecha_vigencia")]
        public DateOnly? FechaVigencia { get; set; }

        [Column("valor_leido", TypeName = "decimal(18,8)")]
        public decimal? ValorLeido { get; set; }

        /// <summary>Ver <see cref="TasasCambioCatalogo.ResultadoDetalle"/>.</summary>
        [Required]
        [StringLength(20)]
        [Column("resultado", TypeName = "varchar(20)")]
        public string Resultado { get; set; } = string.Empty;

        [StringLength(500)]
        [Column("motivo")]
        public string? Motivo { get; set; }

        [Column("id_tasa_cambio")]
        public int? IdTasaCambio { get; set; }

        public TasaCambioEjecucion? Ejecucion { get; set; }
        public TasaCambio? TasaCambio { get; set; }
    }
}
