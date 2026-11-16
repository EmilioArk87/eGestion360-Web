using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>Una fila del archivo tal como viene, con su número en la hoja de Excel.</summary>
    public sealed record FilaBrigada(int NumeroFila, string? Nombre, string? Dni, string? Edad, string? Genero);

    /// <summary>El archivo leído: nombre, huella SHA-256 (hexadecimal en minúsculas), la hoja donde estaban los datos y sus filas.</summary>
    public sealed record ArchivoBrigada(string NombreArchivo, string HashSha256, string Hoja, IReadOnlyList<FilaBrigada> Filas);

    /// <param name="Ok">Verdadero si se encontraron los encabezados y al menos una fila.</param>
    /// <param name="Error">Qué falló y cómo corregirlo, si no está bien.</param>
    public sealed record ResultadoLecturaBrigada(bool Ok, string? Error, ArchivoBrigada? Archivo)
    {
        public static ResultadoLecturaBrigada Falla(string error) => new(false, error, null);
    }

    /// <summary>
    /// Lee el listado de una brigada desde un .xlsx (ClosedXML), en memoria: el archivo no se guarda en disco. Busca, en cada hoja,
    /// una fila de encabezados con "Nombre completo", "DNI", "Edad" y "Género" (sin importar tildes ni mayúsculas; también acepta
    /// "Nombre", "Identidad" y "Sexo") y toma las filas que siguen. La hoja de resumen con fórmulas se ignora porque no trae esos
    /// encabezados. Sin acceso a datos.
    /// </summary>
    public static class LectorExcelBrigada
    {
        public const long TamanoMaximoBytes = 5 * 1024 * 1024;
        public const int FilasMaximas = 5000;

        /// <summary>Filas en las que se buscan los encabezados (por si el listado trae un título arriba).</summary>
        private const int FilasDeBusquedaDeEncabezados = 15;

        private static readonly string[] Nombre = { "nombre completo", "nombres y apellidos", "nombre" };
        private static readonly string[] Dni = { "dni", "identidad", "numero de identidad", "documento" };
        private static readonly string[] Edad = { "edad" };
        private static readonly string[] Genero = { "genero", "sexo" };

        public static ResultadoLecturaBrigada Leer(Stream contenido, string nombreArchivo)
        {
            ArgumentNullException.ThrowIfNull(contenido);

            var nombre = Path.GetFileName(nombreArchivo ?? string.Empty);
            if (!nombre.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return ResultadoLecturaBrigada.Falla("Sube un archivo de Excel con extensión .xlsx.");

            byte[] bytes;
            using (var memoria = new MemoryStream())
            {
                var buffer = new byte[81920];
                int leidos;
                while ((leidos = contenido.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memoria.Write(buffer, 0, leidos);
                    if (memoria.Length > TamanoMaximoBytes)
                        return ResultadoLecturaBrigada.Falla("El archivo pasa de 5 MB. Sube solo el listado de la brigada.");
                }
                bytes = memoria.ToArray();
            }

            if (bytes.Length == 0)
                return ResultadoLecturaBrigada.Falla("El archivo está vacío.");

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            XLWorkbook libro;
            try
            {
                libro = new XLWorkbook(new MemoryStream(bytes));
            }
            catch (Exception)
            {
                return ResultadoLecturaBrigada.Falla("No se pudo abrir el archivo. Guárdalo de nuevo como libro de Excel (.xlsx) e inténtalo otra vez.");
            }

            using (libro)
            {
                foreach (var hoja in libro.Worksheets)
                {
                    var usado = hoja.RangeUsed();
                    if (usado == null) continue;

                    var ultimaFila = usado.LastRow().RowNumber();
                    var ultimaColumna = usado.LastColumn().ColumnNumber();
                    var primeraFila = usado.FirstRow().RowNumber();

                    for (var f = primeraFila; f <= Math.Min(ultimaFila, primeraFila + FilasDeBusquedaDeEncabezados); f++)
                    {
                        var columnas = Encabezados(hoja, f, ultimaColumna);
                        if (columnas.Nombre == null || columnas.Dni == null) continue;

                        var filas = new List<FilaBrigada>();
                        for (var r = f + 1; r <= ultimaFila; r++)
                        {
                            var fila = new FilaBrigada(
                                r,
                                Texto(hoja.Cell(r, columnas.Nombre.Value)),
                                Texto(hoja.Cell(r, columnas.Dni.Value)),
                                columnas.Edad is { } e ? Texto(hoja.Cell(r, e)) : null,
                                columnas.Genero is { } g ? Texto(hoja.Cell(r, g)) : null);

                            if (fila.Nombre == null && fila.Dni == null && fila.Edad == null && fila.Genero == null) continue;   // fila vacía
                            filas.Add(fila);
                            if (filas.Count > FilasMaximas)
                                return ResultadoLecturaBrigada.Falla($"El listado pasa de {FilasMaximas} filas. Divídelo en varios archivos.");
                        }

                        if (filas.Count == 0)
                            return ResultadoLecturaBrigada.Falla($"La hoja «{hoja.Name}» tiene los encabezados pero ninguna fila con datos.");

                        return new ResultadoLecturaBrigada(true, null, new ArchivoBrigada(nombre, hash, hoja.Name, filas));
                    }
                }
            }

            return ResultadoLecturaBrigada.Falla(
                "No se encontraron los encabezados del listado. La primera fila debe tener las columnas «Nombre completo», «DNI», «Edad» y «Género».");
        }

        private sealed record Columnas(int? Nombre, int? Dni, int? Edad, int? Genero);

        private static Columnas Encabezados(IXLWorksheet hoja, int fila, int ultimaColumna)
        {
            int? nombre = null, dni = null, edad = null, genero = null;
            for (var c = 1; c <= ultimaColumna; c++)
            {
                var t = Normalizar(Texto(hoja.Cell(fila, c)));
                if (t.Length == 0) continue;
                if (nombre == null && Nombre.Contains(t)) nombre = c;
                else if (dni == null && Dni.Contains(t)) dni = c;
                else if (edad == null && Edad.Contains(t)) edad = c;
                else if (genero == null && Genero.Contains(t)) genero = c;
            }
            return new Columnas(nombre, dni, edad, genero);
        }

        /// <summary>El texto de una celda como se ve en Excel; en una fórmula, su último valor calculado. Nulo si está vacía.</summary>
        private static string? Texto(IXLCell celda)
        {
            string texto;
            try
            {
                texto = celda.HasFormula ? celda.CachedValue.ToString(CultureInfo.InvariantCulture) : celda.GetFormattedString();
            }
            catch (Exception)
            {
                texto = celda.CachedValue.ToString(CultureInfo.InvariantCulture);
            }

            texto = texto.Trim();
            return texto.Length == 0 ? null : texto;
        }

        /// <summary>Minúsculas, sin tildes, sin signos de número (N°) y con un solo espacio entre palabras.</summary>
        internal static string Normalizar(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

            var sinTildes = new StringBuilder();
            foreach (var c in valor.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c is not ('°' or 'º' or '.' or ':'))
                    sinTildes.Append(char.ToLowerInvariant(c));

            return string.Join(' ', sinTildes.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
