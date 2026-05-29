using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Facturacion
{
    /// <summary>
    /// Asocia un pago con una factura aplicando un monto específico. Tabla N:M con
    /// monto: un pago puede aplicarse a varias facturas, y una factura puede recibir
    /// múltiples aplicaciones de distintos pagos.
    /// </summary>
    [Table("pago_aplicaciones")]
    public class PagoAplicacion
    {
        [Key]
        [Column("id_aplicacion")]
        public int IdAplicacion { get; set; }

        [Column("id_pago")]
        public int IdPago { get; set; }

        [Column("id_factura")]
        public int IdFactura { get; set; }

        [Column("monto", TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        [Column("fecha_aplicacion")]
        public DateTime FechaAplicacion { get; set; }

        [StringLength(100), Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        public Pago Pago { get; set; } = null!;
        public Factura Factura { get; set; } = null!;
    }
}
