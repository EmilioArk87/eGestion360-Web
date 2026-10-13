using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Pages.Catalogos.TiposCambio;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// Tendencias: la geometría de la gráfica (pura), la serie que sale de la base (con el aislamiento entre empresas
    /// obligatorio) y la página.
    /// </summary>
    public class TendenciasTests : IDisposable
    {
        private const int A = DatosBase.EmpresaA;
        private const int B = DatosBase.EmpresaB;

        private static readonly DateOnly Antes = new(2026, 9, 25);
        private static readonly DateOnly Jueves = EscenarioTasas.Jueves;
        private static readonly DateOnly Viernes = EscenarioTasas.Viernes;
        private static readonly DateOnly Lunes = EscenarioTasas.Lunes;

        private readonly EscenarioTasas _e = new();
        private readonly List<IDisposable> _desechables = new();

        public void Dispose()
        {
            foreach (var d in _desechables) d.Dispose();
            _e.Dispose();
        }

        // ── Geometría de la gráfica ───────────────────────────────────────────

        private static List<PuntoTendencia> Cuatro() => new()
        {
            new(new DateOnly(2026, 10, 2), 26.8925m, 27.0270m),
            new(new DateOnly(2026, 10, 5), 26.8901m, 27.0246m),
            new(new DateOnly(2026, 10, 6), 26.8920m, 27.0265m),
            new(new DateOnly(2026, 10, 12), 26.8883m, 27.0227m)
        };

        [Fact]
        public void Con_ambas_series_hay_dos_lineas_la_banda_y_un_punto_por_serie_y_fecha()
        {
            var g = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Ambas, null);

            Assert.StartsWith("M", g.TrazoCompra);
            Assert.StartsWith("M", g.TrazoVenta);
            Assert.NotEmpty(g.Banda);
            Assert.Equal(8, g.Puntos.Count);
            Assert.Equal(4, g.EjeX.Count);
            Assert.Equal(4, g.EjeY.Count);
            Assert.Equal(new[] { "02/10", "05/10", "06/10", "12/10" }, g.EjeX.Select(x => x.Texto));
        }

        [Theory]
        [InlineData(SerieTendencia.Compra)]
        [InlineData(SerieTendencia.Venta)]
        public void Con_una_sola_serie_no_hay_la_otra_linea_ni_banda_ni_sus_puntos(string serie)
        {
            var g = GraficaTendencia.Calcular(Cuatro(), serie, null);

            Assert.Equal(serie == SerieTendencia.Compra, g.TrazoCompra.Length > 0);
            Assert.Equal(serie == SerieTendencia.Venta, g.TrazoVenta.Length > 0);
            Assert.Empty(g.Banda);
            Assert.Equal(4, g.Puntos.Count);
            Assert.All(g.Puntos, p => Assert.Equal(serie, p.Serie));
        }

        [Fact]
        public void Las_coordenadas_usan_punto_decimal_y_caen_dentro_del_area()
        {
            var g = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Ambas, null);

            // Solo comandos de trazo, dígitos y punto decimal: ninguna coma decimal de la cultura del servidor.
            Assert.Matches(@"^M[\d\. ]+( L[\d\. ]+)*$", g.TrazoCompra);
            Assert.Matches(@"^M[\d\. ]+( L[\d\. ]+)*$", g.TrazoVenta);
            foreach (var p in g.Puntos)
            {
                var izq = double.Parse(p.Izquierda.TrimEnd('%'), CultureInfo.InvariantCulture);
                var arr = double.Parse(p.Arriba.TrimEnd('%'), CultureInfo.InvariantCulture);
                Assert.InRange(izq, 0, 100);
                Assert.InRange(arr, 0, 100);
            }
            // La venta siempre queda arriba de la compra (menor porcentaje desde arriba) en la misma fecha.
            foreach (var fecha in g.Puntos.Select(p => p.Fecha).Distinct())
            {
                var compra = g.Puntos.Single(p => p.Fecha == fecha && p.Serie == SerieTendencia.Compra);
                var venta = g.Puntos.Single(p => p.Fecha == fecha && p.Serie == SerieTendencia.Venta);
                Assert.True(double.Parse(venta.Arriba.TrimEnd('%'), CultureInfo.InvariantCulture)
                            < double.Parse(compra.Arriba.TrimEnd('%'), CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void El_eje_x_es_proporcional_a_los_dias_y_el_ultimo_punto_queda_al_final()
        {
            var g = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Compra, null);

            double Izq(DateOnly f) => double.Parse(g.Puntos.Single(p => p.Fecha == f).Izquierda.TrimEnd('%'), CultureInfo.InvariantCulture);
            var d2 = Izq(new DateOnly(2026, 10, 2));
            var d5 = Izq(new DateOnly(2026, 10, 5));
            var d12 = Izq(new DateOnly(2026, 10, 12));

            Assert.True(d2 < d5 && d5 < d12);
            Assert.Equal(3.0 / 10.0, (d5 - d2) / (d12 - d2), 2);   // 3 de 10 días
        }

        [Fact]
        public void La_fecha_elegida_se_resalta_y_si_no_existe_se_resalta_la_ultima()
        {
            var elegida = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Ambas, new DateOnly(2026, 10, 5));
            Assert.All(elegida.Puntos.Where(p => p.Fecha == new DateOnly(2026, 10, 5)), p => Assert.True(p.Seleccionado));
            Assert.Equal(2, elegida.Puntos.Count(p => p.Seleccionado));

            var inexistente = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Ambas, new DateOnly(2030, 1, 1));
            Assert.All(inexistente.Puntos.Where(p => p.Seleccionado), p => Assert.Equal(new DateOnly(2026, 10, 12), p.Fecha));

            var sin = GraficaTendencia.Calcular(Cuatro(), SerieTendencia.Ambas, null);
            Assert.All(sin.Puntos.Where(p => p.Seleccionado), p => Assert.Equal(new DateOnly(2026, 10, 12), p.Fecha));
        }

        [Fact]
        public void Sin_puntos_o_con_un_solo_punto_no_falla()
        {
            var vacia = GraficaTendencia.Calcular(Array.Empty<PuntoTendencia>(), SerieTendencia.Ambas, null);
            Assert.Empty(vacia.Puntos);
            Assert.Empty(vacia.TrazoCompra);
            Assert.Empty(vacia.Cuadricula);

            var una = GraficaTendencia.Calcular(new[] { new PuntoTendencia(Viernes, 26.89m, 27.02m) }, SerieTendencia.Ambas, null);
            Assert.Equal(2, una.Puntos.Count);
            Assert.Empty(una.Banda);                                   // una banda necesita dos días
            Assert.All(una.Puntos, p => Assert.Equal("52.20%", p.Izquierda));   // centrado en el área de dibujo, sin dividir entre cero
        }

        [Fact]
        public void Un_dia_sin_venta_deja_su_hueco_en_la_serie_de_venta_pero_no_en_la_de_compra()
        {
            var puntos = new[]
            {
                new PuntoTendencia(Jueves, 26.80m, 27.00m),
                new PuntoTendencia(Viernes, 26.90m, null),
                new PuntoTendencia(Lunes, 26.95m, 27.10m)
            };

            var g = GraficaTendencia.Calcular(puntos, SerieTendencia.Ambas, null);

            Assert.Equal(3, g.Puntos.Count(p => p.Serie == SerieTendencia.Compra));
            Assert.Equal(2, g.Puntos.Count(p => p.Serie == SerieTendencia.Venta));
        }

        // ── Serie desde la base ───────────────────────────────────────────────

        [Fact]
        public async Task La_serie_va_de_la_fecha_mas_antigua_a_la_mas_reciente_y_no_incluye_el_futuro()
        {
            _e.Sembrar("USD", "COMPRA", Jueves, 26.80m);
            _e.Sembrar("USD", "VENTA", Jueves, 27.00m);
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            _e.Sembrar("USD", "VENTA", Viernes, 27.05m);
            _e.Sembrar("USD", "COMPRA", Lunes, 26.99m);   // hoy es el viernes 2: la del lunes es "próxima", no tendencia

            using var db = _e.Bd.Crear();
            var serie = await _e.Consultas(db).TendenciaAsync("usd", 30);

            Assert.Equal("USD", serie.MonedaOrigen);
            Assert.Equal(new[] { Jueves, Viernes }, serie.Puntos.Select(p => p.Fecha));
            Assert.Equal(26.90m, serie.Puntos[1].Compra);
            Assert.Equal(27.05m, serie.Puntos[1].Venta);
        }

        [Fact]
        public async Task El_periodo_recorta_la_serie_y_el_anterior_es_el_ultimo_dia_publicado_antes_de_la_ventana()
        {
            _e.Sembrar("USD", "COMPRA", Antes, 26.70m);
            _e.Sembrar("USD", "VENTA", Antes, 26.95m);
            _e.Sembrar("USD", "COMPRA", Jueves, 26.80m);
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            var corta = await consultas.TendenciaAsync("USD", 3);   // desde el 29/09
            Assert.Equal(new[] { Jueves, Viernes }, corta.Puntos.Select(p => p.Fecha));
            Assert.Equal(Antes, corta.Anterior!.Fecha);
            Assert.Equal(26.70m, corta.Anterior.Compra);

            var todo = await consultas.TendenciaAsync("USD", 0);
            Assert.Equal(3, todo.Puntos.Count);
            Assert.Null(todo.Anterior);
        }

        [Fact]
        public async Task La_serie_de_otra_moneda_o_sin_tasas_viene_vacia()
        {
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);

            using var db = _e.Bd.Crear();
            var euro = await _e.Consultas(db).TendenciaAsync("EUR", 30);

            Assert.Empty(euro.Puntos);
            Assert.Null(euro.Anterior);
        }

        [Fact]
        public async Task Aislamiento_la_tasa_propia_de_una_empresa_no_aparece_en_la_serie_de_otra_y_gana_a_la_oficial_de_la_misma_fecha()
        {
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            _e.Sembrar("USD", "COMPRA", Viernes, 27.50m, A);   // propia de A
            _e.Sembrar("USD", "COMPRA", Jueves, 26.80m, A);    // propia de A, sin oficial ese día

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            var deA = await consultas.TendenciaAsync("USD", 30, A);
            Assert.Equal(27.50m, deA.Puntos.Single(p => p.Fecha == Viernes).Compra);
            Assert.Equal(26.80m, deA.Puntos.Single(p => p.Fecha == Jueves).Compra);

            var deB = await consultas.TendenciaAsync("USD", 30, B);
            Assert.Equal(26.90m, deB.Puntos.Single().Compra);          // solo la oficial; ni rastro de la de A
            Assert.DoesNotContain(deB.Puntos, p => p.Fecha == Jueves);

            var sinEmpresa = await consultas.TendenciaAsync("USD", 30);
            Assert.Equal(26.90m, sinEmpresa.Puntos.Single().Compra);
        }

        [Fact]
        public async Task Las_tasas_en_revision_o_reemplazadas_no_cuentan()
        {
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            _e.Sembrar("USD", "COMPRA", Jueves, 99.00m, null, "EN_REVISION");

            using var db = _e.Bd.Crear();
            var serie = await _e.Consultas(db).TendenciaAsync("USD", 30);

            Assert.Equal(Viernes, serie.Puntos.Single().Fecha);
        }

        // ── Página ────────────────────────────────────────────────────────────

        private TendenciasModel Pagina(HttpContext http, ITasaCambioConsultaService? consultas = null)
        {
            var db = _e.Bd.Crear();
            _desechables.Add(db);
            var modelo = new TendenciasModel(consultas ?? _e.Consultas(db), Options.Create(_e.Opciones), _e.Reloj,
                NullLogger<TendenciasModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor()))
            };
            modelo.TempData = new TempDataDictionary(http, new TempDataLocal());
            return modelo;
        }

        private sealed class TempDataLocal : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static HttpContext Sesion(string? rol, int? empresa = null, bool conModulo = true, bool autenticado = true)
        {
            var http = SesionFalsa.Contexto(rol, empresa?.ToString(), autenticado);
            if (conModulo) http.Session.SetString("Modulos", JsonSerializer.Serialize(new[] { "catalogos" }));
            http.Session.SetString("Permisos", JsonSerializer.Serialize(new Dictionary<string, string[]> { ["catalogos"] = new[] { "ver" } }));
            http.Session.SetString("Username", "usuario.prueba");
            return http;
        }

        [Fact]
        public async Task Sin_sesion_va_al_login_y_sin_el_modulo_al_menu()
        {
            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(
                await Pagina(Sesion(null, null, conModulo: false, autenticado: false)).OnGetAsync()).PageName);
            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(
                await Pagina(Sesion("empresa_user", A, conModulo: false)).OnGetAsync()).PageName);
        }

        [Fact]
        public async Task Sin_filtros_muestra_el_dolar_con_ambas_series_y_todo_el_periodo()
        {
            _e.Sembrar("USD", "COMPRA", Jueves, 26.80m);
            _e.Sembrar("USD", "VENTA", Jueves, 27.00m);
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            _e.Sembrar("USD", "VENTA", Viernes, 27.05m);
            var pagina = Pagina(Sesion("empresa_user", A));

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.Equal("USD", pagina.Moneda);
            Assert.Equal(SerieTendencia.Ambas, pagina.Serie);
            Assert.Equal(TendenciasModel.RangoTodo, pagina.Rango);
            Assert.True(pagina.HayDatos);
            Assert.Equal(Viernes, pagina.Elegido!.Fecha);   // sin fecha elegida: la última
            Assert.Equal(4, pagina.Grafica.Puntos.Count);
            Assert.Equal("+0.1000 (+0.373 %)", TendenciasModel.Cambio(26.90m, 26.80m, true));
        }

        [Fact]
        public async Task Los_valores_desconocidos_de_la_URL_se_ignoran_y_una_fecha_elegida_se_respeta()
        {
            _e.Sembrar("USD", "COMPRA", Jueves, 26.80m);
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);

            var raros = Pagina(Sesion("empresa_user", A));
            raros.Moneda = "'; DROP TABLE x; --";
            raros.Serie = "OTRA";
            raros.Rango = "9999";
            await raros.OnGetAsync();
            Assert.Equal("USD", raros.Moneda);
            Assert.Equal(SerieTendencia.Ambas, raros.Serie);
            Assert.Equal(TendenciasModel.RangoTodo, raros.Rango);

            var elegida = Pagina(Sesion("empresa_user", A));
            elegida.Moneda = "usd";
            elegida.Serie = "compra";
            elegida.Rango = "30";
            elegida.Fecha = Jueves;
            await elegida.OnGetAsync();
            Assert.Equal(SerieTendencia.Compra, elegida.Serie);
            Assert.Equal(30, elegida.Dias);
            Assert.Equal(Jueves, elegida.Elegido!.Fecha);

            var fuera = Pagina(Sesion("empresa_user", A));
            fuera.Fecha = new DateOnly(2030, 1, 1);
            await fuera.OnGetAsync();
            Assert.Equal(Viernes, fuera.Elegido!.Fecha);
        }

        [Fact]
        public async Task El_euro_sin_tasas_muestra_el_estado_vacio()
        {
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            var pagina = Pagina(Sesion("empresa_user", A));
            pagina.Moneda = "EUR";

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.False(pagina.HayDatos);
            Assert.Null(pagina.Elegido);
            Assert.Empty(pagina.Grafica.Puntos);
        }

        [Fact]
        public async Task Una_empresa_no_ve_en_su_tendencia_la_tasa_propia_de_otra()
        {
            _e.Sembrar("USD", "COMPRA", Viernes, 26.90m);
            _e.Sembrar("USD", "COMPRA", Viernes, 27.50m, A);

            var deB = Pagina(Sesion("empresa_user", B));
            await deB.OnGetAsync();
            Assert.Equal(26.90m, deB.Elegido!.Compra);

            var deA = Pagina(Sesion("empresa_user", A));
            await deA.OnGetAsync();
            Assert.Equal(27.50m, deA.Elegido!.Compra);
        }

        [Fact]
        public async Task Sin_las_tablas_del_script_020_muestra_el_aviso_en_lugar_de_fallar()
        {
            var conexion = new SqliteConnection("DataSource=:memory:");
            conexion.Open();
            _desechables.Add(conexion);
            var db = new ContextoDePrueba(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexion).Options);
            _desechables.Add(db);
            var pagina = Pagina(Sesion("admin"), new TasaCambioConsultaService(db, Options.Create(_e.Opciones), _e.Reloj));

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.True(pagina.ModuloNoInstalado);
            Assert.Equal(IndexModel.AvisoNoInstalado, pagina.AvisoBaseDatos);
            Assert.False(pagina.HayDatos);
        }

        [Fact]
        public void Los_cambios_se_escriben_con_signo_y_punto_decimal_y_faltantes_con_raya()
        {
            Assert.Equal("−0.0042", TendenciasModel.Cambio(26.8883m, 26.8925m));
            Assert.Equal("0.0000", TendenciasModel.Cambio(26.89m, 26.89m));
            Assert.Equal("+0.0100", TendenciasModel.Cambio(26.90m, 26.89m));
            Assert.Equal("—", TendenciasModel.Cambio(null, 26.89m));
            Assert.Equal("—", TendenciasModel.Cambio(26.89m, null));
            Assert.Equal("26.8925", TendenciasModel.Tasa4(26.8925m));
            Assert.Equal("—", TendenciasModel.Tasa4(null));
            Assert.Equal("0.1345", TendenciasModel.Diferencia(new PuntoTendencia(Viernes, 26.8925m, 27.0270m)));
            Assert.Equal("—", TendenciasModel.Diferencia(new PuntoTendencia(Viernes, 26.8925m, null)));
        }
    }
}
