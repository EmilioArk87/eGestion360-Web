namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Tenant de la petición en curso (ADR-001: se resuelve en el servidor, nunca desde la URL ni desde un campo del
    /// formulario). En F1.1 se deduce de la empresa de la sesión; en F1.5 saldrá de los claims de la cookie de
    /// autenticación sin cambiar esta interfaz. Lo consumirán los filtros globales de EF (F1.3), el interceptor de
    /// <c>SaveChanges</c> (F1.2) y el interceptor de conexión de RLS (F1.4).
    /// </summary>
    public interface ITenantContext
    {
        /// <summary>
        /// Tenant de la petición. Nulo cuando no hay: el administrador general sin empresa, una petición anónima o un
        /// proceso en segundo plano que aún no fijó el suyo.
        /// </summary>
        int? IdTenant { get; }

        /// <summary>Empresa de la petición, si la hay.</summary>
        int? IdEmpresa { get; }

        /// <summary>Identificador que une todos los registros de log de una petición; también viaja en la respuesta.</summary>
        string? IdCorrelacion { get; }
    }

    /// <summary>Tenant de una petición web: lo deja el <see cref="TenantMiddleware"/> en <c>HttpContext.Items</c>.</summary>
    public sealed class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _http;

        public TenantContext(IHttpContextAccessor http) => _http = http;

        public int? IdTenant => LeerEntero(TenantMiddleware.ClaveIdTenant);

        public int? IdEmpresa => LeerEntero(TenantMiddleware.ClaveIdEmpresa);

        public string? IdCorrelacion => _http.HttpContext?.TraceIdentifier;

        private int? LeerEntero(string clave)
            => _http.HttpContext?.Items.TryGetValue(clave, out var valor) == true && valor is int numero ? numero : null;
    }

    /// <summary>Tenant con valores fijos, para procesos que no son una petición web (jobs) y para pruebas.</summary>
    public sealed class TenantContextFijo : ITenantContext
    {
        public int? IdTenant { get; set; }
        public int? IdEmpresa { get; set; }
        public string? IdCorrelacion { get; set; }
    }
}
