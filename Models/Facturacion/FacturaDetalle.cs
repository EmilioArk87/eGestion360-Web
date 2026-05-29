using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Models.Facturacion
{
    [Table("factura_detalle")]
    public class FacturaDetalle
    {
        [Key]
        [Column("id_factura_detalle")]
        public int IdFacturaDetalle { get; set; }

        [Column("id_factura")]
        public int IdFactura { get; set; }

        [Column("numero_linea")]
        public int NumeroLinea { get; set; }

        [Column("id_producto")]
        public int? IdProducto { get; set; }

        [Required, StringLength(300)]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Column("cantidad", TypeName = "decimal(18,4)")]
        public decimal Cantidad { get; set; }

        [Column("precio_unitario", TypeName = "decimal(18,4)")]
        public decimal PrecioUnitario { get; set; }

        /// <summary>Porcentaje de descuento aplicado sobre la línea (0..100).</summary>
        [Column("descuento_porc", TypeName = "decimal(9,4)")]
        public decimal DescuentoPorc { get; set; }

        [Column("id_impuesto")]
        public int? IdImpuesto { get; set; }

        /// <summary>Snapshot de la tasa al momento de emisión (0, 15, 18…).</summary>
        [Column("impuesto_tasa", TypeName = "decimal(9,4)")]
        public decimal ImpuestoTasa { get; set; }

        // ── Totales calculados por línea ──────────────────────────────────────

        [Column("base_imponible", TypeName = "decimal(18,2)")]
        public decimal BaseImponible { get; set; }

        [Column("monto_impuesto", TypeName = "decimal(18,2)")]
        public decimal MontoImpuesto { get; set; }

        [Column("total_linea", TypeName = "decimal(18,2)")]
        public decimal TotalLinea { get; set; }

        public Factura Factura { get; set; } = null!;
        public ProductoServicio? Producto { get; set; }
        public Impuesto? Impuesto { get; set; }
    }
}
