using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Las pantallas de mantenimiento de los datos de un tenant (módulos y roles de sus empresas, sus personas y sus usuarios)
    /// necesitan saber DE CUÁL tenant. Con la política RLS encendida la base solo entrega las filas del tenant del contexto
    /// (ADR-023, D36), así que el administrador general las usa dentro de un tenant de soporte que elige en
    /// <c>/Admin/Soporte</c> (F1.4c, ADR-025). Sin tenant de soporte, este filtro lo lleva a elegirlo y lo devuelve después a la
    /// pantalla que pidió.
    ///
    /// Solo actúa sobre el administrador general sin empresa en la sesión: un administrador de empresa trabaja en su tenant, y
    /// una petición sin sesión la resuelve cada página con su propio redirección al inicio de sesión. Es un filtro global por
    /// prefijo de ruta, como <see cref="AdminOnlyPageFilter"/> y <see cref="EmpresaRequeridaPageFilter"/>, para cubrir de una vez
    /// todos los handlers de la carpeta.
    /// </summary>
    public class TenantDeSoporteRequeridoPageFilter : IPageFilter
    {
        /// <summary>Pantallas que trabajan con los datos de un tenant; la lista la vigila <c>FiltroDeTenantTests</c>.</summary>
        public static readonly string[] RutasQueExigenTenantDeSoporte =
        {
            "/Admin/EmpresaModulos", "/Admin/EmpresaRoles", "/Admin/RolesPermisos", "/Admin/Personas", "/Admin/Usuarios",
        };

        /// <summary>Página donde el administrador general elige el tenant de soporte.</summary>
        public const string PaginaSoporte = "/Admin/Soporte/Index";

        public static bool Aplica(string? ruta)
            => !string.IsNullOrEmpty(ruta)
               && RutasQueExigenTenantDeSoporte.Any(p => ruta.Equals(p, StringComparison.OrdinalIgnoreCase)
                                                         || ruta.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            if (!Aplica(context.ActionDescriptor.ViewEnginePath)) return;

            var http = context.HttpContext;
            if (!AuthHelper.IsAuthenticated(http) || !AuthHelper.IsAdmin(http)) return;
            if (AuthHelper.GetEmpresaId(http) is > 0) return;
            if (AuthHelper.GetTenantDeSoporte(http) != null) return;

            // La ruta de la petición, para volver a ella después de elegir; el destino se valida como local al usarlo.
            var volver = http.Request.Path + http.Request.QueryString;
            context.Result = new RedirectToPageResult(PaginaSoporte, new { volver });
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}
