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
    ///   * Del administrador general (hoy el operador de la plataforma, D23): empresas, administración, usuarios y las
    ///     tasas de cambio. Solo si la sesión es la suya: un administrador de empresa que entre a las mismas rutas conserva el
    ///     filtro de su tenant.
    ///
    /// Es un filtro global por prefijo de ruta, como <see cref="AdminOnlyPageFilter"/> y
    /// <see cref="EmpresaRequeridaPageFilter"/>, para cubrir de una vez todos los handlers de la carpeta. Este es el interino
    /// de F1.3: F1.4 (RLS sin excepción para la plataforma) y F1.6 (acceso de soporte con motivo) lo van estrechando.
    /// </summary>
    public class AmbitoDePlataformaPageFilter : IPageFilter
    {
        /// <summary>Pantallas sin sesión: ven todo mientras averiguan quién entra.</summary>
        public static readonly string[] RutasSinSesion =
        {
            "/Login", "/ForgotPassword", "/ResetPassword", "/Register",
        };

        /// <summary>Pantallas del administrador general; con otra sesión, el filtro del tenant sigue aplicando.</summary>
        public static readonly string[] RutasDelAdministradorGeneral =
        {
            "/Empresas", "/Admin", "/UserManagement", "/Catalogos/TiposCambio",
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
