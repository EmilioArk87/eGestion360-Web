using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Pages.Catalogos.TiposCambio;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// Configuración del job editable desde la pantalla (script 024): validación del formulario, aplicación sobre las
    /// opciones vivas, guardado en la base (SQLite en memoria), permisos de la página y el worker que arranca cuando
    /// se enciende el interruptor sin reiniciar.
    /// </summary>
    public class ConfiguracionTasasTests : IDisposable
    {
        private readonly EscenarioTasas _e = new();

        public void Dispose() => _e.Dispose();

        private static ConfiguracionTasasInput Valido() => new()
        {
            Habilitado = true,
            Monedas = "usd, eur",
            PrimerIntento = "16:30",
            UltimoIntento = "22:00",
            BarridoMatutino = "06:45",
            DiasPuestaAlDia = "5",
            MaxIntentosPorFecha = "4",
            VariacionDiariaMaxPct = "2,5",
            DiferenciaCompraVentaMaxPct = "1.5",
            RangoUsdMin = "20", RangoUsdMax = "30",
            RangoEurMin = "", RangoEurMax = "",
            Destinatarios = "uno@ejemplo.com\r\ndos@ejemplo.com, UNO@ejemplo.com"
        };

        private static TasaCambioConfiguracion? Validar(ConfiguracionTasasInput i, out Dictionary<string, string> errores)
        {
            errores = new Dictionary<string, string>();
            return ConfiguracionTasas.Validar(i, "HNL", errores);
        }

        // ── Validación ────────────────────────────────────────────────────────

        [Fact]
        public void Un_formulario_valido_se_lee_sin_depender_de_la_cultura_y_sin_repetidos()
        {
            var fila = Validar(Valido(), out var errores);

            Assert.Empty(errores);
            Assert.NotNull(fila);
            Assert.True(fila!.Habilitado);
            Assert.Equal("USD,EUR", fila.Monedas);
            Assert.Equal("16:30", fila.PrimerIntento);
            Assert.Equal((byte)5, fila.DiasPuestaAlDia);
            Assert.Equal(2.5m, fila.VariacionDiariaMaxPct);
            Assert.Equal(1.5m, fila.DiferenciaCompraVentaMaxPct);
            Assert.Equal((20m, 30m), (fila.RangoUsdMin, fila.RangoUsdMax));
            Assert.Null(fila.RangoEurMin);
            Assert.Equal("uno@ejemplo.com,dos@ejemplo.com", fila.DestinatariosAlerta);
        }

        [Fact]
        public void Los_campos_en_blanco_quedan_nulos_para_usar_appsettings()
        {
            var fila = Validar(new ConfiguracionTasasInput { Habilitado = false }, out var errores);

            Assert.Empty(errores);
            Assert.NotNull(fila);
            Assert.False(fila!.Habilitado);
            Assert.Null(fila.Monedas);
            Assert.Null(fila.PrimerIntento);
            Assert.Null(fila.DiasPuestaAlDia);
            Assert.Null(fila.DestinatariosAlerta);
        }

        [Theory]
        [InlineData(nameof(ConfiguracionTasasInput.PrimerIntento), "25:00")]
        [InlineData(nameof(ConfiguracionTasasInput.BarridoMatutino), "7 de la mañana")]
        [InlineData(nameof(ConfiguracionTasasInput.DiasPuestaAlDia), "0")]
        [InlineData(nameof(ConfiguracionTasasInput.DiasPuestaAlDia), "31")]
        [InlineData(nameof(ConfiguracionTasasInput.MaxIntentosPorFecha), "abc")]
        [InlineData(nameof(ConfiguracionTasasInput.VariacionDiariaMaxPct), "0")]
        [InlineData(nameof(ConfiguracionTasasInput.VariacionDiariaMaxPct), "3.123")]
        [InlineData(nameof(ConfiguracionTasasInput.DiferenciaCompraVentaMaxPct), "-1")]
        [InlineData(nameof(ConfiguracionTasasInput.Monedas), "DOLAR")]
        [InlineData(nameof(ConfiguracionTasasInput.Monedas), "USD, HNL")]
        [InlineData(nameof(ConfiguracionTasasInput.Destinatarios), "no-es-un-correo")]
        public void Un_valor_invalido_se_rechaza_con_el_error_en_su_campo(string campo, string valor)
        {
            var entrada = Valido();
            typeof(ConfiguracionTasasInput).GetProperty(campo)!.SetValue(entrada, valor);

            var fila = Validar(entrada, out var errores);

            Assert.Null(fila);
            Assert.True(errores.ContainsKey(campo), $"Debía fallar {campo}; fallaron: {string.Join(", ", errores.Keys)}");
        }

        [Fact]
        public void El_ultimo_intento_debe_ser_despues_del_primero()
        {
            var entrada = Valido();
            entrada.PrimerIntento = "20:00";
            entrada.UltimoIntento = "19:00";

            Assert.Null(Validar(entrada, out var errores));
            Assert.Contains(nameof(entrada.UltimoIntento), errores.Keys);
        }

        [Fact]
        public void Un_rango_a_medias_o_invertido_se_rechaza()
        {
            var aMedias = Valido();
            aMedias.RangoEurMin = "15";
            Assert.Null(Validar(aMedias, out var e1));
            Assert.Contains(nameof(aMedias.RangoEurMax), e1.Keys.Concat(e1.Keys.Select(k => k.Replace("Min", "Max"))));

            var invertido = Valido();
            invertido.RangoUsdMin = "40";
            invertido.RangoUsdMax = "30";
            Assert.Null(Validar(invertido, out var e2));
            Assert.Contains(nameof(invertido.RangoUsdMax), e2.Keys);
        }

        // ── Aplicación sobre las opciones ─────────────────────────────────────

        [Fact]
        public void Aplicar_pone_lo_guardado_y_sin_fila_vuelve_a_los_valores_base()
        {
            var opciones = new TasasCambioOptions { Habilitado = false, PrimerIntento = "17:00" };
            var basales = ValoresBaseTasas.Copiar(opciones);
            var fila = Validar(Valido(), out _)!;

            ConfiguracionTasas.Aplicar(opciones, basales, fila);

            Assert.True(opciones.Habilitado);
            Assert.Equal(new[] { "USD", "EUR" }, opciones.MonedasEfectivas);
            Assert.Equal(new TimeOnly(16, 30), opciones.HoraPrimerIntento);
            Assert.Equal(5, opciones.DiasPuestaAlDia);
            Assert.Equal(4, opciones.Reintentos.MaxPorFecha);
            Assert.Equal(2.5m, opciones.Validacion.VariacionDiariaMaxPct);
            Assert.Equal(20m, opciones.Validacion.Rangos["USD"].Min);
            Assert.Equal(60m, opciones.Validacion.Rangos["EUR"].Max);   // sin rango guardado del euro: el de appsettings
            Assert.Equal(new[] { "uno@ejemplo.com", "dos@ejemplo.com" }, opciones.Alertas.Destinatarios);

            ConfiguracionTasas.Aplicar(opciones, basales, null);

            Assert.False(opciones.Habilitado);
            Assert.Equal(new TimeOnly(17, 0), opciones.HoraPrimerIntento);
            Assert.Equal(15m, opciones.Validacion.Rangos["USD"].Min);
            Assert.Empty(opciones.Alertas.Destinatarios);
            // La copia base no se contaminó con lo aplicado.
            Assert.Equal(15m, basales.Rangos["USD"].Min);
        }

        [Fact]
        public void En_desarrollo_el_interruptor_guardado_no_se_aplica_pero_lo_demas_si()
        {
            var opciones = new TasasCambioOptions { Habilitado = false };
            var basales = ValoresBaseTasas.Copiar(opciones);

            ConfiguracionTasas.Aplicar(opciones, basales, Validar(Valido(), out _)!, usarInterruptorGuardado: false);

            Assert.False(opciones.Habilitado);
            Assert.Equal(5, opciones.DiasPuestaAlDia);
        }

        // ── Servicio con la base ──────────────────────────────────────────────

        private (TasasCambioConfiguracionService Servicio, TasasCambioOptions Opciones) Servicio(string entorno = "Production",
            ITasasCambioContextos? contextos = null)
        {
            var opciones = new TasasCambioOptions();
            var servicios = new ServiceCollection();
            servicios.AddScoped<ITasasCambioContextos>(_ => contextos ?? new ContextosDePrueba(_e.Bd));
            var proveedor = servicios.BuildServiceProvider();
            var servicio = new TasasCambioConfiguracionService(Options.Create(opciones), proveedor.GetRequiredService<IServiceScopeFactory>(),
                _e.Reloj, new EntornoFalso(entorno), NullLogger<TasasCambioConfiguracionService>.Instance);
            return (servicio, opciones);
        }

        private sealed class TempDataLocal : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class EntornoFalso : IHostEnvironment
        {
            public EntornoFalso(string nombre) => EnvironmentName = nombre;
            public string EnvironmentName { get; set; }
            public string ApplicationName { get; set; } = "pruebas";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        }

        private sealed class ContextosQueFallan : ITasasCambioContextos
        {
            public ApplicationDbContext Crear() => throw new InvalidOperationException("sin base");
        }

        [Fact]
        public async Task Guardar_crea_la_fila_la_actualiza_y_aplica_de_inmediato_a_las_opciones_vivas()
        {
            var (servicio, opciones) = Servicio();
            Assert.False(opciones.Habilitado);

            await servicio.GuardarAsync(Validar(Valido(), out _)!, "emilio");
            Assert.True(opciones.Habilitado);
            Assert.Equal(5, opciones.DiasPuestaAlDia);

            var segunda = Validar(new ConfiguracionTasasInput { Habilitado = false, DiasPuestaAlDia = "7" }, out _)!;
            await servicio.GuardarAsync(segunda, "otro");

            Assert.False(opciones.Habilitado);
            Assert.Equal(7, opciones.DiasPuestaAlDia);
            Assert.Equal(new TimeOnly(17, 0), opciones.HoraPrimerIntento);   // el blanco vuelve al valor base

            using var db = _e.Bd.Crear();
            var filas = await db.TasasCambioConfiguracion.AsNoTracking().ToListAsync();
            var unica = Assert.Single(filas);
            Assert.Equal("otro", unica.ActualizadoPor);
            Assert.False(unica.Habilitado);
            Assert.Equal(_e.Reloj.GetUtcNow().UtcDateTime, unica.ActualizadoUtc);
        }

        [Fact]
        public async Task Aplicar_lee_la_base_al_arrancar_otro_proceso()
        {
            var (primero, _) = Servicio();
            await primero.GuardarAsync(Validar(Valido(), out _)!, "emilio");

            var (segundo, opciones) = Servicio();
            Assert.False(opciones.Habilitado);
            await segundo.AplicarAsync();

            Assert.True(opciones.Habilitado);
            Assert.Equal(new TimeOnly(16, 30), opciones.HoraPrimerIntento);
        }

        [Fact]
        public async Task En_desarrollo_lo_guardado_aplica_pero_el_interruptor_se_queda_como_en_appsettings()
        {
            var (produccion, _) = Servicio();
            await produccion.GuardarAsync(Validar(Valido(), out _)!, "emilio");

            var (desarrollo, opciones) = Servicio("Development");
            await desarrollo.AplicarAsync();

            Assert.True(desarrollo.IgnoraInterruptorGuardado);
            Assert.False(opciones.Habilitado);
            Assert.Equal(5, opciones.DiasPuestaAlDia);
        }

        [Fact]
        public async Task Si_la_base_no_responde_aplicar_no_falla_y_quedan_los_valores_de_appsettings()
        {
            var (servicio, opciones) = Servicio(contextos: new ContextosQueFallan());

            await servicio.AplicarAsync();

            Assert.False(opciones.Habilitado);
            Assert.Equal(3, opciones.DiasPuestaAlDia);
        }

        // ── Página ────────────────────────────────────────────────────────────

        private ConfiguracionModel Pagina(string rol, TasasCambioConfiguracionService servicio, TasasCambioOptions opciones, bool autenticado = true)
        {
            var http = SesionFalsa.Contexto(rol, null, autenticado);
            http.Session.SetString("Modulos", JsonSerializer.Serialize(new[] { "catalogos" }));
            http.Session.SetString("Username", "usuario.prueba");
            var modelo = new ConfiguracionModel(servicio, Options.Create(opciones), NullLogger<ConfiguracionModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor()))
            };
            modelo.TempData = new TempDataDictionary(http, new TempDataLocal());
            return modelo;
        }

        [Theory]
        [InlineData("empresa_admin")]
        [InlineData("empresa_user")]
        public async Task Quien_no_es_admin_del_sistema_no_ve_ni_guarda_la_configuracion(string rol)
        {
            var (servicio, opciones) = Servicio();

            var get = Pagina(rol, servicio, opciones);
            Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(await get.OnGetAsync()).PageName);
            Assert.NotNull(get.TempData[IndexModel.ClaveError]);

            var post = Pagina(rol, servicio, opciones);
            post.Entrada = Valido();
            Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(await post.OnPostAsync()).PageName);

            Assert.False(opciones.Habilitado);
            using var db = _e.Bd.Crear();
            Assert.Empty(db.TasasCambioConfiguracion);
        }

        [Fact]
        public async Task Sin_sesion_va_al_login()
        {
            var (servicio, opciones) = Servicio();
            var pagina = Pagina("admin", servicio, opciones, autenticado: false);

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await pagina.OnGetAsync()).PageName);
        }

        [Fact]
        public async Task El_admin_del_sistema_ve_lo_que_rige_hoy_y_no_las_claves()
        {
            var (servicio, opciones) = Servicio();
            opciones.Bch.ApiKey = "valor-ficticio-solo-para-pruebas";
            var pagina = Pagina("admin", servicio, opciones);

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.False(pagina.Entrada.Habilitado);
            Assert.Equal("USD, EUR", pagina.Entrada.Monedas);
            Assert.Equal("17:00", pagina.Entrada.PrimerIntento);
            Assert.True(pagina.TieneClaveBch);   // solo se informa que está puesta
            Assert.DoesNotContain(pagina.Entrada.GetType().GetProperties(), p => p.Name.Contains("Clave") || p.Name.Contains("Token"));
        }

        [Fact]
        public async Task El_admin_del_sistema_enciende_el_job_y_vuelve_a_la_pantalla_de_tasas()
        {
            var (servicio, opciones) = Servicio();
            var pagina = Pagina("admin", servicio, opciones);
            pagina.Entrada = Valido();

            var resultado = Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());

            Assert.Equal("Index", resultado.PageName);
            Assert.Contains("HABILITADO", (string)pagina.TempData[IndexModel.ClaveMensaje]!);
            Assert.True(opciones.Habilitado);
        }

        [Fact]
        public async Task Un_formulario_con_errores_no_guarda_nada_y_se_vuelve_a_mostrar()
        {
            var (servicio, opciones) = Servicio();
            var pagina = Pagina("admin", servicio, opciones);
            pagina.Entrada = Valido();
            pagina.Entrada.PrimerIntento = "xx";

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.NotNull(pagina.Error(nameof(pagina.Entrada.PrimerIntento)));
            Assert.False(opciones.Habilitado);
            using var db = _e.Bd.Crear();
            Assert.Empty(db.TasasCambioConfiguracion);
        }

        // ── Worker ────────────────────────────────────────────────────────────

        private sealed class LogQueGuarda : ILogger<TasasCambioBackgroundService>
        {
            private readonly List<string> _mensajes = new();
            public IReadOnlyList<string> Mensajes { get { lock (_mensajes) return _mensajes.ToList(); } }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_mensajes) _mensajes.Add(formatter(state, exception));
            }
        }

        [Fact]
        public async Task El_worker_apagado_espera_y_continua_cuando_se_enciende_sin_reiniciar()
        {
            var opciones = new TasasCambioOptions { Habilitado = false };
            var log = new LogQueGuarda();
            var proveedor = new ServiceCollection().BuildServiceProvider();
            var worker = new TasasCambioBackgroundService(proveedor.GetRequiredService<IServiceScopeFactory>(), Options.Create(opciones),
                new ColaEjecucionTasasCambio(), TimeProvider.System, log)
            {
                EsperaDeshabilitado = TimeSpan.FromMilliseconds(20)
            };

            await worker.StartAsync(CancellationToken.None);
            try
            {
                await Task.Delay(150);
                Assert.Contains(log.Mensajes, m => m.Contains("espera a que se habilite"));
                Assert.DoesNotContain(log.Mensajes, m => m.Contains("worker iniciado"));

                opciones.Habilitado = true;   // lo que hace la pantalla al guardar

                var limite = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < limite && !log.Mensajes.Any(m => m.Contains("worker iniciado"))) await Task.Delay(25);
                Assert.Contains(log.Mensajes, m => m.Contains("worker iniciado"));
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
    }
}
