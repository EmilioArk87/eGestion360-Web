using System.Text.Json;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;

namespace eGestion360Web.Services.Eventos
{
    public sealed class DomainEventPublisher : IDomainEventPublisher
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };

        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        public DomainEventPublisher(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public void Publish(
            int idEmpresa,
            string eventType,
            string aggregateType,
            string aggregateId,
            object payload,
            DateTime? occurredAt = null,
            int eventVersion = 1)
        {
            if (idEmpresa <= 0)
                throw new ArgumentException("idEmpresa requerido y > 0", nameof(idEmpresa));
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("eventType requerido", nameof(eventType));
            if (string.IsNullOrWhiteSpace(aggregateType))
                throw new ArgumentException("aggregateType requerido", nameof(aggregateType));
            if (string.IsNullOrWhiteSpace(aggregateId))
                throw new ArgumentException("aggregateId requerido", nameof(aggregateId));
            if (payload is null)
                throw new ArgumentNullException(nameof(payload));

            var now = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(payload, JsonOpts);

            var creadoPor = _http.HttpContext?.Session.GetString("Username") ?? "system";

            var evt = new DomainEvent
            {
                IdEmpresa       = idEmpresa,
                EventType       = eventType.Trim().ToLowerInvariant(),
                AggregateType   = aggregateType.Trim().ToLowerInvariant(),
                AggregateId     = aggregateId.Trim(),
                EventVersion    = eventVersion,
                Payload         = json,
                OccurredAt      = occurredAt ?? now,
                Status          = DomainEventStatus.Pending,
                Intentos        = 0,
                MaxIntentos     = 10,
                LockedUntil     = null,
                WorkerId        = null,
                ProximoIntentoEn= null,
                UltimoError     = null,
                ProcessedAt     = null,
                CreadoPor       = creadoPor,
                FechaCreacion   = now
            };

            _db.Set<DomainEvent>().Add(evt);
            // OJO: NO llamamos SaveChanges. El caller commitea atómicamente.
        }
    }
}
