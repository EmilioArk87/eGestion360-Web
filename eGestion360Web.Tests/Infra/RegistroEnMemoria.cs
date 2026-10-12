using Microsoft.Extensions.Logging;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>
    /// Registro de log que guarda en memoria los mensajes y los ámbitos (<c>BeginScope</c>), para comprobar qué se registró
    /// y con qué datos (tenant, empresa, usuario, correlación).
    /// </summary>
    public sealed class RegistroEnMemoria<T> : ILogger<T>
    {
        /// <summary>Un ámbito abierto con sus pares clave-valor; <see cref="Abierto"/> pasa a false al cerrarlo.</summary>
        public sealed class Ambito
        {
            public IReadOnlyDictionary<string, object?> Valores { get; init; } = new Dictionary<string, object?>();
            /// <summary>Cómo lo imprime la consola: el <c>ToString</c> del estado del ámbito.</summary>
            public string? Texto { get; init; }
            public bool Abierto { get; set; } = true;
        }

        public List<Ambito> Ambitos { get; } = new();

        /// <summary>Cada mensaje con cuántos ámbitos estaban abiertos al escribirlo.</summary>
        public List<(LogLevel Nivel, string Mensaje, int AmbitosAbiertos)> Mensajes { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? pares.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?> { ["valor"] = state };
            var ambito = new Ambito { Valores = valores, Texto = state.ToString() };
            Ambitos.Add(ambito);
            return new Cierre(ambito);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Mensajes.Add((logLevel, formatter(state, exception), Ambitos.Count(a => a.Abierto)));

        private sealed class Cierre : IDisposable
        {
            private readonly Ambito _ambito;
            public Cierre(Ambito ambito) => _ambito = ambito;
            public void Dispose() => _ambito.Abierto = false;
        }
    }
}
