using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>Una petición vista por <see cref="ManejadorHttpFalso"/>: URL completa y encabezados.</summary>
    public sealed record PeticionRegistrada(string Url, IReadOnlyDictionary<string, string> Encabezados);

    /// <summary>
    /// Respuestas HTTP grabadas por prefijo de URL, sin red. La última regla que coincide gana, así una prueba puede
    /// cambiar la respuesta entre dos ejecuciones. Sin regla: 404.
    /// </summary>
    public sealed class ManejadorHttpFalso : HttpMessageHandler
    {
        private readonly List<(string Prefijo, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _reglas = new();

        public List<PeticionRegistrada> Peticiones { get; } = new();

        public void Responder(string prefijo, Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _reglas.Add((prefijo, responder));

        public void Contenido(string prefijo, byte[] contenido) =>
            Responder(prefijo, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(contenido) });

        public void Texto(string prefijo, string contenido) => Contenido(prefijo, Encoding.UTF8.GetBytes(contenido));

        public void Estado(string prefijo, HttpStatusCode estado) =>
            Responder(prefijo, _ => new HttpResponseMessage(estado));

        public int Llamadas(string prefijo) => Peticiones.Count(p => p.Url.StartsWith(prefijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Peticiones.Add(new PeticionRegistrada(url,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));

            var regla = _reglas.LastOrDefault(r => url.StartsWith(r.Prefijo, StringComparison.Ordinal));
            return Task.FromResult(regla.Responder != null ? regla.Responder(request) : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    public sealed class FabricaHttpFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _manejador;

        public FabricaHttpFalsa(HttpMessageHandler manejador) => _manejador = manejador;

        public HttpClient CreateClient(string name) => new(_manejador, disposeHandler: false);
    }

    /// <summary>Cada contexto, uno nuevo sobre la misma base SQLite en memoria.</summary>
    public sealed class ContextosDePrueba : ITasasCambioContextos
    {
        private readonly BaseDeDatosDePrueba _bd;

        public ContextosDePrueba(BaseDeDatosDePrueba bd) => _bd = bd;

        public ApplicationDbContext Crear() => _bd.Crear();
    }

    /// <summary>Guarda las alertas en vez de enviarlas.</summary>
    public sealed class NotificadorFalso : ITasasCambioNotificador
    {
        public List<(string Asunto, string Html)> Enviadas { get; } = new();
        public bool Resultado { get; set; } = true;

        public Task<bool> NotificarAsync(string asunto, string contenidoHtml, CancellationToken ct = default)
        {
            Enviadas.Add((asunto, contenidoHtml));
            return Task.FromResult(Resultado);
        }
    }

    /// <summary>Archivos de las fuentes generados en la prueba: Excel del BCH, XML del BCE, JSON del API.</summary>
    public static class ArchivosDePrueba
    {
        /// <summary>
        /// Excel con la forma del real: título, subtítulo, encabezados Fecha / Compra / Venta, filas con fecha de Excel
        /// y notas al pie. <paramref name="marca"/> agrega una nota distinta para que el contenido (y su hash) cambie.
        /// </summary>
        public static byte[] ExcelBch(IEnumerable<(DateOnly Fecha, decimal Compra, decimal Venta)> filas, bool conEncabezados = true,
            string hoja = BchExcelProveedor.HojaPreferida, string? marca = null, bool hojaPreviaVacia = false)
        {
            using var libro = new XLWorkbook();
            if (hojaPreviaVacia) libro.Worksheets.Add("Índice").Cell(1, 1).Value = "Contenido";
            var ws = libro.Worksheets.Add(hoja);
            var fila = 1;

            if (conEncabezados)
            {
                ws.Cell(fila++, 1).Value = "BANCO CENTRAL DE HONDURAS";
                ws.Cell(fila++, 1).Value = "Precio Promedio Diario del Dólar";
                ws.Cell(fila++, 1).Value = "Lempiras por US$1.00";
                fila++;
                ws.Cell(fila, 1).Value = "Fecha";
                ws.Cell(fila, 2).Value = "Compra";
                ws.Cell(fila, 3).Value = "Venta";
                fila++;
            }

            foreach (var (fecha, compra, venta) in filas)
            {
                ws.Cell(fila, 1).Value = fecha.ToDateTime(TimeOnly.MinValue);
                ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
                ws.Cell(fila, 2).Value = (double)compra;
                ws.Cell(fila, 3).Value = (double)venta;
                fila++;
            }

            fila++;
            ws.Cell(fila++, 1).Value = "Fuente: Banco Central de Honduras (BCH).";
            ws.Cell(fila++, 1).Value = "Nota: 1/ Precio promedio del dólar en el mercado cambiario. Actualizado al 02/10/2026.";
            ws.Cell(fila, 1).Value = "Fecha de actualización:";
            ws.Cell(fila++, 2).Value = "02/10/2026 15:00";
            if (marca != null) ws.Cell(fila, 1).Value = marca;

            using var flujo = new MemoryStream();
            libro.SaveAs(flujo);
            return flujo.ToArray();
        }

        /// <summary>XML del BCE con la forma real (gesmes + Cube anidados), varias monedas por día.</summary>
        public static byte[] XmlBce(IEnumerable<(DateOnly Fecha, decimal EurUsd)> dias)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<gesmes:Envelope xmlns:gesmes=\"http://www.gesmes.org/xml/2002-08-01\" xmlns=\"http://www.ecb.int/vocabulary/2002-08-01/eurofxref\">");
            sb.Append("<gesmes:subject>Reference rates</gesmes:subject><gesmes:Sender><gesmes:name>European Central Bank</gesmes:name></gesmes:Sender><Cube>");
            foreach (var (fecha, eurUsd) in dias.OrderByDescending(d => d.Fecha))
            {
                sb.Append($"<Cube time=\"{fecha:yyyy-MM-dd}\">");
                sb.Append($"<Cube currency=\"USD\" rate=\"{eurUsd.ToString(CultureInfo.InvariantCulture)}\"/>");
                sb.Append("<Cube currency=\"JPY\" rate=\"161.25\"/><Cube currency=\"GBP\" rate=\"0.8412\"/>");
                sb.Append("</Cube>");
            }
            sb.Append("</Cube></gesmes:Envelope>");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>JSON del API del BCH (formato supuesto): arreglo de objetos con Fecha y Valor.</summary>
        public static string JsonApi(IEnumerable<(DateOnly Fecha, decimal Valor)> cifras) =>
            "[" + string.Join(",", cifras.Select(c =>
                $"{{\"Fecha\":\"{c.Fecha:yyyy-MM-dd}T00:00:00\",\"Valor\":{c.Valor.ToString(CultureInfo.InvariantCulture)},\"Indicador\":\"x\"}}")) + "]";
    }

    /// <summary>
    /// Todo lo que necesita el orquestador para una prueba: base SQLite en memoria, HTTP falso, reloj fijo, opciones y
    /// alertas falsas. Las esperas entre reintentos HTTP se registran en vez de esperarse.
    /// </summary>
    public sealed class EscenarioTasas : IDisposable
    {
        public const string UrlExcel = "https://bch.prueba/tipo-de-cambio.xlsx";
        public const string UrlBceDiaria = "https://bce.prueba/eurofxref-daily.xml";
        public const string UrlBce90 = "https://bce.prueba/eurofxref-hist-90d.xml";
        public const string UrlApi = "https://api.bch.prueba";

        /// <summary>Valor inventado para el API del BCH en las pruebas: no es una credencial.</summary>
        public const string ValorApiDePrueba = "valor-de-prueba-123";

        /// <summary>
        /// Viernes 2 de octubre de 2026, 10:00 en Honduras (UTC-6): reloj por omisión del escenario. Antes del primer intento
        /// la fecha objetivo es hoy (<see cref="Viernes"/>), cuya tasa el BCH publicó la tarde anterior.
        /// </summary>
        public static DateTimeOffset ViernesManana => new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-6));

        /// <summary>Jueves 1 de octubre de 2026, 18:00 en Honduras (UTC-6): su fecha objetivo es el viernes 2.</summary>
        public static DateTimeOffset JuevesTarde => new(2026, 10, 1, 18, 0, 0, TimeSpan.FromHours(-6));

        /// <summary>Viernes 2 de octubre de 2026, 18:00 en Honduras (UTC-6): su fecha objetivo es el lunes 5.</summary>
        public static DateTimeOffset ViernesTarde => new(2026, 10, 2, 18, 0, 0, TimeSpan.FromHours(-6));

        public static readonly DateOnly Viernes = new(2026, 10, 2);
        public static readonly DateOnly Jueves = new(2026, 10, 1);

        /// <summary>Lunes 5 de octubre de 2026: la fecha objetivo del viernes en la tarde y del fin de semana.</summary>
        public static readonly DateOnly Lunes = new(2026, 10, 5);

        public BaseDeDatosDePrueba Bd { get; } = new();
        public ManejadorHttpFalso Http { get; } = new();
        public NotificadorFalso Notificador { get; } = new();
        public BloqueoJobEnMemoria Bloqueo { get; } = new();
        public List<TimeSpan> EsperasHttp { get; } = new();
        public RelojFijo Reloj { get; set; } = new(ViernesManana);
        public TasasCambioOptions Opciones { get; } = OpcionesDePrueba();

        public static TasasCambioOptions OpcionesDePrueba() => new()
        {
            Habilitado = true,
            Bch = new BchOptions { UrlExcel = UrlExcel },
            Bce = new BceOptions { UrlDiaria = UrlBceDiaria, UrlHistorica90d = UrlBce90 }
        };

        /// <summary>Activa el proveedor API con valores de prueba (no son credenciales reales).</summary>
        public void HabilitarApi(string claveEn = "header")
        {
            Opciones.Bch.BaseUrl = UrlApi;
            Opciones.Bch.ApiKey = ValorApiDePrueba;
            Opciones.Bch.ClaveEn = claveEn;
            Opciones.Bch.IndicadorUsdCompra = 618;
            Opciones.Bch.IndicadorUsdVenta = 619;
        }

        public ClienteHttpTasas Cliente() =>
            new(new FabricaHttpFalsa(Http), NullLogger<ClienteHttpTasas>.Instance,
                (espera, _) => { EsperasHttp.Add(espera); return Task.CompletedTask; });

        public TasaCambioSyncService Servicio()
        {
            var opciones = Options.Create(Opciones);
            var cliente = Cliente();
            var proveedores = new ITasaCambioProveedor[]
            {
                new BchApiProveedor(cliente, opciones), new BchExcelProveedor(cliente, opciones), new BceProveedor(cliente, opciones)
            };
            return new TasaCambioSyncService(new ContextosDePrueba(Bd), proveedores, new TasaCambioValidador(opciones), Bloqueo,
                Notificador, opciones, Reloj, NullLogger<TasaCambioSyncService>.Instance);
        }

        public TasaCambioConsultaService Consultas(ApplicationDbContext db) => new(db, Options.Create(Opciones), Reloj);

        /// <summary>Excel del BCH con estas filas.</summary>
        public void ResponderExcel(IEnumerable<(DateOnly, decimal, decimal)> filas, string? marca = null) =>
            Http.Contenido(UrlExcel, ArchivosDePrueba.ExcelBch(filas, marca: marca));

        /// <summary>BCE (diario y 90 días) con estos EUR/USD.</summary>
        public void ResponderBce(IEnumerable<(DateOnly, decimal)> dias)
        {
            var xml = ArchivosDePrueba.XmlBce(dias);
            Http.Contenido(UrlBceDiaria, xml);
            Http.Contenido(UrlBce90, xml);
        }

        /// <summary>Del lunes 28-sep al viernes 2-oct de 2026; el viernes es el TCR real del BCH (26.8925 / 27.0270).</summary>
        public static List<(DateOnly, decimal, decimal)> SemanaBch() => new()
        {
            (new DateOnly(2026, 9, 28), 26.8410m, 26.9752m),
            (new DateOnly(2026, 9, 29), 26.8533m, 26.9876m),
            (new DateOnly(2026, 9, 30), 26.8650m, 26.9993m),
            (new DateOnly(2026, 10, 1), 26.8788m, 27.0132m),
            (new DateOnly(2026, 10, 2), 26.8925m, 27.0270m),
        };

        public static List<(DateOnly, decimal)> SemanaBce() => new()
        {
            (new DateOnly(2026, 9, 28), 1.0850m),
            (new DateOnly(2026, 9, 29), 1.0861m),
            (new DateOnly(2026, 9, 30), 1.0868m),
            (new DateOnly(2026, 10, 1), 1.0872m),
            (new DateOnly(2026, 10, 2), 1.0876m),
        };

        /// <summary>Inserta una tasa directamente (como si ya estuviera en la base).</summary>
        public TasaCambio Sembrar(string moneda, string tipo, DateOnly fecha, decimal valor, int? idEmpresa = null,
            string estado = TasasCambioCatalogo.EstadoTasa.Vigente, string fuente = TasasCambioCatalogo.Fuente.BchXlsx)
        {
            using var db = Bd.Crear();
            var tasa = new TasaCambio
            {
                IdEmpresa = idEmpresa, MonedaOrigen = moneda, MonedaDestino = "HNL", TipoTasa = tipo, Tasa = valor,
                FechaVigencia = fecha, FechaHoraObtencion = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Fuente = idEmpresa != null ? TasasCambioCatalogo.Fuente.Manual : fuente, Estado = estado, Version = 1,
                CreadoPor = "pruebas", FechaCreacion = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            db.TasasCambio.Add(tasa);
            db.SaveChanges();
            return tasa;
        }

        public void Dispose()
        {
            Http.Dispose();
            Bd.Dispose();
        }
    }
}
