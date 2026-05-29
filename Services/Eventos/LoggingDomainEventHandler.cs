using Microsoft.Extensions.Logging;

namespace eGestion360Web.Services.Eventos
{
    /// <summary>
    /// Handler de smoke-test: solo registra cada evento entregado. Sirve para validar
    /// que el pipeline outbox → dispatcher funciona antes de que existan handlers reales.
    ///
    /// Acepta TODOS los tipos de evento. Cuando entren handlers reales (contabilidad,
    /// bancos), este handler puede quedarse o desactivarse vía DI.
    /// </summary>
    public sealed class LoggingDomainEventHandler : IDomainEventHandler
    {
        private readonly ILogger<LoggingDomainEventHandler> _log;
        public LoggingDomainEventHandler(ILogger<LoggingDomainEventHandler> log) => _log = log;

        public string Name => "logging";

        public bool CanHandle(string eventType) => true;

        public Task HandleAsync(DomainEventDispatch evt, CancellationToken ct)
        {
            _log.LogInformation(
                "[Outbox] {EventType} empresa={IdEmpresa} agg={AggType}/{AggId} v{Ver} ocurrido={Occurred:o} id={IdEvento}",
                evt.EventType, evt.IdEmpresa, evt.AggregateType, evt.AggregateId,
                evt.EventVersion, evt.OccurredAt, evt.IdEvento);
            return Task.CompletedTask;
        }
    }
}
