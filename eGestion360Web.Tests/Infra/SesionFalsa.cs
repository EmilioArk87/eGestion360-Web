using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;

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

        /// <summary>Un HttpContext con esta sesión: usuario, rol y, si se indica, empresa.</summary>
        public static HttpContext Contexto(string? rol = "empresa_user", string? empresaId = null, bool autenticado = true)
        {
            var valores = new Dictionary<string, string>();
            if (autenticado) valores["UserId"] = "1";
            if (rol != null) valores["Role"] = rol;
            if (empresaId != null) valores["EmpresaId"] = empresaId;
            return new DefaultHttpContext { Session = new SesionFalsa(valores) };
        }
    }
}
