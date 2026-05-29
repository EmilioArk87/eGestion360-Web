using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services.Eventos;

namespace eGestion360Web.Services.Facturacion
{
    public sealed class FacturacionService : IFacturacionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDomainEventPublisher _events;
        private readonly ILogger<FacturacionService> _log;

        public FacturacionService(
            ApplicationDbContext db,
            IDomainEventPublisher events,
            ILogger<FacturacionService> log)
        {
            _db = db;
            _events = events;
            _log = log;
        }

        // ──────────────────────────────────────────────────────────────────
        //  EMITIR
        // ──────────────────────────────────────────────────────────────────

        public async Task<EmitirFacturaResult> EmitirAsync(EmitirFacturaInput input, CancellationToken ct = default)
        {
            var errores = new List<string>();

            // ── Validaciones de entrada ──
            if (input.IdEmpresa <= 0) errores.Add("Empresa requerida.");
            if (input.IdCliente <= 0) errores.Add("Cliente requerido.");
            if (input.Lineas == null || input.Lineas.Count == 0) errores.Add("La factura debe tener al menos una línea.");
            if (string.IsNullOrWhiteSpace(input.Serie)) input.Serie = "F-01";

            var tipoVenta = (input.TipoVenta ?? "").Trim().ToLowerInvariant();
            if (tipoVenta != FacturaTipoVenta.Contado && tipoVenta != FacturaTipoVenta.Credito)
                errores.Add($"Tipo de venta inválido: {input.TipoVenta}");

            if (tipoVenta == FacturaTipoVenta.Contado && !input.IdFormaPago.HasValue)
                errores.Add("Venta al contado requiere forma de pago.");
            if (tipoVenta == FacturaTipoVenta.Credito && !input.IdCondicionPago.HasValue)
                errores.Add("Venta a crédito requiere condición de pago.");

            if (errores.Count > 0) return new EmitirFacturaResult(false, null, null, null, errores);

            // ── Verificar referencias multitenant ──
            var cliente = await _db.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCliente == input.IdCliente && c.IdEmpresa == input.IdEmpresa && !c.Eliminado, ct);
            if (cliente is null) errores.Add("Cliente no existe en la empresa.");

