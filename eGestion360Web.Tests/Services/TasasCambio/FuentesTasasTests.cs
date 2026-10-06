using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>Proveedores: Excel del BCH, XML del BCE, API del BCH (formato supuesto), euro derivado y HTTP.</summary>
    public class FuentesTasasTests : IDisposable
    {
        private readonly EscenarioTasas _e = new();

        public void Dispose() => _e.Dispose();

        private static readonly DateOnly Lunes = new(2026, 9, 28);

        // ── Excel del BCH ───────────────────────────────────────────────────

        [Fact]
        public void Excel_con_encabezados_reales_lee_compra_y_venta_e_ignora_titulos_y_notas_al_pie()
        {
            var libro = BchExcelProveedor.LeerLibro(ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch()));

            Assert.Equal("Tipo de Cambio Diario", libro.Hoja);
            Assert.True(libro.ConEncabezados);
            Assert.Equal(5, libro.Filas.Count);
            var viernes = libro.Filas.Last();
            Assert.Equal(EscenarioTasas.Viernes, viernes.Fecha);
            Assert.Equal(26.8925m, viernes.Compra);
            Assert.Equal(27.0270m, viernes.Venta);
        }

        [Fact]
        public void Excel_sin_encabezados_usa_las_columnas_A_B_C()
        {
            var libro = BchExcelProveedor.LeerLibro(ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch(), conEncabezados: false));

            Assert.False(libro.ConEncabezados);
            Assert.Equal(5, libro.Filas.Count);
            Assert.Equal((Lunes, 26.8410m, 26.9752m), (libro.Filas[0].Fecha, libro.Filas[0].Compra, libro.Filas[0].Venta));
        }

        [Fact]
        public void Excel_busca_la_hoja_Tipo_de_Cambio_Diario_aunque_no_sea_la_primera()
        {
            var libro = BchExcelProveedor.LeerLibro(ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch(), hojaPreviaVacia: true));
            Assert.Equal("Tipo de Cambio Diario", libro.Hoja);
            Assert.Equal(5, libro.Filas.Count);
        }

        [Fact]
        public void Excel_sin_esa_hoja_usa_la_primera()
        {
            var libro = BchExcelProveedor.LeerLibro(ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch(), hoja: "Hoja1"));
            Assert.Equal("Hoja1", libro.Hoja);
            Assert.Equal(5, libro.Filas.Count);
        }

        [Fact]
        public void Excel_con_encabezados_con_tildes_y_fechas_y_valores_como_texto()
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TIPO DE CAMBIO DIARIO");
            ws.Cell(2, 2).Value = "FECHA";
            ws.Cell(2, 3).Value = "Precio de Compra";
            ws.Cell(2, 4).Value = "Precio de Venta ";
            ws.Cell(3, 2).Value = "01/10/2026";
            ws.Cell(3, 3).Value = "26,8788";
            ws.Cell(3, 4).Value = "L 27.0132";
            ws.Cell(4, 2).Value = "2026-10-02";
            ws.Cell(4, 3).Value = 26.8925;
            ws.Cell(4, 4).Value = 27.027;
            ws.Cell(6, 2).Value = "1/ Cifras preliminares.";
            ws.Cell(6, 3).Value = "n.d.";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            var libro = BchExcelProveedor.LeerLibro(ms.ToArray());

            Assert.Equal(2, libro.Filas.Count);
            Assert.Equal((EscenarioTasas.Jueves, 26.8788m, 27.0132m), (libro.Filas[0].Fecha, libro.Filas[0].Compra, libro.Filas[0].Venta));
            Assert.Equal(27.0270m, libro.Filas[1].Venta);
        }

        [Fact]
        public void Excel_sin_filas_validas_o_que_no_es_Excel_es_formato_inesperado_permanente()
        {
            using var wb = new XLWorkbook();
            wb.Worksheets.Add("Tipo de Cambio Diario").Cell(1, 1).Value = "Sin datos";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            var vacio = Assert.Throws<ErrorFuenteException>(() => BchExcelProveedor.LeerLibro(ms.ToArray()));
            Assert.Equal(TipoErrorFuente.Permanente, vacio.Error.Tipo);

            var basura = Assert.Throws<ErrorFuenteException>(() => BchExcelProveedor.LeerLibro("<html>mantenimiento</html>"u8.ToArray()));
            Assert.Equal(TipoErrorFuente.Permanente, basura.Error.Tipo);
            Assert.Contains("Formato inesperado", basura.Error.Mensaje);
        }

        [Fact]
        public async Task Proveedor_Excel_devuelve_USD_HNL_compra_y_venta_desde_la_fecha_pedida_con_hash_y_estado()
        {
            var contenido = ArchivosDePrueba.ExcelBch(EscenarioTasas.SemanaBch());
            _e.Http.Contenido(EscenarioTasas.UrlExcel, contenido);
            var proveedor = new BchExcelProveedor(_e.Cliente(), Options.Create(_e.Opciones));

            var r = await proveedor.ObtenerAsync(new SolicitudLectura(EscenarioTasas.Jueves, EscenarioTasas.Viernes));

            Assert.True(r.Exitoso);
            Assert.Equal(4, r.Lecturas.Count);   // jueves y viernes, compra y venta
            Assert.All(r.Lecturas, l => Assert.Equal(("USD", "HNL", TasasCambioCatalogo.Fuente.BchXlsx), (l.MonedaOrigen, l.MonedaDestino, l.Fuente)));
            var compra = r.Lecturas.Single(l => l.Fecha == EscenarioTasas.Viernes && l.TipoTasa == TasasCambioCatalogo.TipoTasa.Compra);
            Assert.Equal(26.8925m, compra.Valor);
            Assert.Equal(200, r.HttpStatus);
            Assert.Equal(EscenarioTasas.UrlExcel, r.Endpoint);
            Assert.Equal(ClienteHttpTasas.Hash(contenido), r.HashContenido);
            Assert.Equal(64, r.HashContenido!.Length);
        }

        // ── BCE ─────────────────────────────────────────────────────────────

        [Fact]
        public void Xml_del_BCE_lee_EUR_USD_por_fecha_en_cultura_invariante()
        {
            var anterior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");   // coma decimal: no debe afectar
                var tipos = BceProveedor.LeerXml(ArchivosDePrueba.XmlBce(EscenarioTasas.SemanaBce()));

                Assert.Equal(5, tipos.Count);
                Assert.Equal((Lunes, 1.0850m), tipos[0]);                         // ordenados por fecha
                Assert.Equal((EscenarioTasas.Viernes, 1.0876m), tipos[^1]);
            }
            finally
            {
                CultureInfo.CurrentCulture = anterior;
            }
        }

        [Fact]
        public void Xml_del_BCE_invalido_o_sin_USD_es_permanente()
        {
            Assert.Equal(TipoErrorFuente.Permanente,
                Assert.Throws<ErrorFuenteException>(() => BceProveedor.LeerXml("no es xml"u8.ToArray())).Error.Tipo);
            Assert.Equal(TipoErrorFuente.Permanente,
                Assert.Throws<ErrorFuenteException>(() => BceProveedor.LeerXml("<Envelope><Cube><Cube time=\"2026-10-02\"><Cube currency=\"JPY\" rate=\"161\"/></Cube></Cube></Envelope>"u8.ToArray())).Error.Tipo);
        }

        [Fact]
        public async Task Proveedor_BCE_usa_el_archivo_diario_para_un_dia_y_el_de_90_dias_para_un_rango()
        {
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            var proveedor = new BceProveedor(_e.Cliente(), Options.Create(_e.Opciones));

            var dia = await proveedor.ObtenerAsync(new SolicitudLectura(EscenarioTasas.Viernes, EscenarioTasas.Viernes));
            var rango = await proveedor.ObtenerAsync(new SolicitudLectura(Lunes, EscenarioTasas.Viernes));

            Assert.Equal(EscenarioTasas.UrlBceDiaria, dia.Endpoint);
            Assert.Equal(EscenarioTasas.UrlBce90, rango.Endpoint);
            Assert.All(rango.Lecturas, l => Assert.Equal(("EUR", "USD", TasasCambioCatalogo.TipoTasa.Referencia), (l.MonedaOrigen, l.MonedaDestino, l.TipoTasa)));
        }

        // ── Euro derivado ───────────────────────────────────────────────────

        [Fact]
        public void Euro_derivado_multiplica_EUR_USD_por_USD_HNL_compra_y_venta_por_separado()
        {
            var bce = new[] { new LecturaTasa("EUR", "USD", "REFERENCIA", EscenarioTasas.Viernes, 1.0876m, "BCE", null) };
            var compra = new LecturaTasa("USD", "HNL", "COMPRA", EscenarioTasas.Viernes, 26.8925m, "BCH_XLSX", null);
            var venta = new LecturaTasa("USD", "HNL", "VENTA", EscenarioTasas.Viernes, 27.0270m, "BCH_XLSX", null);

            var eurCompra = EuroDerivado.Calcular(compra, bce)!;
            var eurVenta = EuroDerivado.Calcular(venta, bce)!;

            Assert.Equal(Math.Round(1.0876m * 26.8925m, 4), eurCompra.Valor);   // 29.2483
            Assert.Equal(Math.Round(1.0876m * 27.0270m, 4), eurVenta.Valor);    // 29.3946
            Assert.Equal(("EUR", "HNL", "COMPRA"), (eurCompra.MonedaOrigen, eurCompra.MonedaDestino, eurCompra.TipoTasa));
            Assert.True(eurCompra.EsDerivada);
            Assert.Equal(TasasCambioCatalogo.Fuente.Derivada, eurCompra.Fuente);
            Assert.Contains("EUR/USD BCE 2026-10-02 = 1.0876", eurCompra.Referencia);
            Assert.Contains("USD/HNL COMPRA BCH_XLSX 2026-10-02 = 26.8925", eurCompra.Referencia);
        }

        [Fact]
        public void Euro_derivado_usa_el_EUR_USD_de_la_fecha_anterior_mas_cercana_y_no_uno_muy_viejo()
        {
            var bce = new[]
            {
                new LecturaTasa("EUR", "USD", "REFERENCIA", new DateOnly(2026, 9, 30), 1.0868m, "BCE", null),
                new LecturaTasa("EUR", "USD", "REFERENCIA", EscenarioTasas.Viernes, 1.0876m, "BCE", null),
                new LecturaTasa("EUR", "USD", "REFERENCIA", new DateOnly(2026, 10, 6), 1.0900m, "BCE", null),
            };
            var lunes = new LecturaTasa("USD", "HNL", "VENTA", new DateOnly(2026, 10, 5), 27.0300m, "BCH_XLSX", null);
            var viejo = new LecturaTasa("USD", "HNL", "VENTA", new DateOnly(2026, 9, 20), 27.0000m, "BCH_XLSX", null);

            var derivado = EuroDerivado.Calcular(lunes, bce)!;
            Assert.Equal(Math.Round(1.0876m * 27.0300m, 4), derivado.Valor);   // la del viernes, no la del martes
            Assert.Contains("EUR/USD BCE 2026-10-02", derivado.Referencia);

            Assert.Null(EuroDerivado.Calcular(viejo, bce));   // sin EUR/USD en los 7 días anteriores
        }

        // ── API del BCH (formato no verificado) ─────────────────────────────

        [Fact]
        public void Json_del_API_se_lee_de_forma_tolerante()
        {
            var arreglo = BchApiProveedor.LeerJson("[{\"Fecha\":\"2026-10-02T00:00:00\",\"Valor\":26.8925}]"u8.ToArray());
            var envuelto = BchApiProveedor.LeerJson("{\"datos\":{\"cifras\":[{\"fecha\":\"02/10/2026\",\"valor\":\"26.8925\"}]}}"u8.ToArray());
            var mayusculas = BchApiProveedor.LeerJson("{\"Value\":[{\"FECHA\":\"2026-10-02\",\"VALOR\":26.8925,\"Otro\":1}]}"u8.ToArray());
            var vacio = BchApiProveedor.LeerJson("[]"u8.ToArray());

            foreach (var r in new[] { arreglo, envuelto, mayusculas })
                Assert.Equal((EscenarioTasas.Viernes, 26.8925m), Assert.Single(r));
            Assert.Empty(vacio);
        }

        [Theory]
        [InlineData("<html>no es json</html>")]
        [InlineData("{\"mensaje\":\"sin lista\"}")]
        [InlineData("[{\"Dia\":\"2026-10-02\",\"Monto\":26.89}]")]
        public void Json_del_API_inesperado_es_permanente(string json)
        {
            var ex = Assert.Throws<ErrorFuenteException>(() => BchApiProveedor.LeerJson(System.Text.Encoding.UTF8.GetBytes(json)));
            Assert.Equal(TipoErrorFuente.Permanente, ex.Error.Tipo);
        }

        [Fact]
        public void API_deshabilitado_sin_URL_clave_o_indicadores_del_dolar()
        {
            var opt = EscenarioTasas.OpcionesDePrueba();
            Assert.False(opt.Bch.ApiHabilitado);

            _e.HabilitarApi();
            Assert.True(_e.Opciones.Bch.ApiHabilitado);
            Assert.False(_e.Opciones.Bch.EuroOficialHabilitado);

            _e.Opciones.Bch.ApiKey = "";
            Assert.False(_e.Opciones.Bch.ApiHabilitado);
            _e.HabilitarApi();
            _e.Opciones.Bch.IndicadorUsdVenta = 0;
            Assert.False(_e.Opciones.Bch.ApiHabilitado);
        }

        [Fact]
        public async Task API_manda_la_clave_en_el_encabezado_y_nunca_en_el_endpoint()
        {
            _e.HabilitarApi("header");
            _e.Opciones.Bch.ParametroAutenticacion = "Ocp-Apim-Subscription-Key";
            _e.Http.Texto(EscenarioTasas.UrlApi, ArchivosDePrueba.JsonApi(new[] { (EscenarioTasas.Viernes, 26.8925m) }));
            var proveedor = new BchApiProveedor(_e.Cliente(), Options.Create(_e.Opciones));

            var r = await proveedor.ObtenerAsync(new SolicitudLectura(EscenarioTasas.Jueves, EscenarioTasas.Viernes));

            Assert.True(r.Exitoso);
            Assert.Equal(2, r.Lecturas.Count);   // compra (618) y venta (619)
            Assert.All(_e.Http.Peticiones, p =>
            {
                Assert.Equal(EscenarioTasas.ValorApiDePrueba, p.Encabezados["Ocp-Apim-Subscription-Key"]);
                Assert.DoesNotContain(EscenarioTasas.ValorApiDePrueba, p.Url);
            });
            Assert.Contains("/api/v1/indicadores/618/cifras?fechaInicio=2026-10-01&fechaFinal=2026-10-02", _e.Http.Peticiones[0].Url);
            Assert.DoesNotContain(EscenarioTasas.ValorApiDePrueba, r.Endpoint);
        }

        [Fact]
        public async Task API_con_la_clave_en_la_URL_no_la_deja_en_el_endpoint_ni_en_el_error()
        {
            _e.HabilitarApi("query");
            _e.Http.Estado(EscenarioTasas.UrlApi, HttpStatusCode.Unauthorized);
            var proveedor = new BchApiProveedor(_e.Cliente(), Options.Create(_e.Opciones));

            var r = await proveedor.ObtenerAsync(new SolicitudLectura(EscenarioTasas.Jueves, EscenarioTasas.Viernes));

            Assert.Contains("clave=" + EscenarioTasas.ValorApiDePrueba, _e.Http.Peticiones[0].Url);
            Assert.False(r.Exitoso);
            Assert.Equal(TipoErrorFuente.Permanente, r.Error!.Tipo);
            Assert.Equal(401, r.HttpStatus);
            Assert.Single(_e.Http.Peticiones);   // un 401 no se reintenta
            Assert.DoesNotContain(EscenarioTasas.ValorApiDePrueba, r.Endpoint);
            Assert.DoesNotContain(EscenarioTasas.ValorApiDePrueba, r.Error.Mensaje);
        }

        // ── HTTP: clasificación y reintentos ────────────────────────────────

        [Theory]
        [InlineData(HttpStatusCode.NotFound, TipoErrorFuente.Permanente)]
        [InlineData(HttpStatusCode.Unauthorized, TipoErrorFuente.Permanente)]
        [InlineData(HttpStatusCode.Forbidden, TipoErrorFuente.Permanente)]
        [InlineData(HttpStatusCode.BadRequest, TipoErrorFuente.Permanente)]
        [InlineData(HttpStatusCode.RequestTimeout, TipoErrorFuente.Transitorio)]
        [InlineData(HttpStatusCode.TooManyRequests, TipoErrorFuente.Transitorio)]
        [InlineData(HttpStatusCode.InternalServerError, TipoErrorFuente.Transitorio)]
        [InlineData(HttpStatusCode.ServiceUnavailable, TipoErrorFuente.Transitorio)]
        public void Codigos_HTTP_se_clasifican_en_transitorios_y_permanentes(HttpStatusCode codigo, TipoErrorFuente esperado) =>
            Assert.Equal(esperado, ClienteHttpTasas.Clasificar(codigo).Tipo);

        [Fact]
        public async Task Error_transitorio_se_reintenta_3_veces_con_esperas_crecientes()
        {
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.ServiceUnavailable);

            var ex = await Assert.ThrowsAsync<ErrorFuenteException>(() =>
                _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel));

            Assert.Equal(TipoErrorFuente.Transitorio, ex.Error.Tipo);
            Assert.Equal(503, ex.Error.HttpStatus);
            Assert.Equal(4, _e.Http.Llamadas(EscenarioTasas.UrlExcel));
            Assert.Equal(3, _e.EsperasHttp.Count);
            Assert.InRange(_e.EsperasHttp[0].TotalSeconds, 2, 3);
            Assert.InRange(_e.EsperasHttp[1].TotalSeconds, 4, 5);
            Assert.InRange(_e.EsperasHttp[2].TotalSeconds, 8, 9);
        }

        [Fact]
        public async Task Error_permanente_no_se_reintenta()
        {
            _e.Http.Estado(EscenarioTasas.UrlExcel, HttpStatusCode.NotFound);

            var ex = await Assert.ThrowsAsync<ErrorFuenteException>(() =>
                _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel));

            Assert.Equal(TipoErrorFuente.Permanente, ex.Error.Tipo);
            Assert.Equal(1, _e.Http.Llamadas(EscenarioTasas.UrlExcel));
            Assert.Empty(_e.EsperasHttp);
        }

        [Fact]
        public async Task Retry_After_se_respeta_y_uno_muy_largo_corta_los_reintentos()
        {
            var llamadas = 0;
            _e.Http.Responder(EscenarioTasas.UrlExcel, _ =>
            {
                llamadas++;
                if (llamadas > 1) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
                return r;
            });

            await _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel);
            Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(_e.EsperasHttp));

            _e.EsperasHttp.Clear();
            _e.Http.Responder(EscenarioTasas.UrlExcel, _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return r;
            });
            var ex = await Assert.ThrowsAsync<ErrorFuenteException>(() => _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel));
            Assert.Equal(TipoErrorFuente.Transitorio, ex.Error.Tipo);
            Assert.Empty(_e.EsperasHttp);
        }

        [Fact]
        public async Task Tiempo_agotado_y_error_de_red_son_transitorios()
        {
            _e.Http.Responder(EscenarioTasas.UrlExcel, _ => throw new TaskCanceledException("timeout", new TimeoutException()));
            var tiempo = await Assert.ThrowsAsync<ErrorFuenteException>(() => _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel));
            Assert.Equal(TipoErrorFuente.Transitorio, tiempo.Error.Tipo);
            Assert.Contains("Tiempo de espera", tiempo.Error.Mensaje);

            _e.Http.Responder(EscenarioTasas.UrlExcel, _ => throw new HttpRequestException("No se puede resolver el host"));
            var red = await Assert.ThrowsAsync<ErrorFuenteException>(() => _e.Cliente().ObtenerAsync(EscenarioTasas.UrlExcel, EscenarioTasas.UrlExcel));
            Assert.Equal(TipoErrorFuente.Transitorio, red.Error.Tipo);
            Assert.Equal(8, _e.Http.Llamadas(EscenarioTasas.UrlExcel));   // 4 + 4
        }
    }
}
