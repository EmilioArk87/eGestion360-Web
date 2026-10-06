using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Services.TasasCambio
{
    public sealed class TasaCambioConsultaService : ITasaCambioConsultaService
    {
        private readonly ApplicationDbContext _db;
        private readonly TasasCambioOptions _opt;
        private readonly CalendarioTasasCambio _calendario;

        public TasaCambioConsultaService(ApplicationDbContext db, IOptions<TasasCambioOptions> opciones, TimeProvider reloj)
        {
            _db = db;
            _opt = opciones.Value;
            _calendario = new CalendarioTasasCambio(reloj, _opt);
        }

        // ──────────────────────────────────────────────────────────────────
        //  VIGENTES
        // ──────────────────────────────────────────────────────────────────

        public async Task<TasaVigente?> ObtenerVigenteAsync(string monedaOrigen, string tipoTasa, DateOnly fecha, int? idEmpresa = null,
            string monedaDestino = "HNL", CancellationToken ct = default)
        {
            var origen = Codigo(monedaOrigen);
            var destino = Codigo(monedaDestino);
            var tipo = Codigo(tipoTasa);

            var consulta = _db.TasasCambio.AsNoTracking()
                .Where(t => t.MonedaOrigen == origen && t.MonedaDestino == destino && t.TipoTasa == tipo
                            && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado && t.FechaVigencia <= fecha);
            consulta = idEmpresa is > 0
                ? consulta.Where(t => t.IdEmpresa == null || t.IdEmpresa == idEmpresa)
                : consulta.Where(t => t.IdEmpresa == null);

            return await consulta
                .OrderByDescending(t => t.FechaVigencia)
                .ThenByDescending(t => t.IdEmpresa != null)   // a igual fecha, la de la empresa gana
                .ThenByDescending(t => t.IdTasaCambio)
                .Select(t => new TasaVigente(t.IdTasaCambio, t.IdEmpresa, t.MonedaOrigen, t.MonedaDestino, t.TipoTasa, t.Tasa,
                    t.FechaVigencia, t.Fuente, t.ReferenciaFuente, t.EsDerivada, t.Version, t.FechaHoraObtencion))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<TasaVigenteActual>> VigentesActualesAsync(int? idEmpresa = null, CancellationToken ct = default)
        {
            var hoy = _calendario.Hoy;
            var local = _opt.MonedaLocalEfectiva;
            var resultado = new List<TasaVigenteActual>();
            foreach (var moneda in _opt.MonedasEfectivas)
                foreach (var tipo in new[] { Cat.TipoTasa.Compra, Cat.TipoTasa.Venta })
                    resultado.Add(new TasaVigenteActual(moneda, tipo, await ObtenerVigenteAsync(moneda, tipo, hoy, idEmpresa, local, ct)));
            return resultado;
        }

        // ──────────────────────────────────────────────────────────────────
        //  HISTÓRICO
        // ──────────────────────────────────────────────────────────────────

        public async Task<PaginaTasas> HistoricoAsync(FiltroHistoricoTasas filtro, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filtro);
            var pagina = Math.Max(1, filtro.Pagina);
            var tamano = Math.Clamp(filtro.TamanoPagina, 1, 500);

            var consulta = _db.TasasCambio.AsNoTracking().Where(t => !t.Eliminado);

            var empresa = filtro.IdEmpresa is > 0 ? filtro.IdEmpresa : null;
            if (empresa == null)
                consulta = consulta.Where(t => t.IdEmpresa == null);
            else if (filtro.IncluirOficiales)
                consulta = consulta.Where(t => t.IdEmpresa == null || t.IdEmpresa == empresa);
            else
                consulta = consulta.Where(t => t.IdEmpresa == empresa);

            if (!string.IsNullOrWhiteSpace(filtro.MonedaOrigen))
            {
                var moneda = Codigo(filtro.MonedaOrigen);
                consulta = consulta.Where(t => t.MonedaOrigen == moneda);
            }
            if (!string.IsNullOrWhiteSpace(filtro.TipoTasa))
            {
                var tipo = Codigo(filtro.TipoTasa);
                consulta = consulta.Where(t => t.TipoTasa == tipo);
            }
            if (!string.IsNullOrWhiteSpace(filtro.Estado))
            {
                var estado = Codigo(filtro.Estado);
                consulta = consulta.Where(t => t.Estado == estado);
            }
            if (!string.IsNullOrWhiteSpace(filtro.Fuente))
            {
                var fuente = Codigo(filtro.Fuente);
                consulta = consulta.Where(t => t.Fuente == fuente);
            }
            if (filtro.Desde is { } desde) consulta = consulta.Where(t => t.FechaVigencia >= desde);
            if (filtro.Hasta is { } hasta) consulta = consulta.Where(t => t.FechaVigencia <= hasta);

            var total = await consulta.CountAsync(ct);
            var filas = await consulta
                .OrderByDescending(t => t.FechaVigencia)
                .ThenBy(t => t.MonedaOrigen)
                .ThenBy(t => t.TipoTasa)
                .ThenByDescending(t => t.IdEmpresa != null)
                .ThenByDescending(t => t.Version)
                .ThenByDescending(t => t.IdTasaCambio)
                .Skip((pagina - 1) * tamano)
                .Take(tamano)
                .Select(t => new TasaHistorialFila(t.IdTasaCambio, t.IdEmpresa, t.MonedaOrigen, t.MonedaDestino, t.TipoTasa, t.Tasa,
                    t.FechaVigencia, t.Fuente, t.ReferenciaFuente, t.EsDerivada, t.Estado, t.Version, t.IdTasaAnterior, t.IdEjecucion,
                    t.FechaHoraObtencion, t.CreadoPor, t.FechaCreacion, t.ModificadoPor, t.FechaModificacion))
                .ToListAsync(ct);

            return new PaginaTasas(filas, total, pagina, tamano);
        }

        // ──────────────────────────────────────────────────────────────────
        //  BITÁCORA Y REVISIÓN
        // ──────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<EjecucionTasasResumen>> UltimasEjecucionesAsync(int maximo = 20, CancellationToken ct = default)
        {
            var ejecuciones = await _db.TasasCambioEjecuciones.AsNoTracking()
                .Where(e => e.Job == Cat.Job)
                .OrderByDescending(e => e.IdEjecucion)
                .Take(Math.Clamp(maximo, 1, 200))
                .Include(e => e.Detalles)
                .ToListAsync(ct);

            return ejecuciones.Select(e => new EjecucionTasasResumen(
                e.IdEjecucion, e.Disparador, e.FechaObjetivo, e.Intento, e.IdEjecucionOrigen, e.Estado, e.Fuente, e.Endpoint,
                e.HttpStatus, e.Leidos, e.Insertados, e.Reemplazados, e.Duplicados, e.Invalidos, e.Mensaje, e.DetalleError,
                e.ProximoIntentoUtc, e.Notificado, e.Servidor, e.EjecutadoPor, e.InicioUtc, e.FinUtc,
                e.Detalles
                    .OrderBy(d => d.FechaVigencia).ThenBy(d => d.MonedaOrigen).ThenBy(d => d.TipoTasa).ThenBy(d => d.IdDetalle)
                    .Select(d => new EjecucionTasasDetalleFila(d.IdDetalle, d.MonedaOrigen, d.MonedaDestino, d.TipoTasa,
                        d.FechaVigencia, d.ValorLeido, d.Resultado, d.Motivo, d.IdTasaCambio))
                    .ToList()))
                .ToList();
        }

        public async Task<IReadOnlyList<TasaPendienteRevision>> PendientesRevisionAsync(int? idEmpresa = null, CancellationToken ct = default)
        {
            var empresa = idEmpresa is > 0 ? idEmpresa : null;
            var pendientes = await _db.TasasCambio.AsNoTracking()
                .Where(t => t.Estado == Cat.EstadoTasa.EnRevision && !t.Eliminado
                            && (t.IdEmpresa == null || (empresa != null && t.IdEmpresa == empresa)))
                .OrderByDescending(t => t.FechaVigencia).ThenBy(t => t.MonedaOrigen).ThenBy(t => t.TipoTasa).ThenBy(t => t.IdTasaCambio)
                .ToListAsync(ct);
            if (pendientes.Count == 0) return Array.Empty<TasaPendienteRevision>();

            var ids = pendientes.Select(p => (int?)p.IdTasaCambio).ToList();
            var motivos = (await _db.TasasCambioEjecucionesDetalle.AsNoTracking()
                    .Where(d => ids.Contains(d.IdTasaCambio) && d.Resultado == Cat.ResultadoDetalle.EnRevision)
                    .Select(d => new { d.IdTasaCambio, d.IdDetalle, d.Motivo })
                    .ToListAsync(ct))
                .GroupBy(d => d.IdTasaCambio!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.IdDetalle).First().Motivo);

            var resultado = new List<TasaPendienteRevision>();
            foreach (var p in pendientes)
            {
                var vigente = await _db.TasasCambio.AsNoTracking()
                    .Where(t => t.IdEmpresa == p.IdEmpresa && t.MonedaOrigen == p.MonedaOrigen && t.MonedaDestino == p.MonedaDestino
                                && t.TipoTasa == p.TipoTasa && t.FechaVigencia == p.FechaVigencia
                                && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado)
                    .Select(t => (decimal?)t.Tasa)
                    .FirstOrDefaultAsync(ct);

                resultado.Add(new TasaPendienteRevision(p.IdTasaCambio, p.IdEmpresa, p.MonedaOrigen, p.MonedaDestino, p.TipoTasa,
                    p.Tasa, p.FechaVigencia, p.Fuente, p.ReferenciaFuente, p.IdEjecucion, p.FechaCreacion,
                    motivos.GetValueOrDefault(p.IdTasaCambio), vigente));
            }
            return resultado;
        }

        private static string Codigo(string? texto) => (texto ?? string.Empty).Trim().ToUpperInvariant();
    }
}
