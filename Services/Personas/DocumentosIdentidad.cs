using System.Text;

namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Reglas de documentos de identidad, sin acceso a datos. La estructura del DNI hondureño viene del
    /// RNP: 13 dígitos = municipio (4: departamento 2 + municipio 2) + año (4) + correlativo (5).
    /// </summary>
    public static class DocumentosIdentidad
    {
        public const string Dni = "DNI";
        public const string Rtn = "RTN";
        public const string PaisHonduras = "HN";

        /// <summary>Departamentos de Honduras: 01 a 18 (INE y SINIT).</summary>
        public const int DepartamentoMinimo = 1;
        public const int DepartamentoMaximo = 18;

        /// <summary>Año mínimo que se acepta en un DNI.</summary>
        public const int AnioMinimoDni = 1900;

        /// <summary>
        /// Forma normalizada de un número: sin guiones, espacios ni puntos y en mayúsculas. Es la misma
        /// regla de la columna calculada persona_documentos.numero_normalizado.
        /// </summary>
        public static string NormalizarNumero(string? numero)
        {
            if (string.IsNullOrWhiteSpace(numero)) return string.Empty;
            var sb = new StringBuilder(numero.Length);
            foreach (var c in numero.Trim())
            {
                if (c == '-' || c == ' ' || c == '.') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Partes de un DNI de 13 dígitos.</summary>
        public sealed record PartesDni(string Departamento, string Municipio, int Anio, string Correlativo);

        /// <summary>Separa un DNI normalizado en sus partes. Falso si no son exactamente 13 dígitos.</summary>
        public static bool TryAnalizarDni(string normalizado, out PartesDni partes)
        {
            partes = new PartesDni(string.Empty, string.Empty, 0, string.Empty);
            if (normalizado.Length != 13 || !normalizado.All(char.IsAsciiDigit)) return false;

            partes = new PartesDni(
                normalizado[..2],
                normalizado[..4],
                int.Parse(normalizado.AsSpan(4, 4)),
                normalizado[8..]);
            return true;
        }

        /// <summary>
        /// Mensaje de error si el departamento o el año del DNI no son válidos; nulo si lo son.
        /// El año del DNI NO es la fecha de nacimiento: aquí solo se comprueba que sea un año posible.
        /// </summary>
        public static string? ValidarPartesDni(PartesDni partes, int anioActual)
        {
            if (!int.TryParse(partes.Departamento, out var depto) || depto < DepartamentoMinimo || depto > DepartamentoMaximo)
                return "El DNI no corresponde a un departamento válido.";

            if (partes.Anio < AnioMinimoDni || partes.Anio > anioActual)
                return "El año del DNI no es válido.";

            return null;
        }

        /// <summary>
        /// Oculta un documento para los listados (decisión D4): solo se ven los 4 últimos caracteres.
        /// El DNI de 13 dígitos se muestra como ****-****-*2345; un número corto (hasta 6 caracteres) se deja tal
        /// cual, porque no es un documento de identidad (por ejemplo, un código de empleado).
        /// </summary>
        public static string Enmascarar(string? numero)
        {
            var normalizado = NormalizarNumero(numero);
            if (normalizado.Length <= 6) return normalizado;

            return normalizado.Length == 13 && normalizado.All(char.IsAsciiDigit)
                ? $"****-****-*{normalizado[^4..]}"
                : new string('*', normalizado.Length - 4) + normalizado[^4..];
        }

        /// <summary>Muestra un DNI de 13 dígitos como 0801-1990-12345; cualquier otro texto se devuelve igual.</summary>
        public static string FormatearDni(string digitos) =>
            digitos.Length == 13 && digitos.All(char.IsAsciiDigit)
                ? $"{digitos[..4]}-{digitos.Substring(4, 4)}-{digitos[8..]}"
                : digitos;
    }
}
