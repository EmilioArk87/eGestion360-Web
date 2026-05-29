namespace eGestion360Web.Services.Eventos
{
    /// <summary>
    /// Versión liviana de un evento entregada al handler. No expone la entidad de
    /// outbox para que el handler no toque el estado del worker.
    /// </summary>
    public sealed record DomainEventDispatch(
        long IdEvento,
        int IdEmpresa,
        string EventType,
        string AggregateType,
        string AggregateId,
        int EventVersion,
        string PayloadJson,
        DateTime OccurredAt
    );

    /// <summary>
    /// Consumidor de eventos de dominio. Los módulos implementan esta interfaz
    /// y se registran como IDomainEventHandler en DI; el dispatcher resuelve todos
    /// los registrados y entrega los eventos a los que devuelven true en CanHandle.
    ///
    /// Reglas:
    ///   - HandleAsync DEBE ser idempotente. El dispatcher puede reentregar el evento
    ///     ante fallos transitorios. Patrón típico: el handler verifica si ya procesó
    ///     este id_evento (mediante UNIQUE en su propia tabla, p.ej. asientos.id_evento_origen).
    ///   - Lanzar excepción para indicar fallo; el dispatcher aplicará backoff y reintentará.
    ///   - No bloquear: si el trabajo es largo, considerar dispararlo de forma asíncrona.
    /// </summary>
    public interface IDomainEventHandler
    {
        /// <summary>Nombre estable del handler (usado en logs y posibles tablas de auditoría).</summary>
        string Name { get; }

        /// <summary>True si este handler quiere procesar el evento indicado.</summary>
        bool CanHandle(string eventType);

        Task HandleAsync(DomainEventDispatch evt, CancellationToken ct);
    }
}
