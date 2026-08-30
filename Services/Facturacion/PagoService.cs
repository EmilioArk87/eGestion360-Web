using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services.Eventos;

namespace eGestion360Web.Services.Facturacion
{
    public sealed class PagoService : IPagoService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDomainEventPublisher _events;
        private readonly ILogger<PagoService> _log;

        public PagoService(ApplicationDbContext db, IDomainEventPublisher events, ILogger<PagoService> log)
        {
            _db = db;
            _events = events;
            _log = log;
        }

        // ──────────────────────────────────────────────────────────────────
        //  REGISTRAR PAGO
        // ──────────────────────────────────────────────────────────────────

        public async Task<RegistrarPagoResult> RegistrarPagoAsync(RegistrarPagoInput input, CancellationToken ct = default)
        {
            var errs = new List<string>();

            if (input.IdEmpresa <= 0) errs.Add("Empresa requerida.");
            if (input.IdCliente <= 0) errs.Add("Cliente requerido.");
            if (input.IdFormaPago <= 0) errs.Add("Forma de pago requerida.");
            if (input.Monto <= 0) errs.Add("Monto debe ser > 0.");
            if (string.IsNullOrWhiteSpace(input.Serie)) input.Serie = "RC-01";
            if (errs.Count > 0) return new RegistrarPagoResult(false, null, null, null, 0, errs);

            // Validaciones referenciales
            var clienteOk = await _db.Clientes.AnyAsync(c => c.IdCliente == input.IdCliente && c.IdEmpresa == input.IdEmpresa && !c.Eliminado, ct);
            if (!clienteOk) errs.Add("Cliente no existe en la empresa.");

            var fpOk = await _db.FormasPago.AnyAsync(f => f.IdFormaPago == input.IdFormaPago && f.IdEmpresa == input.IdEmpresa && !f.Eliminado, ct);
            if (!fpOk) errs.Add("Forma de pago no existe en la empresa.");

            if (errs.Count > 0) return new RegistrarPagoResult(false, null, null, null, 0, errs);

            // Validar aplicaciones iniciales (si las hay)
            var aplicaciones = input.Aplicaciones?.Where(a => a.Monto > 0).ToList() ?? new List<AplicacionInput>();
            decimal totalAplicar = aplicaciones.Sum(a => a.Monto);

            if (totalAplicar > input.Monto)
                errs.Add($"Suma de aplicaciones ({totalAplicar:N2}) supera el monto del pago ({input.Monto:N2}).");

            if (errs.Count > 0) return new RegistrarPagoResult(false, null, null, null, 0, errs);

            // Cargar facturas referenciadas y validar
            var idsFacturas = aplicaciones.Select(a => a.IdFactura).Distinct().ToList();
            var facturas = await _db.Facturas
                .Where(f => f.IdEmpresa == input.IdEmpresa && idsFacturas.Contains(f.IdFactura) && !f.Eliminado)
                .ToDictionaryAsync(f => f.IdFactura, ct);

            foreach (var ap in aplicaciones)
            {
                if (!facturas.TryGetValue(ap.IdFactura, out var fac))
                { errs.Add($"Factura {ap.IdFactura} no existe."); continue; }

                if (fac.IdCliente != input.IdCliente)
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} pertenece a otro cliente."); continue; }

                if (fac.Estado == FacturaEstado.Anulada || fac.Estado == FacturaEstado.Pagada)
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} está {fac.Estado}, no admite pago."); continue; }

