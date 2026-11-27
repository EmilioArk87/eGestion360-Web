using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Plataforma
{
    /// <summary>Lo que el operador puede hacer con el estado de un suscriptor (D80).</summary>
    public enum AccionDeEstado
    {
        /// <summary>De activo a suspendido.</summary>
        Suspender,

        /// <summary>Sigue suspendido, pero pasa de solo lectura a bloqueo o al revés.</summary>
        CambiarModo,

        /// <summary>De suspendido a activo.</summary>
        Reactivar,

        /// <summary>De suspendido a cancelado. No borra datos.</summary>
        Cancelar,

        /// <summary>De cancelado a suspendido.</summary>
        RecuperarCancelado,
    }

    /// <summary>
    /// Las reglas de la consola del operador sobre un suscriptor (F1.9a, ADR-032, D80 y D81), sin base de datos: qué cambios de estado se
    /// permiten y a cuáles se les pide modo de suspensión. El suscriptor interno (la propia plataforma) no se toca desde la consola: la base
    /// ya impide que deje de estar activo y su plan es el que sostiene al operador.
    /// </summary>
    public static class ReglasDeSuscriptor
    {
        public const int LargoMaximoDelMotivo = 300;

        /// <summary>Verdadero si la consola puede cambiar el estado o el plan de un suscriptor de este tipo.</summary>
        public static bool SePuedeAdministrar(string tipo) => tipo != TipoTenant.Interno;

        /// <summary>Las acciones que caben desde un estado, en el orden en que se ofrecen.</summary>
        public static IReadOnlyList<AccionDeEstado> AccionesDesde(string estado) => estado switch
        {
            EstadoTenant.Activo => new[] { AccionDeEstado.Suspender },
            EstadoTenant.Suspendido => new[] { AccionDeEstado.Reactivar, AccionDeEstado.CambiarModo, AccionDeEstado.Cancelar },
            EstadoTenant.Cancelado => new[] { AccionDeEstado.RecuperarCancelado },
            _ => Array.Empty<AccionDeEstado>(),
        };

        /// <summary>Si la acción deja al suscriptor suspendido y por tanto hay que decir en qué modo.</summary>
        public static bool PideModo(AccionDeEstado accion)
            => accion is AccionDeEstado.Suspender or AccionDeEstado.CambiarModo or AccionDeEstado.RecuperarCancelado;

        /// <summary>El estado en que queda el suscriptor después de la acción.</summary>
        public static string EstadoDespues(AccionDeEstado accion) => accion switch
        {
            AccionDeEstado.Reactivar => EstadoTenant.Activo,
            AccionDeEstado.Cancelar => EstadoTenant.Cancelado,
            _ => EstadoTenant.Suspendido,
        };

        public static string Nombre(AccionDeEstado accion) => accion switch
        {
            AccionDeEstado.Suspender => "Suspender",
            AccionDeEstado.CambiarModo => "Cambiar el modo de suspensión",
            AccionDeEstado.Reactivar => "Reactivar",
            AccionDeEstado.Cancelar => "Cancelar",
            AccionDeEstado.RecuperarCancelado => "Recuperar (queda suspendido)",
            _ => accion.ToString(),
        };

        public static string NombreDelModo(string modo) => modo == ModoDeSuspension.Bloqueo ? "Bloqueo total" : "Solo lectura";
    }
}
