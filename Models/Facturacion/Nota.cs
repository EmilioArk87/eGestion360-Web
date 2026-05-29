using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Models.Facturacion
{
    /// <summary>
    /// Nota de crédito o débito. MVP: cabecera única sin líneas detalladas.
    /// - Nota de crédito (tipo='credito'): reduce el saldo de la factura origen.
    /// - Nota de débito  (tipo='debito'): aumenta el saldo de la factura origen.
    /// El correlativo se reserva en factura_secuencias con tipo_documento='nota_credito'|'nota_debito'.
    /// </summary>
    [Table("notas")]
    public class Nota
    {
        [Key]
        [Column("id_nota")]
        public int IdNota { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>credito | debito</summary>
        [Required, StringLength(20)]
        [Column("tipo")]
        public string Tipo { get; set; } = "credito";

        /// <summary>emitida | anulada</summary>
        [Required, StringLength(20)]
        [Column("estado")]
        public string Estado { get; set; } = "emitida";

        [Required, StringLength(20)]
        [Column("serie")]
        public string Serie { get; set; } = "NC-01";

        [Column("numero")]
        public int? Numero { get; set; }

        [Column("id_cliente")]
        public int IdCliente { get; set; }

        [Column("id_factura_origen")]
        public int IdFacturaOrigen { get; set; }

        [Column("fecha_emision")]
        public DateTime FechaEmision { get; set; }

        [Column("monto", TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        [Required, StringLength(3)]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

        [Required, StringLength(500)]
        [Column("motivo")]
        public string Motivo { get; set; } = string.Empty;

        [StringLength(500)]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

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
        public Factura FacturaOrigen { get; set; } = null!;
    }

    public static class NotaTipo
    {
        public const string Credito = "credito";
        public const string Debito  = "debito";
    }
}