                if (fac.Moneda != (input.Moneda?.ToUpperInvariant() ?? "HNL"))
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} en moneda {fac.Moneda} no coincide con el pago en {input.Moneda}."); continue; }

                if (ap.Monto > fac.SaldoPendiente)
                { errs.Add($"Aplicación a {fac.Serie}-{fac.Numero} ({ap.Monto:N2}) supera su saldo pendiente ({fac.SaldoPendiente:N2})."); }
            }

            if (errs.Count > 0) return new RegistrarPagoResult(false, null, null, null, 0, errs);

            // Reservar correlativo del recibo
            var (numero, _, errSec) = await ReservarNumeroAsync(input.IdEmpresa, "recibo", input.Serie, input.Usuario, ct);
            if (errSec is not null) return new RegistrarPagoResult(false, null, null, null, 0, new[] { errSec });

            var now = DateTime.UtcNow;
            var saldoFavor = input.Monto - totalAplicar;
            var estado = saldoFavor == 0m
                ? PagoEstado.Aplicado
                : (totalAplicar > 0m ? PagoEstado.ParcialmenteAplicado : PagoEstado.Recibido);

            var pago = new Pago
            {
                IdEmpresa     = input.IdEmpresa,
                Estado        = estado,
                Serie         = input.Serie,
                Numero        = numero,
                IdCliente     = input.IdCliente,
                Fecha         = input.Fecha == default ? now : input.Fecha,
                IdFormaPago   = input.IdFormaPago,
                Monto         = input.Monto,
                SaldoFavor    = saldoFavor,
                Moneda        = (input.Moneda ?? "HNL").ToUpperInvariant(),
                TipoCambio    = input.TipoCambio > 0 ? input.TipoCambio : 1m,
                Referencia    = input.Referencia,
                Observaciones = input.Observaciones,
                CreadoPor     = input.Usuario,
                FechaCreacion = now
            };
            _db.Pagos.Add(pago);

            await _db.SaveChangesAsync(ct);

            // Crear aplicaciones (si las hay) y recalcular saldo de cada factura
            var aplicacionesGuardadas = new List<(Factura Factura, decimal Monto)>();
            foreach (var ap in aplicaciones)
            {
                var fac = facturas[ap.IdFactura];
                var aplic = new PagoAplicacion
                {
                    IdPago          = pago.IdPago,
                    IdFactura       = fac.IdFactura,
                    Monto           = ap.Monto,
                    FechaAplicacion = now,
                    CreadoPor       = input.Usuario
                };
                _db.PagoAplicaciones.Add(aplic);

                fac.SaldoPendiente -= ap.Monto;
                fac.Estado = fac.SaldoPendiente <= 0m
                    ? FacturaEstado.Pagada
                    : FacturaEstado.ParcialmentePagada;
                fac.ModificadoPor = input.Usuario;
                fac.FechaModificacion = now;
                _db.Facturas.Update(fac);

                aplicacionesGuardadas.Add((fac, ap.Monto));
            }

            // ── Evento pago.recibido ──
            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: "pago.recibido",
                aggregateType: "pago",
                aggregateId: pago.IdPago.ToString(),
                payload: new
                {
                    pago_id        = pago.IdPago,
                    cliente_id     = pago.IdCliente,
                    serie          = pago.Serie,
                    numero         = pago.Numero,
                    fecha          = pago.Fecha,
                    forma_pago_id  = pago.IdFormaPago,
                    monto          = pago.Monto,
                    moneda         = pago.Moneda,
                    tipo_cambio    = pago.TipoCambio,
                    referencia     = pago.Referencia,
                    saldo_favor    = pago.SaldoFavor
                },
                occurredAt: pago.Fecha
            );

            // ── Evento pago.aplicado (sólo si hubo aplicaciones) ──
            if (aplicacionesGuardadas.Count > 0)
            {
                _events.Publish(
                    idEmpresa: input.IdEmpresa,
                    eventType: "pago.aplicado",
                    aggregateType: "pago",
                    aggregateId: pago.IdPago.ToString(),
                    payload: new
                    {
                        pago_id     = pago.IdPago,
                        cliente_id  = pago.IdCliente,
                        moneda      = pago.Moneda,
                        forma_pago_id = pago.IdFormaPago,
                        aplicaciones = aplicacionesGuardadas.Select(x => new
                        {
                            factura_id     = x.Factura.IdFactura,
                            serie          = x.Factura.Serie,
                            numero         = x.Factura.Numero,
                            monto_aplicado = x.Monto,
                            saldo_restante = x.Factura.SaldoPendiente,
                            estado_factura = x.Factura.Estado
                        }).ToList()
                    },
                    occurredAt: pago.Fecha
                );
            }

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Pago {Serie}-{Numero} (id {Id}) registrado por {Monto} {Moneda}, saldo favor {SF}",
                pago.Serie, pago.Numero, pago.IdPago, pago.Monto, pago.Moneda, pago.SaldoFavor);

            return new RegistrarPagoResult(true, pago.IdPago, pago.Serie, pago.Numero, pago.SaldoFavor, Array.Empty<string>());
        }

        // ──────────────────────────────────────────────────────────────────
        //  APLICAR SALDO A FAVOR
        // ──────────────────────────────────────────────────────────────────

        public async Task<AplicarPagoResult> AplicarSaldoAFavorAsync(AplicarPagoInput input, CancellationToken ct = default)
        {
            var errs = new List<string>();
            if (input.IdEmpresa <= 0) errs.Add("Empresa requerida.");
            if (input.IdPago <= 0) errs.Add("Pago requerido.");
            if (input.Aplicaciones == null || input.Aplicaciones.Count == 0) errs.Add("Debe indicar al menos una aplicación.");
            if (errs.Count > 0) return new AplicarPagoResult(false, 0, errs);

            var pago = await _db.Pagos
                .FirstOrDefaultAsync(p => p.IdPago == input.IdPago && p.IdEmpresa == input.IdEmpresa && !p.Eliminado, ct);

            if (pago is null) return new AplicarPagoResult(false, 0, new[] { "Pago no encontrado." });
            if (pago.Estado == PagoEstado.Anulado) return new AplicarPagoResult(false, 0, new[] { "El pago está anulado." });
            if (pago.SaldoFavor <= 0) return new AplicarPagoResult(false, 0, new[] { "El pago no tiene saldo a favor." });

            var aplicaciones = input.Aplicaciones?.Where(a => a.Monto > 0).ToList() ?? new List<AplicacionInput>();
            decimal totalAplicar = aplicaciones.Sum(a => a.Monto);

            if (totalAplicar > pago.SaldoFavor)
                return new AplicarPagoResult(false, pago.SaldoFavor,
                    new[] { $"Suma a aplicar ({totalAplicar:N2}) supera el saldo a favor ({pago.SaldoFavor:N2})." });

            var idsFacturas = aplicaciones.Select(a => a.IdFactura).Distinct().ToList();
            var facturas = await _db.Facturas
                .Where(f => f.IdEmpresa == input.IdEmpresa && idsFacturas.Contains(f.IdFactura) && !f.Eliminado)
                .ToDictionaryAsync(f => f.IdFactura, ct);

            foreach (var ap in aplicaciones)
            {
                if (!facturas.TryGetValue(ap.IdFactura, out var fac))
                { errs.Add($"Factura {ap.IdFactura} no existe."); continue; }
                if (fac.IdCliente != pago.IdCliente)
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} pertenece a otro cliente."); continue; }
                if (fac.Estado == FacturaEstado.Anulada || fac.Estado == FacturaEstado.Pagada)
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} está {fac.Estado}, no admite pago."); continue; }
                if (fac.Moneda != pago.Moneda)
                { errs.Add($"Factura {fac.Serie}-{fac.Numero} en moneda {fac.Moneda} ≠ pago en {pago.Moneda}."); continue; }
                if (ap.Monto > fac.SaldoPendiente)
                { errs.Add($"Aplicación a {fac.Serie}-{fac.Numero} ({ap.Monto:N2}) supera saldo ({fac.SaldoPendiente:N2})."); }
            }

            if (errs.Count > 0) return new AplicarPagoResult(false, pago.SaldoFavor, errs);

            var now = DateTime.UtcNow;
            var aplicacionesGuardadas = new List<(Factura Factura, decimal Monto)>();

            foreach (var ap in aplicaciones)
            {
                var fac = facturas[ap.IdFactura];
                _db.PagoAplicaciones.Add(new PagoAplicacion
                {
                    IdPago = pago.IdPago,
                    IdFactura = fac.IdFactura,
                    Monto = ap.Monto,
                    FechaAplicacion = now,
                    CreadoPor = input.Usuario
                });

                fac.SaldoPendiente -= ap.Monto;
                fac.Estado = fac.SaldoPendiente <= 0m
                    ? FacturaEstado.Pagada
                    : FacturaEstado.ParcialmentePagada;
                fac.ModificadoPor = input.Usuario;
                fac.FechaModificacion = now;
                _db.Facturas.Update(fac);

                aplicacionesGuardadas.Add((fac, ap.Monto));
            }

            pago.SaldoFavor -= totalAplicar;
            pago.Estado = pago.SaldoFavor <= 0m ? PagoEstado.Aplicado : PagoEstado.ParcialmenteAplicado;
            pago.ModificadoPor = input.Usuario;
            pago.FechaModificacion = now;

            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: "pago.aplicado",
                aggregateType: "pago",
                aggregateId: pago.IdPago.ToString(),
                payload: new
                {
                    pago_id     = pago.IdPago,
                    cliente_id  = pago.IdCliente,
                    moneda      = pago.Moneda,
                    forma_pago_id = pago.IdFormaPago,
                    aplicaciones = aplicacionesGuardadas.Select(x => new
                    {
                        factura_id     = x.Factura.IdFactura,
                        serie          = x.Factura.Serie,
                        numero         = x.Factura.Numero,
                        monto_aplicado = x.Monto,
                        saldo_restante = x.Factura.SaldoPendiente,
                        estado_factura = x.Factura.Estado
                    }).ToList()
                },
                occurredAt: now
            );

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Pago {Id} aplicado {Total} a {N} facturas; saldo favor restante {SF}",
                pago.IdPago, totalAplicar, aplicaciones.Count, pago.SaldoFavor);

            return new AplicarPagoResult(true, pago.SaldoFavor, Array.Empty<string>());
        }

        // ──────────────────────────────────────────────────────────────────
        //  ANULAR PAGO
        // ──────────────────────────────────────────────────────────────────

        public async Task<AnularPagoResult> AnularPagoAsync(AnularPagoInput input, CancellationToken ct = default)
        {
            if (input.IdEmpresa <= 0 || input.IdPago <= 0 || string.IsNullOrWhiteSpace(input.Motivo))
                return new AnularPagoResult(false, new[] { "Empresa, pago y motivo son requeridos." });

            var pago = await _db.Pagos
                .Include(p => p.Aplicaciones)
                .FirstOrDefaultAsync(p => p.IdPago == input.IdPago && p.IdEmpresa == input.IdEmpresa && !p.Eliminado, ct);

            if (pago is null) return new AnularPagoResult(false, new[] { "Pago no encontrado." });
            if (pago.Estado == PagoEstado.Anulado) return new AnularPagoResult(false, new[] { "El pago ya está anulado." });

            var now = DateTime.UtcNow;

            // Revertir aplicaciones (devolver saldo a las facturas)
            var idsFac = pago.Aplicaciones.Select(a => a.IdFactura).Distinct().ToList();
            var facturas = await _db.Facturas
                .Where(f => idsFac.Contains(f.IdFactura) && f.IdEmpresa == input.IdEmpresa)
                .ToDictionaryAsync(f => f.IdFactura, ct);

            foreach (var ap in pago.Aplicaciones)
            {
                if (!facturas.TryGetValue(ap.IdFactura, out var fac)) continue;
                fac.SaldoPendiente += ap.Monto;
                if (fac.SaldoPendiente >= fac.Total) { fac.SaldoPendiente = fac.Total; fac.Estado = FacturaEstado.Emitida; }
                else fac.Estado = FacturaEstado.ParcialmentePagada;
                fac.ModificadoPor = input.Usuario;
                fac.FechaModificacion = now;
            }

            // Borrar aplicaciones físicamente (rastro queda en domain_events)
            _db.PagoAplicaciones.RemoveRange(pago.Aplicaciones);

            pago.Estado            = PagoEstado.Anulado;
            pago.SaldoFavor        = 0m;
            pago.MotivoAnulacion   = input.Motivo.Trim();
            pago.FechaAnulacion    = now;
            pago.ModificadoPor     = input.Usuario;
            pago.FechaModificacion = now;

            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: "pago.anulado",
                aggregateType: "pago",
                aggregateId: pago.IdPago.ToString(),
                payload: new
                {
                    pago_id  = pago.IdPago,
                    serie    = pago.Serie,
                    numero   = pago.Numero,
                    motivo   = pago.MotivoAnulacion,
                    monto    = pago.Monto,
                    moneda   = pago.Moneda
                },
                occurredAt: now
            );

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Pago {Id} anulado", pago.IdPago);
            return new AnularPagoResult(true, Array.Empty<string>());
        }

        // ──────────────────────────────────────────────────────────────────
        //  Reserva atómica de correlativo (compartida con notas)
        // ──────────────────────────────────────────────────────────────────

        private async Task<(int Numero, string? Cai, string? Error)> ReservarNumeroAsync(
            int idEmpresa, string tipoDocumento, string serie, string usuario, CancellationToken ct)
        {
            var existe = await _db.FacturaSecuencias
                .AnyAsync(s => s.IdEmpresa == idEmpresa && s.TipoDocumento == tipoDocumento && s.Serie == serie, ct);

            if (!existe)
            {
                _db.FacturaSecuencias.Add(new FacturaSecuencia
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
                catch (DbUpdateException) { /* carrera tolerable */ }
            }

            var sql = @"
UPDATE factura_secuencias
SET proximo_numero = proximo_numero + 1
OUTPUT INSERTED.proximo_numero - 1 AS Numero,
       INSERTED.cai_numero         AS Cai
WHERE id_empresa = @IdEmpresa
  AND tipo_documento = @TipoDocumento
  AND serie = @Serie
  AND activo = 1;";

            var r = await _db.Database.SqlQueryRaw<ResNum>(
                sql,
                new SqlParameter("@IdEmpresa", idEmpresa),
                new SqlParameter("@TipoDocumento", tipoDocumento),
                new SqlParameter("@Serie", serie)).FirstOrDefaultAsync(ct);

            if (r is null) return (0, null, $"No hay secuencia activa para {tipoDocumento}/{serie}.");
            return (r.Numero, r.Cai, null);
        }

        private sealed class ResNum { public int Numero { get; set; } public string? Cai { get; set; } }
    }
}
