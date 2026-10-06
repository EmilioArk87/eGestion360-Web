using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>Una fila útil del Excel del BCH: 1 USD = Compra/Venta HNL en esa fecha.</summary>
    public sealed record FilaTipoCambio(DateOnly Fecha, decimal Compra, decimal Venta, int NumeroFila);

    /// <summary>Lo leído del libro: la hoja usada, si traía encabezados y las filas con fecha, compra y venta.</summary>
    public sealed record LibroTipoCambio(string Hoja, bool ConEncabezados, IReadOnlyList<FilaTipoCambio> Filas);

    /// <summary>
    /// Excel "Precio Promedio Diario del Dólar" del BCH, el mismo que descargaba la aplicación WinForms.
    ///
    ///   * Hoja "Tipo de Cambio Diario" si existe (sin distinguir tildes ni mayúsculas); si no, la primera.
    ///   * La fila de encabezados es la primera (de las 30 primeras) con celdas que contienen "fecha", "compra" y
    ///     "venta". Sin encabezados se toman las columnas A, B y C.
    ///   * Se ignora toda fila cuya fecha no sea válida: títulos, notas al pie, fuentes.
    ///
    /// Reemplaza a Services/BchTasaCambioService.cs (nunca se registró), que guardaba HNL→USD (al revés de la
    /// convención: aquí 1 USD = tasa HNL) y sobrescribía la tasa con un MERGE en vez de versionarla.
    /// </summary>
    public sealed class BchExcelProveedor : ITasaCambioProveedor
    {
        public const string HojaPreferida = "Tipo de Cambio Diario";
        private const int FilasParaBuscarEncabezados = 30;

        private readonly ClienteHttpTasas _http;
        private readonly TasasCambioOptions _opt;

        public BchExcelProveedor(ClienteHttpTasas http, IOptions<TasasCambioOptions> opciones)
        {
            _http = http;
            _opt = opciones.Value;
        }

        public string Fuente => TasasCambioCatalogo.Fuente.BchXlsx;

        public bool Habilitado => !string.IsNullOrWhiteSpace(_opt.Bch.UrlExcel);

        public async Task<ResultadoProveedor> ObtenerAsync(SolicitudLectura solicitud, CancellationToken ct = default)
        {
            var url = _opt.Bch.UrlExcel;
            RespuestaFuente? respuesta = null;
            try
            {
                respuesta = await _http.ObtenerAsync(url, url, ct: ct);
                var libro = LeerLibro(respuesta.Contenido);
                var local = _opt.MonedaLocalEfectiva;

                var lecturas = new List<LecturaTasa>();
                foreach (var fila in libro.Filas.Where(f => f.Fecha >= solicitud.Desde))
                {
                    var referencia = TextoTasas.Cortar($"BCH Excel, hoja '{libro.Hoja}', fila {fila.NumeroFila}", 400);
                    lecturas.Add(new LecturaTasa("USD", local, TasasCambioCatalogo.TipoTasa.Compra, fila.Fecha, fila.Compra, Fuente, referencia));
                    lecturas.Add(new LecturaTasa("USD", local, TasasCambioCatalogo.TipoTasa.Venta, fila.Fecha, fila.Venta, Fuente, referencia));
                }

                return new ResultadoProveedor
                {
                    Fuente = Fuente, Lecturas = lecturas, Endpoint = url,
                    HttpStatus = respuesta.HttpStatus, HashContenido = respuesta.HashContenido
                };
            }
            catch (ErrorFuenteException ex)
            {
                return new ResultadoProveedor
                {
                    Fuente = Fuente, Error = ex.Error, Endpoint = url,
                    HttpStatus = respuesta?.HttpStatus ?? ex.Error.HttpStatus, HashContenido = respuesta?.HashContenido
                };
            }
        }

        /// <summary>Lee el libro. Lanza <see cref="ErrorFuenteException"/> (permanente) si no se puede abrir o no trae datos.</summary>
        public static LibroTipoCambio LeerLibro(byte[] contenido)
        {
            XLWorkbook libro;
            try
            {
                libro = new XLWorkbook(new MemoryStream(contenido));
            }
            catch (Exception ex)
            {
                throw ErrorFuenteException.FormatoInesperado("no se pudo abrir el Excel del BCH", ex);
            }

            using (libro)
            {
                try
                {
                    var preferida = TextoTasas.Normalizar(HojaPreferida);
                    var hoja = libro.Worksheets.FirstOrDefault(h => TextoTasas.Normalizar(h.Name) == preferida)
                               ?? libro.Worksheets.First();

                    var (filaEncabezados, colFecha, colCompra, colVenta) = BuscarEncabezados(hoja);
                    var filas = new Dictionary<DateOnly, FilaTipoCambio>();

                    foreach (var fila in hoja.RowsUsed())
                    {
                        var numero = fila.RowNumber();
                        if (numero <= filaEncabezados) continue;

                        if (!TryLeerFecha(fila.Cell(colFecha), out var fecha)) continue;
                        if (!TryLeerValor(fila.Cell(colCompra), out var compra) || compra <= 0) continue;
                        if (!TryLeerValor(fila.Cell(colVenta), out var venta) || venta <= 0) continue;

                        // Si una fecha se repite, vale la última fila (la más abajo).
                        filas[fecha] = new FilaTipoCambio(fecha, compra, venta, numero);
                    }

                    if (filas.Count == 0)
                        throw ErrorFuenteException.FormatoInesperado(
                            $"la hoja '{hoja.Name}' no trae filas con fecha, compra y venta");

                    return new LibroTipoCambio(hoja.Name, filaEncabezados > 0,
                        filas.Values.OrderBy(f => f.Fecha).ToList());
                }
                catch (ErrorFuenteException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw ErrorFuenteException.FormatoInesperado("no se pudo leer el Excel del BCH", ex);
                }
            }
        }

        /// <summary>Fila y columnas de los encabezados; (0, A, B, C) si no hay una fila con los tres.</summary>
        private static (int Fila, int Fecha, int Compra, int Venta) BuscarEncabezados(IXLWorksheet hoja)
        {
            foreach (var fila in hoja.RowsUsed().Take(FilasParaBuscarEncabezados))
            {
                int? fecha = null, compra = null, venta = null;
                foreach (var celda in fila.CellsUsed())
                {
                    if (!celda.Value.IsText) continue;
                    var texto = TextoTasas.Normalizar(celda.Value.GetText());
                    var columna = celda.Address.ColumnNumber;
                    if (fecha == null && texto.Contains("fecha")) fecha = columna;
                    else if (compra == null && texto.Contains("compra")) compra = columna;
                    else if (venta == null && texto.Contains("venta")) venta = columna;
                }
                if (fecha != null && compra != null && venta != null)
                    return (fila.RowNumber(), fecha.Value, compra.Value, venta.Value);
            }
            return (0, 1, 2, 3);
        }

        private static bool TryLeerFecha(IXLCell celda, out DateOnly fecha)
        {
            fecha = default;
            var valor = celda.Value;
            if (valor.IsDateTime)
            {
                fecha = DateOnly.FromDateTime(valor.GetDateTime());
                return true;
            }
            if (valor.IsNumber)
            {
                // Un número de serie de Excel sin formato de fecha; solo se acepta en un rango razonable.
                var numero = valor.GetNumber();
                if (numero < 36526 || numero > 73051) return false;   // 2000-01-01 a 2100-01-01
                fecha = DateOnly.FromDateTime(DateTime.FromOADate(numero));
                return true;
            }
            return valor.IsText && TextoTasas.TryLeerFecha(valor.GetText(), out fecha);
        }

        private static bool TryLeerValor(IXLCell celda, out decimal valor)
        {
            valor = 0m;
            var contenido = celda.Value;
            if (contenido.IsNumber)
            {
                var numero = contenido.GetNumber();
                if (double.IsNaN(numero) || double.IsInfinity(numero) || numero > 1_000_000) return false;
                valor = Math.Round((decimal)numero, 8);
                return true;
            }
            return contenido.IsText && TextoTasas.TryLeerDecimal(contenido.GetText(), out valor) && valor <= 1_000_000m;
        }
    }
}
