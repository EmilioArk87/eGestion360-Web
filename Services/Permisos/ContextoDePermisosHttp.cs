using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Arma el <see cref="ContextoDePermisos"/> de una petición con lo que ya está en ella: la cookie de identidad (F1.5), el tenant
    /// que fijó <see cref="TenantMiddleware"/> y los módulos que concede el plan y tiene activos la empresa (F1.7). Reúne en un solo
    /// lugar las lecturas de <see cref="AuthHelper"/> para que el evaluador, que no conoce <c>HttpContext</c>, responda lo mismo que
    /// responden hoy las guardas de cada pantalla.
    /// </summary>
    public static class ContextoDePermisosHttp
    {
        public static ContextoDePermisos Crear(HttpContext http)
        {
            // El operador es quien hoy pasa cualquier guarda (AuthHelper.IsAdmin): mismo criterio para no dar más ni menos que hoy.
            var esOperador = AuthHelper.IsAdmin(http);

            // El tenant de la petición (el de la cookie o el de soporte del operador). El evaluador no lee los roles de otro tenant.
            int? idTenant = http.Items.TryGetValue(TenantMiddleware.ClaveIdTenant, out var valor) && valor is int tenant ? tenant : null;

            var rolLegado = AuthHelper.IsEmpresaAdmin(http) ? AuthHelper.EmpresaAdminRole : AuthHelper.EmpresaUserRole;

            // Los módulos con permisos que esta persona puede usar: la misma pregunta que hacen hoy las guardas (HasModulo).
            var modulos = CatalogoDePermisos.ModulosConPermisos
                .Where(m => AuthHelper.HasModulo(http, m))
                .ToHashSet(StringComparer.Ordinal);

            return new ContextoDePermisos(esOperador, idTenant, AuthHelper.GetEmpresaRolId(http), rolLegado, modulos);
        }
    }
}
