using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Models.Facturacion
{
    [Table("pagos")]
    public class Pago
    {
        [Key]
        [Column("id_pago")]
        public int IdPago { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>recibido | parcialmente_aplicado | aplicado | anulado</summary>
        [Required, StringLength(30)]
        [Column("estado")]
        public string Estado { get; set; } = "recibido";

        [Required, StringLength(20)]
        [Column("serie")]
        public string Serie { get; set; } = "RC-01";

        [Column("numero")]
        public int? Numero { get; set; }

        [Column("id_cliente")]
        public int IdCliente { get; set; }

        [Column("fecha")]
        public DateTime Fecha { get; set; }

        [Column("id_forma_pago")]
        public int IdFormaPago { get; set; }

        [Column("monto", TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        [Column("saldo_favor", TypeName = "decimal(18,2)")]
        public decimal SaldoFavor { get; set; }

        [Required, StringLength(3)]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Column("tipo_cambio", TypeName = "decimal(18,8)")]
        public decimal TipoCambio { get; set; } = 1m;

        /// <summary>Número de cheque, transferencia, voucher de tarjeta, etc.</summary>
        [StringLength(100)]
        [Column("referencia")]
        public string? Referencia { get; set; }

        [StringLength(500)]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

        [StringLength(500)]
        [Column("motivo_anulacion")]
        public string? MotivoAnulacion { get; set; }

        [Column("fecha_anulacion")]
        public DateTime? FechaAnulacion { get; set; }

        // Auditoría
        [Column("eliminado")]
        public bool Eliminado { get; set; }

        [Column("fecha_eliminado")]
        public DateTime? FechaEliminado { get; set; }

        [StringLength(100), Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100), Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp, Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();

        public Empresa Empresa { get; set; } = null!;
        public Cliente Cliente { get; set; } = null!;
        public FormaPago FormaPago { get; set; } = null!;
        public ICollection<PagoAplicacion> Aplicaciones { get; set; } = new List<PagoAplicacion>();
    }

    public static class PagoEstado
    {
        public const string Recibido               = "recibido";
        public const string ParcialmenteAplicado   = "parcialmente_aplicado";
        public const string Aplicado               = "aplicado";
        public const string Anulado                = "anulado";
    }
}
