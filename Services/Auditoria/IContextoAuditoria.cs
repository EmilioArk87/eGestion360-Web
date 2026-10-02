namespace eGestion360Web.Services.Auditoria
{
    /// <summary>
    /// Quién hace el cambio y desde dónde, para las filas de la bitácora. Lo usa
    /// <see cref="AuditoriaCambiosInterceptor"/>; en la aplicación sale de la sesión.
    /// </summary>
    public interface IContextoAuditoria
    {
        /// <summary>Usuario de la sesión (el mismo que ya se guarda en creado_por) o "system" si no hay sesión.</summary>
        string Usuario { get; }

        /// <summary>Empresa de la sesión, si la hay.</summary>
        int? IdEmpresa { get; }

        /// <summary>'app' para la aplicación; los scripts SQL escriben 'script:NNN'.</summary>
        string Origen { get; }
    }

    /// <summary>Contexto de auditoría de la aplicación web: usuario y empresa salen de la sesión.</summary>
    public sealed class ContextoAuditoriaHttp : IContextoAuditoria
    {
        private readonly IHttpContextAccessor _http;

        public ContextoAuditoriaHttp(IHttpContextAccessor http) => _http = http;

        public string Usuario
        {
            get
            {
                var usuario = LeerSesion("Username");
                return string.IsNullOrWhiteSpace(usuario) ? "system" : usuario;
            }
        }

        public int? IdEmpresa => int.TryParse(LeerSesion("EmpresaId"), out var id) ? id : null;

        public string Origen => "app";

        /// <summary>Sin petición (servicios en segundo plano) o sin sesión configurada, devuelve nulo en vez de fallar.</summary>
        private string? LeerSesion(string clave)
        {
            try
            {
                return _http.HttpContext?.Session.GetString(clave);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>Contexto de auditoría con valores fijos, para procesos que no son una petición web (y para pruebas).</summary>
    public sealed class ContextoAuditoriaFijo : IContextoAuditoria
    {
        public string Usuario { get; set; } = "system";
        public int? IdEmpresa { get; set; }
        public string Origen { get; set; } = "app";
    }
}
