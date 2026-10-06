using System.Globalization;
using System.Text;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>Utilidades de texto y números de las fuentes de tasas.</summary>
    public static class TextoTasas
    {
        private static readonly CultureInfo Honduras = CultureInfo.GetCultureInfo("es-HN");

        private static readonly string[] FormatosFecha =
        {
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "d-MMM-yyyy", "dd-MMM-yyyy", "d MMM yyyy", "dd MMM yyyy"
        };

        /// <summary>Minúsculas y sin tildes, para comparar encabezados y nombres de hoja.</summary>
        public static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            var descompuesto = texto.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (var c in descompuesto)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>
        /// Fecha escrita como texto, con día antes del mes ("02/10/2026", "2-oct-2026") o ISO ("2026-10-02",
        /// "2026-10-02T00:00:00"). Solo formatos exactos: una nota al pie nunca se lee como fecha.
        /// </summary>
        public static bool TryLeerFecha(string? texto, out DateOnly fecha)
        {
            fecha = default;
            if (string.IsNullOrWhiteSpace(texto)) return false;
            var limpio = texto.Trim().TrimEnd('.');

            foreach (var cultura in new[] { CultureInfo.InvariantCulture, Honduras })
            {
                if (DateTime.TryParseExact(limpio, FormatosFecha, cultura, DateTimeStyles.AllowWhiteSpaces, out var dt))
                {
                    fecha = DateOnly.FromDateTime(dt);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Número escrito como texto: admite punto o coma decimal y quita símbolos de moneda y espacios
        /// ("L 26.8925", "26,8925"). Con punto y coma a la vez, la coma se toma como separador de miles.
        /// </summary>
        public static bool TryLeerDecimal(string? texto, out decimal valor)
        {
            valor = 0m;
            if (string.IsNullOrWhiteSpace(texto)) return false;
            var limpio = new string(texto.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
            if (limpio.Length == 0) return false;
            if (limpio.Contains('.') && limpio.Contains(',')) limpio = limpio.Replace(",", "");
            else limpio = limpio.Replace(',', '.');
            return decimal.TryParse(limpio, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out valor);
        }

        /// <summary>Corta un texto al largo de su columna.</summary>
        public static string? Cortar(string? texto, int largo) =>
            texto == null || texto.Length <= largo ? texto : texto[..largo];

        /// <summary>Redondeo con el que se comparan dos tasas: 4 decimales, como publica el BCH.</summary>
        public static decimal Redondear4(decimal valor) => Math.Round(valor, 4, MidpointRounding.AwayFromZero);

        public static string Formatear(decimal valor) => valor.ToString("0.0000####", CultureInfo.InvariantCulture);
    }
}
