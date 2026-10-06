using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Orquestador del job de tasas de cambio (reemplaza a la aplicación WinForms que descargaba el Excel del BCH al
    /// iniciar sesión en Windows).
    ///
    /// Una ejecución:
    ///   1. Toma el bloqueo (una sola a la vez, <see cref="IBloqueoJob"/>) y marca FALLIDA cualquier EN_CURSO de más de
    ///      15 minutos (un proceso que murió a medias).
    ///   2. Abre su fila en la bitácora (EN_CURSO), en un contexto propio, separado del de las tasas.
    ///   3. Lee el dólar: API del BCH y, si falla, está deshabilitado o no trae nada, el Excel del BCH.
    ///      El euro: indicador oficial del BCH si está configurado; si no, EUR/USD del BCE × USD/HNL (compra y venta).
    ///   4. Valida cada lectura (<see cref="TasaCambioValidador"/>) y la guarda versionada: mismo valor a 4 decimales =
    ///      DUPLICADA (no escribe); valor distinto = la vigente pasa a REEMPLAZADA y la nueva entra VIGENTE con
    ///      Version + 1 (en una transacción); dato raro = EN_REVISION (no vigente) y alerta; dato imposible = no se guarda.
    ///   5. Cierra la fila de la bitácora con su estado y el detalle por moneda/tipo, y alerta si corresponde.
    ///
    /// Estado de la ejecución según la fecha objetivo (<see cref="CalendarioTasasCambio.FechaObjetivo"/>):
    ///   EXITOSA (todas las monedas y tipos vigentes) · PARCIAL (falta algo pero hay algo, o hubo tasas a revisión) ·
    ///   REINTENTADA (error transitorio, o sin publicación todavía hoy; con ProximoIntentoUtc) · FALLIDA (sin datos
    ///   por errores: agotó los reintentos o error permanente en todas las fuentes del dólar) · OMITIDA_DUPLICADA (la
    ///   respuesta es idéntica a la de la última exitosa y no falta nada) · OMITIDA_INVALIDA (lo leído no pasó la
    ///   validación) · OMITIDA_SIN_DATOS (no hubo publicación: fin de semana o feriado; no es fallo).
    ///
    /// Alertas, una sola vez por fecha y estado (columna notificado): FALLIDA, tasas EN_REVISION y dos días hábiles
    /// seguidos sin tasa.
    /// </summary>
    public sealed class TasaCambioSyncService : ITasaCambioSyncService
    {
        public const string RecursoBloqueo = "eGestion360:" + Cat.Job;
        public const string UsuarioJob = "job";

        private static readonly TimeSpan EnCursoMaximo = TimeSpan.FromMinutes(15);

        /// <summary>Tope de la puesta al día automática; más atrás, con la carga manual de un rango.</summary>
        private const int MaxDiasPuestaAlDia = 90;

        private static readonly string[] TiposJob = TasasCambioDatos.TiposJob;

        private readonly ITasasCambioContextos _contextos;
        private readonly IReadOnlyList<ITasaCambioProveedor> _proveedores;
        private readonly TasaCambioValidador _validador;
        private readonly IBloqueoJob _bloqueo;
        private readonly ITasasCambioNotificador _notificador;
        private readonly TasasCambioOptions _opt;
        private readonly CalendarioTasasCambio _calendario;
        private readonly ILogger<TasaCambioSyncService> _log;

        public TasaCambioSyncService(
            ITasasCambioContextos contextos,
            IEnumerable<ITasaCambioProveedor> proveedores,
            TasaCambioValidador validador,
            IBloqueoJob bloqueo,
            ITasasCambioNotificador notificador,
            IOptions<TasasCambioOptions> opciones,
            TimeProvider reloj,
            ILogger<TasaCambioSyncService> log)
        {
            _contextos = contextos;
            _proveedores = proveedores.ToList();
            _validador = validador;
            _bloqueo = bloqueo;
            _notificador = notificador;
            _opt = opciones.Value;
            _calendario = new CalendarioTasasCambio(reloj, _opt);
            _log = log;
        }

        // ──────────────────────────────────────────────────────────────────
        //  EJECUCIÓN DEL JOB
        // ──────────────────────────────────────────────────────────────────

        public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, CancellationToken ct = default) =>
            EjecutarInternoAsync(disparador, ejecutadoPor, null, ct);

        public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, DateOnly desde, DateOnly hasta,
            CancellationToken ct = default)
        {
            if (hasta < desde) throw new ArgumentException("La fecha final es anterior a la inicial.", nameof(hasta));
            if (hasta.DayNumber - desde.DayNumber > 366)
                throw new ArgumentException("El rango manual no puede pasar de un año.", nameof(hasta));
            return EjecutarInternoAsync(disparador, ejecutadoPor, new SolicitudLectura(desde, hasta), ct);
        }

        private async Task<ResultadoEjecucionTasas> EjecutarInternoAsync(string disparador, string ejecutadoPor,
            SolicitudLectura? rango, CancellationToken ct)
        {
            if (!_opt.Habilitado)
            {
                _log.LogInformation("Tasas de cambio: el job está deshabilitado (TasasCambio:Habilitado = false); no se ejecuta.");
                return ResultadoEjecucionTasas.NoEjecutada("El job de tasas de cambio está deshabilitado.");
            }
            if (!Cat.Disparador.Todos.Contains(disparador))
                throw new ArgumentException($"Disparador desconocido: {disparador}.", nameof(disparador));

            await using var bloqueo = await _bloqueo.IntentarTomarAsync(RecursoBloqueo, ct);
            if (bloqueo == null)
            {
                _log.LogInformation("Tasas de cambio: ya hay una ejecución en curso; se omite la de {Disparador}.", disparador);
                return ResultadoEjecucionTasas.NoEjecutada("Ya hay una ejecución de tasas de cambio en curso.");
            }

            await using var bitacora = _contextos.Crear();
            await using var db = _contextos.Crear();

            var ahora = _calendario.AhoraUtc;
            await CerrarEnCursoViejasAsync(bitacora, ahora, ct);

            var fechaObjetivo = rango?.Hasta ?? _calendario.FechaObjetivo();
            var ejecucion = await AbrirEjecucionAsync(bitacora, disparador, ejecutadoPor, fechaObjetivo, rango != null, ahora, ct);

            var corrida = new Corrida(ejecucion, _calendario.Hoy, fechaObjetivo, rango,
                string.IsNullOrWhiteSpace(ejecutadoPor) ? UsuarioJob : ejecutadoPor.Trim(), ahora);

            try
            {
                await ProcesarAsync(db, corrida, ct);
                await DecidirEstadoAsync(db, corrida, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ejecucion.Estado = Cat.EstadoEjecucion.Fallida;
                ejecucion.Mensaje = "Cancelada: la aplicación se detuvo durante la ejecución.";
                ejecucion.FinUtc = _calendario.AhoraUtc;
                await bitacora.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Tasas de cambio: error inesperado en la ejecución {IdEjecucion}.", ejecucion.IdEjecucion);
                db.ChangeTracker.Clear();
                corrida.Estado = Cat.EstadoEjecucion.Fallida;
                corrida.Notas.Add("Error inesperado: " + ex.Message);
                corrida.Errores.Add(ex.ToString());
            }

            CerrarEjecucion(ejecucion, corrida);
            await bitacora.SaveChangesAsync(CancellationToken.None);

            try
            {
                await AlertarAsync(bitacora, db, corrida, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Tasas de cambio: no se pudieron enviar las alertas de la ejecución {IdEjecucion}.", ejecucion.IdEjecucion);
            }

            _log.LogInformation("Tasas de cambio: ejecución {IdEjecucion} ({Disparador}) para {Fecha:yyyy-MM-dd}: {Estado}. {Mensaje}",
                ejecucion.IdEjecucion, disparador, fechaObjetivo, ejecucion.Estado, ejecucion.Mensaje);

            return new ResultadoEjecucionTasas(true, ejecucion.IdEjecucion, ejecucion.Estado, fechaObjetivo,
                ejecucion.Leidos, ejecucion.Insertados, ejecucion.Reemplazados, ejecucion.Duplicados, ejecucion.Invalidos,
                ejecucion.Mensaje ?? string.Empty);
        }

        /// <summary>Una EN_CURSO de más de 15 minutos es un proceso que murió a medias (reciclado de IIS, caída).</summary>
        private async Task CerrarEnCursoViejasAsync(ApplicationDbContext bitacora, DateTime ahora, CancellationToken ct)
        {
            var limite = ahora - EnCursoMaximo;
            var viejas = await bitacora.TasasCambioEjecuciones
                .Where(e => e.Job == Cat.Job && e.Estado == Cat.EstadoEjecucion.EnCurso && e.InicioUtc < limite)
                .ToListAsync(ct);
            if (viejas.Count == 0) return;

            foreach (var vieja in viejas)
            {
                vieja.Estado = Cat.EstadoEjecucion.Fallida;
                vieja.FinUtc = ahora;
                vieja.Mensaje = TextoTasas.Cortar(
                    "Quedó EN_CURSO más de 15 minutos (proceso interrumpido); se cerró al empezar la siguiente ejecución. " + vieja.Mensaje, 1000);
            }
            await bitacora.SaveChangesAsync(ct);
            _log.LogWarning("Tasas de cambio: {Cantidad} ejecución(es) EN_CURSO de más de 15 minutos marcadas FALLIDA.", viejas.Count);
        }

        /// <summary>
        /// Crea la fila EN_CURSO. Si la última ejecución de la misma fecha objetivo quedó REINTENTADA, esta es su
        /// continuación: Intento + 1 y el mismo origen de la cadena.
        /// </summary>
        private async Task<TasaCambioEjecucion> AbrirEjecucionAsync(ApplicationDbContext bitacora, string disparador,
            string ejecutadoPor, DateOnly fechaObjetivo, bool esRango, DateTime ahora, CancellationToken ct)
        {
            var previa = await bitacora.TasasCambioEjecuciones.AsNoTracking()
                .Where(e => e.Job == Cat.Job && e.FechaObjetivo == fechaObjetivo && e.Estado != Cat.EstadoEjecucion.EnCurso)
                .OrderByDescending(e => e.IdEjecucion)
                .FirstOrDefaultAsync(ct);
            var continua = !esRango && previa?.Estado == Cat.EstadoEjecucion.Reintentada;

            var ejecucion = new TasaCambioEjecucion
            {
                Job = Cat.Job,
                Disparador = disparador,
                FechaObjetivo = fechaObjetivo,
                Intento = continua ? (byte)Math.Min(previa!.Intento + 1, byte.MaxValue) : (byte)1,
                IdEjecucionOrigen = continua ? previa!.IdEjecucionOrigen ?? previa.IdEjecucion : null,
                Estado = Cat.EstadoEjecucion.EnCurso,
                Servidor = TextoTasas.Cortar(Environment.MachineName, 100) ?? "desconocido",
                VersionApp = TextoTasas.Cortar(Assembly.GetEntryAssembly()?.GetName().Version?.ToString(), 30),
                EjecutadoPor = TextoTasas.Cortar(string.IsNullOrWhiteSpace(ejecutadoPor) ? UsuarioJob : ejecutadoPor.Trim(), 100)!,
                InicioUtc = ahora
            };
            bitacora.TasasCambioEjecuciones.Add(ejecucion);
            await bitacora.SaveChangesAsync(ct);
            return ejecucion;
        }

        // ──────────────────────────────────────────────────────────────────
        //  LECTURA Y GUARDADO
        // ──────────────────────────────────────────────────────────────────

        private async Task ProcesarAsync(ApplicationDbContext db, Corrida corrida, CancellationToken ct)
        {
            var local = _opt.MonedaLocalEfectiva;
            var monedas = _opt.MonedasEfectivas;

            // 1. Rango a leer
            if (corrida.Rango is { } rango)
            {
                corrida.Desde = rango.Desde;
                corrida.Hasta = rango.Hasta;
                corrida.FechaMinima = null;
            }
            else
            {
                var ultima = await db.TasasCambio.AsNoTracking()
                    .Where(t => t.IdEmpresa == null && t.MonedaOrigen == "USD" && t.MonedaDestino == local
                                && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado)
                    .OrderByDescending(t => t.FechaVigencia)
                    .Select(t => (DateOnly?)t.FechaVigencia)
                    .FirstOrDefaultAsync(ct);

                var base_ = ultima is { } u && u < corrida.Hoy ? u : corrida.Hoy;
                var desde = base_.AddDays(-Math.Max(0, _opt.DiasPuestaAlDia));
                var tope = corrida.Hoy.AddDays(-MaxDiasPuestaAlDia);
                if (desde < tope) desde = tope;
                if (desde > corrida.FechaObjetivo) desde = corrida.FechaObjetivo;

                corrida.Desde = desde;
                corrida.Hasta = corrida.Hoy;
                corrida.FechaMinima = desde;
            }
            var solicitud = new SolicitudLectura(corrida.Desde, corrida.Hasta);

            // 2. Dólar: API del BCH y, si no sirve, el Excel
            var api = Proveedor(Cat.Fuente.BchApi);
            var excel = Proveedor(Cat.Fuente.BchXlsx);
            ResultadoProveedor? usd = null;
            var intentosUsd = new List<ResultadoProveedor>();

            if (api is { Habilitado: true })
            {
                var r = await api.ObtenerAsync(solicitud, ct);
                intentosUsd.Add(r);
                if (r.Exitoso && r.Lecturas.Any(l => l.MonedaOrigen == "USD")) usd = r;
                else corrida.Notas.Add(r.Exitoso
                    ? "El API del BCH no trajo cifras del dólar; se usó el Excel."
                    : $"El API del BCH falló ({r.Error!.Mensaje}); se usó el Excel.");
            }
            if (usd == null && excel is { Habilitado: true })
            {
                var r = await excel.ObtenerAsync(solicitud, ct);
                intentosUsd.Add(r);
                if (r.Exitoso) usd = r;
            }

            var principal = usd ?? intentosUsd.LastOrDefault();
            if (principal != null)
            {
                corrida.Ejecucion.Fuente = principal.Fuente;
                corrida.Ejecucion.Endpoint = TextoTasas.Cortar(principal.Endpoint, 400);
                corrida.Ejecucion.HttpStatus = principal.HttpStatus is { } s ? (short)s : null;
                corrida.Ejecucion.HashContenido = principal.HashContenido;
            }

            if (usd == null)
            {
                var errores = intentosUsd.Where(i => i.Error != null).Select(i => (i.Fuente, Error: i.Error!)).ToList();
                foreach (var (fuente, error) in errores)
                {
                    corrida.Errores.Add($"{fuente}: {error.Mensaje}" + (error.Detalle != null ? $" ({error.Detalle})" : ""));
                }
                if (intentosUsd.Count == 0)
                    corrida.ErrorUsd = new ErrorFuente(TipoErrorFuente.Permanente, "No hay ninguna fuente del dólar habilitada.");
                else if (errores.Count == intentosUsd.Count)
                    corrida.ErrorUsd = errores.Any(e => e.Error.EsTransitorio)
                        ? new ErrorFuente(TipoErrorFuente.Transitorio, string.Join("; ", errores.Select(e => $"{e.Fuente}: {e.Error.Mensaje}")))
                        : new ErrorFuente(TipoErrorFuente.Permanente, string.Join("; ", errores.Select(e => $"{e.Fuente}: {e.Error.Mensaje}")));
                // Si alguna respondió bien pero sin cifras, no es error: no hay publicación.
                return;
            }

            // 3. Misma respuesta que la última exitosa y la fecha objetivo completa: nada que hacer
            if (corrida.Rango == null && usd.HashContenido != null)
            {
                var ultimaExitosa = await db.TasasCambioEjecuciones.AsNoTracking()
                    .Where(e => e.Job == Cat.Job && e.IdEjecucion != corrida.Ejecucion.IdEjecucion && e.Fuente == usd.Fuente
                                && e.HashContenido != null
                                && (e.Estado == Cat.EstadoEjecucion.Exitosa || e.Estado == Cat.EstadoEjecucion.OmitidaDuplicada))
                    .OrderByDescending(e => e.IdEjecucion)
                    .Select(e => new { e.IdEjecucion, e.HashContenido })
                    .FirstOrDefaultAsync(ct);

                if (ultimaExitosa?.HashContenido == usd.HashContenido
                    && (await FaltantesAsync(db, corrida.FechaObjetivo, ct)).Count == 0)
                {
                    corrida.SinCambiosDesde = ultimaExitosa.IdEjecucion;
                    return;
                }
            }

            // 4. Dólar (y euro oficial si vino del API)
            // En la puesta al día no se corta por arriba: una fecha futura la marca el validador. En un rango manual
            // solo se procesa lo pedido (el Excel trae todo el histórico).
            var deLaFuente = usd.Lecturas
                .Where(l => l.MonedaDestino == local && monedas.Contains(l.MonedaOrigen) && TiposJob.Contains(l.TipoTasa)
                            && (corrida.Rango == null || l.Fecha <= corrida.Hasta))
                .ToList();
            await ProcesarLecturasAsync(db, corrida, deLaFuente.Where(l => l.MonedaOrigen == "USD"), ct);

            if (!monedas.Contains("EUR")) return;

            var euroOficial = deLaFuente.Where(l => l.MonedaOrigen == "EUR").ToList();
            if (usd.Fuente == Cat.Fuente.BchApi && euroOficial.Count > 0)
            {
                await ProcesarLecturasAsync(db, corrida, euroOficial, ct);
                return;
            }
            if (_opt.Bch.EuroOficialHabilitado)
                corrida.Notas.Add("No llegó el euro oficial del BCH; se derivó con el BCE.");

            // 5. Euro derivado: EUR/USD del BCE × USD/HNL validado. Solo donde hace falta: fechas y tipos cuyo dólar
            //    cambió en esta ejecución (nuevo o reemplazo) o que todavía no tienen el euro vigente. Si no hay ninguno,
            //    no se descarga el BCE.
            var usdValidas = corrida.Validas.Where(l => l.MonedaOrigen == "USD").ToList();
            if (usdValidas.Count == 0) return;

            var cambiadas = corrida.Detalles
                .Where(d => d.MonedaOrigen == "USD" && d.FechaVigencia != null
                            && (d.Resultado == Cat.ResultadoDetalle.Insertada || d.Resultado == Cat.ResultadoDetalle.Reemplazo))
                .Select(d => (d.FechaVigencia!.Value, d.TipoTasa))
                .ToHashSet();
            var minimo = usdValidas.Min(l => l.Fecha);
            var maximo = usdValidas.Max(l => l.Fecha);
            var euroVigente = (await db.TasasCambio.AsNoTracking()
                    .Where(t => t.IdEmpresa == null && t.MonedaOrigen == "EUR" && t.MonedaDestino == local
                                && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado
                                && t.FechaVigencia >= minimo && t.FechaVigencia <= maximo)
                    .Select(t => new { t.FechaVigencia, t.TipoTasa })
                    .ToListAsync(ct))
                .Select(t => (t.FechaVigencia, t.TipoTasa))
                .ToHashSet();
            usdValidas = usdValidas
                .Where(u => cambiadas.Contains((u.Fecha, u.TipoTasa)) || !euroVigente.Contains((u.Fecha, u.TipoTasa)))
                .ToList();
            if (usdValidas.Count == 0) return;

            var bce = Proveedor(Cat.Fuente.Bce);
            if (bce is not { Habilitado: true })
            {
                corrida.ErrorEur = new ErrorFuente(TipoErrorFuente.Permanente, "El BCE no está configurado.");
                return;
            }

            var min = usdValidas.Min(l => l.Fecha);
            var max = usdValidas.Max(l => l.Fecha);
            var solicitudBce = min == max && min >= corrida.Hoy.AddDays(-1)
                ? new SolicitudLectura(min, max)
                : new SolicitudLectura(min.AddDays(-EuroDerivado.MaxDiasAtrasBce), max);

            var rBce = await bce.ObtenerAsync(solicitudBce, ct);
            if (!rBce.Exitoso)
            {
                corrida.ErrorEur = rBce.Error;
                corrida.Errores.Add($"{rBce.Fuente}: {rBce.Error!.Mensaje}");
                corrida.Notas.Add($"No se pudo derivar el euro: el BCE falló ({rBce.Error.Mensaje}).");
                return;
            }

            var derivadas = usdValidas
                .Select(u => EuroDerivado.Calcular(u, rBce.Lecturas))
                .Where(d => d != null)
                .Select(d => d!)
                .ToList();
            corrida.Notas.Add("Euro derivado con el BCE.");
            await ProcesarLecturasAsync(db, corrida, derivadas, ct);
        }

        /// <summary>Valida y guarda lecturas, por fecha ascendente (la variación se mide contra lo ya guardado).</summary>
        private async Task ProcesarLecturasAsync(ApplicationDbContext db, Corrida corrida, IEnumerable<LecturaTasa> lecturas,
            CancellationToken ct)
        {
            var grupos = lecturas
                .GroupBy(l => (l.MonedaOrigen, l.MonedaDestino, l.Fecha))
                .OrderBy(g => g.Key.Fecha)
                .ThenBy(g => g.Key.MonedaOrigen);

            foreach (var grupo in grupos)
            {
                // Una lectura por tipo (si la fuente repite, vale la última).
                var porTipo = grupo.GroupBy(l => l.TipoTasa).ToDictionary(g => g.Key, g => g.Last());
                var compra = porTipo.GetValueOrDefault(Cat.TipoTasa.Compra)?.Valor;
                var venta = porTipo.GetValueOrDefault(Cat.TipoTasa.Venta)?.Valor;

                foreach (var lectura in porTipo.Values.OrderBy(l => l.TipoTasa))
                {
                    corrida.Leidos++;
                    var anterior = await db.TasasCambio.AsNoTracking()
                        .Where(t => t.IdEmpresa == null && t.MonedaOrigen == lectura.MonedaOrigen
                                    && t.MonedaDestino == lectura.MonedaDestino && t.TipoTasa == lectura.TipoTasa
                                    && t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado && t.FechaVigencia < lectura.Fecha)
                        .OrderByDescending(t => t.FechaVigencia)
                        .Select(t => (decimal?)t.Tasa)
                        .FirstOrDefaultAsync(ct);

                    var validacion = _validador.Validar(lectura,
                        new ContextoValidacion(corrida.Hoy, corrida.FechaMinima, anterior, compra, venta));

                    var detalle = await GuardarOficialAsync(db, corrida, lectura, validacion, ct);
                    corrida.Detalles.Add(detalle);
                    if (validacion.Nivel == NivelValidacion.Valida && detalle.Resultado != Cat.ResultadoDetalle.Error)
                        corrida.Validas.Add(lectura);
                }
            }
        }

        /// <summary>Guarda una lectura oficial (id_empresa nulo) según su validación y lo que ya hay para esa clave.</summary>
        private async Task<TasaCambioEjecucionDetalle> GuardarOficialAsync(ApplicationDbContext db, Corrida corrida,
            LecturaTasa lectura, ResultadoValidacion validacion, CancellationToken ct)
        {
            var detalle = new TasaCambioEjecucionDetalle
            {
                MonedaOrigen = lectura.MonedaOrigen,
                MonedaDestino = lectura.MonedaDestino,
                TipoTasa = lectura.TipoTasa,
                FechaVigencia = lectura.Fecha,
                ValorLeido = Math.Round(lectura.Valor, 8)
            };

            if (validacion.Nivel == NivelValidacion.Invalida)
            {
                detalle.Resultado = Cat.ResultadoDetalle.Invalida;
                detalle.Motivo = TextoTasas.Cortar(validacion.Motivo, 500);
                corrida.Invalidos++;
                return detalle;
            }

            try
            {
                var existentes = await ExistentesAsync(db, null, lectura.MonedaOrigen, lectura.MonedaDestino, lectura.TipoTasa,
                    lectura.Fecha, ct);
                var vigente = existentes.FirstOrDefault(t => t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado);
                var valor = TextoTasas.Redondear4(lectura.Valor);

                if (vigente != null && TextoTasas.Redondear4(vigente.Tasa) == valor)
                {
                    detalle.Resultado = Cat.ResultadoDetalle.Duplicada;
                    detalle.IdTasaCambio = vigente.IdTasaCambio;
                    corrida.Duplicados++;
                    return detalle;
                }

                var revisadaAntes = existentes.FirstOrDefault(t => !t.Eliminado && TextoTasas.Redondear4(t.Tasa) == valor
                    && (t.Estado == Cat.EstadoTasa.EnRevision || t.Estado == Cat.EstadoTasa.Rechazada));
                if (revisadaAntes != null)
                {
                    // Ya está esperando revisión o alguien la rechazó: no se vuelve a insertar ni a alertar.
                    detalle.Resultado = Cat.ResultadoDetalle.Duplicada;
                    detalle.Motivo = $"Igual a la tasa #{revisadaAntes.IdTasaCambio}, {revisadaAntes.Estado}.";
                    detalle.IdTasaCambio = revisadaAntes.IdTasaCambio;
                    corrida.Duplicados++;
                    return detalle;
                }

                var nueva = NuevaTasa(lectura, null, corrida.Usuario, corrida.Ahora, corrida.Ejecucion.IdEjecucion);
                nueva.Version = SiguienteVersion(existentes);

                if (validacion.Nivel == NivelValidacion.EnRevision)
                {
                    nueva.Estado = Cat.EstadoTasa.EnRevision;
                    nueva.IdTasaAnterior = vigente?.IdTasaCambio;
                    db.TasasCambio.Add(nueva);
                    await db.SaveChangesAsync(ct);

                    detalle.Resultado = Cat.ResultadoDetalle.EnRevision;
                    detalle.Motivo = TextoTasas.Cortar(validacion.Motivo, 500);
                    detalle.IdTasaCambio = nueva.IdTasaCambio;
                    corrida.Invalidos++;
                    corrida.EnRevision.Add((nueva, validacion.Motivo ?? string.Empty));
                    return detalle;
                }

                if (vigente != null)
                {
                    await ReemplazarAsync(db, vigente, nueva, corrida.Usuario, corrida.Ahora, ct);
                    detalle.Resultado = Cat.ResultadoDetalle.Reemplazo;
                    detalle.Motivo = $"Reemplaza a la #{vigente.IdTasaCambio} ({TextoTasas.Formatear(vigente.Tasa)}).";
                    corrida.Reemplazados++;
                }
                else
                {
                    db.TasasCambio.Add(nueva);
                    await db.SaveChangesAsync(ct);
                    detalle.Resultado = Cat.ResultadoDetalle.Insertada;
                    corrida.Insertados++;
                }
                detalle.IdTasaCambio = nueva.IdTasaCambio;
                return detalle;
            }
            catch (DbUpdateException ex)
            {
                // Por ejemplo, otra escritura ganó el índice único de la vigente: se registra y se sigue con lo demás.
                db.ChangeTracker.Clear();
                _log.LogWarning(ex, "Tasas de cambio: no se pudo guardar {Moneda} {Tipo} {Fecha:yyyy-MM-dd}.",
                    lectura.MonedaOrigen, lectura.TipoTasa, lectura.Fecha);
                detalle.Resultado = Cat.ResultadoDetalle.Error;
                detalle.Motivo = TextoTasas.Cortar("No se pudo guardar: " + (ex.InnerException?.Message ?? ex.Message), 500);
                corrida.ErroresGuardado++;
                return detalle;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  ESTADO FINAL
        // ──────────────────────────────────────────────────────────────────

        private enum Falta { SinDatos, Transitorio, Permanente, Invalida }

        private async Task DecidirEstadoAsync(ApplicationDbContext db, Corrida corrida, CancellationToken ct)
        {
            var f = corrida.FechaObjetivo;

            if (corrida.SinCambiosDesde is { } desde)
            {
                corrida.Estado = Cat.EstadoEjecucion.OmitidaDuplicada;
                corrida.Notas.Insert(0, $"La fuente no cambió desde la ejecución #{desde} y la fecha objetivo ya tiene sus tasas.");
                return;
            }

            var faltantes = await FaltantesAsync(db, f, ct);
            var esperadas = _opt.MonedasEfectivas.Count * TiposJob.Length;
            var hayObservaciones = corrida.Invalidos > 0 || corrida.ErroresGuardado > 0;

            if (faltantes.Count == 0)
            {
                corrida.Estado = hayObservaciones ? Cat.EstadoEjecucion.Parcial : Cat.EstadoEjecucion.Exitosa;
                return;
            }

            // Por qué falta cada moneda/tipo de la fecha objetivo. El dólar primero: si falta, el euro (que se deriva
            // de él) falta por el mismo motivo.
            var motivos = new Dictionary<(string Moneda, string Tipo), Falta>();
            foreach (var (moneda, tipo) in faltantes.OrderBy(x => x.Moneda == "USD" ? 0 : 1))
            {
                var leida = corrida.Detalles.FirstOrDefault(d => d.FechaVigencia == f && d.MonedaOrigen == moneda && d.TipoTasa == tipo);
                Falta motivo;
                if (leida != null)
                    motivo = leida.Resultado == Cat.ResultadoDetalle.Error ? Falta.Transitorio : Falta.Invalida;
                else if (moneda == "USD")
                    motivo = MotivoPorError(corrida.ErrorUsd);
                else if (motivos.TryGetValue(("USD", tipo), out var delDolar))
                    motivo = delDolar;
                else if (corrida.ErrorEur != null)
                    motivo = MotivoPorError(corrida.ErrorEur);
                else
                    motivo = Falta.SinDatos;

                motivos[(moneda, tipo)] = motivo;
                if (leida == null)
                {
                    corrida.Detalles.Add(new TasaCambioEjecucionDetalle
                    {
                        MonedaOrigen = moneda, MonedaDestino = _opt.MonedaLocalEfectiva, TipoTasa = tipo, FechaVigencia = f,
                        Resultado = motivo is Falta.SinDatos or Falta.Invalida ? Cat.ResultadoDetalle.SinDatos : Cat.ResultadoDetalle.Error,
                        Motivo = TextoTasas.Cortar(motivo switch
                        {
                            Falta.SinDatos => moneda == "USD" || !faltantes.Contains(("USD", tipo))
                                ? "La fuente no trae esta fecha."
                                : "Sin tasa del dólar de esta fecha para derivar el euro.",
                            Falta.Invalida => "La tasa del dólar de esta fecha no pasó la validación.",
                            _ when moneda != "USD" && corrida.ErrorEur != null && !faltantes.Contains(("USD", tipo)) => corrida.ErrorEur.Mensaje,
                            _ => corrida.ErrorUsd?.Mensaje ?? "Sin dato del dólar para esta fecha."
                        }, 500)
                    });
                }
            }

            var obtenidas = esperadas - faltantes.Count;
            var reintentable = motivos.Values.Any(m => m == Falta.Transitorio)
                               || (motivos.Values.Any(m => m == Falta.SinDatos) && f == corrida.Hoy);

            if (corrida.Rango == null && reintentable && ProximoIntento(corrida) is { } proximo)
            {
                corrida.Estado = Cat.EstadoEjecucion.Reintentada;
                corrida.ProximoIntentoUtc = proximo;
                corrida.Notas.Insert(0, $"Faltan tasas del {f:yyyy-MM-dd}; próximo intento {_calendario.AHonduras(proximo):HH:mm} (hora de Honduras).");
                return;
            }

            if (motivos.Values.Any(m => m is Falta.Transitorio or Falta.Permanente))
            {
                corrida.Estado = obtenidas > 0 ? Cat.EstadoEjecucion.Parcial : Cat.EstadoEjecucion.Fallida;
                corrida.Notas.Insert(0, $"No se pudieron obtener tasas del {f:yyyy-MM-dd}.");
            }
            else if (motivos.Values.All(m => m == Falta.Invalida))
            {
                corrida.Estado = obtenidas > 0 ? Cat.EstadoEjecucion.Parcial : Cat.EstadoEjecucion.OmitidaInvalida;
                corrida.Notas.Insert(0, $"Las tasas leídas del {f:yyyy-MM-dd} no pasaron la validación.");
            }
            else
            {
                corrida.Estado = obtenidas > 0
                    ? Cat.EstadoEjecucion.Parcial
                    : motivos.Values.Any(m => m == Falta.Invalida) ? Cat.EstadoEjecucion.OmitidaInvalida : Cat.EstadoEjecucion.OmitidaSinDatos;
                corrida.Notas.Insert(0, $"No hay publicación para el {f:yyyy-MM-dd}.");
            }
        }

        private static Falta MotivoPorError(ErrorFuente? error) => error switch
        {
            null => Falta.SinDatos,
            { EsTransitorio: true } => Falta.Transitorio,
            _ => Falta.Permanente
        };

        /// <summary>
        /// Cuándo reintentar según la tabla de esperas, o nulo si se agotó MaxPorFecha o el próximo intento caería
        /// después de UltimoIntento de hoy.
        /// </summary>
        private DateTime? ProximoIntento(Corrida corrida)
        {
            var intento = corrida.Ejecucion.Intento;
            if (intento >= Math.Max(1, _opt.Reintentos.MaxPorFecha)) return null;

            var esperas = _opt.EsperasEfectivas;
            var minutos = esperas[Math.Min(intento - 1, esperas.Count - 1)];
            var proximo = corrida.Ahora.AddMinutes(Math.Max(1, minutos));
            return proximo <= _calendario.LimiteReintentosHoyUtc ? proximo : null;
        }

        private void CerrarEjecucion(TasaCambioEjecucion ejecucion, Corrida corrida)
        {
            ejecucion.Estado = corrida.Estado ?? Cat.EstadoEjecucion.Fallida;
            ejecucion.Leidos = corrida.Leidos;
            ejecucion.Insertados = corrida.Insertados;
            ejecucion.Reemplazados = corrida.Reemplazados;
            ejecucion.Duplicados = corrida.Duplicados;
            ejecucion.Invalidos = corrida.Invalidos;
            ejecucion.ProximoIntentoUtc = corrida.ProximoIntentoUtc;
            ejecucion.FinUtc = _calendario.AhoraUtc;

            var resumen = $"Leídas {corrida.Leidos}, nuevas {corrida.Insertados}, reemplazadas {corrida.Reemplazados}, " +
                          $"duplicadas {corrida.Duplicados}, inválidas o en revisión {corrida.Invalidos}" +
                          (corrida.ErroresGuardado > 0 ? $", sin guardar por error {corrida.ErroresGuardado}" : "") + ".";
            ejecucion.Mensaje = TextoTasas.Cortar(string.Join(" ", corrida.Notas.Append(resumen)), 1000);
            ejecucion.DetalleError = corrida.Errores.Count > 0 ? string.Join(Environment.NewLine, corrida.Errores) : null;

            foreach (var detalle in corrida.Detalles) ejecucion.Detalles.Add(detalle);
        }

        // ──────────────────────────────────────────────────────────────────
        //  ALERTAS
        // ──────────────────────────────────────────────────────────────────

        private async Task AlertarAsync(ApplicationDbContext bitacora, ApplicationDbContext db, Corrida corrida, CancellationToken ct)
        {
            var ejecucion = corrida.Ejecucion;
            var notificado = false;

            if (corrida.EnRevision.Count > 0)
            {
                var filas = new StringBuilder();
                foreach (var (tasa, motivo) in corrida.EnRevision)
                    filas.Append($"<tr><td>#{tasa.IdTasaCambio}</td><td>{tasa.FechaVigencia:yyyy-MM-dd}</td><td>{tasa.MonedaOrigen}/{tasa.MonedaDestino}</td>" +
                                 $"<td>{tasa.TipoTasa}</td><td>{TextoTasas.Formatear(tasa.Tasa)}</td><td>{WebUtility.HtmlEncode(motivo)}</td></tr>");
                var fechas = string.Join(", ", corrida.EnRevision.Select(e => e.Tasa.FechaVigencia).Distinct().OrderBy(x => x).Select(x => x.ToString("yyyy-MM-dd")));

                notificado |= await _notificador.NotificarAsync(
                    $"eGestion360 · Tasas de cambio en revisión ({fechas})",
                    "<p>El job de tasas de cambio guardó tasas <strong>EN_REVISION</strong> (no vigentes) porque no pasaron la validación. " +
                    "Apruébelas o recházelas en la pantalla de tasas de cambio.</p>" +
                    "<table border='1' cellpadding='4' cellspacing='0'><tr><th>Id</th><th>Fecha</th><th>Par</th><th>Tipo</th><th>Tasa</th><th>Motivo</th></tr>" +
                    filas + "</table>" +
                    $"<p>Ejecución #{ejecucion.IdEjecucion}.</p>", ct);
            }

            if (ejecucion.Estado == Cat.EstadoEjecucion.Fallida
                && !await YaNotificadaAsync(bitacora, ejecucion, Cat.EstadoEjecucion.Fallida, ct))
            {
                notificado |= await _notificador.NotificarAsync(
                    $"eGestion360 · Falló la obtención de tasas de cambio del {ejecucion.FechaObjetivo:yyyy-MM-dd}",
                    $"<p>El job de tasas de cambio no pudo obtener las tasas del <strong>{ejecucion.FechaObjetivo:yyyy-MM-dd}</strong> " +
                    $"(intento {ejecucion.Intento}).</p><p>{WebUtility.HtmlEncode(ejecucion.Mensaje)}</p>" +
                    (ejecucion.DetalleError != null ? $"<pre>{WebUtility.HtmlEncode(TextoTasas.Cortar(ejecucion.DetalleError, 2000))}</pre>" : "") +
                    $"<p>Ejecución #{ejecucion.IdEjecucion}. Revise la bitácora en la pantalla de tasas de cambio.</p>", ct);
            }

            if (ejecucion.Estado == Cat.EstadoEjecucion.OmitidaSinDatos && CalendarioTasasCambio.EsDiaHabil(ejecucion.FechaObjetivo))
            {
                var anterior = CalendarioTasasCambio.DiaHabilAnterior(ejecucion.FechaObjetivo);
                _log.LogWarning("Tasas de cambio: no hay tasa publicada para el día hábil {Fecha:yyyy-MM-dd}.", ejecucion.FechaObjetivo);

                var faltaAnterior = (await FaltantesAsync(db, anterior, ct)).Any(x => x.Moneda == "USD");
                if (faltaAnterior && !await YaNotificadaAsync(bitacora, ejecucion, Cat.EstadoEjecucion.OmitidaSinDatos, ct))
                {
                    notificado |= await _notificador.NotificarAsync(
                        $"eGestion360 · Faltan las tasas de cambio de dos días hábiles seguidos",
                        $"<p>No hay tasa oficial del dólar del <strong>{anterior:yyyy-MM-dd}</strong> ni del " +
                        $"<strong>{ejecucion.FechaObjetivo:yyyy-MM-dd}</strong>. Si no fueron feriados, revise la fuente del BCH " +
                        $"o registre la tasa a mano.</p><p>Ejecución #{ejecucion.IdEjecucion}.</p>", ct);
                }
            }

            if (notificado)
            {
                ejecucion.Notificado = true;
                await bitacora.SaveChangesAsync(CancellationToken.None);
            }
        }

        /// <summary>¿Otra ejecución de la misma fecha objetivo y estado ya avisó?</summary>
        private static Task<bool> YaNotificadaAsync(ApplicationDbContext bitacora, TasaCambioEjecucion ejecucion, string estado,
            CancellationToken ct) =>
            bitacora.TasasCambioEjecuciones.AnyAsync(e => e.Job == Cat.Job && e.IdEjecucion != ejecucion.IdEjecucion
                && e.FechaObjetivo == ejecucion.FechaObjetivo && e.Estado == estado && e.Notificado, ct);

        // ──────────────────────────────────────────────────────────────────
        //  REVISIÓN Y CAPTURA MANUAL
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoOperacionTasa> AprobarAsync(int idTasaCambio, string usuario, CancellationToken ct = default)
        {
            usuario = Usuario(usuario);
            await using var db = _contextos.Crear();

            var tasa = await db.TasasCambio.FirstOrDefaultAsync(t => t.IdTasaCambio == idTasaCambio && !t.Eliminado, ct);
            if (tasa == null) return new(false, "No existe la tasa indicada.");
            if (tasa.Estado != Cat.EstadoTasa.EnRevision)
                return new(false, $"La tasa #{idTasaCambio} no está en revisión (estado {tasa.Estado}).", idTasaCambio);

            var existentes = await ExistentesAsync(db, tasa.IdEmpresa, tasa.MonedaOrigen, tasa.MonedaDestino, tasa.TipoTasa,
                tasa.FechaVigencia, ct);
            var vigente = existentes.FirstOrDefault(t => t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado && t.IdTasaCambio != tasa.IdTasaCambio);
            var maxVersion = existentes.Max(t => t.Version);
            var ahora = _calendario.AhoraUtc;

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                if (vigente != null)
                {
                    vigente.Estado = Cat.EstadoTasa.Reemplazada;
                    vigente.ModificadoPor = usuario;
                    vigente.FechaModificacion = ahora;
                    await db.SaveChangesAsync(ct);
                }

                tasa.Estado = Cat.EstadoTasa.Vigente;
                tasa.IdTasaAnterior = vigente?.IdTasaCambio ?? tasa.IdTasaAnterior;
                if (tasa.Version < maxVersion) tasa.Version = (short)(maxVersion + 1);
                tasa.ModificadoPor = usuario;
                tasa.FechaModificacion = ahora;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogWarning(ex, "Tasas de cambio: no se pudo aprobar la tasa {IdTasa}.", idTasaCambio);
                return new(false, "No se pudo aprobar: la tasa cambió mientras tanto. Recargue la pantalla e intente de nuevo.", idTasaCambio);
            }

            await AnotarRevisionAsync(tasa,
                vigente != null ? Cat.ResultadoDetalle.Reemplazo : Cat.ResultadoDetalle.Insertada,
                $"Aprobada por {usuario}" + (vigente != null ? $"; reemplaza a la #{vigente.IdTasaCambio}." : "."), ct);

            _log.LogInformation("Tasas de cambio: {Usuario} aprobó la tasa {IdTasa}.", usuario, idTasaCambio);
            return new(true, vigente != null
                ? $"Tasa aprobada; reemplaza a la #{vigente.IdTasaCambio}."
                : "Tasa aprobada.", idTasaCambio);
        }

        public async Task<ResultadoOperacionTasa> RechazarAsync(int idTasaCambio, string usuario, string motivo, CancellationToken ct = default)
        {
            usuario = Usuario(usuario);
            if (string.IsNullOrWhiteSpace(motivo)) return new(false, "Indique el motivo del rechazo.", idTasaCambio);

            await using var db = _contextos.Crear();
            var tasa = await db.TasasCambio.FirstOrDefaultAsync(t => t.IdTasaCambio == idTasaCambio && !t.Eliminado, ct);
            if (tasa == null) return new(false, "No existe la tasa indicada.");
            if (tasa.Estado != Cat.EstadoTasa.EnRevision)
                return new(false, $"La tasa #{idTasaCambio} no está en revisión (estado {tasa.Estado}).", idTasaCambio);

            tasa.Estado = Cat.EstadoTasa.Rechazada;
            tasa.ModificadoPor = usuario;
            tasa.FechaModificacion = _calendario.AhoraUtc;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogWarning(ex, "Tasas de cambio: no se pudo rechazar la tasa {IdTasa}.", idTasaCambio);
                return new(false, "No se pudo rechazar: la tasa cambió mientras tanto. Recargue la pantalla e intente de nuevo.", idTasaCambio);
            }

            await AnotarRevisionAsync(tasa, Cat.ResultadoDetalle.Invalida, $"Rechazada por {usuario}: {motivo.Trim()}", ct);
            _log.LogInformation("Tasas de cambio: {Usuario} rechazó la tasa {IdTasa}.", usuario, idTasaCambio);
            return new(true, "Tasa rechazada.", idTasaCambio);
        }

        public async Task<ResultadoOperacionTasa> RegistrarManualAsync(int idEmpresa, string monedaOrigen, string tipoTasa,
            DateOnly fecha, decimal tasa, string usuario, CancellationToken ct = default)
        {
            usuario = Usuario(usuario);
            var local = _opt.MonedaLocalEfectiva;
            var origen = (monedaOrigen ?? string.Empty).Trim().ToUpperInvariant();
            var tipo = (tipoTasa ?? string.Empty).Trim().ToUpperInvariant();

            if (idEmpresa <= 0) return new(false, "Seleccione una empresa: la tasa manual es propia de una empresa.");
            if (origen.Length != 3) return new(false, "Indique la moneda con su código ISO de 3 letras.");
            if (origen == local) return new(false, $"La moneda origen no puede ser la moneda local ({local}).");
            if (!Cat.TipoTasa.Todos.Contains(tipo)) return new(false, "El tipo debe ser COMPRA, VENTA o REFERENCIA.");
            if (tasa <= 0) return new(false, "La tasa debe ser mayor que cero.");
            if (_validador.RevisarFecha(fecha, _calendario.Hoy, null) is { } errorFecha) return new(false, errorFecha);
            if (_validador.RevisarRango(origen, tasa) is { } errorRango) return new(false, errorRango);

            await using var db = _contextos.Crear();
            if (!await db.Monedas.AnyAsync(m => m.CodigoIso == origen, ct)) return new(false, $"La moneda {origen} no existe.");
            if (!await db.Empresas.AnyAsync(e => e.IdEmpresa == idEmpresa, ct)) return new(false, "La empresa no existe.");

            var existentes = await ExistentesAsync(db, idEmpresa, origen, local, tipo, fecha, ct);
            var vigente = existentes.FirstOrDefault(t => t.Estado == Cat.EstadoTasa.Vigente && !t.Eliminado);
            if (vigente != null && TextoTasas.Redondear4(vigente.Tasa) == TextoTasas.Redondear4(tasa))
                return new(true, "La tasa ya estaba registrada con ese valor; no se hizo ningún cambio.", vigente.IdTasaCambio);

            var ahora = _calendario.AhoraUtc;
            var nueva = NuevaTasa(
                new LecturaTasa(origen, local, tipo, fecha, tasa, Cat.Fuente.Manual, $"Captura manual de {usuario}"),
                idEmpresa, usuario, ahora, null);
            nueva.Version = SiguienteVersion(existentes);

            try
            {
                if (vigente != null) await ReemplazarAsync(db, vigente, nueva, usuario, ahora, ct);
                else
                {
                    db.TasasCambio.Add(nueva);
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (DbUpdateException ex)
            {
                _log.LogWarning(ex, "Tasas de cambio: no se pudo registrar la tasa manual de la empresa {IdEmpresa}.", idEmpresa);
                return new(false, "No se pudo registrar: otra persona guardó una tasa para la misma fecha. Recargue e intente de nuevo.");
            }

            _log.LogInformation("Tasas de cambio: {Usuario} registró la tasa manual {IdTasa} de la empresa {IdEmpresa}.",
                usuario, nueva.IdTasaCambio, idEmpresa);
            return new(true, vigente != null
                ? $"Tasa registrada; reemplaza a la #{vigente.IdTasaCambio}."
                : "Tasa registrada.", nueva.IdTasaCambio);
        }

        /// <summary>
        /// Deja la decisión de una revisión en la bitácora de la ejecución que obtuvo la tasa (tasas_cambio no tiene
        /// columna de motivo). Si no se puede, queda en el log; la decisión ya está guardada.
        /// </summary>
        private async Task AnotarRevisionAsync(TasaCambio tasa, string resultado, string motivo, CancellationToken ct)
        {
            if (tasa.IdEjecucion is not { } idEjecucion)
            {
                _log.LogInformation("Tasas de cambio: revisión de la tasa {IdTasa}: {Motivo}", tasa.IdTasaCambio, motivo);
                return;
            }

            try
            {
                await using var bitacora = _contextos.Crear();
                bitacora.TasasCambioEjecucionesDetalle.Add(new TasaCambioEjecucionDetalle
                {
                    IdEjecucion = idEjecucion,
                    MonedaOrigen = tasa.MonedaOrigen,
                    MonedaDestino = tasa.MonedaDestino,
                    TipoTasa = tasa.TipoTasa,
                    FechaVigencia = tasa.FechaVigencia,
                    ValorLeido = tasa.Tasa,
                    Resultado = resultado,
                    Motivo = TextoTasas.Cortar(motivo, 500),
                    IdTasaCambio = tasa.IdTasaCambio
                });
                await bitacora.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Tasas de cambio: no se pudo anotar en la bitácora la revisión de la tasa {IdTasa}: {Motivo}",
                    tasa.IdTasaCambio, motivo);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  UTILIDADES
        // ──────────────────────────────────────────────────────────────────

        private Task<List<(string Moneda, string Tipo)>> FaltantesAsync(ApplicationDbContext db, DateOnly fecha, CancellationToken ct) =>
            TasasCambioDatos.FaltantesAsync(db, _opt, fecha, ct);

        /// <summary>Todas las filas de una clave (incluidas las eliminadas, para numerar la versión).</summary>
        private static Task<List<TasaCambio>> ExistentesAsync(ApplicationDbContext db, int? idEmpresa, string origen, string destino,
            string tipo, DateOnly fecha, CancellationToken ct) =>
            db.TasasCambio
                .Where(t => t.IdEmpresa == idEmpresa && t.MonedaOrigen == origen && t.MonedaDestino == destino
                            && t.TipoTasa == tipo && t.FechaVigencia == fecha)
                .ToListAsync(ct);

        private static short SiguienteVersion(List<TasaCambio> existentes) =>
            (short)(existentes.Count == 0 ? 1 : existentes.Max(t => t.Version) + 1);

        /// <summary>
        /// La vigente pasa a REEMPLAZADA y la nueva entra VIGENTE, en una transacción y en dos guardados: el índice
        /// único filtrado de SQL Server no admite dos VIGENTE de la misma clave ni por un instante.
        /// </summary>
        private static async Task ReemplazarAsync(ApplicationDbContext db, TasaCambio vigente, TasaCambio nueva, string usuario,
            DateTime ahora, CancellationToken ct)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            vigente.Estado = Cat.EstadoTasa.Reemplazada;
            vigente.ModificadoPor = usuario;
            vigente.FechaModificacion = ahora;
            await db.SaveChangesAsync(ct);

            nueva.Estado = Cat.EstadoTasa.Vigente;
            nueva.IdTasaAnterior = vigente.IdTasaCambio;
            db.TasasCambio.Add(nueva);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        private static TasaCambio NuevaTasa(LecturaTasa lectura, int? idEmpresa, string usuario, DateTime ahora, long? idEjecucion) => new()
        {
            IdEmpresa = idEmpresa,
            MonedaOrigen = lectura.MonedaOrigen,
            MonedaDestino = lectura.MonedaDestino,
            TipoTasa = lectura.TipoTasa,
            Tasa = Math.Round(lectura.Valor, 8),
            FechaVigencia = lectura.Fecha,
            FechaHoraObtencion = ahora,
            Fuente = lectura.Fuente,
            ReferenciaFuente = TextoTasas.Cortar(lectura.Referencia, 400),
            EsDerivada = lectura.EsDerivada,
            Estado = Cat.EstadoTasa.Vigente,
            Version = 1,
            IdEjecucion = idEjecucion,
            CreadoPor = TextoTasas.Cortar(usuario, 100)!,
            FechaCreacion = ahora
        };

        private ITasaCambioProveedor? Proveedor(string fuente) => _proveedores.FirstOrDefault(p => p.Fuente == fuente);

        private static string Usuario(string? usuario) =>
            TextoTasas.Cortar(string.IsNullOrWhiteSpace(usuario) ? "sistema" : usuario.Trim(), 100)!;

        /// <summary>Lo que se va acumulando durante una ejecución.</summary>
        private sealed class Corrida
        {
            public Corrida(TasaCambioEjecucion ejecucion, DateOnly hoy, DateOnly fechaObjetivo, SolicitudLectura? rango,
                string usuario, DateTime ahora)
            {
                Ejecucion = ejecucion;
                Hoy = hoy;
                FechaObjetivo = fechaObjetivo;
                Rango = rango;
                Usuario = TextoTasas.Cortar(usuario, 100)!;
                Ahora = ahora;
            }

            public TasaCambioEjecucion Ejecucion { get; }
            public DateOnly Hoy { get; }
            public DateOnly FechaObjetivo { get; }
            public SolicitudLectura? Rango { get; }
            public string Usuario { get; }
            public DateTime Ahora { get; }

            public DateOnly Desde { get; set; }
            public DateOnly Hasta { get; set; }
            public DateOnly? FechaMinima { get; set; }

            public int Leidos { get; set; }
            public int Insertados { get; set; }
            public int Reemplazados { get; set; }
            public int Duplicados { get; set; }
            public int Invalidos { get; set; }
            public int ErroresGuardado { get; set; }

            public List<TasaCambioEjecucionDetalle> Detalles { get; } = new();
            public List<LecturaTasa> Validas { get; } = new();
            public List<(TasaCambio Tasa, string Motivo)> EnRevision { get; } = new();
            public List<string> Notas { get; } = new();
            public List<string> Errores { get; } = new();

            public ErrorFuente? ErrorUsd { get; set; }
            public ErrorFuente? ErrorEur { get; set; }
            public long? SinCambiosDesde { get; set; }

            public string? Estado { get; set; }
            public DateTime? ProximoIntentoUtc { get; set; }
        }
    }
}
