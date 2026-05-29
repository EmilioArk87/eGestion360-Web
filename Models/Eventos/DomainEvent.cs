using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Eventos
{
    /// <summary>
    /// Outbox de eventos de dominio.
    /// Cada fila representa un hecho de negocio publicado por algún módulo operativo,
    /// listo para ser consumido por handlers (contabilidad, notificaciones, etc.).
    ///
    /// Reglas:
    ///   - El INSERT debe ocurrir en la MISMA transacción que la operación de negocio.
    ///   - Los handlers son idempotentes: deben tolerar que un mismo evento se entregue
    ///     más de una vez (usando id_evento como clave de idempotencia).
    ///   - El worker reclama eventos con UPDATE atómico (locked_until + worker_id).
    /// </summary>
    [Table("domain_events")]
    public class DomainEvent
    {
        [Key]
        [Column("id_evento")]
        public long IdEvento { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>
        /// Identificador del tipo de evento, ej. "factura.emitida.credito".
        /// Convención: dominio.accion(.subtipo) en minúsculas, separado por puntos.
        /// </summary>
        [Required]
        [StringLength(100)]
        [Column("event_type")]
        public string EventType { get; set; } = string.Empty;

        /// <summary>Tipo del agregado de origen, ej. "factura", "pago", "deposito".</summary>
        [Required]
        [StringLength(50)]
        [Column("aggregate_type")]
        public string AggregateType { get; set; } = string.Empty;

        /// <summary>Identificador del agregado origen (string para soportar int/Guid/compuesto).</summary>
        [Required]
        [StringLength(50)]
        [Column("aggregate_id")]
        public string AggregateId { get; set; } = string.Empty;

        /// <summary>Versión del esquema del evento. Empieza en 1; subir si cambia la forma del payload.</summary>
        [Column("event_version")]
        public int EventVersion { get; set; } = 1;

        /// <summary>JSON con los datos de negocio del evento. NO contiene cuentas contables ni lógica de asientos.</summary>
        [Required]
        [Column("payload")]
        public string Payload { get; set; } = "{}";

        /// <summary>Cuándo ocurrió el hecho de negocio (no cuándo se insertó).</summary>
        [Column("occurred_at")]
        public DateTime OccurredAt { get; set; }

        /// <summary>pending | processing | processed | failed | dead</summary>
        [Required]
        [StringLength(20)]
        [Column("status")]
        public string Status { get; set; } = "pending";

        [Column("intentos")]
        public int Intentos { get; set; }

        [Column("max_intentos")]
        public int MaxIntentos { get; set; } = 10;

        /// <summary>Worker ha bloqueado este evento hasta esta fecha. Otros workers no lo tocan.</summary>
        [Column("locked_until")]
        public DateTime? LockedUntil { get; set; }

        [StringLength(100)]
        [Column("worker_id")]
        public string? WorkerId { get; set; }

        /// <summary>Para backoff exponencial: el dispatcher no tomará el evento antes de esta fecha.</summary>
        [Column("proximo_intento_en")]
        public DateTime? ProximoIntentoEn { get; set; }

        [Column("ultimo_error")]
        public string? UltimoError { get; set; }

        [Column("processed_at")]
        public DateTime? ProcessedAt { get; set; }

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }

    public static class DomainEventStatus
    {
        public const string Pending    = "pending";
        public const string Processing = "processing";
        public const string Processed  = "processed";
        public const string Failed     = "failed";
        public const string Dead       = "dead";
    }
}
