using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Opciones del job de tasas de cambio que se editan desde la pantalla (script 024): una sola fila
    /// (<see cref="IdUnico"/>). Una columna nula significa "usar el valor de appsettings.json"; la fila ausente deja
    /// todo como estaba. No guarda secretos: la clave del API del BCH y el token del disparador siguen en variables
    /// de entorno o user-secrets.
    /// </summary>
    [Table("tasas_cambio_configuracion")]
    public class TasaCambioConfiguracion
    {
        public const byte IdUnico = 1;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Column("id_configuracion")]
        public byte IdConfiguracion { get; set; } = IdUnico;

        /// <summary>Interruptor del job automático (worker, endpoint externo y ejecución manual).</summary>
        [Column("habilitado")]
        public bool Habilitado { get; set; }

        /// <summary>Códigos separados por coma (USD,EUR).</summary>
        [StringLength(100)]
        [Column("monedas", TypeName = "varchar(100)")]
        public string? Monedas { get; set; }

        [StringLength(5)]
        [Column("primer_intento", TypeName = "char(5)")]
        public string? PrimerIntento { get; set; }

        [StringLength(5)]
        [Column("ultimo_intento", TypeName = "char(5)")]
        public string? UltimoIntento { get; set; }

        [StringLength(5)]
        [Column("barrido_matutino", TypeName = "char(5)")]
        public string? BarridoMatutino { get; set; }

        [Column("dias_puesta_al_dia")]
        public byte? DiasPuestaAlDia { get; set; }

        [Column("max_intentos_por_fecha")]
        public byte? MaxIntentosPorFecha { get; set; }

        [Column("variacion_diaria_max_pct", TypeName = "decimal(5,2)")]
        public decimal? VariacionDiariaMaxPct { get; set; }

        [Column("diferencia_compra_venta_max_pct", TypeName = "decimal(5,2)")]
        public decimal? DiferenciaCompraVentaMaxPct { get; set; }

        [Column("rango_usd_min", TypeName = "decimal(12,4)")]
        public decimal? RangoUsdMin { get; set; }

        [Column("rango_usd_max", TypeName = "decimal(12,4)")]
        public decimal? RangoUsdMax { get; set; }

        [Column("rango_eur_min", TypeName = "decimal(12,4)")]
        public decimal? RangoEurMin { get; set; }

        [Column("rango_eur_max", TypeName = "decimal(12,4)")]
        public decimal? RangoEurMax { get; set; }

        /// <summary>Correos separados por coma.</summary>
        [StringLength(1000)]
        [Column("destinatarios_alerta")]
        public string? DestinatariosAlerta { get; set; }

        [Required]
        [StringLength(100)]
        [Column("actualizado_por")]
        public string ActualizadoPor { get; set; } = string.Empty;

        [Column("actualizado_utc")]
        public DateTime ActualizadoUtc { get; set; }
    }
}
