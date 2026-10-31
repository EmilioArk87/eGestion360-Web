using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using eGestion360Web.Services.Identidad;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>Sesión en memoria para probar código que lee HttpContext.Session sin levantar el servidor.</summary>
    public sealed class SesionFalsa : ISession
    {
        private readonly Dictionary<string, byte[]> _valores = new();

        public SesionFalsa(IDictionary<string, string>? valores = null)
        {
            if (valores == null) return;
            foreach (var (clave, valor) in valores)
                _valores[clave] = Encoding.UTF8.GetBytes(valor);
        }

        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _valores.Keys;

        public void Clear() => _valores.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _valores.Remove(key);
        public void Set(string key, byte[] value) => _valores[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _valores.TryGetValue(key, out value);

        /// <summary>
        /// Un HttpContext con la identidad de la cookie (claims, como en la aplicación desde F1.5) y su espejo en la sesión: usuario, rol y,
        /// si se indica, empresa. <paramref name="empresaId"/> es texto para poder probar valores inválidos: lo que no es un número no llega
        /// a las reclamaciones, igual que una cookie no puede traerlo.
        /// </summary>
        public static HttpContext Contexto(string? rol = "empresa_user", string? empresaId = null, bool autenticado = true,
            bool debeCambiarClave = false, int? idTenant = null, bool esOperador = false)
        {
            var valores = new Dictionary<string, string>();
            var http = new DefaultHttpContext();
            if (autenticado)
            {
                valores["UserId"] = "1";
                int? empresa = int.TryParse(empresaId, out var e) ? e : null;
                http.User = ReclamosDeIdentidad.Crear(new DatosDeIdentidad(
                    IdCuenta: 1, IdUserLegado: 1, NombreUsuario: "usuario.prueba", Correo: "usuario.prueba@example.test",
                    NombrePersona: "Usuario de Prueba", Rol: rol ?? string.Empty, EsOperador: esOperador || rol == "admin",
                    DebeCambiarClave: debeCambiarClave, IdMembresia: empresa != null ? 1 : null, IdTenant: idTenant,
                    CodigoTenant: null, IdEmpresa: empresa, IdRolEmpresa: null, MfaVerificada: false, Sello: "prueba"));
                if (debeCambiarClave) valores["MustChangePassword"] = "1";
            }
            if (rol != null) valores["Role"] = rol;
            if (empresaId != null) valores["EmpresaId"] = empresaId;
            http.Session = new SesionFalsa(valores);
            return http;
        }

        /// <summary>Un HttpContext cuya identidad son exactamente estos datos (con el espejo mínimo en la sesión).</summary>
        public static HttpContext ConIdentidad(DatosDeIdentidad datos)
        {
            var http = new DefaultHttpContext { User = ReclamosDeIdentidad.Crear(datos), Session = new SesionFalsa() };
            SesionDesdeIdentidadMiddleware.Reflejar(http.Session, datos);
            return http;
        }
    }
}
