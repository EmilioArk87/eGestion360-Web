using System.Globalization;
using System.Text;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Reglas que debe cumplir una contraseña nueva. Vive en un solo lugar porque
    /// se aplica en cuatro puntos de entrada: alta pública (/Register), alta por
    /// administrador (/Admin/Usuarios/Create), cambio (/ChangePassword) y
    /// recuperación (/ResetPassword).
    /// </summary>
    public static class PasswordPolicy
    {
        public const int LargoMinimo = 8;

        /// <summary>
        /// BCrypt ignora en silencio todo lo que pase de 72 bytes, así que ese es el
        /// máximo real: aceptar más sería prometer una seguridad que no se aplica.
        /// Se mide en bytes UTF-8 porque un acento ocupa dos.
        /// </summary>
        public const int LargoMaximoBytes = 72;

        /// <summary>
        /// Términos del propio sistema. Una contraseña que los contenga es de las
        /// primeras que prueba quien ataca este portal en concreto.
        /// </summary>
        private static readonly string[] TerminosDelSistema =
        {
            "egestion", "egestion360", "gestion360", "siptecnologia", "siptec", "sip"
        };

        /// <summary>
        /// Contraseñas más usadas, ya normalizadas (minúsculas, sin acentos y sin
        /// sustituciones tipo "@" por "a"), de modo que "P@ssw0rd" también caiga acá.
        /// </summary>
        private static readonly HashSet<string> Comunes = new(StringComparer.Ordinal)
        {
            // Español
            "contrasena", "contrasenia", "clave", "claveclave", "usuario", "administrador",
            "admin", "administrator", "adminadmin", "sistema", "empresa", "invitado",
            "hola", "holahola", "holamundo", "bienvenido", "bienvenida", "prueba",
            "pruebas", "test", "testtest", "temporal", "cambiame", "cambiar",
            "secreto", "seguridad", "familia", "amigos", "trabajo", "oficina",
            "argentina", "paraguay", "uruguay", "chile", "colombia", "mexico",
            "espana", "brasil", "asuncion", "buenosaires", "america", "futbol",
            "boca", "river", "barcelona", "realmadrid", "cerro", "olimpia",
            "verano", "invierno", "primavera", "otono", "enero", "febrero",
            "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre",
            "octubre", "noviembre", "diciembre", "navidad", "vacaciones",
            "teamo", "amor", "amoramor", "corazon", "mama", "papa", "hijos",
            "perro", "gato", "casa", "dinero", "trabajar", "libertad",
            // Inglés y universales
            "password", "passwd", "pass", "passpass", "password1", "passwordpassword",
            "welcome", "welcome1", "letmein", "login", "logmein", "master",
            "qwerty", "qwertyui", "qwertyuiop", "asdfgh", "asdfghjk", "asdfghjkl",
            "zxcvbn", "zxcvbnm", "qazwsx", "qwertz", "azerty",
            "iloveyou", "sunshine", "princess", "dragon", "monkey", "shadow",
            "football", "baseball", "superman", "batman", "pokemon", "starwars",
            "michael", "jordan", "jennifer", "charlie", "freedom", "whatever",
            "trustno", "trustnoone", "changeme", "default", "secret", "access",
            "computer", "internet", "google", "facebook", "microsoft", "windows",
            "abcabc", "abcdef", "abcdefg", "abcdefgh", "abc123", "abcd1234",
            "123456", "1234567", "12345678", "123456789", "1234567890",
            "112233", "121212", "123123", "123321", "654321", "111111",
            "000000", "666666", "888888", "999999", "aaaaaa", "qwe123",
            "1q2w3e4r", "1qaz2wsx", "zaqwsx", "asd123", "administrator1"
        };

        /// <summary>
        /// Las mismas reglas en un objeto serializable, para que el checklist en vivo
        /// del navegador use esta fuente y no una copia paralela que se desincronice.
        /// El servidor vuelve a validar igual: esto es ayuda visual, no control.
        /// </summary>
        public static object ReglasParaCliente() => new
        {
            largoMinimo = LargoMinimo,
            largoMaximoBytes = LargoMaximoBytes,
            terminosDelSistema = TerminosDelSistema,
            comunes = Comunes.ToArray()
        };

        /// <summary>
        /// Devuelve el primer incumplimiento en texto para el usuario, o <c>null</c>
        /// si la contraseña es aceptable. Se devuelve uno solo a propósito: una lista
        /// de reproches es peor de leer que un motivo concreto por intento.
        /// </summary>
        /// <param name="password">La contraseña propuesta, sin recortar.</param>
        /// <param name="contexto">
        /// Datos de la propia cuenta (usuario, email…) que la contraseña no debe contener.
        /// Los nulos y vacíos se ignoran.
        /// </param>
        public static string? Validar(string? password, params string?[] contexto)
        {
            // Ausente o en blanco lo resuelve [Required]; acá no se opina.
            if (string.IsNullOrEmpty(password))
            {
                return null;
            }

            if (password.Length < LargoMinimo)
            {
                return $"La contraseña debe tener al menos {LargoMinimo} caracteres.";
            }

            if (Encoding.UTF8.GetByteCount(password) > LargoMaximoBytes)
            {
                return $"La contraseña no puede superar los {LargoMaximoBytes} caracteres.";
            }

            if (password.Trim().Length == 0)
            {
                return "La contraseña no puede ser solo espacios.";
            }

            // Dos formas: la normal (minúsculas y sin acentos) y la que además deshace
            // las sustituciones tipo "@" por "a". Se comparan las dos porque cada una
            // atrapa lo que a la otra se le escapa: sin deshacer sustituciones pasaría
            // "P@ssw0rd", y deshaciéndolas "12345678" se convertiría en letras y dejaría
            // de parecer una secuencia.
            var normalizada = Normalizar(password);
            var sinSustituciones = DeshacerSustituciones(normalizada);

            if (Comunes.Contains(normalizada) || Comunes.Contains(sinSustituciones))
            {
                return "Esa contraseña es de las más usadas y es de las primeras que se prueban. Elegí otra.";
            }

            if (UnSoloCaracterRepetido(normalizada))
            {
                return "La contraseña no puede ser el mismo carácter repetido.";
            }

            if (EsSecuencia(normalizada))
            {
                return "La contraseña no puede ser una secuencia como 12345678 o abcdefgh.";
            }

            foreach (var termino in TerminosDelSistema)
            {
                if (normalizada.Contains(termino, StringComparison.Ordinal) ||
                    sinSustituciones.Contains(termino, StringComparison.Ordinal))
                {
                    return "La contraseña no puede contener el nombre del sistema ni de la empresa.";
                }
            }

            foreach (var dato in contexto)
            {
                var termino = TerminoContextual(dato);
                if (termino != null &&
                    (normalizada.Contains(termino, StringComparison.Ordinal) ||
                     sinSustituciones.Contains(termino, StringComparison.Ordinal)))
                {
                    return "La contraseña no puede contener tu usuario ni tu email.";
                }
            }

            var anio = DateTime.Now.Year;
            if (normalizada == anio.ToString(CultureInfo.InvariantCulture) ||
                normalizada == (anio - 1).ToString(CultureInfo.InvariantCulture))
            {
                return "La contraseña no puede ser solamente el año.";
            }

            return null;
        }

        /// <summary>
        /// Pasa a minúsculas y quita acentos, dejando dígitos y símbolos como estaban.
        /// Sobre esta forma se miden las secuencias y las repeticiones.
        /// </summary>
        private static string Normalizar(string valor)
        {
            var sinAcentos = new StringBuilder(valor.Length);
            foreach (var c in valor.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sinAcentos.Append(c);
                }
            }

            return sinAcentos.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>
        /// Deshace las sustituciones habituales ("@" por "a", "0" por "o"…) para que
        /// "C0ntr@sena" se compare igual que "contrasena" contra la lista de comunes.
        /// </summary>
        private static string DeshacerSustituciones(string normalizada)
        {
            var resultado = new StringBuilder(normalizada.Length);
            foreach (var c in normalizada)
            {
                resultado.Append(c switch
                {
                    '@' or '4' => 'a',
                    '3' => 'e',
                    '1' or '!' or '|' => 'i',
                    '0' => 'o',
                    '$' or '5' => 's',
                    '7' => 't',
                    _ => c
                });
            }

            return resultado.ToString();
        }

        /// <summary>
        /// Del usuario o el email saca la parte que no conviene ver dentro de la
        /// contraseña. Ignora los términos de menos de 4 caracteres: con un usuario
        /// como "ana" se rechazarían contraseñas legítimas por casualidad.
        /// </summary>
        private static string? TerminoContextual(string? dato)
        {
            if (string.IsNullOrWhiteSpace(dato))
            {
                return null;
            }

            var termino = dato.Trim();

            // Del email interesa la parte local: "egaray@siptecnologia.xyz" -> "egaray".
            var arroba = termino.IndexOf('@');
            if (arroba > 0)
            {
                termino = termino.Substring(0, arroba);
            }

            termino = Normalizar(termino);
            return termino.Length >= 4 ? termino : null;
        }

        private static bool UnSoloCaracterRepetido(string valor)
        {
            foreach (var c in valor)
            {
                if (c != valor[0])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Detecta secuencias corridas en todo el largo, ascendentes o descendentes:
        /// 12345678, 87654321, abcdefgh.
        /// </summary>
        private static bool EsSecuencia(string valor)
        {
            var sube = true;
            var baja = true;

            for (var i = 1; i < valor.Length; i++)
            {
                var salto = valor[i] - valor[i - 1];
                if (salto != 1) sube = false;
                if (salto != -1) baja = false;
            }

            return sube || baja;
        }
    }
}
