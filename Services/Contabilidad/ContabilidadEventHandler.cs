using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Contabilidad;
using eGestion360Web.Services.Eventos;

namespace eGestion360Web.Services.Contabilidad
{
    /// <summary>
    /// Handler contable: escucha los eventos de facturación en el outbox
    /// (<c>domain_events</c>) y genera los asientos correspondientes.
    ///
    /// Eventos que consume:
    ///   - factura.emitida.contado | factura.emitida.credito
    ///   - pago.recibido | pago.aplicado | pago.anulado
    ///   - nota_credito.emitida | nota_debito.emitida
    ///
    /// Idempotencia (obligatoria — el dispatcher puede reentregar): antes de crear un
    /// asiento se verifica que no exista ya uno con <c>id_evento_origen == evt.IdEvento</c>
    /// para esa empresa (respaldado por el índice único UX_ct_asientos_empresa_evento).
    ///
    /// El handler comparte el <see cref="ApplicationDbContext"/> con el
    /// <c>OutboxDispatcherBackgroundService</c> (ambos scoped en el mismo scope por lote):
    /// al llamar SaveChanges persiste el asiento; el dispatcher persiste luego el estado
    /// del evento. Si HandleAsync lanza, el dispatcher aplica backoff y reintenta.
    ///
    /// NOTA: el mapeo evento → cuentas (qué cuenta de ventas, ISV, CxC, caja/banco usar)
    /// depende del plan de cuentas por empresa y de reglas tributarias de Honduras que
    /// deben validarse contra fuente oficial. Ese mapeo está pendiente
    /// (<see cref="ConstruirAsientoAsync"/>): hasta implementarlo, el handler no crea
    /// asientos. Por eso su registro en Program.cs está comentado (habilitarlo cuando
    /// existan el plan de cuentas y las reglas de mapeo).
    /// </summary>
    public sealed class ContabilidadEventHandler : IDomainEventHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ContabilidadEventHandler> _log;

        public ContabilidadEventHandler(ApplicationDbContext db, ILogger<ContabilidadEventHandler> log)
        {
            _db = db;
            _log = log;
        }

        public string Name => "contabilidad";

        public bool CanHandle(string eventType) =>
            eventType.StartsWith("factura.emitida", StringComparison.Ordinal)
            || eventType.StartsWith("pago.", StringComparison.Ordinal)
            || eventType.StartsWith("nota_credito", StringComparison.Ordinal)
            || eventType.StartsWith("nota_debito", StringComparison.Ordinal);

        public async Task HandleAsync(DomainEventDispatch evt, CancellationToken ct)
        {
            // 1) Idempotencia — ¿ya generamos el asiento de este evento?
            bool yaProcesado = await _db.Asientos
                .AsNoTracking()
                .AnyAsync(a => a.IdEmpresa == evt.IdEmpresa && a.IdEventoOrigen == evt.IdEvento, ct);
            if (yaProcesado)
            {
                _log.LogDebug("[Contabilidad] Evento {IdEvento} ya tenía asiento; se omite.", evt.IdEvento);
                return;
            }

            // 2) ¿La empresa tiene el módulo contable configurado (plan de cuentas)?
            //    Es opcional por empresa: sin plan de cuentas no hay nada que contabilizar.
            bool tienePlanCuentas = await _db.CuentasContables
                .AsNoTracking()
                .AnyAsync(c => c.IdEmpresa == evt.IdEmpresa, ct);
            if (!tienePlanCuentas)
            {
                _log.LogDebug("[Contabilidad] Empresa {IdEmpresa} sin plan de cuentas; evento {IdEvento} omitido.",
                    evt.IdEmpresa, evt.IdEvento);
                return;
            }

            // 3) Construir el asiento a partir del evento (mapeo contable).
            Asiento? asiento = await ConstruirAsientoAsync(evt, ct);
            if (asiento is null)
            {
                // Mapeo aún no implementado para este tipo de evento: no se crea asiento.
                _log.LogWarning(
                    "[Contabilidad] Mapeo contable pendiente para {EventType} (evento {IdEvento}); no se generó asiento.",
                    evt.EventType, evt.IdEvento);
                return;
            }

            // 4) Persistir (mismo scope/transacción del dispatcher). El índice único sobre
            //    id_evento_origen protege ante una eventual doble entrega concurrente.
            _db.Asientos.Add(asiento);
            await _db.SaveChangesAsync(ct);

            _log.LogInformation(
                "[Contabilidad] Asiento generado para {EventType} (evento {IdEvento}, empresa {IdEmpresa}).",
                evt.EventType, evt.IdEvento, evt.IdEmpresa);
        }

        /// <summary>
        /// Traduce un evento de dominio en un asiento contable balanceado.
        ///
        /// TODO (pendiente de implementar con el plan de cuentas + fundamento legal):
        ///   1. Deserializar <c>evt.PayloadJson</c> al DTO del evento (totales, ISV, cliente…).
        ///   2. Resolver el período contable abierto para la fecha del documento
        ///      (<see cref="PeriodoContable"/> con estado 'abierto').
        ///   3. Resolver las cuentas involucradas según el tipo de evento y una tabla de
        ///      parametrización cuenta↔concepto por empresa (ventas, ISV por pagar, CxC,
        ///      caja/banco, devoluciones, recargos…).
        ///   4. Construir cabecera (origen = automatico, id_evento_origen = evt.IdEvento)
        ///      y movimientos (débitos/créditos) de forma que Σdébito == Σcrédito.
        ///   5. Devolver el asiento (estado inicial 'borrador' o 'mayorizado' según política).
        ///
        /// Ejemplo de lectura del payload (el publisher serializa en snake_case):
        ///   using var doc = JsonDocument.Parse(evt.PayloadJson);
        ///   var root = doc.RootElement;
        ///   // var total = root.GetProperty("total").GetDecimal();
        /// </summary>
        private Task<Asiento?> ConstruirAsientoAsync(DomainEventDispatch evt, CancellationToken ct)
        {
            _ = evt;
            _ = ct;
            // Sin mapeo definido todavía: devolver null para no generar asientos incompletos.
            return Task.FromResult<Asiento?>(null);
        }
    }
}
