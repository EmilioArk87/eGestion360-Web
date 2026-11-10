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

        /// <summary>El modo que rige: solo <c>Aplicar</c> (sin importar mayúsculas) niega; todo lo demás registra.</summary>
        public ModoDePermisos ModoActual
            => string.Equals(Modo?.Trim(), nameof(ModoDePermisos.Aplicar), StringComparison.OrdinalIgnoreCase)
                ? ModoDePermisos.Aplicar
                : ModoDePermisos.Registrar;
    }
}
