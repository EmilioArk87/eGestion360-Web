using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Models.Facturacion
{
    [Table("facturas")]
    public class Factura
    {
        [Key]
        [Column("id_factura")]
        public int IdFactura { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        // ── Identificación documental ────────────────────────────────────────

        /// <summary>borrador | emitida | parcialmente_pagada | pagada | anulada</summary>
        [Required, StringLength(30)]
        [Column("estado")]
        public string Estado { get; set; } = "borrador";

        /// <summary>contado | credito</summary>
        [Required, StringLength(20)]
        [Column("tipo_venta")]
        public string TipoVenta { get; set; } = "contado";

        [Required, StringLength(20)]
        [Column("serie")]
        public string Serie { get; set; } = "F-01";

        [Column("numero")]
        public int? Numero { get; set; }

        /// <summary>CAI (Constancia de Asignación Numérica) hondureño. Opcional en v1.</summary>
        [StringLength(50)]
        [Column("cai_numero")]
        public string? CaiNumero { get; set; }

        // ── Cliente y fechas ─────────────────────────────────────────────────

        [Column("id_cliente")]
        public int IdCliente { get; set; }

        [Column("fecha_emision")]
        public DateTime FechaEmision { get; set; }

        [Column("fecha_vencimiento")]
        public DateTime? FechaVencimiento { get; set; }

        // ── Pago ─────────────────────────────────────────────────────────────

        /// <summary>Solo para contado. Define caja/banco a afectar (vía evento).</summary>
        [Column("id_forma_pago")]
        public int? IdFormaPago { get; set; }

        /// <summary>Solo para crédito. Define vencimientos.</summary>
        [Column("id_condicion_pago")]
        public int? IdCondicionPago { get; set; }

        // ── Moneda ───────────────────────────────────────────────────────────

        [Required, StringLength(3)]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Column("tipo_cambio", TypeName = "decimal(18,8)")]
        public decimal TipoCambio { get; set; } = 1m;

        // ── Totales (calculados server-side) ─────────────────────────────────

        [Column("subtotal", TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column("descuento_global", TypeName = "decimal(18,2)")]
        public decimal DescuentoGlobal { get; set; }

        [Column("base_imponible", TypeName = "decimal(18,2)")]
        public decimal BaseImponible { get; set; }

        [Column("isv_15", TypeName = "decimal(18,2)")]
        public decimal Isv15 { get; set; }

        [Column("isv_18", TypeName = "decimal(18,2)")]
        public decimal Isv18 { get; set; }

        [Column("exento", TypeName = "decimal(18,2)")]
        public decimal Exento { get; set; }

        [Column("retencion", TypeName = "decimal(18,2)")]
        public decimal Retencion { get; set; }

        [Column("total", TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [Column("saldo_pendiente", TypeName = "decimal(18,2)")]
        public decimal SaldoPendiente { get; set; }

        // ── Misceláneo ───────────────────────────────────────────────────────

        [StringLength(500)]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

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

        public Empresa Empresa { get; set; } = null!;
        public Cliente Cliente { get; set; } = null!;
        public FormaPago? FormaPago { get; set; }
        public CondicionPago? CondicionPago { get; set; }
        public ICollection<FacturaDetalle> Detalle { get; set; } = new List<FacturaDetalle>();
    }

    public static class FacturaEstado
    {
        public const string Borrador            = "borrador";
        public const string Emitida             = "emitida";
        public const string ParcialmentePagada  = "parcialmente_pagada";
        public const string Pagada              = "pagada";
        public const string Anulada             = "anulada";
    }

    public static class FacturaTipoVenta
    {
        public const string Contado = "contado";
        public const string Credito = "credito";
    }
}
