using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Data.Configuracion
{
    // Tasas de cambio y bitácora del job que las obtiene (script 020).

    // Una sola tasa VIGENTE por par, tipo, fecha y empresa: lo garantiza el índice único filtrado. En SQL Server
    // los NULL de id_empresa cuentan como iguales (una sola tasa oficial por clave); SQLite los trata como distintos,
    // así que las pruebas en memoria no cubren el caso de dos tasas oficiales duplicadas.
    public sealed class TasaCambioConfiguracion : IEntityTypeConfiguration<TasaCambio>
    {
        public void Configure(EntityTypeBuilder<TasaCambio> entity)
        {
            entity.HasKey(e => e.IdTasaCambio);

            entity.HasIndex(e => new { e.MonedaOrigen, e.MonedaDestino, e.TipoTasa, e.FechaVigencia, e.IdEmpresa })
                  .IsUnique()
                  .HasDatabaseName("UX_tasas_cambio_vigente")
                  .HasFilter("[estado] = 'VIGENTE' AND [eliminado] = 0");

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne<Moneda>()
                  .WithMany()
                  .HasForeignKey(e => e.MonedaOrigen)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Moneda>()
                  .WithMany()
                  .HasForeignKey(e => e.MonedaDestino)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TasaAnterior)
                  .WithMany()
                  .HasForeignKey(e => e.IdTasaAnterior)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(e => e.Ejecucion)
                  .WithMany()
                  .HasForeignKey(e => e.IdEjecucion)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }

    public sealed class TasaCambioEjecucionConfiguracion : IEntityTypeConfiguration<TasaCambioEjecucion>
    {
        public void Configure(EntityTypeBuilder<TasaCambioEjecucion> entity)
        {
            entity.HasKey(e => e.IdEjecucion);
            entity.HasIndex(e => new { e.Job, e.FechaObjetivo, e.InicioUtc })
                  .HasDatabaseName("IX_tce_job_fecha");

            entity.HasOne(e => e.EjecucionOrigen)
                  .WithMany()
                  .HasForeignKey(e => e.IdEjecucionOrigen)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }

    public sealed class TasaCambioEjecucionDetalleConfiguracion : IEntityTypeConfiguration<TasaCambioEjecucionDetalle>
    {
        public void Configure(EntityTypeBuilder<TasaCambioEjecucionDetalle> entity)
        {
            entity.HasKey(e => e.IdDetalle);

            entity.HasOne(e => e.Ejecucion)
                  .WithMany(x => x.Detalles)
                  .HasForeignKey(e => e.IdEjecucion)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TasaCambio)
                  .WithMany()
                  .HasForeignKey(e => e.IdTasaCambio)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }
}
