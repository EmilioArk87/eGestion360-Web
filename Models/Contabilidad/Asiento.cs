using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>
    /// Cabecera de un asiento contable (tabla <c>ct_asientos</c>). Cumple partida doble:
    /// <see cref="TotalDebito"/> debe ser igual a <see cref="TotalCredito"/> al persistir.
    /// Si <see cref="Origen"/> es automático, <see cref="IdEventoOrigen"/> referencia el
    /// <c>domain_events.id_evento</c> que lo generó (idempotencia).
    /// </summary>
    [Table("ct_asientos")]
    public class Asiento
    {
        [Key]
        [Column("id_asiento")]
        public int IdAsiento { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_periodo")]
        public int IdPeriodo { get; set; }

        /// <summary>Correlativo asignado al mayorizar (null mientras es borrador).</summary>
        [Column("numero")]
        public int? Numero { get; set; }

        [Column("fecha", TypeName = "date")]
        public DateTime Fecha { get; set; }

        /// <summary>apertura | diario | ajuste | cierre</summary>
        [Required, StringLength(20)]
        [Column("tipo_asiento")]
        public string TipoAsiento { get; set; } = AsientoTipo.Diario;

        [Required, StringLength(500)]
        [Column("concepto")]
        public string Concepto { get; set; } = string.Empty;

        /// <summary>manual | automatico</summary>
        [Required, StringLength(20)]
        [Column("origen")]
        public string Origen { get; set; } = AsientoOrigen.Manual;

        /// <summary>domain_events.id_evento que originó el asiento (solo automáticos).</summary>
        [Column("id_evento_origen")]
        public long? IdEventoOrigen { get; set; }

        [Column("total_debito", TypeName = "decimal(18,2)")]
        public decimal TotalDebito { get; set; }

        [Column("total_credito", TypeName = "decimal(18,2)")]
        public decimal TotalCredito { get; set; }

        /// <summary>borrador | mayorizado | anulado</summary>
        [Required, StringLength(20)]
        [Column("estado")]
        public string Estado { get; set; } = AsientoEstado.Borrador;

        [StringLength(500)]
        [Column("motivo_anulacion")]
        public string? MotivoAnulacion { get; set; }

        [Column("fecha_anulacion")]
        public DateTime? FechaAnulacion { get; set; }

        // ── Auditoría ────────────────────────────────────────────────────────

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

        // ── Navegación ───────────────────────────────────────────────────────

        public PeriodoContable Periodo { get; set; } = null!;
        public ICollection<AsientoMovimiento> Movimientos { get; set; } = new List<AsientoMovimiento>();
    }

    public static class AsientoEstado
    {
        public const string Borrador   = "borrador";
        public const string Mayorizado = "mayorizado";
        public const string Anulado    = "anulado";
    }

    public static class AsientoTipo
    {
        public const string Apertura = "apertura";
        public const string Diario   = "diario";
        public const string Ajuste   = "ajuste";
        public const string Cierre   = "cierre";
    }

    public static class AsientoOrigen
    {
        public const string Manual     = "manual";
        public const string Automatico = "automatico";
    }
}
