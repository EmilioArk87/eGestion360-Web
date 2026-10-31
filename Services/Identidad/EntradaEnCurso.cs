using System.Globalization;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Un inicio de sesión a medias: la clave ya fue correcta pero falta el segundo factor o elegir dónde trabajar. Vive en la sesión
    /// (servidor), dura <see cref="Vigencia"/> y no da acceso a nada: la identidad de verdad solo existe cuando se emite la cookie.
    /// Si el sitio se reinicia a mitad, la persona vuelve a escribir su clave.
    /// </summary>
    /// <param name="IdCuenta">Cuenta cuya clave fue correcta.</param>
    /// <param name="Nombre">Con qué nombre entró (para la auditoría).</param>
    /// <param name="Exigencia">Lo que se le pide sobre la verificación en dos pasos.</param>
    /// <param name="MfaVerificada">Ya probó el segundo factor (o terminó de activarlo) en este inicio de sesión.</param>
    public sealed record EntradaEnCurso(int IdCuenta, string Nombre, ExigenciaDeMfa Exigencia, bool MfaVerificada)
    {
        public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

        private const string ClaveCuenta = "Entrada.Cuenta";
        private const string ClaveNombre = "Entrada.Nombre";
        private const string ClaveExigencia = "Entrada.Exigencia";
        private const string ClaveMfa = "Entrada.Mfa";
        private const string ClaveHasta = "Entrada.Hasta";

        /// <summary>Qué página sigue: el segundo factor, su alta, o elegir el sitio (que entra directo si solo hay uno).</summary>
        public string SiguientePagina => Exigencia switch
        {
            ExigenciaDeMfa.Verificar when !MfaVerificada => "/Mfa",
            ExigenciaDeMfa.Alta when !MfaVerificada => "/MfaAlta",
            _ => "/SeleccionarContexto"
        };

        public void Guardar(ISession sesion, DateTime ahoraUtc)
        {
            sesion.SetString(ClaveCuenta, IdCuenta.ToString(CultureInfo.InvariantCulture));
            sesion.SetString(ClaveNombre, Nombre);
            sesion.SetString(ClaveExigencia, Exigencia.ToString());
            sesion.SetString(ClaveMfa, MfaVerificada ? "1" : "0");
            sesion.SetString(ClaveHasta, ahoraUtc.Add(Vigencia).Ticks.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>La entrada en curso de esta sesión, o <c>null</c> si no hay o ya venció.</summary>
        public static EntradaEnCurso? Leer(ISession sesion, DateTime ahoraUtc)
        {
            if (!int.TryParse(sesion.GetString(ClaveCuenta), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idCuenta)) return null;
            if (!long.TryParse(sesion.GetString(ClaveHasta), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || new DateTime(ticks, DateTimeKind.Utc) <= ahoraUtc)
            {
                Limpiar(sesion);
                return null;
            }

            var exigencia = Enum.TryParse<ExigenciaDeMfa>(sesion.GetString(ClaveExigencia), out var e) ? e : ExigenciaDeMfa.NoRequerida;
            return new EntradaEnCurso(idCuenta, sesion.GetString(ClaveNombre) ?? string.Empty, exigencia, sesion.GetString(ClaveMfa) == "1");
        }

        public static void Limpiar(ISession sesion)
        {
            foreach (var clave in new[] { ClaveCuenta, ClaveNombre, ClaveExigencia, ClaveMfa, ClaveHasta }) sesion.Remove(clave);
        }
    }
}
