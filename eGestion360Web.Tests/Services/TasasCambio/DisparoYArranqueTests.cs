using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// El disparador externo y el arranque del proceso solo hacen evaluar la programación: nunca fuerzan una ejecución.
    /// Worker, planificador y orquestador reales sobre SQLite; las fuentes, grabadas. Cada <see cref="Proceso"/> es un
    /// proceso nuevo de IIS (sin memoria del anterior): lo que recuerda el job sale de la base.
    /// </summary>
    public class DisparoYArranqueTests : IDisposable
    {
        /// <summary>Valor inventado para el encabezado del disparador externo; no es una credencial de ningún entorno.</summary>
        private const string ValorEncabezadoFicticio = "valor-ficticio-solo-para-pruebas";

        private readonly EscenarioTasas _e = new();

        public void Dispose() => _e.Dispose();

        private static DateTimeOffset Honduras(int dia, int hora, int minuto = 0) =>
            new(2026, 10, dia, hora, minuto, 0, TimeSpan.FromHours(-6));

        /// <summary>Un proceso de la aplicación: su worker, su cola y su disparador externo, sobre la misma base.</summary>
        private sealed record ProcesoApp(TasasCambioBackgroundService Worker, ColaEjecucionTasasCambio Cola, DisparadorExternoTasasCambio Disparador)
        {
            /// <summary>POST al endpoint con el token correcto y, enseguida, lo que el worker hace con la cola.</summary>
            public async Task<int> LlamadaExternaAsync()
            {
                var http = new DefaultHttpContext();
                http.Request.Headers[TasasCambioEndpoints.EncabezadoAutenticacion] = ValorEncabezadoFicticio;
                var codigo = ((IStatusCodeHttpResult)TasasCambioEndpoints.Atender(http.Request, Disparador)).StatusCode!.Value;
                await Worker.ProcesarColaAsync();
                return codigo;
            }
        }

        private ProcesoApp Proceso()
        {
            _e.Opciones.Disparador.Token = ValorEncabezadoFicticio;
            var opciones = Options.Create(_e.Opciones);
            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => _e.Bd.Crear());
            servicios.AddSingleton(opciones);
            servicios.AddSingleton<TimeProvider>(_e.Reloj);
            servicios.AddScoped<TasasCambioPlanificador>();
            servicios.AddScoped<ITasaCambioSyncService>(_ => _e.Servicio());
            var proveedor = servicios.BuildServiceProvider();

            var cola = new ColaEjecucionTasasCambio();
            var worker = new TasasCambioBackgroundService(proveedor.GetRequiredService<IServiceScopeFactory>(), opciones, cola, _e.Reloj,
                NullLogger<TasasCambioBackgroundService>.Instance);
            var disparador = new DisparadorExternoTasasCambio(opciones, cola, _e.Reloj, NullLogger<DisparadorExternoTasasCambio>.Instance);
            return new ProcesoApp(worker, cola, disparador);
        }

        private void En(DateTimeOffset momento) => _e.Reloj = new RelojFijo(momento);

        private int DescargasExcel => _e.Http.Llamadas(EscenarioTasas.UrlExcel);
        private int DescargasBce => _e.Http.Llamadas(EscenarioTasas.UrlBce90) + _e.Http.Llamadas(EscenarioTasas.UrlBceDiaria);

        private List<TasaCambioEjecucion> Bitacora()
        {
            using var db = _e.Bd.Crear();
            return db.TasasCambioEjecuciones.AsNoTracking().OrderBy(e => e.IdEjecucion).ToList();
        }

        /// <summary>USD y EUR, compra y venta, de lunes 28-sep a jueves 1-oct, ya vigentes.</summary>
        private void SembrarHastaJueves() => SembrarDias(0, 4);

        /// <summary>USD y EUR, compra y venta, de <paramref name="cantidad"/> días de la semana del escenario, ya vigentes.</summary>
        private void SembrarDias(int desde, int cantidad)
        {
            var bce = EscenarioTasas.SemanaBce().ToDictionary(b => b.Item1, b => b.Item2);
            foreach (var (fecha, compra, venta) in EscenarioTasas.SemanaBch().Skip(desde).Take(cantidad))
            {
                _e.Sembrar("USD", "COMPRA", fecha, compra);
                _e.Sembrar("USD", "VENTA", fecha, venta);
                _e.Sembrar("EUR", "COMPRA", fecha, Math.Round(bce[fecha] * compra, 4), fuente: Cat.Fuente.Derivada);
                _e.Sembrar("EUR", "VENTA", fecha, Math.Round(bce[fecha] * venta, 4), fuente: Cat.Fuente.Derivada);
            }
        }

        /// <summary>Las cuatro tasas del lunes 5 (el BCH las publica el viernes en la tarde), ya vigentes.</summary>
        private void SembrarLunes()
        {
            _e.Sembrar("USD", "COMPRA", EscenarioTasas.Lunes, 26.8901m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Lunes, 27.0246m);
            _e.Sembrar("EUR", "COMPRA", EscenarioTasas.Lunes, 30.1277m, fuente: Cat.Fuente.Derivada);
            _e.Sembrar("EUR", "VENTA", EscenarioTasas.Lunes, 30.2784m, fuente: Cat.Fuente.Derivada);
        }

        private void SembrarEjecucion(DateOnly fechaObjetivo, string estado, DateTimeOffset inicio, DateTimeOffset? proximo = null)
        {
            using var db = _e.Bd.Crear();
            db.TasasCambioEjecuciones.Add(new TasaCambioEjecucion
            {
                Disparador = Cat.Disparador.Programado, FechaObjetivo = fechaObjetivo, Estado = estado, Intento = 1,
                ProximoIntentoUtc = proximo?.UtcDateTime, Servidor = "x", EjecutadoPor = "job", InicioUtc = inicio.UtcDateTime,
                FinUtc = inicio.UtcDateTime
            });
            db.SaveChanges();
        }

        // ── A1 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A1_llamada_externa_sin_nada_pendiente_responde_202_sin_descarga_ni_fila_en_la_bitacora()
        {
            SembrarDias(0, 5);   // lunes 28 a viernes 2
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            // Viernes antes del primer intento: la fecha objetivo es el viernes (hoy), que ya está.
            foreach (var momento in new[] { Honduras(2, 3, 0), Honduras(2, 7, 30), Honduras(2, 12, 0), Honduras(2, 16, 55) })
            {
                En(momento);
                Assert.Equal(202, await Proceso().LlamadaExternaAsync());
            }

            // Viernes en la tarde y fin de semana con el lunes ya guardado: no hay publicación nueva que buscar. (Sin el
            // lunes sería trabajo pendiente y sí descargaría; eso lo cubren A3 y A4.)
            SembrarLunes();
            foreach (var momento in new[] { Honduras(2, 17, 30), Honduras(3, 18, 0), Honduras(4, 10, 0) })
            {
                En(momento);
                Assert.Equal(202, await Proceso().LlamadaExternaAsync());
            }

            Assert.Equal(0, DescargasExcel);
            Assert.Equal(0, DescargasBce);
            Assert.Empty(Bitacora());
        }

        // ── A2 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A2_llamada_externa_con_un_reintento_que_no_vence_no_descarga_ni_escribe()
        {
            SembrarHastaJueves();
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            // Cadena del jueves en la tarde por la tasa del viernes
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.Reintentada, Honduras(1, 17, 0), proximo: Honduras(1, 17, 45));

            En(Honduras(1, 17, 30));
            Assert.Equal(202, await Proceso().LlamadaExternaAsync());

            Assert.Equal(0, DescargasExcel);
            Assert.Single(Bitacora());
        }

        // ── A3 ──────────────────────────────────────────────────────────────

        private async Task<TasaCambioEjecucion> UnaLlamadaQueEjecuta()
        {
            Assert.Equal(202, await Proceso().LlamadaExternaAsync());
            var nueva = Bitacora().Last();
            Assert.Equal(Cat.Disparador.Externo, nueva.Disparador);
            Assert.Equal(1, DescargasExcel);
            return nueva;
        }

        [Fact]
        public async Task A3_llamada_externa_con_el_primer_intento_del_dia_pendiente_ejecuta_como_EXTERNO()
        {
            SembrarHastaJueves();
            _e.ResponderExcel(EscenarioTasas.SemanaBch());   // el BCH ya publicó la del viernes
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            En(Honduras(1, 17, 5));   // jueves en la tarde: objetivo viernes

            var ejecucion = await UnaLlamadaQueEjecuta();

            Assert.Equal((EscenarioTasas.Viernes, Cat.EstadoEjecucion.Exitosa), (ejecucion.FechaObjetivo, ejecucion.Estado));
        }

        [Fact]
        public async Task A3_llamada_externa_con_un_reintento_vencido_continua_la_cadena_como_EXTERNO()
        {
            SembrarHastaJueves();
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.Reintentada, Honduras(1, 17, 0), proximo: Honduras(1, 17, 15));
            En(Honduras(1, 17, 30));

            var ejecucion = await UnaLlamadaQueEjecuta();

            Assert.Equal(2, ejecucion.Intento);
            Assert.Equal(Bitacora()[0].IdEjecucion, ejecucion.IdEjecucionOrigen);
            Assert.Equal(Cat.EstadoEjecucion.Exitosa, ejecucion.Estado);
        }

        [Fact]
        public async Task A3_llamada_externa_con_el_barrido_matutino_pendiente_ejecuta_como_EXTERNO()
        {
            SembrarDias(0, 5);
            using (var db = _e.Bd.Crear())   // al viernes le falta el euro y su cadena (jueves en la tarde) ya terminó
            {
                db.TasasCambio.RemoveRange(db.TasasCambio.Where(t => t.MonedaOrigen == "EUR" && t.FechaVigencia == EscenarioTasas.Viernes));
                db.SaveChanges();
            }
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.Parcial, Honduras(1, 22, 45));
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            En(Honduras(2, 6, 30));
            Assert.Equal(202, await Proceso().LlamadaExternaAsync());   // antes de las 07:00 no toca
            Assert.Equal(0, DescargasExcel);

            En(Honduras(2, 7, 30));
            var ejecucion = await UnaLlamadaQueEjecuta();

            Assert.Equal((EscenarioTasas.Viernes, Cat.EstadoEjecucion.Exitosa), (ejecucion.FechaObjetivo, ejecucion.Estado));
        }

        [Fact]
        public async Task A3_llamada_externa_con_un_hueco_de_puesta_al_dia_ejecuta_como_EXTERNO()
        {
            // Nada en la base: el viernes (fecha objetivo de la mañana del viernes) nunca se intentó, porque nadie
            // despertó el sitio el jueves en la tarde
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            En(Honduras(2, 10, 0));

            var ejecucion = await UnaLlamadaQueEjecuta();

            Assert.Equal((EscenarioTasas.Viernes, Cat.EstadoEjecucion.Exitosa), (ejecucion.FechaObjetivo, ejecucion.Estado));
        }

        // ── A4 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A4_arranque_sin_nada_pendiente_no_descarga_ni_escribe()
        {
            SembrarDias(0, 5);   // el viernes (objetivo de la mañana del viernes) ya está
            _e.ResponderExcel(EscenarioTasas.SemanaBch());

            foreach (var momento in new[] { Honduras(2, 2, 0), Honduras(2, 9, 0), Honduras(2, 15, 0) })
            {
                En(momento);
                Assert.Null(await Proceso().Worker.ArrancarAsync());
            }

            Assert.Equal(0, DescargasExcel);
            Assert.Empty(Bitacora());
        }

        [Fact]
        public async Task A4_arranque_con_hueco_hace_la_puesta_al_dia()
        {
            // Hay hasta el miércoles; falta el viernes (la tasa de hoy) y nunca se intentó
            foreach (var (fecha, compra, venta) in EscenarioTasas.SemanaBch().Take(3))
            {
                _e.Sembrar("USD", "COMPRA", fecha, compra);
                _e.Sembrar("USD", "VENTA", fecha, venta);
            }
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            En(Honduras(2, 9, 0));

            var decision = await Proceso().Worker.ArrancarAsync();

            Assert.Contains("Puesta al día", decision!.Motivo);
            var ejecucion = Assert.Single(Bitacora());
            Assert.Equal((Cat.Disparador.Programado, EscenarioTasas.Viernes, Cat.EstadoEjecucion.Exitosa),
                (ejecucion.Disparador, ejecucion.FechaObjetivo, ejecucion.Estado));
            Assert.Equal(1, DescargasExcel);
        }

        // ── A5 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A5_el_barrido_matutino_sale_de_la_bitacora_un_reinicio_no_lo_repite()
        {
            SembrarHastaJueves();
            // El viernes no se publicó: su cadena (jueves en la tarde) terminó sin tasa
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.OmitidaSinDatos, Honduras(1, 22, 45));
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));   // sigue sin el viernes
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            En(Honduras(2, 7, 10));
            var barrido = await Proceso().Worker.ArrancarAsync();
            Assert.True(barrido!.EsBarridoMatutino);
            Assert.Equal(2, Bitacora().Count);
            Assert.Equal(Cat.EstadoEjecucion.OmitidaSinDatos, Bitacora()[^1].Estado);

            // Reinicio del proceso (IIS) a las 07:20 y vueltas posteriores: no se repite
            En(Honduras(2, 7, 20));
            Assert.Null(await Proceso().Worker.ArrancarAsync());
            En(Honduras(2, 11, 0));
            Assert.Null(await Proceso().Worker.EvaluarProgramacionAsync());

            Assert.Equal(2, Bitacora().Count);
            Assert.Equal(1, DescargasExcel);
        }

        [Fact]
        public async Task A5_el_barrido_matutino_no_corre_si_no_falta_nada()
        {
            SembrarDias(0, 5);
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            En(Honduras(2, 7, 5));

            Assert.Null(await Proceso().Worker.ArrancarAsync());
            Assert.Null(await Proceso().Worker.EvaluarProgramacionAsync());

            Assert.Empty(Bitacora());
            Assert.Equal(0, DescargasExcel);
        }

        // ── A6 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A6_cadena_terminada_no_se_reabre_hasta_el_barrido_y_MANUAL_siempre_ejecuta()
        {
            SembrarHastaJueves();
            _e.Opciones.Reintentos.MaxPorFecha = 1;                     // la cadena del viernes termina en su primer intento
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));      // la del viernes no se publica
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            En(Honduras(1, 17, 0));   // jueves en la tarde: objetivo viernes
            await Proceso().LlamadaExternaAsync();
            Assert.Equal(Cat.EstadoEjecucion.OmitidaSinDatos, Assert.Single(Bitacora()).Estado);

            // Ni llamadas externas ni reinicios abren otra cadena: jueves noche y madrugada del viernes
            foreach (var momento in new[] { Honduras(1, 17, 30), Honduras(1, 20, 0), Honduras(1, 23, 0), Honduras(2, 0, 30), Honduras(2, 3, 0), Honduras(2, 6, 30) })
            {
                En(momento);
                var proceso = Proceso();
                Assert.Null(await proceso.Worker.ArrancarAsync());
                Assert.Equal(202, await proceso.LlamadaExternaAsync());
            }
            Assert.Single(Bitacora());

            // Viernes 07:00: el barrido lo intenta una vez más (en la mañana, sin reintentos)
            En(Honduras(2, 7, 0));
            await Proceso().LlamadaExternaAsync();
            var barrido = Bitacora()[^1];
            Assert.Equal((Cat.Disparador.Externo, EscenarioTasas.Viernes, Cat.EstadoEjecucion.OmitidaSinDatos),
                (barrido.Disparador, barrido.FechaObjetivo, barrido.Estado));
            Assert.Empty(_e.Notificador.Enviadas);   // solo falta un día hábil (el jueves sí tiene): queda en el log

            En(Honduras(2, 7, 30));
            await Proceso().LlamadaExternaAsync();
            En(Honduras(2, 12, 0));
            await Proceso().LlamadaExternaAsync();
            Assert.Equal(2, Bitacora().Count);

            // "Ejecutar ahora" de la pantalla: siempre ejecuta
            var pantalla = Proceso();
            Assert.True(pantalla.Cola.Solicitar(Cat.Disparador.Manual, "ana"));
            await pantalla.Worker.ProcesarColaAsync();
            var manual = Bitacora()[^1];
            Assert.Equal((Cat.Disparador.Manual, "ana"), (manual.Disparador, manual.EjecutadoPor));
            Assert.Equal(3, Bitacora().Count);
            Assert.Equal(3, DescargasExcel);
        }

        [Fact]
        public async Task A6_el_barrido_avisa_si_siguen_faltando_dos_dias_habiles()
        {
            // Hasta el jueves; ni viernes ni lunes. Las dos cadenas ya terminaron sin tasa: la del viernes (su barrido,
            // el viernes 07:05) y la del lunes (viernes en la tarde).
            foreach (var (fecha, compra, venta) in EscenarioTasas.SemanaBch().Take(4))
            {
                _e.Sembrar("USD", "COMPRA", fecha, compra);
                _e.Sembrar("USD", "VENTA", fecha, venta);
            }
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.OmitidaSinDatos, Honduras(2, 7, 5));
            SembrarEjecucion(EscenarioTasas.Lunes, Cat.EstadoEjecucion.OmitidaSinDatos, Honduras(2, 22, 45));
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            En(Honduras(3, 7, 0));   // sábado: barrido por el lunes
            await Proceso().LlamadaExternaAsync();
            En(Honduras(4, 7, 0));   // domingo: otro barrido, mismo aviso: no se repite
            await Proceso().LlamadaExternaAsync();

            var aviso = Assert.Single(_e.Notificador.Enviadas);
            Assert.Contains("dos días hábiles", aviso.Asunto);
            Assert.Equal(4, Bitacora().Count);
        }

        // ── El día amanece con su tasa (el BCH publica la del día hábil siguiente en la tarde) ──

        [Fact]
        public async Task Cada_tarde_guarda_la_tasa_del_dia_habil_siguiente_y_el_lunes_amanece_con_la_suya()
        {
            SembrarHastaJueves();
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            // Jueves 17:05: el BCH ya publicó la del viernes
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            En(Honduras(1, 17, 5));
            await Proceso().LlamadaExternaAsync();

            // Viernes 17:05: ya publicó la del lunes. Con la regla anterior (objetivo = hoy) el viernes ya estaba guardado
            // desde el jueves, el job no corría y el lunes amanecía con la tasa del viernes.
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Append((EscenarioTasas.Lunes, 26.8901m, 27.0246m)));
            En(Honduras(2, 17, 5));
            await Proceso().LlamadaExternaAsync();

            Assert.Equal(new[] { (EscenarioTasas.Viernes, Cat.EstadoEjecucion.Exitosa), (EscenarioTasas.Lunes, Cat.EstadoEjecucion.Exitosa) },
                Bitacora().Select(b => (b.FechaObjetivo, b.Estado)));
            using (var db = _e.Bd.Crear())
            {
                var lunes = db.TasasCambio.AsNoTracking()
                    .Where(t => t.FechaVigencia == EscenarioTasas.Lunes && t.Estado == Cat.EstadoTasa.Vigente).ToList();
                Assert.Equal(4, lunes.Count);   // USD y EUR, compra y venta
                Assert.Equal(26.8901m, lunes.Single(t => t.MonedaOrigen == "USD" && t.TipoTasa == Cat.TipoTasa.Compra).Tasa);
            }

            // Fin de semana y lunes en la mañana: no falta nada, no se descarga otra vez
            foreach (var momento in new[] { Honduras(3, 7, 0), Honduras(4, 7, 0), Honduras(5, 0, 30), Honduras(5, 7, 5), Honduras(5, 12, 0) })
            {
                En(momento);
                Assert.Equal(202, await Proceso().LlamadaExternaAsync());
            }
            Assert.Equal(2, Bitacora().Count);
            Assert.Equal(2, DescargasExcel);
        }

        // ── A7 ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task A7_dia_completo_con_llamada_externa_cada_30_minutos_y_un_reinicio_solo_ejecuta_lo_que_toca()
        {
            // El jueves completo: el BCH publica la tasa del viernes a las 18:10 del jueves
            SembrarHastaJueves();
            var publicacion = Honduras(1, 18, 10);
            var sinViernes = ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch().Take(4));
            var conViernes = ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch());
            _e.Reloj = new RelojFijo(Honduras(1, 0, 0));
            var reloj = _e.Reloj;
            _e.Http.Responder(EscenarioTasas.UrlExcel, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(reloj.GetUtcNow() >= publicacion ? conViernes : sinViernes)
            });
            _e.ResponderBce(EscenarioTasas.SemanaBce());

            var proceso = Proceso();
            await proceso.Worker.ArrancarAsync();
            var filasPorHora = new Dictionary<string, int>();

            for (var paso = 0; paso < 48; paso++)   // 00:00, 00:30 … 23:30
            {
                var hora = TimeOnly.FromDateTime(reloj.GetUtcNow().ToOffset(TimeSpan.FromHours(-6)).DateTime);
                if (hora == new TimeOnly(15, 30))
                {
                    proceso = Proceso();               // reciclado de IIS a media tarde
                    await proceso.Worker.ArrancarAsync();
                }

                var antes = Bitacora().Count;
                Assert.Equal(202, await proceso.LlamadaExternaAsync());
                if (Bitacora().Count > antes) filasPorHora[hora.ToString("HH:mm")] = Bitacora().Count - antes;

                reloj.Avanzar(TimeSpan.FromMinutes(30));
            }

            // Solo el primer intento (17:00), los reintentos según 15/30/60 min y el éxito tras la publicación de las 18:10
            Assert.Equal(new[] { "17:00", "17:30", "18:00", "19:00" }, filasPorHora.Keys);
            Assert.All(filasPorHora.Values, v => Assert.Equal(1, v));

            var bitacora = Bitacora();
            Assert.Equal(4, bitacora.Count);
            Assert.Equal(new[] { Cat.EstadoEjecucion.Reintentada, Cat.EstadoEjecucion.Reintentada, Cat.EstadoEjecucion.Reintentada, Cat.EstadoEjecucion.Exitosa },
                bitacora.Select(b => b.Estado));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, bitacora.Select(b => b.Intento));
            Assert.All(bitacora, b => Assert.Equal(Cat.Disparador.Externo, b.Disparador));
            Assert.All(bitacora.Skip(1), b => Assert.Equal(bitacora[0].IdEjecucion, b.IdEjecucionOrigen));
            Assert.Equal(new DateTime?[] { Honduras(1, 17, 15).UtcDateTime, Honduras(1, 18, 0).UtcDateTime, Honduras(1, 19, 0).UtcDateTime, null },
                bitacora.Select(b => b.ProximoIntentoUtc));

            Assert.Equal(4, DescargasExcel);   // una por ejecución, ninguna después del éxito
            Assert.Equal(1, DescargasBce);     // solo cuando entró el dólar del viernes y hubo que derivar el euro
            using var db = _e.Bd.Crear();
            Assert.Equal(4, db.TasasCambio.Count(t => t.FechaVigencia == EscenarioTasas.Viernes && t.Estado == Cat.EstadoTasa.Vigente));
            Assert.Empty(_e.Notificador.Enviadas);
        }
    }
}
