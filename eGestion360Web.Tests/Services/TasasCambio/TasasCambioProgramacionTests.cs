using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
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
    /// <summary>Planificador, worker (con un sincronizador falso) y disparador externo (endpoint).</summary>
    public class TasasCambioProgramacionTests : IDisposable
    {
        private readonly EscenarioTasas _e = new();

        public void Dispose() => _e.Dispose();

        private static RelojFijo Honduras(int dia, int hora, int minuto = 0) =>
            new(new DateTimeOffset(2026, 10, dia, hora, minuto, 0, TimeSpan.FromHours(-6)));

        private static DateTime Utc(int dia, int hora, int minuto = 0) => Honduras(dia, hora, minuto).GetUtcNow().UtcDateTime;

        private async Task<DecisionProgramada?> Decidir(RelojFijo reloj)
        {
            using var db = _e.Bd.Crear();
            return await new TasasCambioPlanificador(db, Options.Create(_e.Opciones), reloj).DecidirAsync();
        }

        private void SembrarEjecucion(DateOnly fechaObjetivo, string estado, DateTime inicioUtc, DateTime? proximoUtc = null)
        {
            using var db = _e.Bd.Crear();
            db.TasasCambioEjecuciones.Add(new TasaCambioEjecucion
            {
                Disparador = Cat.Disparador.Programado, FechaObjetivo = fechaObjetivo, Estado = estado, ProximoIntentoUtc = proximoUtc,
                Servidor = "x", EjecutadoPor = "job", InicioUtc = inicioUtc
            });
            db.SaveChanges();
        }

        /// <summary>Las cuatro tasas oficiales (USD y EUR, compra y venta) de una fecha.</summary>
        private void Completar(DateOnly fecha)
        {
            foreach (var moneda in new[] { "USD", "EUR" })
                foreach (var tipo in new[] { "COMPRA", "VENTA" })
                    _e.Sembrar(moneda, tipo, fecha, moneda == "USD" ? 27m : 29m);
        }

        // ── Calendario: fecha objetivo y límite de fechas futuras ──────────

        [Theory]
        [InlineData(1, 10, 0, 1)]    // jueves en la mañana: hoy
        [InlineData(1, 16, 59, 1)]   // justo antes del primer intento: hoy
        [InlineData(1, 17, 0, 2)]    // jueves desde las 17:00: el viernes
        [InlineData(2, 18, 0, 5)]    // viernes en la tarde: el lunes
        [InlineData(3, 10, 0, 5)]    // sábado: el lunes
        [InlineData(4, 23, 0, 5)]    // domingo: el lunes
        [InlineData(5, 6, 0, 5)]     // lunes de madrugada: hoy
        public void Fecha_objetivo_segun_el_dia_y_la_hora(int dia, int hora, int minuto, int diaObjetivo)
        {
            var calendario = new CalendarioTasasCambio(Honduras(dia, hora, minuto), _e.Opciones);
            Assert.Equal(new DateOnly(2026, 10, diaObjetivo), calendario.FechaObjetivo());
        }

        [Fact]
        public void El_limite_de_fechas_futuras_es_dos_dias_habiles_adelante()
        {
            Assert.Equal(new DateOnly(2026, 10, 5), CalendarioTasasCambio.LimiteFechaFutura(new DateOnly(2026, 10, 1)));   // jueves → lunes
            Assert.Equal(new DateOnly(2026, 10, 6), CalendarioTasasCambio.LimiteFechaFutura(new DateOnly(2026, 10, 2)));   // viernes → martes
            Assert.Equal(new DateOnly(2026, 10, 6), CalendarioTasasCambio.LimiteFechaFutura(new DateOnly(2026, 10, 3)));   // sábado → martes
        }

        // ── Planificador ────────────────────────────────────────────────────

        [Fact]
        public async Task Deshabilitado_nunca_toca()
        {
            _e.Opciones.Habilitado = false;
            Assert.Null(await Decidir(Honduras(2, 17, 5)));
        }

        [Fact]
        public async Task Dia_habil_despues_de_las_17_sin_la_tasa_del_dia_habil_siguiente_toca_el_primer_intento()
        {
            var d = await Decidir(Honduras(2, 17, 5));   // viernes en la tarde: objetivo lunes

            Assert.NotNull(d);
            Assert.Equal(Cat.Disparador.Programado, d!.Disparador);
            Assert.Contains("2026-10-05", d.Motivo);
            Assert.False(d.EsBarridoMatutino);
        }

        [Fact]
        public async Task Sin_nada_que_falte_no_toca_nunca()
        {
            Completar(EscenarioTasas.Viernes);
            Completar(EscenarioTasas.Lunes);

            Assert.Null(await Decidir(Honduras(2, 3, 0)));    // madrugada: objetivo viernes (hoy)
            Assert.Null(await Decidir(Honduras(2, 7, 5)));    // barrido: no falta nada, no corre
            Assert.Null(await Decidir(Honduras(2, 16, 55)));
            Assert.Null(await Decidir(Honduras(2, 17, 5)));   // objetivo lunes, ya está
            Assert.Null(await Decidir(Honduras(3, 18, 0)));   // sábado: objetivo lunes
        }

        [Fact]
        public async Task Despues_de_UltimoIntento_no_se_hace_el_primer_intento_del_dia()
        {
            Completar(EscenarioTasas.Jueves);
            Assert.Null(await Decidir(Honduras(2, 23, 40)));
        }

        [Fact]
        public async Task Fuera_de_la_tarde_sin_ninguna_ejecucion_para_la_fecha_objetivo_es_un_hueco_de_puesta_al_dia()
        {
            // Viernes 10:00: el viernes (hoy) no tiene tasa y nadie la buscó el jueves en la tarde (el proceso estuvo caído)
            var d = await Decidir(Honduras(2, 10, 0));
            Assert.Equal(Cat.Disparador.Programado, d!.Disparador);
            Assert.Contains("Puesta al día", d.Motivo);
            Assert.True(d.EsBarridoMatutino);   // hace las veces del barrido de hoy

            Assert.False((await Decidir(Honduras(2, 3, 0)))!.EsBarridoMatutino);   // de madrugada también, pero no es el barrido
        }

        [Fact]
        public async Task Con_una_ejecucion_para_la_fecha_objetivo_no_toca_el_primer_intento()
        {
            SembrarEjecucion(EscenarioTasas.Lunes, Cat.EstadoEjecucion.OmitidaSinDatos, Utc(2, 17, 0));
            Assert.Null(await Decidir(Honduras(2, 17, 30)));
            Assert.Null(await Decidir(Honduras(2, 23, 0)));
        }

        [Fact]
        public async Task Reintento_solo_cuando_vence_su_ProximoIntentoUtc()
        {
            SembrarEjecucion(EscenarioTasas.Lunes, Cat.EstadoEjecucion.Reintentada, Utc(2, 17, 0), Utc(2, 17, 15));

            Assert.Null(await Decidir(Honduras(2, 17, 10)));   // todavía no vence: no se abre nada más
            Assert.Equal(Cat.Disparador.Reintento, (await Decidir(Honduras(2, 17, 15)))!.Disparador);
            Assert.Equal(Cat.Disparador.Reintento, (await Decidir(Honduras(2, 17, 20)))!.Disparador);
        }

        [Fact]
        public async Task La_cadena_de_otra_fecha_objetivo_no_se_persigue()
        {
            // Cadena de la mañana del viernes (un error pasajero de la fuente se reintenta a cualquier hora)
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.Reintentada, Utc(2, 8, 0), Utc(2, 9, 0));

            // Viernes 10:00: la fecha objetivo todavía es el viernes, así que su cadena sigue
            Assert.Equal(Cat.Disparador.Reintento, (await Decidir(Honduras(2, 10, 0)))!.Disparador);
            // Viernes 18:00: el objetivo ya es el lunes (sin ejecuciones): primer intento; el viernes entra en su puesta al día
            var d = await Decidir(Honduras(2, 18, 0));
            Assert.Equal(Cat.Disparador.Programado, d!.Disparador);
            Assert.Contains("Primer intento", d.Motivo);
        }

        [Fact]
        public async Task Cadena_terminada_solo_la_reintenta_el_barrido_matutino_una_vez()
        {
            // La cadena del jueves en la tarde (objetivo: viernes) terminó a las 22:45 sin tasa
            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.OmitidaSinDatos, Utc(1, 22, 45));

            Assert.Null(await Decidir(Honduras(1, 23, 0)));
            Assert.Null(await Decidir(Honduras(2, 3, 0)));     // madrugada del viernes: espera al barrido
            Assert.Null(await Decidir(Honduras(2, 6, 55)));
            var barrido = await Decidir(Honduras(2, 7, 5));
            Assert.True(barrido!.EsBarridoMatutino);
            Assert.Equal(Cat.Disparador.Programado, barrido.Disparador);

            SembrarEjecucion(EscenarioTasas.Viernes, Cat.EstadoEjecucion.OmitidaSinDatos, Utc(2, 7, 5));   // el barrido ya corrió
            Assert.Null(await Decidir(Honduras(2, 7, 30)));
            Assert.Null(await Decidir(Honduras(2, 12, 0)));
        }

        // ── Worker con un sincronizador falso ───────────────────────────────

        private sealed class SyncFalso : ITasaCambioSyncService
        {
            public List<(string Disparador, string Usuario)> Llamadas { get; } = new();

            public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, CancellationToken ct = default)
            {
                Llamadas.Add((disparador, ejecutadoPor));
                return Task.FromResult(new ResultadoEjecucionTasas(true, 1, Cat.EstadoEjecucion.Exitosa, null, 0, 0, 0, 0, 0, ""));
            }

            public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
                EjecutarAsync(disparador, ejecutadoPor, ct);

            public Task<ResultadoOperacionTasa> AprobarAsync(int idTasaCambio, string usuario, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ResultadoOperacionTasa> RechazarAsync(int idTasaCambio, string usuario, string motivo, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ResultadoOperacionTasa> RegistrarManualAsync(int idEmpresa, string monedaOrigen, string tipoTasa, DateOnly fecha, decimal tasa, string usuario, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private (TasasCambioBackgroundService Worker, SyncFalso Sync, ColaEjecucionTasasCambio Cola) Worker(RelojFijo reloj)
        {
            var sync = new SyncFalso();
            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => _e.Bd.Crear());
            servicios.AddSingleton(Options.Create(_e.Opciones));
            servicios.AddSingleton<TimeProvider>(reloj);
            servicios.AddScoped<TasasCambioPlanificador>();
            servicios.AddSingleton<ITasaCambioSyncService>(sync);
            var proveedor = servicios.BuildServiceProvider();

            var cola = new ColaEjecucionTasasCambio();
            var worker = new TasasCambioBackgroundService(proveedor.GetRequiredService<IServiceScopeFactory>(), Options.Create(_e.Opciones),
                cola, reloj, NullLogger<TasasCambioBackgroundService>.Instance);
            return (worker, sync, cola);
        }

        [Fact]
        public async Task Worker_deshabilitado_termina_enseguida_sin_hacer_nada()
        {
            _e.Opciones.Habilitado = false;
            var (worker, sync, _) = Worker(Honduras(2, 17, 5));

            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
            Assert.Empty(sync.Llamadas);
            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Worker_ejecuta_lo_que_decide_el_planificador_con_su_disparador_o_con_el_que_se_le_pide()
        {
            var reloj = Honduras(2, 17, 5);
            var (worker, sync, _) = Worker(reloj);

            Assert.NotNull(await worker.EvaluarProgramacionAsync());
            Assert.NotNull(await worker.EvaluarProgramacionAsync(Cat.Disparador.Externo));

            Assert.Equal(new[] { (Cat.Disparador.Programado, "job"), (Cat.Disparador.Externo, "job") }, sync.Llamadas);
        }

        [Fact]
        public async Task Solicitud_forzada_se_ejecuta_siempre_y_la_de_evaluar_solo_si_toca()
        {
            Completar(EscenarioTasas.Viernes);
            Completar(EscenarioTasas.Lunes);
            var (worker, sync, cola) = Worker(Honduras(2, 18, 0));   // no falta nada (objetivo: lunes)

            cola.Solicitar(SolicitudEjecucionTasas.Evaluar(Cat.Disparador.Externo));
            cola.Solicitar(Cat.Disparador.Manual, "ana");
            cola.Solicitar(new SolicitudEjecucionTasas(Cat.Disparador.Manual, "ana", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)));

            Assert.Equal(3, await worker.ProcesarColaAsync());
            Assert.Equal(new[] { (Cat.Disparador.Manual, "ana"), (Cat.Disparador.Manual, "ana") }, sync.Llamadas);
        }

        // ── Disparador externo ──────────────────────────────────────────────

        private const string TokenDePrueba = "token-de-prueba-solo-para-tests";

        private (DisparadorExternoTasasCambio Disparador, ColaEjecucionTasasCambio Cola) Disparador(RelojFijo? reloj = null, string? token = TokenDePrueba)
        {
            _e.Opciones.Disparador.Token = token;
            var cola = new ColaEjecucionTasasCambio();
            return (new DisparadorExternoTasasCambio(Options.Create(_e.Opciones), cola, reloj ?? _e.Reloj,
                NullLogger<DisparadorExternoTasasCambio>.Instance), cola);
        }

        private static int Codigo(DisparadorExternoTasasCambio disparador, string? token)
        {
            var http = new DefaultHttpContext();
            if (token != null) http.Request.Headers[TasasCambioEndpoints.EncabezadoAutenticacion] = token;
            var resultado = TasasCambioEndpoints.Atender(http.Request, disparador);
            return ((IStatusCodeHttpResult)resultado).StatusCode!.Value;
        }

        [Fact]
        public void Sin_token_configurado_o_con_el_job_deshabilitado_responde_404()
        {
            Assert.Equal(404, Codigo(Disparador(token: null).Disparador, TokenDePrueba));
            Assert.Equal(404, Codigo(Disparador(token: "").Disparador, ""));

            _e.Opciones.Habilitado = false;
            Assert.Equal(404, Codigo(Disparador().Disparador, TokenDePrueba));
        }

        [Fact]
        public void Token_ausente_o_incorrecto_responde_401_y_no_encola()
        {
            var (disparador, cola) = Disparador();

            Assert.Equal(401, Codigo(disparador, null));
            Assert.Equal(401, Codigo(disparador, "otro-token"));
            Assert.Equal(401, Codigo(disparador, TokenDePrueba + "x"));
            Assert.Equal(0, cola.Pendientes);
        }

        [Fact]
        public async Task Token_correcto_responde_202_y_encola_una_evaluacion_EXTERNO_no_forzada_y_a_menos_de_60_s_la_ignora()
        {
            var reloj = new RelojFijo(EscenarioTasas.ViernesTarde);
            var (disparador, cola) = Disparador(reloj);

            Assert.Equal(202, Codigo(disparador, TokenDePrueba));
            Assert.Equal(1, cola.Pendientes);
            Assert.True(cola.Lector.TryRead(out var solicitud));
            Assert.Equal((Cat.Disparador.Externo, "job"), (solicitud!.Disparador, solicitud.EjecutadoPor));
            Assert.False(solicitud.Forzar);   // solo despierta y hace evaluar la programación

            reloj.Avanzar(TimeSpan.FromSeconds(30));
            Assert.Equal(202, Codigo(disparador, TokenDePrueba));   // responde igual, pero no encola
            Assert.Equal(0, cola.Pendientes);
            Assert.Equal(ResultadoDisparoExterno.Ignorado, disparador.Atender(TokenDePrueba));

            reloj.Avanzar(TimeSpan.FromSeconds(31));
            Assert.Equal(ResultadoDisparoExterno.Aceptado, disparador.Atender(TokenDePrueba));
            Assert.Equal(1, cola.Pendientes);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task El_endpoint_esta_mapeado_como_POST_y_acepta_un_cuerpo_vacio_con_Content_Length_0()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Pruebas" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TasasCambio:Habilitado"] = "true",
                ["TasasCambio:Disparador:Token"] = TokenDePrueba
            });
            builder.Services.Configure<TasasCambioOptions>(builder.Configuration.GetSection(TasasCambioOptions.Seccion));
            builder.Services.AddSingleton<TimeProvider>(new RelojFijo(EscenarioTasas.ViernesTarde));
            builder.Services.AddSingleton<ColaEjecucionTasasCambio>();
            builder.Services.AddSingleton<DisparadorExternoTasasCambio>();
            await using var app = builder.Build();
            app.MapTasasCambioEndpoints();

            var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
                .Single(e => e.RoutePattern.RawText == TasasCambioEndpoints.Ruta);
            Assert.Equal(new[] { "POST" }, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);

            async Task<int> Llamar(string? token)
            {
                var http = new DefaultHttpContext { RequestServices = app.Services };
                http.Request.Method = "POST";
                http.Request.Path = TasasCambioEndpoints.Ruta;
                http.Request.ContentLength = 0;
                http.Request.Body = new MemoryStream();
                if (token != null) http.Request.Headers[TasasCambioEndpoints.EncabezadoAutenticacion] = token;
                http.Response.Body = new MemoryStream();
                await endpoint.RequestDelegate!(http);
                Assert.Equal(0, http.Response.Body.Length);   // sin datos en la respuesta
                return http.Response.StatusCode;
            }

            Assert.Equal(401, await Llamar(null));
            Assert.Equal(401, await Llamar("incorrecto"));
            Assert.Equal(202, await Llamar(TokenDePrueba));
            Assert.Equal(1, app.Services.GetRequiredService<ColaEjecucionTasasCambio>().Pendientes);
        }
    }
}
