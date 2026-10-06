using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>Consultas que comparten el job y su planificador.</summary>
    public static class TasasCambioDatos
    {
        /// <summary>Tipos que obtiene el job para cada moneda.</summary>
        public static readonly string[] TiposJob = { Cat.TipoTasa.Compra, Cat.TipoTasa.Venta };

        /// <summary>Monedas y tipos del job sin tasa oficial VIGENTE en <paramref name="fecha"/>.</summary>
        public static async Task<List<(string Moneda, string Tipo)>> FaltantesAsync(ApplicationDbContext db, TasasCambioOptions opt,
            DateOnly fecha, CancellationToken ct = default)
        {
            var local = opt.MonedaLocalEfectiva;
            var hay = await db.TasasCambio.AsNoTracking()
                .Where(t => t.IdEmpresa == null && t.MonedaDestino == local && t.FechaVigencia == fecha
                            && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado)
                .Select(t => new { t.MonedaOrigen, t.TipoTasa })
                .ToListAsync(ct);

            return opt.MonedasEfectivas
                .SelectMany(m => TiposJob.Select(t => (Moneda: m, Tipo: t)))
                .Where(c => !hay.Any(h => h.MonedaOrigen == c.Moneda && h.TipoTasa == c.Tipo))
                .ToList();
        }
    }

    /// <summary>
    /// Algo que atender fuera del ciclo de 10 minutos del worker.
    /// <para>
    /// <see cref="Forzar"/> = verdadero (pantalla, "Ejecutar ahora" o carga de un rango): se ejecuta siempre.
    /// Falso (disparador externo): solo despierta al worker para que pregunte YA al planificador; si no toca nada, no
    /// descarga ni escribe en la bitácora, y si toca, la ejecución queda con este <see cref="Disparador"/>.
    /// </para>
    /// </summary>
    /// <param name="Desde">Con <paramref name="Hasta"/>, carga manual de un rango; nulos = puesta al día normal.</param>
    public sealed record SolicitudEjecucionTasas(
        string Disparador,
        string EjecutadoPor,
        DateOnly? Desde = null,
        DateOnly? Hasta = null,
        bool Forzar = true)
    {
        /// <summary>Solo evaluar la programación ahora (lo que pide el disparador externo).</summary>
        public static SolicitudEjecucionTasas Evaluar(string disparador) =>
            new(disparador, TasaCambioSyncService.UsuarioJob, Forzar: false);
    }

    /// <summary>
    /// Cola en memoria de solicitudes (singleton). La atiende <see cref="TasasCambioBackgroundService"/>, así la petición
    /// web responde enseguida y la ejecución corre con el ciclo de vida del worker. Si el job está deshabilitado, nadie la
    /// atiende.
    /// </summary>
    public sealed class ColaEjecucionTasasCambio
    {
        public const int Capacidad = 5;

        private readonly Channel<SolicitudEjecucionTasas> _canal = Channel.CreateBounded<SolicitudEjecucionTasas>(
            new BoundedChannelOptions(Capacidad) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

        /// <summary>Encola una solicitud. Falso si la cola está llena (ya hay varias esperando).</summary>
        public bool Solicitar(SolicitudEjecucionTasas solicitud)
        {
            ArgumentNullException.ThrowIfNull(solicitud);
            return _canal.Writer.TryWrite(solicitud);
        }

        /// <summary>
        /// Encola una ejecución FORZADA de la puesta al día (el botón "Ejecutar ahora" de la pantalla):
        /// <c>Solicitar(TasasCambioCatalogo.Disparador.Manual, usuario)</c>.
        /// </summary>
        public bool Solicitar(string disparador, string ejecutadoPor) => Solicitar(new SolicitudEjecucionTasas(disparador, ejecutadoPor));

        public int Pendientes => _canal.Reader.Count;

        public ChannelReader<SolicitudEjecucionTasas> Lector => _canal.Reader;
    }

    /// <param name="EsBarridoMatutino">Verdadero si es el barrido de las 07:00 (o la puesta al día que hace sus veces).</param>
    public sealed record DecisionProgramada(string Disparador, bool EsBarridoMatutino, string Motivo);

    /// <summary>
    /// Decide si en este momento toca una ejecución. Lo consultan la vuelta de 10 minutos del worker, el arranque del
    /// proceso y el disparador externo; ninguno de ellos fuerza nada. Todo sale de la base (tasas_cambio y la bitácora),
    /// nada de la memoria del proceso, así que un reciclado de IIS no repite ni pierde trabajo.
    ///
    /// Sobre la fecha objetivo (<see cref="CalendarioTasasCambio.FechaObjetivo"/>: en la tarde de un día hábil y en
    /// fin de semana, el día hábil siguiente; antes del primer intento de un día hábil, hoy) y su última ejecución:
    ///   1. Cadena de reintentos abierta (REINTENTADA): manda ella. Toca REINTENTO solo si ya venció su
    ///      ProximoIntentoUtc; si no, no toca nada.
    ///   2. Si a la fecha objetivo no le falta nada, no toca nada.
    ///   3. Nunca hubo una ejecución para la fecha objetivo:
    ///        * en la ventana de la tarde (día hábil, ya pasó PrimerIntento): primer intento por la tasa del día
    ///          hábil siguiente, mientras no pase UltimoIntento;
    ///        * en otro momento: puesta al día (hueco: el proceso estuvo caído o nadie lo despertó en la tarde).
    ///   4. La cadena de la fecha objetivo terminó (FALLIDA, OMITIDA_SIN_DATOS…) y sigue faltando algo: no se abre otra
    ///      hasta el barrido matutino, que la intenta una vez más después de BarridoMatutino (07:00) si hoy no hubo
    ///      ninguna ejecución desde esa hora (dato de la bitácora) y, si sigue faltando, avisa.
    ///
    /// El botón "Ejecutar ahora" (MANUAL) no pasa por aquí: siempre ejecuta.
    /// </summary>
    public sealed class TasasCambioPlanificador
    {
        private readonly ApplicationDbContext _db;
        private readonly TasasCambioOptions _opt;
        private readonly CalendarioTasasCambio _calendario;

        public TasasCambioPlanificador(ApplicationDbContext db, IOptions<TasasCambioOptions> opciones, TimeProvider reloj)
        {
            _db = db;
            _opt = opciones.Value;
            _calendario = new CalendarioTasasCambio(reloj, _opt);
        }

        public async Task<DecisionProgramada?> DecidirAsync(CancellationToken ct = default)
        {
            if (!_opt.Habilitado) return null;

            var ahoraUtc = _calendario.AhoraUtc;
            var hoy = _calendario.Hoy;
            var hora = TimeOnly.FromDateTime(_calendario.AhoraHonduras);
            var objetivo = _calendario.FechaObjetivo();

            var ultima = await _db.TasasCambioEjecuciones.AsNoTracking()
                .Where(e => e.Job == Cat.Job && e.FechaObjetivo == objetivo && e.Estado != Cat.EstadoEjecucion.EnCurso)
                .OrderByDescending(e => e.IdEjecucion)
                .Select(e => new { e.IdEjecucion, e.Estado, e.ProximoIntentoUtc })
                .FirstOrDefaultAsync(ct);

            // 1. Cadena de reintentos abierta: se respeta su espera
            if (ultima?.Estado == Cat.EstadoEjecucion.Reintentada)
                return ultima.ProximoIntentoUtc is { } proximo && proximo <= ahoraUtc
                    ? new DecisionProgramada(Cat.Disparador.Reintento, false,
                        $"Reintento de la ejecución #{ultima.IdEjecucion} para {objetivo:yyyy-MM-dd}.")
                    : null;

            // 2. Nada que buscar
            if ((await TasasCambioDatos.FaltantesAsync(_db, _opt, objetivo, ct)).Count == 0) return null;

            var pasoBarrido = hora >= _opt.HoraBarridoMatutino;

            // 3. Nunca se intentó la fecha objetivo
            if (ultima == null)
            {
                if (_calendario.EnVentanaDeLaTarde)
                    return hora <= _opt.HoraUltimoIntento
                        ? new DecisionProgramada(Cat.Disparador.Programado, false, $"Primer intento de {objetivo:yyyy-MM-dd}.")
                        : null;

                return new DecisionProgramada(Cat.Disparador.Programado, pasoBarrido && !await BarridoHechoHoyAsync(ct),
                    $"Puesta al día: no hubo ninguna ejecución para {objetivo:yyyy-MM-dd}.");
            }

            // 4. La cadena terminó sin completar la fecha: solo el barrido matutino la intenta otra vez
            if (pasoBarrido && !await BarridoHechoHoyAsync(ct))
                return new DecisionProgramada(Cat.Disparador.Programado, true,
                    $"Barrido matutino de {hoy:yyyy-MM-dd}: falta la tasa del {objetivo:yyyy-MM-dd}.");

            return null;
        }

        /// <summary>
        /// ¿Hubo hoy alguna ejecución desde la hora del barrido matutino? Se deduce de la bitácora (no de la memoria del
        /// proceso): cualquier ejecución después de las 07:00 ya hizo la puesta al día que haría el barrido.
        /// </summary>
        private Task<bool> BarridoHechoHoyAsync(CancellationToken ct)
        {
            var desde = _calendario.AUtc(_calendario.Hoy, _opt.HoraBarridoMatutino);
            return _db.TasasCambioEjecuciones.AsNoTracking().AnyAsync(e => e.Job == Cat.Job && e.InicioUtc >= desde, ct);
        }
    }
}
