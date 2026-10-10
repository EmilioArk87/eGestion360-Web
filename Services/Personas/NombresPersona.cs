using System.Globalization;
using System.Text;

namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Reglas de nombres de persona, sin acceso a datos: limpieza, validación, formato de nombre propio,
    /// nombre normalizado y composición de los campos legados nombres y apellidos.
    /// </summary>
    public static class NombresPersona
    {
        /// <summary>Quita los espacios del inicio y del final y deja un solo espacio entre palabras.</summary>
        public static string Limpiar(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
            return string.Join(' ', valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Solo letras (con tildes, ñ y diéresis), espacio, apóstrofo y guion, y al menos una letra.
        /// Se espera un texto ya limpiado con <see cref="Limpiar"/>.
        /// </summary>
        public static bool EsValido(string valor)
        {
            var hayLetra = false;
            foreach (var c in valor)
            {
                if (char.IsLetter(c)) { hayLetra = true; continue; }
                if (c == ' ' || c == '-' || c == '\'' || c == '’') continue;
                return false;
            }
            return hayLetra;
        }

        /// <summary>
        /// Pasa a formato de nombre propio cada palabra que viene toda en mayúsculas ("JUAN" → "Juan").
        /// Las palabras que ya tienen alguna minúscula no se tocan, igual que hace el script 018 al migrar.
        /// </summary>
        public static string AFormatoPropio(string valor)
        {
            if (valor.Length == 0) return valor;
            var palabras = valor.Split(' ');
            for (var i = 0; i < palabras.Length; i++)
            {
                var palabra = palabras[i];
                if (palabra.Length < 2 || palabra.Any(char.IsLower)) continue;

                var sb = new StringBuilder(palabra.Length);
                var inicio = true;
                foreach (var c in palabra)
                {
                    if (char.IsLetter(c))
                    {
                        sb.Append(inicio ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                        inicio = false;
                    }
                    else
                    {
                        sb.Append(c);
                        inicio = true; // tras un guion o un apóstrofo empieza otra palabra ("O'Brien")
                    }
                }
                palabras[i] = sb.ToString();
            }
            return string.Join(' ', palabras);
        }

        /// <summary>
        /// Nombre normalizado para detectar parecidos: mayúsculas, sin tildes ni diéresis, Ñ como N, el
        /// apóstrofo se elimina, el guion pasa a espacio y los espacios quedan simples. Debe seguir el
        /// orden primer nombre, segundo nombre, primer apellido, segundo apellido: es la misma regla
        /// con la que el script 018 llenó personas.nombre_normalizado.
        /// </summary>
        public static string Normalizar(params string?[] partes)
        {
            var texto = string.Join(' ', partes.Select(Limpiar).Where(p => p.Length > 0));
            var descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (var c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c == '\'' || c == '’') continue;
                sb.Append(c == '-' ? ' ' : char.ToUpperInvariant(c));
            }
            return Limpiar(sb.ToString());
        }

        /// <summary>Compone el campo legado personas.nombres con el primer y el segundo nombre.</summary>
        public static string ComponerNombres(string? primerNombre, string? segundoNombre) =>
            Limpiar($"{Limpiar(primerNombre)} {Limpiar(segundoNombre)}");

        /// <summary>Compone el campo legado personas.apellidos con el primer y el segundo apellido.</summary>
        public static string ComponerApellidos(string? primerApellido, string? segundoApellido) =>
            Limpiar($"{Limpiar(primerApellido)} {Limpiar(segundoApellido)}");

        /// <summary>
        /// Nombre corto para saludar o mostrar en la barra: primer nombre y primer apellido («Emilio Garay»). Si la persona
        /// todavía no tiene el nombre separado, el texto antiguo completo.
        /// </summary>
        public static string Corto(string? primerNombre, string? primerApellido, string? nombres, string? apellidos) =>
            !string.IsNullOrWhiteSpace(primerNombre) && !string.IsNullOrWhiteSpace(primerApellido)
                ? Limpiar($"{Limpiar(primerNombre)} {Limpiar(primerApellido)}")
                : Limpiar($"{Limpiar(nombres)} {Limpiar(apellidos)}");
    }
}
