namespace eGestion360Web.Services.Facturacion
{
    /// <summary>
    /// Gestión de pagos recibidos y su aplicación a facturas.
    ///
    /// Flujo típico:
    ///   1. Cliente paga → RegistrarPagoAsync (opcionalmente con aplicaciones iniciales).
    ///   2. El pago aparece como 'recibido' y/o 'parcialmente_aplicado'.
    ///   3. Más tarde se puede AplicarSaldoAFavorAsync a más facturas.
    ///   4. Cuando el saldo a favor llega a 0 → 'aplicado'.
    ///
    /// Cada operación publica eventos al outbox:
    ///   - pago.recibido (al registrar)
    ///   - pago.aplicado (cuando se aplica monto a facturas; payload incluye lista)
    ///   - pago.anulado (al anular)
    /// </summary>
    public interface IPagoService
    {
        Task<RegistrarPagoResult> RegistrarPagoAsync(RegistrarPagoInput input, CancellationToken ct = default);

        /// <summary>Aplica saldo a favor restante de un pago existente a facturas adicionales.</summary>
        Task<AplicarPagoResult> AplicarSaldoAFavorAsync(AplicarPagoInput input, CancellationToken ct = default);

        Task<AnularPagoResult> AnularPagoAsync(AnularPagoInput input, CancellationToken ct = default);
    }

    // ── DTOs entrada ────────────────────────────────────────────────────────

    public sealed class RegistrarPagoInput
    {
        public int IdEmpresa { get; set; }
        public int IdCliente { get; set; }
        public DateTime Fecha { get; set; }
        public int IdFormaPago { get; set; }
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "HNL";
        public decimal TipoCambio { get; set; } = 1m;
        public string? Referencia { get; set; }
        public string? Observaciones { get; set; }
        public string Serie { get; set; } = "RC-01";
        public string Usuario { get; set; } = "system";
        /// <summary>Opcional: aplicaciones iniciales (factura_id, monto). Si vacío, queda como anticipo.</summary>
        public List<AplicacionInput> Aplicaciones { get; set; } = new();
    }

    public sealed class AplicacionInput
    {
        public int IdFactura { get; set; }
        public decimal Monto { get; set; }
    }

    public sealed class AplicarPagoInput
    {
        public int IdEmpresa { get; set; }
        public int IdPago { get; set; }
        public List<AplicacionInput> Aplicaciones { get; set; } = new();
        public string Usuario { get; set; } = "system";
    }

    public sealed class AnularPagoInput
    {
        public int IdEmpresa { get; set; }
        public int IdPago { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string Usuario { get; set; } = "system";
    }

    // ── DTOs salida ─────────────────────────────────────────────────────────

    public sealed record RegistrarPagoResult(bool Ok, int? IdPago, string? Serie, int? Numero, decimal SaldoFavor, IReadOnlyList<string> Errores);
    public sealed record AplicarPagoResult(bool Ok, decimal SaldoFavorRestante, IReadOnlyList<string> Errores);
    public sealed record AnularPagoResult(bool Ok, IReadOnlyList<string> Errores);
}