            CondicionPago? condicion = null;
            if (input.IdCondicionPago.HasValue)
            {
                condicion = await _db.CondicionesPago.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.IdCondicionPago == input.IdCondicionPago.Value && c.IdEmpresa == input.IdEmpresa && !c.Eliminado, ct);
                if (condicion is null) errores.Add("Condición de pago no existe en la empresa.");
            }

            if (input.IdFormaPago.HasValue)
            {
                var formaOk = await _db.FormasPago.AsNoTracking()
                    .AnyAsync(f => f.IdFormaPago == input.IdFormaPago.Value && f.IdEmpresa == input.IdEmpresa && !f.Eliminado, ct);
                if (!formaOk) errores.Add("Forma de pago no existe en la empresa.");
            }

            if (errores.Count > 0) return new EmitirFacturaResult(false, null, null, null, errores);

            // ── Cargar impuestos referenciados (snapshot de tasas) ──
            input.Lineas ??= new List<EmitirFacturaLinea>();
            var idsImpuesto = input.Lineas.Where(l => l.IdImpuesto.HasValue).Select(l => l.IdImpuesto!.Value).Distinct().ToList();
            var impuestos = await _db.Impuestos.AsNoTracking()
                .Where(i => i.IdEmpresa == input.IdEmpresa && idsImpuesto.Contains(i.IdImpuesto) && !i.Eliminado)
                .ToDictionaryAsync(i => i.IdImpuesto, ct);

            // ── Construir detalle + totales server-side ──
            var detalle = new List<FacturaDetalle>();
            decimal subtotal = 0m, isv15 = 0m, isv18 = 0m, exento = 0m;
            int linea = 0;

            foreach (var l in input.Lineas)
            {
                linea++;

                if (l.Cantidad <= 0)   errores.Add($"Línea {linea}: cantidad debe ser > 0.");
                if (l.PrecioUnitario < 0) errores.Add($"Línea {linea}: precio no puede ser negativo.");
                if (string.IsNullOrWhiteSpace(l.Descripcion)) errores.Add($"Línea {linea}: descripción requerida.");
                if (l.DescuentoPorc < 0 || l.DescuentoPorc > 100) errores.Add($"Línea {linea}: descuento fuera de rango (0..100).");

                decimal tasa = 0m;
                if (l.IdImpuesto.HasValue)
                {
                    if (!impuestos.TryGetValue(l.IdImpuesto.Value, out var imp))
                        errores.Add($"Línea {linea}: impuesto no existe.");
                    else
                        tasa = imp.Tasa;
                }

                var bruto = Math.Round(l.Cantidad * l.PrecioUnitario, 4, MidpointRounding.AwayFromZero);
                var baseImponible = Math.Round(bruto * (1m - l.DescuentoPorc / 100m), 2, MidpointRounding.AwayFromZero);
                var montoImpuesto = Math.Round(baseImponible * tasa / 100m, 2, MidpointRounding.AwayFromZero);
                var totalLinea = baseImponible + montoImpuesto;

                detalle.Add(new FacturaDetalle
                {
                    NumeroLinea     = linea,
                    IdProducto      = l.IdProducto,
                    Descripcion     = l.Descripcion.Trim(),
                    Cantidad        = l.Cantidad,
                    PrecioUnitario  = l.PrecioUnitario,
                    DescuentoPorc   = l.DescuentoPorc,
                    IdImpuesto      = l.IdImpuesto,
                    ImpuestoTasa    = tasa,
                    BaseImponible   = baseImponible,
                    MontoImpuesto   = montoImpuesto,
                    TotalLinea      = totalLinea
                });

                subtotal += baseImponible;
                if      (tasa == 15m) isv15 += montoImpuesto;
                else if (tasa == 18m) isv18 += montoImpuesto;
                else if (tasa == 0m)  exento += baseImponible;
            }

            if (errores.Count > 0) return new EmitirFacturaResult(false, null, null, null, errores);

            // ── Totales de cabecera ──
            var descuentoGlobal = Math.Max(0m, input.DescuentoGlobal);
            var retencion       = Math.Max(0m, input.Retencion);
            var baseFinal       = Math.Max(0m, subtotal - descuentoGlobal);
            var total           = baseFinal + isv15 + isv18 - retencion;

            // ── Reservar número atómicamente ──
            var (numero, caiNumero, errSec) = await ReservarNumeroAsync(input.IdEmpresa, "factura", input.Serie!, input.Usuario, ct);
            if (errSec is not null) return new EmitirFacturaResult(false, null, null, null, new[] { errSec });

            // ── Calcular fecha de vencimiento ──
            DateTime? fechaVenc = null;
            if (tipoVenta == FacturaTipoVenta.Credito && condicion is not null)
                fechaVenc = input.FechaEmision.AddDays(condicion.DiasCredito);

            // ── Crear factura ──
            var now = DateTime.UtcNow;
            var factura = new Factura
            {
                IdEmpresa        = input.IdEmpresa,
                Estado           = FacturaEstado.Emitida,
                TipoVenta        = tipoVenta,
                Serie            = input.Serie!,
                Numero           = numero,
                CaiNumero        = caiNumero,
                IdCliente        = input.IdCliente,
                FechaEmision     = input.FechaEmision,
                FechaVencimiento = fechaVenc,
                IdFormaPago      = input.IdFormaPago,
                IdCondicionPago  = input.IdCondicionPago,
                Moneda           = input.Moneda?.ToUpperInvariant() ?? "HNL",
                TipoCambio       = input.TipoCambio > 0 ? input.TipoCambio : 1m,
                Subtotal         = subtotal,
                DescuentoGlobal  = descuentoGlobal,
                BaseImponible    = baseFinal,
                Isv15            = isv15,
                Isv18            = isv18,
                Exento           = exento,
                Retencion        = retencion,
                Total            = total,
                SaldoPendiente   = tipoVenta == FacturaTipoVenta.Credito ? total : 0m,
                Observaciones    = input.Observaciones,
                CreadoPor        = input.Usuario,
                FechaCreacion    = now,
                Detalle          = detalle
            };

            _db.Set<Factura>().Add(factura);

            // ── Persistir primero para obtener IdFactura ──
            await _db.SaveChangesAsync(ct);

            // ── Publicar evento al outbox y commitear ──
            var eventType = tipoVenta == FacturaTipoVenta.Contado
                ? "factura.emitida.contado"
                : "factura.emitida.credito";

            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: eventType,
                aggregateType: "factura",
                aggregateId: factura.IdFactura.ToString(),
                payload: new
                {
                    factura_id        = factura.IdFactura,
                    cliente_id        = factura.IdCliente,
                    serie             = factura.Serie,
                    numero            = factura.Numero,
                    cai_numero        = factura.CaiNumero,
                    fecha_emision     = factura.FechaEmision,
                    fecha_vencimiento = factura.FechaVencimiento,
                    tipo_venta        = factura.TipoVenta,
                    forma_pago_id     = factura.IdFormaPago,
                    condicion_pago_id = factura.IdCondicionPago,
                    moneda            = factura.Moneda,
                    tipo_cambio       = factura.TipoCambio,
                    subtotal          = factura.Subtotal,
                    descuento_global  = factura.DescuentoGlobal,
                    base_imponible    = factura.BaseImponible,
                    isv_15            = factura.Isv15,
                    isv_18            = factura.Isv18,
                    exento            = factura.Exento,
                    retencion         = factura.Retencion,
                    total             = factura.Total,
                    lineas = detalle.Select(d => new
                    {
                        numero_linea    = d.NumeroLinea,
                        producto_id     = d.IdProducto,
                        descripcion     = d.Descripcion,
                        cantidad        = d.Cantidad,
                        precio_unitario = d.PrecioUnitario,
                        descuento_porc  = d.DescuentoPorc,
                        impuesto_id     = d.IdImpuesto,
                        impuesto_tasa   = d.ImpuestoTasa,
                        base_imponible  = d.BaseImponible,
                        monto_impuesto  = d.MontoImpuesto,
                        total_linea     = d.TotalLinea
                    }).ToList()
                },
                occurredAt: factura.FechaEmision
            );

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Factura emitida {Serie}-{Numero} (id {Id}) empresa {Emp} total {Total}",
                factura.Serie, factura.Numero, factura.IdFactura, factura.IdEmpresa, factura.Total);

            return new EmitirFacturaResult(true, factura.IdFactura, factura.Serie, factura.Numero, Array.Empty<string>());
        }

        // ──────────────────────────────────────────────────────────────────
        //  ANULAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<AnularFacturaResult> AnularAsync(AnularFacturaInput input, CancellationToken ct = default)
        {
            var errores = new List<string>();

            if (input.IdEmpresa <= 0) errores.Add("Empresa requerida.");
            if (input.IdFactura <= 0) errores.Add("Factura requerida.");
            if (string.IsNullOrWhiteSpace(input.Motivo)) errores.Add("Motivo de anulación requerido.");
            if (errores.Count > 0) return new AnularFacturaResult(false, errores);

            var factura = await _db.Set<Factura>()
                .FirstOrDefaultAsync(f => f.IdFactura == input.IdFactura && f.IdEmpresa == input.IdEmpresa && !f.Eliminado, ct);

            if (factura is null) return new AnularFacturaResult(false, new[] { "Factura no encontrada." });

            if (factura.Estado == FacturaEstado.Anulada)
                return new AnularFacturaResult(false, new[] { "La factura ya está anulada." });

            if (factura.Estado == FacturaEstado.ParcialmentePagada || factura.Estado == FacturaEstado.Pagada)
                return new AnularFacturaResult(false, new[] { "No se puede anular: la factura ya tiene pagos aplicados. Emita una nota de crédito en su lugar." });

            var now = DateTime.UtcNow;
            factura.Estado            = FacturaEstado.Anulada;
            factura.MotivoAnulacion   = input.Motivo.Trim();
            factura.FechaAnulacion    = now;
            factura.ModificadoPor     = input.Usuario;
            factura.FechaModificacion = now;
            factura.SaldoPendiente    = 0m;

            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: "factura.anulada",
                aggregateType: "factura",
                aggregateId: factura.IdFactura.ToString(),
                payload: new
                {
                    factura_id = factura.IdFactura,
                    serie      = factura.Serie,
                    numero     = factura.Numero,
                    motivo     = factura.MotivoAnulacion,
                    total      = factura.Total
                },
                occurredAt: now
            );

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Factura {Serie}-{Numero} (id {Id}) anulada", factura.Serie, factura.Numero, factura.IdFactura);
            return new AnularFacturaResult(true, Array.Empty<string>());
        }

        // ──────────────────────────────────────────────────────────────────
        //  RESERVA ATÓMICA DE CORRELATIVO
        // ──────────────────────────────────────────────────────────────────

        private async Task<(int Numero, string? Cai, string? Error)> ReservarNumeroAsync(
            int idEmpresa, string tipoDocumento, string serie, string usuario, CancellationToken ct)
        {
            // Crear secuencia si no existe (race tolerable: la unique constraint protege)
            var existe = await _db.Set<FacturaSecuencia>()
                .AnyAsync(s => s.IdEmpresa == idEmpresa && s.TipoDocumento == tipoDocumento && s.Serie == serie, ct);

            if (!existe)
            {
                _db.Set<FacturaSecuencia>().Add(new FacturaSecuencia
                {
                    IdEmpresa     = idEmpresa,
                    TipoDocumento = tipoDocumento,
                    Serie         = serie,
                    ProximoNumero = 1,
                    Activo        = true,
                    CreadoPor     = usuario,
                    FechaCreacion = DateTime.UtcNow
                });
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { /* ignorar carrera */ }
            }

            // UPDATE atómico devolviendo el número reservado y el CAI vigente
            var sql = @"
UPDATE factura_secuencias
SET proximo_numero = proximo_numero + 1
OUTPUT INSERTED.proximo_numero - 1 AS Numero,
       INSERTED.cai_numero         AS Cai,
       INSERTED.rango_final        AS RangoFinal,
       INSERTED.fecha_limite_emision AS FechaLimite
WHERE id_empresa     = @IdEmpresa
  AND tipo_documento = @TipoDocumento
  AND serie          = @Serie
  AND activo         = 1;";

            var result = await _db.Database
                .SqlQueryRaw<ReservaSec>(
                    sql,
                    new SqlParameter("@IdEmpresa", idEmpresa),
                    new SqlParameter("@TipoDocumento", tipoDocumento),
                    new SqlParameter("@Serie", serie))
                .FirstOrDefaultAsync(ct);

            if (result is null)
                return (0, null, $"No hay secuencia activa para {tipoDocumento}/{serie} en la empresa.");

            if (result.RangoFinal.HasValue && result.Numero > result.RangoFinal.Value)
                return (0, null, $"Rango CAI agotado para serie {serie} ({result.RangoFinal}). Configure una nueva secuencia.");

            if (result.FechaLimite.HasValue && DateTime.UtcNow > result.FechaLimite.Value)
                return (0, null, $"CAI vencido para serie {serie} (fecha límite {result.FechaLimite:yyyy-MM-dd}).");

            return (result.Numero, result.Cai, null);
        }

        private sealed class ReservaSec
        {
            public int Numero { get; set; }
            public string? Cai { get; set; }
            public int? RangoFinal { get; set; }
            public DateTime? FechaLimite { get; set; }
        }
    }
}
