using System.Text.Json;
using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>
    /// Un <see cref="IAuditoriaDeSeguridad"/> que solo anota los eventos que recibe, para comprobar qué registra cada servicio sin
    /// escribir en ninguna tabla (F1.8a). <see cref="Detalles"/> devuelve todo lo anotado como texto, para buscar en él lo que
    /// nunca debe aparecer (claves, códigos, tokens).
    /// </summary>
    public sealed class AuditoriaEspia : IAuditoriaDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = new();

        public Task RegistrarAsync(EventoDeSeguridad evento, CancellationToken ct = default)
        {
            Eventos.Add(evento);
            return Task.CompletedTask;
        }

        /// <summary>Los eventos que llegaron por el modo estricto (F1.8b); también están en <see cref="Eventos"/>.</summary>
        public List<EventoDeSeguridad> Estrictos { get; } = new();

        /// <summary>Si es true, el modo estricto falla como si la auditoría no estuviera disponible: el evento no se anota.</summary>
        public bool FallaElModoEstricto { get; set; }

        public Task RegistrarEstrictoAsync(EventoDeSeguridad evento, CancellationToken ct = default)
        {
            if (FallaElModoEstricto) throw new AuditoriaNoDisponibleException("Falla simulada de la auditoría.");
            Eventos.Add(evento);
            Estrictos.Add(evento);
            return Task.CompletedTask;
        }

        public IEnumerable<EventoDeSeguridad> De(string tipo) => Eventos.Where(e => e.Tipo == tipo);

        /// <summary>Cada evento entero (tipo, resultado, usuario, objeto y detalle) como JSON, para buscar texto prohibido.</summary>
        public string Detalles() => string.Join('\n', Eventos.Select(e => JsonSerializer.Serialize(e)));
    }
}
