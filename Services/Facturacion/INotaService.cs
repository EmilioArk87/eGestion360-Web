namespace eGestion360Web.Services.Facturacion
{
    /// <summary>
    /// Emisión de notas de crédito y débito.
    /// - NC reduce el saldo de la factura origen (efecto similar a un pago).
    /// - ND aumenta el saldo de la factura origen.
    /// Eventos: nota_credito.emitida / nota_debito.emitida
    /// </summary>
    public interface INotaService
    {
        Task<EmitirNotaResult> EmitirAsync(EmitirNotaInput input, CancellationToken ct = default);
    }

    public sealed class EmitirNotaInput
    {
        public int IdEmpresa { get; set; }
        /// <summary>credito | debito</summary>
        public string Tipo { get; set; } = "credito";
        public string Serie { get; set; } = "NC-01";
        public int IdFacturaOrigen { get; set; }
        public DateTime FechaEmision { get; set; }
        public decimal Monto { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string? Observaciones { get; set; }
        public string Usuario { get; set; } = "system";
    }

    public sealed record EmitirNotaResult(bool Ok, int? IdNota, string? Serie, int? Numero, IReadOnlyList<string> Errores);
}
