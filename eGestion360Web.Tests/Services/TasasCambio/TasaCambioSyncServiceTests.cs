using System.Net;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// El orquestador contra SQLite en memoria y fuentes HTTP grabadas. SQLite trata los NULL como distintos en los
    /// índices únicos: el caso "dos tasas oficiales VIGENTE de la misma clave" (id_empresa nulo) solo lo impide el índice
    /// filtrado UX_tasas_cambio_vigente de SQL Server; estas pruebas comprueban que el servicio nunca lo intente.
    /// </summary>
    public class TasaCambioSyncServiceTests : IDisposable
    {
        private readonly EscenarioTasas _e = new();

        public TasaCambioSyncServiceTests()
        {
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
        }

        public void Dispose() => _e.Dispose();

        private Task<ResultadoEjecucionTasas> Ejecutar(string disparador = Cat.Disparador.Programado) =>
            _e.Servicio().EjecutarAsync(disparador, TasaCambioSyncService.UsuarioJob);

        private List<TasaCambio> Tasas()
        {
            using var db = _e.Bd.Crear();
            return db.TasasCambio.AsNoTracking().OrderBy(t => t.IdTasaCambio).ToList();
        }

        private TasaCambioEjecucion Ejecucion(long id)
        {
            using var db = _e.Bd.Crear();
            return db.TasasCambioEjecuciones.AsNoTracking().Include(e => e.Detalles).Single(e => e.IdEjecucion == id);
        }

        private static TasaCambio Una(IEnumerable<TasaCambio> tasas, string moneda, string tipo, DateOnly fecha, string estado = Cat.EstadoTasa.Vigente) =>
            tasas.Single(t => t.MonedaOrigen == moneda && t.TipoTasa == tipo && t.FechaVigencia == fecha && t.Estado == estado);

        // ── Ejecución normal ────────────────────────────────────────────────

        [Fact]
        public async Task Primera_ejecucion_guarda_el_dolar_del_Excel_y_el_euro_derivado_vigentes()
        {
            var r = await Ejecutar();

            Assert.True(r.Ejecutada);
            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(EscenarioTasas.Viernes, r.FechaObjetivo);
            // Sin tasas previas la puesta al día va de hoy - 3 (29-sep) a hoy: 4 días × (USD + EUR) × (compra + venta)
            Assert.Equal(16, r.Leidos);
            Assert.Equal(16, r.Insertados);

            var tasas = Tasas();
            Assert.Equal(16, tasas.Count);
            Assert.All(tasas, t => Assert.Null(t.IdEmpresa));

            var usdCompra = Una(tasas, "USD", "COMPRA", EscenarioTasas.Viernes);
            Assert.Equal(26.8925m, usdCompra.Tasa);
            Assert.Equal(Cat.Fuente.BchXlsx, usdCompra.Fuente);
            Assert.False(usdCompra.EsDerivada);
            Assert.Equal(1, usdCompra.Version);
            Assert.Equal(r.IdEjecucion, usdCompra.IdEjecucion);
            Assert.Equal(27.0270m, Una(tasas, "USD", "VENTA", EscenarioTasas.Viernes).Tasa);

            var eurVenta = Una(tasas, "EUR", "VENTA", EscenarioTasas.Viernes);
            Assert.Equal(Math.Round(1.0876m * 27.0270m, 4), eurVenta.Tasa);
            Assert.True(eurVenta.EsDerivada);
            Assert.Equal(Cat.Fuente.Derivada, eurVenta.Fuente);
            Assert.Contains("EUR/USD BCE 2026-10-02 = 1.0876", eurVenta.ReferenciaFuente);
            Assert.Contains("USD/HNL VENTA BCH_XLSX 2026-10-02 = 27.027", eurVenta.ReferenciaFuente);

            var ejecucion = Ejecucion(r.IdEjecucion!.Value);
            Assert.Equal(Cat.Fuente.BchXlsx, ejecucion.Fuente);
            Assert.Equal(EscenarioTasas.UrlExcel, ejecucion.Endpoint);
            Assert.Equal((short)200, ejecucion.HttpStatus);
            Assert.Equal(64, ejecucion.HashContenido!.Length);
            Assert.Equal(16, ejecucion.Detalles.Count);
            Assert.All(ejecucion.Detalles, d => Assert.Equal(Cat.ResultadoDetalle.Insertada, d.Resultado));
            Assert.Equal(Cat.Disparador.Programado, ejecucion.Disparador);
            Assert.Equal("job", ejecucion.EjecutadoPor);
            Assert.Empty(_e.Notificador.Enviadas);
        }

        [Fact]
        public async Task Las_fechas_de_auditoria_salen_del_reloj_en_UTC_y_no_quedan_en_0001()
        {
            var ahora = EscenarioTasas.ViernesTarde.UtcDateTime;   // 2026-10-03 00:00 UTC

            var r = await Ejecutar();
            var manual = await _e.Servicio().RegistrarManualAsync(DatosBase.EmpresaA, "USD", "VENTA", EscenarioTasas.Viernes, 27.10m, "ana");

            var ejecucion = Ejecucion(r.IdEjecucion!.Value);
            Assert.Equal(ahora, ejecucion.InicioUtc);
            Assert.Equal(ahora, ejecucion.FinUtc);
            Assert.All(Tasas(), t =>
            {
                Assert.Equal(ahora, t.FechaCreacion);
                Assert.Equal(ahora, t.FechaHoraObtencion);
                Assert.NotEqual(default(DateTime), t.FechaCreacion);
            });
            Assert.True(manual.Exito);
        }

        [Fact]
        public async Task Deshabilitado_no_hace_nada()
        {
            _e.Opciones.Habilitado = false;

            var r = await Ejecutar();

            Assert.False(r.Ejecutada);
            Assert.Null(r.IdEjecucion);
            Assert.Empty(_e.Http.Peticiones);
            using var db = _e.Bd.Crear();
            Assert.Empty(db.TasasCambioEjecuciones);
            Assert.Empty(db.TasasCambio);
        }

        [Fact]
        public async Task Con_otra_ejecucion_en_curso_no_hace_nada()
        {
            await using var otra = await _e.Bloqueo.IntentarTomarAsync(TasaCambioSyncService.RecursoBloqueo);
            Assert.NotNull(otra);

            var r = await Ejecutar();

            Assert.False(r.Ejecutada);
            Assert.Empty(_e.Http.Peticiones);
            using var db = _e.Bd.Crear();
            Assert.Empty(db.TasasCambioEjecuciones);
        }

        // ── Duplicados y versiones ──────────────────────────────────────────

        [Fact]
        public async Task Mismo_valor_es_DUPLICADA_no_escribe_y_no_vuelve_a_pedir_el_BCE()
        {
            await Ejecutar();
            var llamadasBce = _e.Http.Llamadas(EscenarioTasas.UrlBce90) + _e.Http.Llamadas(EscenarioTasas.UrlBceDiaria);
            _e.ResponderExcel(EscenarioTasas.SemanaBch(), marca: "otra descarga");   // mismo dato, otro archivo (otro hash)

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(0, r.Insertados);
            Assert.Equal(8, r.Duplicados);   // el dólar; el euro ya estaba y su dólar no cambió: no se re-deriva
            Assert.Equal(16, Tasas().Count);
            Assert.All(Ejecucion(r.IdEjecucion!.Value).Detalles, d => Assert.Equal(Cat.ResultadoDetalle.Duplicada, d.Resultado));
            Assert.Equal(llamadasBce, _e.Http.Llamadas(EscenarioTasas.UrlBce90) + _e.Http.Llamadas(EscenarioTasas.UrlBceDiaria));
        }

        [Fact]
        public async Task Misma_respuesta_que_la_ultima_exitosa_queda_OMITIDA_DUPLICADA_sin_reprocesar()
        {
            await Ejecutar();
            var llamadasBce = _e.Http.Llamadas(EscenarioTasas.UrlBce90);

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.OmitidaDuplicada, r.Estado);
            Assert.Equal(0, r.Leidos);
            Assert.Equal(llamadasBce, _e.Http.Llamadas(EscenarioTasas.UrlBce90));   // no se volvió a pedir el BCE
            Assert.Contains("no cambió", r.Mensaje);
        }

        [Fact]
        public async Task Valor_distinto_reemplaza_la_vigente_y_crea_la_version_2()
        {
            await Ejecutar();
            var semana = EscenarioTasas.SemanaBch();
            semana[^1] = (EscenarioTasas.Viernes, 26.8925m, 27.0300m);   // el BCH corrige la venta del viernes
            _e.ResponderExcel(semana);

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(2, r.Reemplazados);   // USD venta y EUR venta (derivado de ella)
            var tasas = Tasas();
            var anterior = Una(tasas, "USD", "VENTA", EscenarioTasas.Viernes, Cat.EstadoTasa.Reemplazada);
            var nueva = Una(tasas, "USD", "VENTA", EscenarioTasas.Viernes);
            Assert.Equal(27.0270m, anterior.Tasa);
            Assert.Equal(27.0300m, nueva.Tasa);
            Assert.Equal(2, nueva.Version);
            Assert.Equal(anterior.IdTasaCambio, nueva.IdTasaAnterior);
            Assert.Equal("job", anterior.ModificadoPor);
            Assert.Equal(Math.Round(1.0876m * 27.0300m, 4), Una(tasas, "EUR", "VENTA", EscenarioTasas.Viernes).Tasa);
            // Una sola VIGENTE por clave (en SQLite el índice filtrado no lo garantiza: lo garantiza el servicio)
            Assert.Single(tasas, t => t.MonedaOrigen == "USD" && t.TipoTasa == "VENTA" && t.FechaVigencia == EscenarioTasas.Viernes
                                      && t.Estado == Cat.EstadoTasa.Vigente);
        }

        // ── Validación y revisión ───────────────────────────────────────────

        private static List<(DateOnly, decimal, decimal)> SemanaConViernesMalo(decimal compra, decimal venta)
        {
            var semana = EscenarioTasas.SemanaBch();
            semana[^1] = (EscenarioTasas.Viernes, compra, venta);
            return semana;
        }

        [Fact]
        public async Task Fuera_de_rango_se_guarda_EN_REVISION_no_vigente_y_alerta_una_sola_vez()
        {
            _e.ResponderExcel(SemanaConViernesMalo(2.68925m, 2.70270m));   // punto decimal corrido

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.OmitidaInvalida, r.Estado);   // nada vigente para el viernes
            Assert.Equal(12, r.Insertados);                                // lunes a jueves (desde el 29) × 4
            Assert.Equal(2, r.Invalidos);
            var tasas = Tasas();
            var revision = Una(tasas, "USD", "COMPRA", EscenarioTasas.Viernes, Cat.EstadoTasa.EnRevision);
            Assert.Equal(2.68925m, revision.Tasa);
            Assert.DoesNotContain(tasas, t => t.FechaVigencia == EscenarioTasas.Viernes && t.Estado == Cat.EstadoTasa.Vigente);
            Assert.DoesNotContain(tasas, t => t.MonedaOrigen == "EUR" && t.FechaVigencia == EscenarioTasas.Viernes);   // no se deriva de un dólar en revisión

            var alerta = Assert.Single(_e.Notificador.Enviadas);
            Assert.Contains("revisión", alerta.Asunto);
            Assert.Contains("Fuera del rango", alerta.Html);
            Assert.True(Ejecucion(r.IdEjecucion!.Value).Notificado);

            // La siguiente lectura trae lo mismo: no se vuelve a insertar ni a alertar
            _e.ResponderExcel(SemanaConViernesMalo(2.68925m, 2.70270m), marca: "otra");
            var r2 = await Ejecutar();
            Assert.All(Ejecucion(r2.IdEjecucion!.Value).Detalles.Where(d => d.FechaVigencia == EscenarioTasas.Viernes && d.MonedaOrigen == "USD"),
                d => Assert.Equal(Cat.ResultadoDetalle.Duplicada, d.Resultado));
            Assert.Single(_e.Notificador.Enviadas);
            Assert.Equal(2, Tasas().Count(t => t.Estado == Cat.EstadoTasa.EnRevision));
        }

        [Fact]
        public async Task Compra_mayor_que_venta_es_INVALIDA_y_no_se_guarda()
        {
            _e.ResponderExcel(SemanaConViernesMalo(27.0270m, 26.8925m));   // columnas invertidas

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.OmitidaInvalida, r.Estado);
            Assert.DoesNotContain(Tasas(), t => t.FechaVigencia == EscenarioTasas.Viernes);
            var detalles = Ejecucion(r.IdEjecucion!.Value).Detalles.Where(d => d.FechaVigencia == EscenarioTasas.Viernes && d.MonedaOrigen == "USD");
            Assert.All(detalles, d => Assert.Equal(Cat.ResultadoDetalle.Invalida, d.Resultado));
            Assert.Empty(_e.Notificador.Enviadas);   // solo FALLIDA y EN_REVISION alertan
        }

        [Fact]
        public async Task Aprobar_pasa_EN_REVISION_a_VIGENTE_y_reemplaza_a_la_vigente()
        {
            await Ejecutar();
            _e.ResponderExcel(SemanaConViernesMalo(28.50m, 28.65m));   // salto de +6 %: a revisión
            await Ejecutar();
            var pendiente = Una(Tasas(), "USD", "COMPRA", EscenarioTasas.Viernes, Cat.EstadoTasa.EnRevision);
            var vigente = Una(Tasas(), "USD", "COMPRA", EscenarioTasas.Viernes);
            Assert.Equal(vigente.IdTasaCambio, pendiente.IdTasaAnterior);
            Assert.Equal(2, pendiente.Version);

            var r = await _e.Servicio().AprobarAsync(pendiente.IdTasaCambio, "ana");

            Assert.True(r.Exito, r.Mensaje);
            var tasas = Tasas();
            Assert.Equal(Cat.EstadoTasa.Reemplazada, tasas.Single(t => t.IdTasaCambio == vigente.IdTasaCambio).Estado);
            var aprobada = tasas.Single(t => t.IdTasaCambio == pendiente.IdTasaCambio);
            Assert.Equal(Cat.EstadoTasa.Vigente, aprobada.Estado);
            Assert.Equal("ana", aprobada.ModificadoPor);
            Assert.Equal(28.50m, (await _e.Consultas(_e.Bd.Crear()).ObtenerVigenteAsync("USD", "COMPRA", EscenarioTasas.Viernes))!.Tasa);

            using var db = _e.Bd.Crear();
            Assert.Contains(db.TasasCambioEjecucionesDetalle.AsNoTracking(), d => d.IdTasaCambio == pendiente.IdTasaCambio
                && d.Motivo != null && d.Motivo.StartsWith("Aprobada por ana"));

            // Ya no está en revisión: no se puede aprobar otra vez
            Assert.False((await _e.Servicio().AprobarAsync(pendiente.IdTasaCambio, "ana")).Exito);
        }

        [Fact]
        public async Task Rechazar_pasa_a_RECHAZADA_con_el_motivo_en_la_bitacora_y_no_vuelve()
        {
            await Ejecutar();
            _e.ResponderExcel(SemanaConViernesMalo(28.50m, 28.65m));
            await Ejecutar();
            var pendiente = Una(Tasas(), "USD", "VENTA", EscenarioTasas.Viernes, Cat.EstadoTasa.EnRevision);

            Assert.False((await _e.Servicio().RechazarAsync(pendiente.IdTasaCambio, "ana", "  ")).Exito);   // sin motivo
            var r = await _e.Servicio().RechazarAsync(pendiente.IdTasaCambio, "ana", "Error de digitación del BCH");

            Assert.True(r.Exito);
            Assert.Equal(Cat.EstadoTasa.Rechazada, Tasas().Single(t => t.IdTasaCambio == pendiente.IdTasaCambio).Estado);
            Assert.Equal(27.0270m, Una(Tasas(), "USD", "VENTA", EscenarioTasas.Viernes).Tasa);   // la vigente sigue
            using (var db = _e.Bd.Crear())
                Assert.Contains(db.TasasCambioEjecucionesDetalle.AsNoTracking(), d => d.IdTasaCambio == pendiente.IdTasaCambio
                    && d.Motivo == "Rechazada por ana: Error de digitación del BCH");

            // El BCH sigue publicando el valor rechazado: no se vuelve a insertar ni a alertar
            var alertas = _e.Notificador.Enviadas.Count;
            _e.ResponderExcel(SemanaConViernesMalo(28.50m, 28.65m), marca: "otra");
            await Ejecutar();
            Assert.Equal(1, Tasas().Count(t => t.MonedaOrigen == "USD" && t.TipoTasa == "VENTA" && t.FechaVigencia == EscenarioTasas.Viernes
                                              && t.Estado == Cat.EstadoTasa.Rechazada));
            Assert.Equal(alertas, _e.Notificador.Enviadas.Count);
        }

        // ── Puesta al día ───────────────────────────────────────────────────

        [Fact]
        public async Task La_puesta_al_dia_rellena_los_huecos_desde_la_ultima_vigente_menos_3_dias()
        {
            foreach (var (fecha, compra, venta) in EscenarioTasas.SemanaBch().Where(s => s.Item1 == new DateOnly(2026, 9, 28) || s.Item1 == EscenarioTasas.Jueves))
            {
                _e.Sembrar("USD", "COMPRA", fecha, compra);
                _e.Sembrar("USD", "VENTA", fecha, venta);
            }

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(4, r.Duplicados);     // lunes 28 y jueves 1, ya estaban
            Assert.Equal(16, r.Insertados);    // USD martes, miércoles y viernes (6) + EUR de los 5 días (10)
            var tasas = Tasas();
            foreach (var (fecha, _, _) in EscenarioTasas.SemanaBch())
            {
                Assert.Single(tasas, t => t.MonedaOrigen == "USD" && t.TipoTasa == "COMPRA" && t.FechaVigencia == fecha && t.Estado == Cat.EstadoTasa.Vigente);
                Assert.Single(tasas, t => t.MonedaOrigen == "EUR" && t.TipoTasa == "VENTA" && t.FechaVigencia == fecha && t.Estado == Cat.EstadoTasa.Vigente);
            }
        }

        [Fact]
        public async Task Rango_manual_carga_solo_lo_pedido_sin_la_regla_de_la_puesta_al_dia()
        {
            _e.Sembrar("USD", "COMPRA", EscenarioTasas.Viernes, 26.8925m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0270m);

            var r = await _e.Servicio().EjecutarAsync(Cat.Disparador.Manual, "ana", new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 29));

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(new DateOnly(2026, 9, 29), r.FechaObjetivo);
            Assert.Equal(8, r.Insertados);   // 28 y 29: USD y EUR, compra y venta
            Assert.All(Tasas().Where(t => t.FechaVigencia < EscenarioTasas.Viernes), t => Assert.Equal("ana", t.CreadoPor));
            Assert.DoesNotContain(Tasas(), t => t.FechaVigencia == EscenarioTasas.Jueves);
        }

        // ── Errores, reintentos y alertas ───────────────────────────────────

        [Fact]
        public async Task Error_transitorio_queda_REINTENTADA_con_ProximoIntentoUtc_y_la_cadena_sigue()
        {
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.ServiceUnavailable);

            var r1 = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Reintentada, r1.Estado);
            var e1 = Ejecucion(r1.IdEjecucion!.Value);
            Assert.Equal(1, e1.Intento);
            Assert.Equal(EscenarioTasas.ViernesTarde.UtcDateTime.AddMinutes(15), e1.ProximoIntentoUtc);
            Assert.Equal((short)503, e1.HttpStatus);
            Assert.Contains("503", e1.DetalleError);
            Assert.Contains(e1.Detalles, d => d.Resultado == Cat.ResultadoDetalle.Error && d.FechaVigencia == EscenarioTasas.Viernes);
            Assert.Empty(_e.Notificador.Enviadas);

            _e.Reloj.Avanzar(TimeSpan.FromMinutes(15));
            var r2 = await Ejecutar(Cat.Disparador.Reintento);
            var e2 = Ejecucion(r2.IdEjecucion!.Value);
            Assert.Equal(Cat.EstadoEjecucion.Reintentada, e2.Estado);
            Assert.Equal(2, e2.Intento);
            Assert.Equal(e1.IdEjecucion, e2.IdEjecucionOrigen);
            Assert.Equal(EscenarioTasas.ViernesTarde.UtcDateTime.AddMinutes(15 + 30), e2.ProximoIntentoUtc);

            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.Reloj.Avanzar(TimeSpan.FromMinutes(30));
            var r3 = await Ejecutar(Cat.Disparador.Reintento);
            var e3 = Ejecucion(r3.IdEjecucion!.Value);
            Assert.Equal(Cat.EstadoEjecucion.Exitosa, e3.Estado);
            Assert.Equal(3, e3.Intento);
            Assert.Equal(e1.IdEjecucion, e3.IdEjecucionOrigen);
            Assert.Null(e3.ProximoIntentoUtc);
        }

        [Fact]
        public async Task Agotar_MaxPorFecha_deja_FALLIDA_y_alerta()
        {
            _e.Opciones.Reintentos.MaxPorFecha = 2;
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.ServiceUnavailable);

            Assert.Equal(Cat.EstadoEjecucion.Reintentada, (await Ejecutar()).Estado);
            _e.Reloj.Avanzar(TimeSpan.FromMinutes(15));
            var r = await Ejecutar(Cat.Disparador.Reintento);

            Assert.Equal(Cat.EstadoEjecucion.Fallida, r.Estado);
            Assert.Null(Ejecucion(r.IdEjecucion!.Value).ProximoIntentoUtc);
            Assert.Contains("Falló", Assert.Single(_e.Notificador.Enviadas).Asunto);
        }

        [Fact]
        public async Task Si_el_reintento_caeria_despues_de_UltimoIntento_no_se_programa()
        {
            _e.Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 2, 23, 20, 0, TimeSpan.FromHours(-6)));
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.ServiceUnavailable);

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Fallida, r.Estado);   // 23:35 > 23:30
        }

        [Fact]
        public async Task Falla_del_API_usa_el_Excel()
        {
            _e.HabilitarApi();
            _e.Http.Estado(EscenarioTasas.UrlApi, HttpStatusCode.InternalServerError);

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(16, r.Insertados);
            Assert.Equal(Cat.Fuente.BchXlsx, Ejecucion(r.IdEjecucion!.Value).Fuente);
            Assert.Contains("API del BCH falló", r.Mensaje);
            Assert.Equal(4, _e.Http.Llamadas(EscenarioTasas.UrlApi));   // 1 + 3 reintentos, luego el Excel
        }

        [Fact]
        public async Task Con_el_API_disponible_se_usa_el_API()
        {
            _e.HabilitarApi();
            var compra = EscenarioTasas.SemanaBch().Select(s => (s.Item1, s.Item2));
            var venta = EscenarioTasas.SemanaBch().Select(s => (s.Item1, s.Item3));
            _e.Http.Responder(EscenarioTasas.UrlApi, req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ArchivosDePrueba.JsonApi(req.RequestUri!.ToString().Contains("/618/") ? compra : venta))
            });

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Exitosa, r.Estado);
            Assert.Equal(Cat.Fuente.BchApi, Ejecucion(r.IdEjecucion!.Value).Fuente);
            Assert.Equal(0, _e.Http.Llamadas(EscenarioTasas.UrlExcel));
            var tasas = Tasas();
            Assert.Equal(26.8925m, Una(tasas, "USD", "COMPRA", EscenarioTasas.Viernes).Tasa);
            Assert.Equal(Cat.Fuente.BchApi, Una(tasas, "USD", "COMPRA", EscenarioTasas.Viernes).Fuente);
            Assert.Equal(Cat.Fuente.Derivada, Una(tasas, "EUR", "COMPRA", EscenarioTasas.Viernes).Fuente);
        }

        [Fact]
        public async Task Fallan_ambas_fuentes_queda_FALLIDA_y_manda_un_solo_correo()
        {
            _e.HabilitarApi();
            _e.Http.Estado(EscenarioTasas.UrlApi, HttpStatusCode.Unauthorized);
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.NotFound);

            var r1 = await Ejecutar();
            var r2 = await Ejecutar(Cat.Disparador.Externo);

            Assert.Equal(Cat.EstadoEjecucion.Fallida, r1.Estado);   // error permanente: no se reintenta
            Assert.Equal(Cat.EstadoEjecucion.Fallida, r2.Estado);
            var alerta = Assert.Single(_e.Notificador.Enviadas);
            Assert.Contains("2026-10-02", alerta.Asunto);
            Assert.True(Ejecucion(r1.IdEjecucion!.Value).Notificado);
            Assert.False(Ejecucion(r2.IdEjecucion!.Value).Notificado);
            Assert.Contains("401", Ejecucion(r1.IdEjecucion!.Value).DetalleError);
            Assert.Contains("404", Ejecucion(r1.IdEjecucion!.Value).DetalleError);
            Assert.Empty(Tasas());
        }

        [Fact]
        public async Task Si_no_sale_el_correo_se_vuelve_a_intentar_en_la_siguiente()
        {
            _e.Notificador.Resultado = false;
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.NotFound);

            var r1 = await Ejecutar();
            _e.Notificador.Resultado = true;
            var r2 = await Ejecutar();

            Assert.False(Ejecucion(r1.IdEjecucion!.Value).Notificado);
            Assert.True(Ejecucion(r2.IdEjecucion!.Value).Notificado);
            Assert.Equal(2, _e.Notificador.Enviadas.Count);
        }

        [Fact]
        public async Task Sin_publicacion_todavia_hoy_queda_REINTENTADA()
        {
            _e.Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 2, 17, 5, 0, TimeSpan.FromHours(-6)));
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));   // hasta el jueves

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Reintentada, r.Estado);
            Assert.Contains(Ejecucion(r.IdEjecucion!.Value).Detalles, d => d.Resultado == Cat.ResultadoDetalle.SinDatos && d.FechaVigencia == EscenarioTasas.Viernes);
            Assert.Equal(12, r.Insertados);   // martes a jueves sí estaban
        }

        [Fact]
        public async Task Fin_de_semana_o_feriado_sin_publicacion_es_OMITIDA_SIN_DATOS_y_no_alerta()
        {
            _e.Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(-6)));   // sábado
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));   // el viernes no se publicó

            var r = await Ejecutar();

            Assert.Equal(EscenarioTasas.Viernes, r.FechaObjetivo);
            Assert.Equal(Cat.EstadoEjecucion.OmitidaSinDatos, r.Estado);
            Assert.Empty(_e.Notificador.Enviadas);   // el jueves sí tiene tasa: no faltan dos días seguidos
        }

        [Fact]
        public async Task Dos_dias_habiles_seguidos_sin_tasa_avisa_una_sola_vez()
        {
            _e.Reloj = new RelojFijo(new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.FromHours(-6)));   // martes, barrido
            _e.ResponderExcel(EscenarioTasas.SemanaBch().Take(4));   // nada desde el jueves 1

            var r1 = await Ejecutar();
            var r2 = await Ejecutar();

            Assert.Equal(new DateOnly(2026, 10, 5), r1.FechaObjetivo);   // antes de las 17:00: el día hábil anterior
            Assert.Equal(Cat.EstadoEjecucion.OmitidaSinDatos, r1.Estado);
            var aviso = Assert.Single(_e.Notificador.Enviadas);
            Assert.Contains("dos días hábiles", aviso.Asunto);
            Assert.Contains("2026-10-02", aviso.Html);
            Assert.Contains("2026-10-05", aviso.Html);
            Assert.True(Ejecucion(r1.IdEjecucion!.Value).Notificado);
            Assert.Equal(Cat.EstadoEjecucion.OmitidaSinDatos, r2.Estado);
        }

        [Fact]
        public async Task Una_EN_CURSO_de_mas_de_15_minutos_se_marca_FALLIDA_al_empezar_la_siguiente()
        {
            long vieja, reciente;
            using (var db = _e.Bd.Crear())
            {
                var ahora = EscenarioTasas.ViernesTarde.UtcDateTime;
                var e1 = new TasaCambioEjecucion { Disparador = "PROGRAMADO", FechaObjetivo = EscenarioTasas.Jueves, Servidor = "x", EjecutadoPor = "job", InicioUtc = ahora.AddMinutes(-20) };
                var e2 = new TasaCambioEjecucion { Disparador = "PROGRAMADO", FechaObjetivo = EscenarioTasas.Jueves, Servidor = "x", EjecutadoPor = "job", InicioUtc = ahora.AddMinutes(-5) };
                db.TasasCambioEjecuciones.AddRange(e1, e2);
                db.SaveChanges();
                (vieja, reciente) = (e1.IdEjecucion, e2.IdEjecucion);
            }

            await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Fallida, Ejecucion(vieja).Estado);
            Assert.NotNull(Ejecucion(vieja).FinUtc);
            Assert.Contains("15 minutos", Ejecucion(vieja).Mensaje);
            Assert.Equal(Cat.EstadoEjecucion.EnCurso, Ejecucion(reciente).Estado);
        }

        [Fact]
        public async Task Si_el_BCE_falla_el_dolar_se_guarda_y_queda_PARCIAL()
        {
            _e.Http.Estado(EscenarioTasas.UrlBce90, HttpStatusCode.NotFound);

            var r = await Ejecutar();

            Assert.Equal(Cat.EstadoEjecucion.Parcial, r.Estado);
            Assert.Equal(8, r.Insertados);
            Assert.DoesNotContain(Tasas(), t => t.MonedaOrigen == "EUR");
            Assert.Contains(Ejecucion(r.IdEjecucion!.Value).Detalles, d => d.MonedaOrigen == "EUR" && d.Resultado == Cat.ResultadoDetalle.Error);
            Assert.Empty(_e.Notificador.Enviadas);
        }

        // ── Captura manual ──────────────────────────────────────────────────

        [Fact]
        public async Task Registrar_manual_crea_una_tasa_propia_de_la_empresa_y_la_versiona()
        {
            var s = _e.Servicio();

            var r1 = await s.RegistrarManualAsync(DatosBase.EmpresaA, "usd", "venta", EscenarioTasas.Viernes, 27.10m, "ana");
            var igual = await s.RegistrarManualAsync(DatosBase.EmpresaA, "USD", "VENTA", EscenarioTasas.Viernes, 27.1000m, "ana");
            var r2 = await s.RegistrarManualAsync(DatosBase.EmpresaA, "USD", "VENTA", EscenarioTasas.Viernes, 27.20m, "beto");

            Assert.True(r1.Exito && igual.Exito && r2.Exito);
            Assert.Equal(r1.IdTasaCambio, igual.IdTasaCambio);
            Assert.Contains("ningún cambio", igual.Mensaje);
            var tasas = Tasas();
            Assert.Equal(2, tasas.Count);
            var primera = tasas.Single(t => t.IdTasaCambio == r1.IdTasaCambio);
            var segunda = tasas.Single(t => t.IdTasaCambio == r2.IdTasaCambio);
            Assert.Equal((DatosBase.EmpresaA, Cat.Fuente.Manual, Cat.EstadoTasa.Reemplazada, "ana"),
                (primera.IdEmpresa!.Value, primera.Fuente, primera.Estado, primera.CreadoPor));
            Assert.Equal((Cat.EstadoTasa.Vigente, (short)2, primera.IdTasaCambio, "beto", "HNL"),
                (segunda.Estado, segunda.Version, segunda.IdTasaAnterior!.Value, segunda.CreadoPor, segunda.MonedaDestino));
            Assert.Null(segunda.IdEjecucion);
        }

        [Theory]
        [InlineData(0, "USD", "VENTA", 27.1, 0, "empresa")]
        [InlineData(1, "US", "VENTA", 27.1, 0, "3 letras")]
        [InlineData(1, "HNL", "VENTA", 27.1, 0, "moneda local")]
        [InlineData(1, "USD", "PROMEDIO", 27.1, 0, "COMPRA, VENTA o REFERENCIA")]
        [InlineData(1, "USD", "VENTA", 0, 0, "mayor que cero")]
        [InlineData(1, "USD", "VENTA", 270270, 0, "Fuera del rango")]
        [InlineData(1, "USD", "VENTA", 27.1, 3, "futura")]
        [InlineData(1, "XYZ", "VENTA", 27.1, 0, "no existe")]
        [InlineData(99, "USD", "VENTA", 27.1, 0, "empresa no existe")]
        public async Task Registrar_manual_rechaza_datos_invalidos(int empresa, string moneda, string tipo, double tasa, int diasAdelante, string mensaje)
        {
            var r = await _e.Servicio().RegistrarManualAsync(empresa, moneda, tipo, EscenarioTasas.Viernes.AddDays(diasAdelante), (decimal)tasa, "ana");

            Assert.False(r.Exito);
            Assert.Contains(mensaje, r.Mensaje, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Tasas());
        }

        [Fact]
        public async Task Registrar_manual_funciona_aunque_el_job_este_deshabilitado()
        {
            _e.Opciones.Habilitado = false;
            Assert.True((await _e.Servicio().RegistrarManualAsync(DatosBase.EmpresaB, "EUR", "COMPRA", EscenarioTasas.Viernes, 29.30m, "ana")).Exito);
        }
    }
}
