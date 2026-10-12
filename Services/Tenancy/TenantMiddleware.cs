using System.Collections;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Fija el tenant de cada petición y abre el ámbito de log con tenant, empresa, usuario e id de correlación (paso F1.1).
    /// Va después de <c>UseSession</c>: en F1.1 el tenant sale de la empresa de la sesión. Si la sesión trae una empresa que
    /// no tiene tenant, lo deja registrado y la petición sigue sin tenant; F1.3 pasará a rechazarla.
    /// </summary>
    public sealed class TenantMiddleware
    {
        /// <summary>Clave de <c>HttpContext.Items</c> con el id del tenant (un <see cref="int"/>; ausente si no hay).</summary>
        public const string ClaveIdTenant = "eg360.id_tenant";

        /// <summary>Clave de <c>HttpContext.Items</c> con el id de la empresa (un <see cref="int"/>; ausente si no hay).</summary>
        public const string ClaveIdEmpresa = "eg360.id_empresa";

        /// <summary>Encabezado de la respuesta con el id de correlación, para que soporte lo cruce con los logs.</summary>
        public const string EncabezadoCorrelacion = "X-Correlation-Id";

        private readonly RequestDelegate _siguiente;

        public TenantMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

        public async Task InvokeAsync(HttpContext context, IResolvedorDeTenant resolvedor, ILogger<TenantMiddleware> log)
        {
            var correlacion = context.TraceIdentifier;
            context.Response.Headers[EncabezadoCorrelacion] = correlacion;

            int? idEmpresa = null;
            string? idUsuario = null;
            try
            {
                // Cargar la sesión una vez, de forma asíncrona, antes de que las páginas la lean de forma síncrona.
                await context.Session.LoadAsync(context.RequestAborted);
                idEmpresa = AuthHelper.GetEmpresaId(context);
                idUsuario = context.Session.GetString("UserId");
            }
            catch (InvalidOperationException)
            {
                // La petición no tiene sesión configurada (por ejemplo, un endpoint sin sesión): sigue sin tenant.
            }

            int? idTenant = null;
            if (idEmpresa is > 0)
            {
                idTenant = await resolvedor.ResolverPorEmpresaAsync(idEmpresa.Value, context.RequestAborted);
                context.Items[ClaveIdEmpresa] = idEmpresa.Value;
            }

            if (idTenant != null) context.Items[ClaveIdTenant] = idTenant.Value;

            using (log.BeginScope(new AmbitoDeLog(idTenant, idEmpresa, idUsuario, correlacion)))
            {
                if (idEmpresa is > 0 && idTenant == null)
                    log.LogWarning("La empresa {IdEmpresa} de la sesión no tiene tenant asignado.", idEmpresa.Value);

                await _siguiente(context);
            }
        }

        /// <summary>
        /// Datos que acompañan a cada registro de log de la petición. Es una lista de pares clave-valor, para que los
        /// proveedores de log estructurado los lean campo por campo, y su <c>ToString</c> da una línea legible para la consola
        /// (un <c>Dictionary</c> se imprimiría como el nombre del tipo).
        /// </summary>
        private sealed class AmbitoDeLog : IReadOnlyList<KeyValuePair<string, object?>>
        {
            private readonly KeyValuePair<string, object?>[] _pares;

            public AmbitoDeLog(int? idTenant, int? idEmpresa, string? idUsuario, string idCorrelacion)
            {
                _pares =
                [
                    new("IdTenant", idTenant),
                    new("IdEmpresa", idEmpresa),
                    new("IdUsuario", idUsuario),
                    new("IdCorrelacion", idCorrelacion)
                ];
            }

            public int Count => _pares.Length;

            public KeyValuePair<string, object?> this[int index] => _pares[index];

            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_pares).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => string.Join(" ", _pares.Select(p => $"{p.Key}:{p.Value ?? "-"}"));
        }
    }
}
