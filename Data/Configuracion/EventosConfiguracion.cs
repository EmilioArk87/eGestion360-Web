using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Eventos;

namespace eGestion360Web.Data.Configuracion
{
    // Outbox de eventos de dominio (Fase 0 Sprint 2).

    public sealed class DomainEventConfiguracion : IEntityTypeConfiguration<DomainEvent>
    {
        public void Configure(EntityTypeBuilder<DomainEvent> entity)
        {
            entity.HasKey(e => e.IdEvento);
            entity.Property(e => e.IdEvento).ValueGeneratedOnAdd();

            // Índice para el worker: trae eventos elegibles ordenados
            entity.HasIndex(e => new { e.Status, e.ProximoIntentoEn })
                  .HasDatabaseName("IX_domain_events_status_proximo");

            // Índice para idempotencia de handlers y trazabilidad por agregado
            entity.HasIndex(e => new { e.IdEmpresa, e.AggregateType, e.AggregateId })
                  .HasDatabaseName("IX_domain_events_aggregate");

            entity.HasIndex(e => new { e.IdEmpresa, e.EventType, e.Status })
                  .HasDatabaseName("IX_domain_events_tenant_type_status");
        }
    }
}
