using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Decide qué pantallas corren en ámbito de plataforma (<see cref="ITenantContext.EsPlataforma"/>): ahí el filtro global
    /// por tenant de EF no se aplica y el código ve las filas de todos los tenants (paso F1.3). La lista es corta, está aquí y
    /// la vigila <c>FiltroDeTenantTests</c>: una pantalla nueva no entra por accidente.
    ///
    ///   * Antes de la sesión (login, olvidé mi clave, restablecer, registro): todavía no hay tenant que filtrar. Cada una
    ///     busca a la persona que entra y arma su sesión; F1.5 lo resuelve con el tenant dentro de la cookie.
    ///   * Del administrador general (hoy el operador de la plataforma, D23): el directorio de empresas, los usuarios, las
    ///     tasas de cambio y las herramientas de la plataforma. Solo si la sesión es la suya: un administrador de empresa que
    ///     entre a las mismas rutas conserva el filtro de su tenant. Con la política RLS encendida la plataforma no ve datos de
    ///     tenant (ADR-023, D36): las pantallas que los muestran ya no están aquí y trabajan dentro del tenant de soporte que el
    ///     administrador elige (<see cref="TenantDeSoporteRequeridoPageFilter"/>, F1.4c, ADR-025).
    ///
    /// Es un filtro global por prefijo de ruta, como <see cref="AdminOnlyPageFilter"/> y
    /// <see cref="EmpresaRequeridaPageFilter"/>, para cubrir de una vez todos los handlers de la carpeta.
    /// </summary>
    public class AmbitoDePlataformaPageFilter : IPageFilter
    {
        /// <summary>Pantallas sin sesión: ven todo mientras averiguan quién entra.</summary>
        public static readonly string[] RutasSinSesion =
        {
            "/Login", "/ForgotPassword", "/ResetPassword", "/Register",
        };

        /// <summary>
        /// Pantallas del administrador general que no trabajan con los datos de un tenant: el directorio de empresas, los usuarios
        /// (tabla de la plataforma), las tasas oficiales (filas globales), el soporte y los catálogos y herramientas de la
        /// plataforma. Con otra sesión, el filtro del tenant sigue aplicando. Las pantallas de <see cref="TenantDeSoporteRequeridoPageFilter"/>
        /// ya no están aquí: trabajan dentro de un tenant de soporte (F1.4c, ADR-025).
        /// </summary>
        public static readonly string[] RutasDelAdministradorGeneral =
        {
            "/Empresas", "/UserManagement", "/Catalogos/TiposCambio",
            "/Admin/Soporte", "/Admin/Modulos", "/Admin/Outbox", "/Admin/EmailConfig", "/Admin/ResetPassword",
        };

        public static bool EsRutaSinSesion(string? ruta) => Coincide(ruta, RutasSinSesion);

        public static bool EsRutaDelAdministradorGeneral(string? ruta) => Coincide(ruta, RutasDelAdministradorGeneral);

        private static bool Coincide(string? ruta, string[] prefijos)
            => !string.IsNullOrEmpty(ruta)
               && prefijos.Any(p => ruta.Equals(p, StringComparison.OrdinalIgnoreCase)
                                    || ruta.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            var ruta = context.ActionDescriptor.ViewEnginePath;
            var http = context.HttpContext;

            var plataforma = EsRutaSinSesion(ruta)
                             || (EsRutaDelAdministradorGeneral(ruta) && AuthHelper.IsAuthenticated(http) && AuthHelper.IsAdmin(http));

            if (plataforma) http.Items[TenantMiddleware.ClaveEsPlataforma] = true;
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}
