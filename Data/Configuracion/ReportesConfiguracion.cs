using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Reportes;

namespace eGestion360Web.Data.Configuracion
{
    // Motor de reportes versionados (ADR-031; script 047). Las reglas de inmutabilidad viven en los disparadores del script: declararlos
    // hace que EF recupere los id generados sin la cláusula OUTPUT directa, que SQL Server no admite en tablas con disparadores.

    public sealed class ReporteConfiguracion : IEntityTypeConfiguration<Reporte>
    {
        public void Configure(EntityTypeBuilder<Reporte> entity)
        {
            entity.HasKey(e => e.IdReporte).HasName("PK_rep_reportes");
            entity.Property(e => e.FechaCreacion).HasPrecision(3);

            entity.HasIndex(e => e.Codigo).IsUnique().HasDatabaseName("UX_rep_reportes_global").HasFilter("[id_tenant] IS NULL");
            entity.HasIndex(e => new { e.IdTenant, e.Codigo }).IsUnique().HasDatabaseName("UX_rep_reportes_tenant").HasFilter("[id_tenant] IS NOT NULL");

            // Las dos tablas se apuntan entre sí: el reporte a su versión activa y cada versión a su reporte.
            entity.HasOne(e => e.VersionActiva)
                  .WithMany()
                  .HasForeignKey(e => e.IdVersionActiva)
                  .HasConstraintName("FK_rep_reportes_version_activa")
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.ToTable(t => t.HasTrigger("TR_rep_reportes_reglas"));
        }
    }

    public sealed class ReporteVersionConfiguracion : IEntityTypeConfiguration<ReporteVersion>
    {
        public void Configure(EntityTypeBuilder<ReporteVersion> entity)
        {
            entity.HasKey(e => e.IdVersion).HasName("PK_rep_reporte_versiones");
            entity.Property(e => e.FechaCreacion).HasPrecision(3);
            entity.Property(e => e.FechaPublicacion).HasPrecision(3);

            // RP6: la huella la calcula la base; la aplicación nunca la escribe.
            entity.Property(e => e.HashSha256)
                  .HasComputedColumnSql("CONVERT(CHAR(64), HASHBYTES('SHA2_256', [contenido_plantilla] + N'|' + ISNULL([configuracion_json], N'')), 2)", stored: true);

            entity.HasIndex(e => new { e.IdReporte, e.NumeroVersion }).IsUnique().HasDatabaseName("UX_rep_reporte_versiones_numero");

            entity.HasOne(e => e.Reporte)
                  .WithMany(r => r.Versiones)
                  .HasForeignKey(e => e.IdReporte)
                  .HasConstraintName("FK_rep_reporte_versiones_reportes")
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.VersionOrigen)
                  .WithMany()
                  .HasForeignKey(e => e.IdVersionOrigen)
                  .HasConstraintName("FK_rep_reporte_versiones_origen")
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.ToTable(t => t.HasTrigger("TR_rep_reporte_versiones_reglas"));
        }
    }

    public sealed class ReporteBitacoraConfiguracion : IEntityTypeConfiguration<ReporteBitacora>
    {
        public void Configure(EntityTypeBuilder<ReporteBitacora> entity)
        {
            entity.HasKey(e => e.IdBitacora).HasName("PK_rep_reporte_bitacora");
            entity.Property(e => e.IdBitacora).ValueGeneratedOnAdd();
            entity.Property(e => e.FechaHora).HasPrecision(3);
            entity.ToTable(t => t.HasTrigger("TR_rep_reporte_bitacora_inmutable"));
        }
    }

    public sealed class ReporteEjecucionConfiguracion : IEntityTypeConfiguration<ReporteEjecucion>
    {
        public void Configure(EntityTypeBuilder<ReporteEjecucion> entity)
        {
            entity.HasKey(e => e.IdEjecucion).HasName("PK_rep_ejecuciones");
            entity.Property(e => e.IdEjecucion).ValueGeneratedOnAdd();
            entity.Property(e => e.FechaHora).HasPrecision(3);

            entity.HasOne(e => e.Reporte)
                  .WithMany()
                  .HasForeignKey(e => e.IdReporte)
                  .HasConstraintName("FK_rep_ejecuciones_reportes")
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Version)
                  .WithMany()
                  .HasForeignKey(e => e.IdVersion)
                  .HasConstraintName("FK_rep_ejecuciones_versiones")
                  .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t =>
            {
                t.HasTrigger("TR_rep_ejecuciones_inmutable");
                t.HasTrigger("TR_rep_ejecuciones_version");
            });
        }
    }
}
