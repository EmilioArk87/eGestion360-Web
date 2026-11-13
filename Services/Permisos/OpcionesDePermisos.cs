namespace eGestion360Web.Services.Permisos
{
    /// <summary>Qué hace el filtro de permisos cuando una persona no tiene el permiso que pide la pantalla.</summary>
    public enum ModoDePermisos
    {
        /// <summary>Anota en el log lo que negaría y deja pasar a todos. Es el modo de partida (F1.6b): permite ver el efecto real antes de bloquear a nadie.</summary>
        Registrar,

        /// <summary>Niega de verdad: responde 403. Se activa por etapas, desde F1.6c.</summary>
        Aplicar,
    }

    /// <summary>
    /// Sección <c>Permisos</c> de la configuración (paso F1.6b). <c>Permisos:Modo</c> vale <c>Registrar</c> (por omisión) o <c>Aplicar</c>;
    /// cualquier otro valor, o su ausencia, deja el modo seguro: <c>Registrar</c>. Se lee en cada petición (<c>IOptionsMonitor</c>), así que
    /// cambiarlo en la configuración rige sin reiniciar el sitio.
    /// </summary>
    public sealed class OpcionesDePermisos
    {
        public const string Seccion = "Permisos";

        /// <summary>Texto configurado; use <see cref="ModoActual"/>.</summary>
        public string? Modo { get; set; }

        /// <summary>
        /// Las pantallas de plataforma y de administración de la empresa. Desde F1.6c ya no comprueban el rol en su código: las protege solo
        /// este filtro, así que aplican siempre salvo que la configuración diga otra cosa de forma expresa (una lista vacía).
        /// </summary>
        public static readonly IReadOnlyList<string> CarpetasDeAdministracion = new[]
        {
            "/Admin", "/Empresas", "/UserManagement", "/DebugUsers", "/ResetCodesHistory", "/ValidarEmails",
        };

        /// <summary>
        /// Carpetas o pantallas que aplican aunque <see cref="Modo"/> sea <c>Registrar</c> (paso F1.6c): prefijos de ruta como <c>/Admin</c> o
        /// <c>/UserManagement</c>. Cubren la ruta exacta y todo lo que cuelga de ella. Así el bloqueo se enciende por etapas, carpeta por carpeta.
        /// Si no se configura, valen <see cref="CarpetasDeAdministracion"/>: quitar la clave de la configuración no deja esas pantallas abiertas.
        /// </summary>
        public string[]? AplicarEn { get; set; }

        /// <summary>El modo global: solo <c>Aplicar</c> (sin importar mayúsculas) niega; todo lo demás registra.</summary>
        public ModoDePermisos ModoActual
            => string.Equals(Modo?.Trim(), nameof(ModoDePermisos.Aplicar), StringComparison.OrdinalIgnoreCase)
                ? ModoDePermisos.Aplicar
                : ModoDePermisos.Registrar;

        /// <summary>El modo que rige para una pantalla: <c>Aplicar</c> si el modo global lo es o si su ruta cuelga de un prefijo de <see cref="AplicarEn"/>.</summary>
        public ModoDePermisos ModoPara(string? ruta)
        {
            if (ModoActual == ModoDePermisos.Aplicar) return ModoDePermisos.Aplicar;
            if (string.IsNullOrEmpty(ruta)) return ModoDePermisos.Registrar;

            foreach (var prefijo in (IReadOnlyList<string?>?)AplicarEn ?? CarpetasDeAdministracion)
            {
                var p = prefijo?.Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(p)) continue;
                if (ruta.Equals(p, StringComparison.OrdinalIgnoreCase) || ruta.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))
                    return ModoDePermisos.Aplicar;
            }
            return ModoDePermisos.Registrar;
        }
    }
}
