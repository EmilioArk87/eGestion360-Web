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

        /// <summary>
        /// Verdadero en ámbito de plataforma: el código ve las filas de todos los tenants porque el filtro global de EF no se
        /// aplica (paso F1.3). Solo lo activan las rutas de la lista revisada de <see cref="AmbitoDePlataformaPageFilter"/> y los
        /// procesos de fondo que recorren varios tenants. Ningún dato de tenant sale al navegador por este camino sin pasar por
        /// una pantalla de esa lista.
        /// </summary>
        bool EsPlataforma { get; }
    }

    /// <summary>
    /// Tenant de una petición web: lo deja el <see cref="TenantMiddleware"/> en <c>HttpContext.Items</c>. Un proceso de fondo
    /// (que no tiene petición) lo fija a mano con <see cref="Fijar"/> o <see cref="FijarPlataforma"/> en su propio ámbito.
    /// </summary>
    public sealed class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _http;
        private bool _manual;
        private int? _idTenant;
        private int? _idEmpresa;
        private bool _plataforma;

        public TenantContext(IHttpContextAccessor http) => _http = http;

        public int? IdTenant => _manual ? _idTenant : LeerEntero(TenantMiddleware.ClaveIdTenant);

        public int? IdEmpresa => _manual ? _idEmpresa : LeerEntero(TenantMiddleware.ClaveIdEmpresa);

        public string? IdCorrelacion => _http.HttpContext?.TraceIdentifier;

        public bool EsPlataforma => _manual
            ? _plataforma
            : _http.HttpContext?.Items.TryGetValue(TenantMiddleware.ClaveEsPlataforma, out var valor) == true && valor is true;

        /// <summary>Trabaja para un tenant (y una empresa) concretos, sin ámbito de plataforma. Para procesos de fondo.</summary>
        public void Fijar(int? idTenant, int? idEmpresa)
        {
            _manual = true;
            _idTenant = idTenant;
            _idEmpresa = idEmpresa;
            _plataforma = false;
        }

        /// <summary>Trabaja en ámbito de plataforma: ve todos los tenants. Para procesos de fondo que los recorren.</summary>
        public void FijarPlataforma()
        {
            _manual = true;
            _idTenant = null;
            _idEmpresa = null;
            _plataforma = true;
        }

        private int? LeerEntero(string clave)
            => _http.HttpContext?.Items.TryGetValue(clave, out var valor) == true && valor is int numero ? numero : null;
    }

    /// <summary>Tenant con valores fijos, para pruebas y para quien necesite un contexto sin petición web.</summary>
    public sealed class TenantContextFijo : ITenantContext
    {
        public int? IdTenant { get; set; }
        public int? IdEmpresa { get; set; }
        public string? IdCorrelacion { get; set; }
        public bool EsPlataforma { get; set; }
    }
}
