using eGestion360Web.Models.Facturacion;

namespace eGestion360Web.Services.Facturacion
{
    /// <summary>
    /// Entrada al módulo de facturación. Encapsula reglas de cálculo, numeración
    /// atómica, publicación de eventos al outbox y validaciones.
    /// </summary>
    public interface IFacturacionService
    {
        /// <summary>
        /// Emite una factura definitiva:
        ///   1. Valida (cliente, líneas, forma/condición de pago según tipo_venta).
        ///   2. Recalcula totales server-side desde el detalle.
        ///   3. Reserva número atómico de la secuencia.
        ///   4. Publica evento factura.emitida.contado o factura.emitida.credito.
        ///   5. Commit atómico (factura + detalle + secuencia + evento).
        /// </summary>
        Task<EmitirFacturaResult> EmitirAsync(EmitirFacturaInput input, CancellationToken ct = default);

        /// <summary>
        /// Anula una factura emitida. Conserva el correlativo (no se reusa).
        /// Publica evento factura.anulada.
        /// </summary>
        Task<AnularFacturaResult> AnularAsync(AnularFacturaInput input, CancellationToken ct = default);
    }

    // ── DTOs de entrada ─────────────────────────────────────────────────────

    public sealed class EmitirFacturaInput
    {
        public int IdEmpresa { get; set; }
        public int IdCliente { get; set; }
        public string TipoVenta { get; set; } = FacturaTipoVenta.Contado;
        public DateTime FechaEmision { get; set; }
        public int? IdFormaPago { get; set; }
        public int? IdCondicionPago { get; set; }
        public string Moneda { get; set; } = "HNL";
        public decimal TipoCambio { get; set; } = 1m;
        public string Serie { get; set; } = "F-01";
        public string? Observaciones { get; set; }
        public decimal DescuentoGlobal { get; set; }
        public decimal Retencion { get; set; }
        public List<EmitirFacturaLinea> Lineas { get; set; } = new();
        public string Usuario { get; set; } = "system";
    }

    public sealed class EmitirFacturaLinea
    {
        public int? IdProducto { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal DescuentoPorc { get; set; }
        public int? IdImpuesto { get; set; }
    }

    public sealed class AnularFacturaInput
    {
        public int IdEmpresa { get; set; }
        public int IdFactura { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string Usuario { get; set; } = "system";
    }

    // ── DTOs de salida ──────────────────────────────────────────────────────

    public sealed record EmitirFacturaResult(bool Ok, int? IdFactura, string? Serie, int? Numero, IReadOnlyList<string> Errores);
    public sealed record AnularFacturaResult(bool Ok, IReadOnlyList<string> Errores);
}
