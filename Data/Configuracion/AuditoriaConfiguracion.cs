using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Auditoria;

namespace eGestion360Web.Data.Configuracion
{
    // Bitácora de cambios por campo (script 014).

    public sealed class BitacoraCambioConfiguracion : IEntityTypeConfiguration<BitacoraCambio>
    {
        public void Configure(EntityTypeBuilder<BitacoraCambio> entity)
        {
            // La tabla tiene un disparador INSTEAD OF UPDATE, DELETE. Declararlo hace que EF recupere
            // el id generado sin la cláusula OUTPUT directa, que SQL Server no admite con disparadores.
            entity.ToTable(t => t.HasTrigger("TR_bitacora_cambios_inmutable"));
        }
    }

    // Auditoría de seguridad y de plataforma (F1.8, ADR-030; script 045). Las dos tablas son inmutables (INSTEAD OF UPDATE, DELETE).

    public sealed class AuditoriaSeguridadConfiguracion : IEntityTypeConfiguration<AuditoriaSeguridad>
    {
        public void Configure(EntityTypeBuilder<AuditoriaSeguridad> entity)
        {
            entity.HasKey(e => e.IdEvento).HasName("PK_auditoria_seguridad");
            entity.Property(e => e.IdEvento).ValueGeneratedOnAdd();
            // La fecha la fija siempre el servicio (con su reloj); el valor por omisión SYSUTCDATETIME() vive solo en el script.
            entity.Property(e => e.FechaHora).HasPrecision(3);
            // El disparador hace que EF recupere el id generado sin la cláusula OUTPUT directa, que SQL Server no admite con disparadores.
            entity.ToTable(t => t.HasTrigger("TR_auditoria_seguridad_inmutable"));
        }
    }

    public sealed class AuditoriaPlataformaConfiguracion : IEntityTypeConfiguration<AuditoriaPlataforma>
    {
        public void Configure(EntityTypeBuilder<AuditoriaPlataforma> entity)
        {
            entity.HasKey(e => e.IdEvento).HasName("PK_auditoria_plataforma");
            entity.Property(e => e.IdEvento).ValueGeneratedOnAdd();
            entity.Property(e => e.FechaHora).HasPrecision(3);
            entity.ToTable(t => t.HasTrigger("TR_auditoria_plataforma_inmutable"));
        }
    }
}
