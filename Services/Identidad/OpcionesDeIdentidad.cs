namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Parámetros de la identidad (sección <c>Identidad</c> de la configuración). Ninguno es un secreto: se pueden cambiar sin
    /// recompilar. Los valores por defecto son los de las decisiones D46 y D47.
    /// </summary>
    public sealed class OpcionesDeIdentidad
    {
        public const string Seccion = "Identidad";

        /// <summary>Fallos de clave seguidos que bloquean la cuenta (D46).</summary>
        public int IntentosParaBloquear { get; set; } = 5;

        /// <summary>Cuánto dura el bloqueo, en minutos (D46). Se levanta solo.</summary>
        public int MinutosDeBloqueo { get; set; } = 15;

        /// <summary>Minutos sin actividad antes de que la cookie caduque.</summary>
        public int MinutosDeInactividad { get; set; } = 30;

        /// <summary>Cada cuántos segundos como máximo se vuelve a comprobar en la base que la cuenta y su acceso siguen igual.</summary>
        public int SegundosDeValidacion { get; set; } = 30;

        /// <summary>
        /// Desde qué fecha (UTC, <c>aaaa-mm-dd</c>) se exige MFA a los administradores de empresa (D47: 14 días de gracia a partir de
        /// publicar F1.5). Vacío: todavía no se les exige, solo se les avisa. El operador de la plataforma siempre lo tiene exigido.
        /// </summary>
        public string? MfaObligatoriaAdminTenantDesde { get; set; }

        /// <summary>Nombre que ve la aplicación autenticadora al agregar la cuenta.</summary>
        public string Emisor { get; set; } = "eGestion360";

        /// <summary>Vigencia de una invitación, en días.</summary>
        public int DiasDeInvitacion { get; set; } = 7;

        public DateOnly? FechaMfaAdminTenant
            => DateOnly.TryParseExact(MfaObligatoriaAdminTenantDesde, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fecha) ? fecha : null;
    }
}
