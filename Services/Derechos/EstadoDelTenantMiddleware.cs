using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Derechos
{
    /// <summary>
    /// Aplica en cada petición el estado del tenant y deja sus derechos a mano para las pantallas (paso F1.7). Va justo después
    /// de <see cref="TenantMiddleware"/>, que ya fijó el tenant. Un tenant <b>activo</b> pasa. Uno <b>suspendido en solo lectura</b>
    /// puede consultar pero no guardar: toda petición que no sea de lectura vuelve a <see cref="PaginaSinAcceso"/>. Uno
    /// <b>cancelado</b> o suspendido en <b>bloqueo</b> no entra a nada. El administrador general (el operador de la plataforma)
    /// no se bloquea nunca: entra a un tenant suspendido para atenderlo. Entrar, salir y la página que explica el motivo quedan
    /// siempre disponibles; si no, nadie podría ver por qué no entra.
    ///
    /// Las peticiones sin tenant (anónimas, el administrador general sin tenant de soporte) pasan tal cual.
    /// </summary>
    public sealed class EstadoDelTenantMiddleware
    {
        /// <summary>Clave de <c>HttpContext.Items</c> con los <see cref="DerechosDeLaPeticion"/> (ausente si la petición no tiene tenant).</summary>
        public const string ClaveDerechos = "eg360.derechos";

        /// <summary>Página que explica por qué el tenant no puede entrar o guardar. Responde 403.</summary>
        public const string PaginaSinAcceso = "/SinAcceso";

        /// <summary>Rutas que nunca se bloquean: iniciar y cerrar sesión, el error y la página del motivo.</summary>
        public static readonly string[] RutasExentas = { "/Login", "/Logout", PaginaSinAcceso, "/Error" };

        private static readonly string[] MetodosDeLectura = { "GET", "HEAD", "OPTIONS" };

        private readonly RequestDelegate _siguiente;

        public EstadoDelTenantMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

        public async Task InvokeAsync(HttpContext context, IDerechosDeUso derechos, ILogger<EstadoDelTenantMiddleware> log)
        {
            if (LeerEntero(context, TenantMiddleware.ClaveIdTenant) is { } idTenant)
            {
                var delTenant = await derechos.DelTenantAsync(idTenant, context.RequestAborted);
                if (delTenant != null)
                {
                    var idEmpresa = LeerEntero(context, TenantMiddleware.ClaveIdEmpresa);
                    var modulos = idEmpresa is > 0
                        ? await derechos.ModulosActivosDeLaEmpresaAsync(idEmpresa.Value, context.RequestAborted)
                        : new HashSet<string>();
                    context.Items[ClaveDerechos] = new DerechosDeLaPeticion(delTenant, modulos);

                    if (delTenant.PlanCodigo == null)
                        log.LogWarning("El tenant {IdTenant} ({Codigo}) no tiene un plan vigente: no concede ningún derecho.", delTenant.IdTenant, delTenant.Codigo);

                    if (Bloquea(delTenant, context.Request.Method, context.Request.Path, AuthHelper.IsAdmin(context)))
                    {
                        log.LogWarning("Tenant {IdTenant} ({Codigo}) {Estado}/{Modo}: se rechaza {Metodo} {Ruta}.",
                            delTenant.IdTenant, delTenant.Codigo, delTenant.Estado, delTenant.ModoSuspension, context.Request.Method, context.Request.Path);
                        await Rechazar(context);
                        return;
                    }
                }
            }

            await _siguiente(context);
        }

        /// <summary>
        /// Si la petición debe rechazarse por el estado del tenant. Un operador (administrador general) nunca se rechaza. Las rutas
        /// exentas tampoco. Cancelado y suspendido en bloqueo rechazan todo; suspendido en lectura, todo lo que no sea consultar.
        /// </summary>
        public static bool Bloquea(DerechosDelTenant tenant, string metodo, PathString ruta, bool esOperador)
        {
            if (esOperador || tenant.Opera) return false;
            if (RutasExentas.Any(r => ruta.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase))) return false;
            if (tenant.Bloqueado) return true;
            return tenant.EnSoloLectura && !MetodosDeLectura.Contains(metodo, StringComparer.OrdinalIgnoreCase);
        }

        private static Task Rechazar(HttpContext context)
        {
            // Una llamada de datos (fetch) no sigue una redirección a una página: recibe el 403 directo.
            var pideDatos = context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            if (pideDatos)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(PaginaSinAcceso);
            return Task.CompletedTask;
        }

        private static int? LeerEntero(HttpContext context, string clave)
            => context.Items.TryGetValue(clave, out var valor) && valor is int numero ? numero : null;
    }
}
