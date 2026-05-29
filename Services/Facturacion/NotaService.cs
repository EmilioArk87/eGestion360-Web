using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Services.Eventos;

namespace eGestion360Web.Services.Facturacion
{
    public sealed class NotaService : INotaService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDomainEventPublisher _events;
        private readonly ILogger<NotaService> _log;

        public NotaService(ApplicationDbContext db, IDomainEventPublisher events, ILogger<NotaService> log)
        {
            _db = db;
            _events = events;
            _log = log;
        }

        public async Task<EmitirNotaResult> EmitirAsync(EmitirNotaInput input, CancellationToken ct = default)
        {
            var errs = new List<string>();
            if (input.IdEmpresa <= 0) errs.Add("Empresa requerida.");
            if (input.IdFacturaOrigen <= 0) errs.Add("Factura origen requerida.");
            if (input.Monto <= 0) errs.Add("Monto debe ser > 0.");
            if (string.IsNullOrWhiteSpace(input.Motivo)) errs.Add("Motivo requerido.");
            var tipo = (input.Tipo ?? "").Trim().ToLowerInvariant();
            if (tipo != NotaTipo.Credito && tipo != NotaTipo.Debito) errs.Add("Tipo debe ser credito o debito.");
            if (string.IsNullOrWhiteSpace(input.Serie)) input.Serie = tipo == NotaTipo.Credito ? "NC-01" : "ND-01";
            if (errs.Count > 0) return new EmitirNotaResult(false, null, null, null, errs);

            var factura = await _db.Facturas
                .FirstOrDefaultAsync(f => f.IdFactura == input.IdFacturaOrigen && f.IdEmpresa == input.IdEmpresa && !f.Eliminado, ct);

            if (factura is null) return new EmitirNotaResult(false, null, null, null, new[] { "Factura origen no existe." });
            if (factura.Estado == FacturaEstado.Anulada) return new EmitirNotaResult(false, null, null, null, new[] { "Factura origen está anulada." });

            if (tipo == NotaTipo.Credito && input.Monto > factura.SaldoPendiente)
                return new EmitirNotaResult(false, null, null, null,
                    new[] { $"Monto de NC ({input.Monto:N2}) supera saldo de factura ({factura.SaldoPendiente:N2})." });

            // Reservar correlativo
            var tipoDoc = tipo == NotaTipo.Credito ? "nota_credito" : "nota_debito";
            var (numero, _, errSec) = await ReservarNumeroAsync(input.IdEmpresa, tipoDoc, input.Serie, input.Usuario, ct);
            if (errSec is not null) return new EmitirNotaResult(false, null, null, null, new[] { errSec });

            var now = DateTime.UtcNow;
            var nota = new Nota
            {
                IdEmpresa       = input.IdEmpresa,
                Tipo            = tipo,
                Estado          = "emitida",
                Serie           = input.Serie,
                Numero          = numero,
                IdCliente       = factura.IdCliente,
                IdFacturaOrigen = factura.IdFactura,
                FechaEmision    = input.FechaEmision == default ? now : input.FechaEmision,
                Monto           = input.Monto,
                Moneda          = factura.Moneda,
                Motivo          = input.Motivo.Trim(),
                Observaciones   = input.Observaciones,
                CreadoPor       = input.Usuario,
                FechaCreacion   = now
            };
            _db.Notas.Add(nota);

            // Persistir primero para obtener IdNota
            await _db.SaveChangesAsync(ct);

            // Afectar saldo de la factura origen
            if (tipo == NotaTipo.Credito)
            {
                factura.SaldoPendiente -= input.Monto;
                if (factura.SaldoPendiente <= 0m) { factura.SaldoPendiente = 0m; factura.Estado = FacturaEstado.Pagada; }
                else factura.Estado = FacturaEstado.ParcialmentePagada;
            }
            else // débito
            {
                factura.SaldoPendiente += input.Monto;
                factura.Total          += input.Monto;
                factura.Estado          = FacturaEstado.ParcialmentePagada;
            }
            factura.ModificadoPor     = input.Usuario;
            factura.FechaModificacion = now;

            _events.Publish(
                idEmpresa: input.IdEmpresa,
                eventType: tipo == NotaTipo.Credito ? "nota_credito.emitida" : "nota_debito.emitida",
                aggregateType: "nota",
                aggregateId: nota.IdNota.ToString(),
                payload: new
                {
                    nota_id         = nota.IdNota,
                    tipo            = tipo,
                    factura_id      = factura.IdFactura,
                    factura_numero  = factura.Serie + "-" + factura.Numero,
                    cliente_id      = factura.IdCliente,
                    monto           = nota.Monto,
                    moneda          = nota.Moneda,
                    motivo          = nota.Motivo,
                    fecha_emision   = nota.FechaEmision,
                    saldo_factura_resultante = factura.SaldoPendiente,
                    estado_factura_resultante = factura.Estado
                },
                occurredAt: nota.FechaEmision
            );

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Nota {Tipo} {Serie}-{Numero} emitida sobre factura {Fac}, monto {M}",
                tipo, nota.Serie, nota.Numero, factura.IdFactura, nota.Monto);

            return new EmitirNotaResult(true, nota.IdNota, nota.Serie, nota.Numero, Array.Empty<string>());
        }

        private async Task<(int Numero, string? Cai, string? Error)> ReservarNumeroAsync(
            int idEmpresa, string tipoDocumento, string serie, string usuario, CancellationToken ct)
        {
            var existe = await _db.FacturaSecuencias
                .AnyAsync(s => s.IdEmpresa == idEmpresa && s.TipoDocumento == tipoDocumento && s.Serie == serie, ct);

            if (!existe)
            {
                _db.FacturaSecuencias.Add(new FacturaSecuencia
                {
                    IdEmpresa = idEmpresa, TipoDocumento = tipoDocumento, Serie = serie,
                    ProximoNumero = 1, Activo = true, CreadoPor = usuario, FechaCreacion = DateTime.UtcNow
                });
                try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { }
            }

            var sql = @"
UPDATE factura_secuencias
SET proximo_numero = proximo_numero + 1
OUTPUT INSERTED.proximo_numero - 1 AS Numero,
       INSERTED.cai_numero         AS Cai
WHERE id_empresa = @IdEmpresa AND tipo_documento = @TipoDocumento AND serie = @Serie AND activo = 1;";

            var r = await _db.Database.SqlQueryRaw<ResNum>(
                sql,
                new SqlParameter("@IdEmpresa", idEmpresa),
                new SqlParameter("@TipoDocumento", tipoDocumento),
                new SqlParameter("@Serie", serie)).FirstOrDefaultAsync(ct);
            if (r is null) return (0, null, $"Sin secuencia activa para {tipoDocumento}/{serie}.");
            return (r.Numero, r.Cai, null);
        }

        private sealed class ResNum { public int Numero { get; set; } public string? Cai { get; set; } }
    }
}
