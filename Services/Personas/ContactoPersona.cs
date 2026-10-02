using System.Text.RegularExpressions;

namespace eGestion360Web.Services.Personas
{
    /// <summary>Reglas de teléfonos y correos de una persona, sin acceso a datos.</summary>
    public static class ContactoPersona
    {
        /// <summary>Prefijo telefónico de Honduras.</summary>
        public const string PrefijoHonduras = "504";

        /// <summary>Dígitos de un teléfono hondureño sin prefijo.</summary>
        public const int DigitosTelefono = 8;

        /// <summary>Largo máximo del correo (personas.email es VARCHAR(150)).</summary>
        public const int LargoMaximoCorreo = 150;

        private static readonly Regex FormatoCorreo = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        /// <summary>
        /// Normaliza un teléfono de Honduras a sus 8 dígitos. Acepta espacios, guiones, puntos,
        /// paréntesis y el prefijo +504 o 504. Falso si el texto no es un teléfono de 8 dígitos.
        /// </summary>
        public static bool TryNormalizarTelefono(string? entrada, out string digitos)
        {
            digitos = string.Empty;
            if (string.IsNullOrWhiteSpace(entrada)) return false;

            var texto = entrada.Trim();
            for (var i = 0; i < texto.Length; i++)
            {
                var c = texto[i];
                var permitido = char.IsAsciiDigit(c) || c == ' ' || c == '-' || c == '.' || c == '(' || c == ')'
                                || (c == '+' && i == 0);
                if (!permitido) return false;
            }

            var soloDigitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
            if (soloDigitos.Length == DigitosTelefono + PrefijoHonduras.Length && soloDigitos.StartsWith(PrefijoHonduras, StringComparison.Ordinal))
                soloDigitos = soloDigitos[PrefijoHonduras.Length..];

            if (soloDigitos.Length != DigitosTelefono) return false;

            digitos = soloDigitos;
            return true;
        }

        /// <summary>Muestra un teléfono de 8 dígitos como 9999-9999; cualquier otro texto se devuelve igual.</summary>
        public static string FormatearTelefono(string digitos) =>
            digitos.Length == DigitosTelefono && digitos.All(char.IsAsciiDigit)
                ? $"{digitos[..4]}-{digitos[4..]}"
                : digitos;

        /// <summary>Normaliza un correo (sin espacios en los extremos y en minúsculas). Falso si el formato no es válido.</summary>
        public static bool TryNormalizarCorreo(string? entrada, out string correo)
        {
            correo = string.Empty;
            if (string.IsNullOrWhiteSpace(entrada)) return false;

            var texto = entrada.Trim().ToLowerInvariant();
            if (texto.Length > LargoMaximoCorreo) return false;

            try
            {
                if (!FormatoCorreo.IsMatch(texto)) return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            correo = texto;
            return true;
        }
    }
}
