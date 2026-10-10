using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Flota;

namespace eGestion360Web.Data.Configuracion
{
    // Flota: las claves foráneas se declaran a mano porque el prefijo Id de las columnas no sigue la convención de EF.

    public sealed class VehiculoConfiguracion : IEntityTypeConfiguration<Vehiculo>
    {
        public void Configure(EntityTypeBuilder<Vehiculo> entity)
        {
            entity.Property(v => v.KmInicial).HasPrecision(18, 2);

            entity.HasOne(v => v.TipoVehiculo)
                  .WithMany()
                  .HasForeignKey(v => v.IdTipoVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(v => v.Ruta)
                  .WithMany()
                  .HasForeignKey(v => v.IdRuta)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class CargaCombustibleConfiguracion : IEntityTypeConfiguration<CargaCombustible>
    {
        public void Configure(EntityTypeBuilder<CargaCombustible> entity)
        {
            entity.Property(c => c.Cantidad).HasPrecision(18, 2);
            entity.Property(c => c.KmOdometro).HasPrecision(18, 2);
            entity.Property(c => c.PrecioUnitario).HasPrecision(18, 2);
            entity.Property(c => c.Total).HasPrecision(18, 2);

            entity.HasOne(c => c.Vehiculo)
                  .WithMany()
                  .HasForeignKey(c => c.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Conductor)
                  .WithMany()
                  .HasForeignKey(c => c.IdConductor)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class GastoRepuestoConfiguracion : IEntityTypeConfiguration<GastoRepuesto>
    {
        public void Configure(EntityTypeBuilder<GastoRepuesto> entity)
        {
            entity.Property(g => g.Cantidad).HasPrecision(18, 2);
            entity.Property(g => g.KmOdometro).HasPrecision(18, 2);
            entity.Property(g => g.PrecioUnitario).HasPrecision(18, 2);
            entity.Property(g => g.Subtotal).HasPrecision(18, 2);

            entity.HasOne(g => g.Vehiculo)
                  .WithMany()
                  .HasForeignKey(g => g.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.CategoriaRepuesto)
                  .WithMany()
                  .HasForeignKey(g => g.IdCategoriaRepuesto)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class OdometroDiarioConfiguracion : IEntityTypeConfiguration<OdometroDiario>
    {
        public void Configure(EntityTypeBuilder<OdometroDiario> entity)
        {
            entity.Property(o => o.KmFinal).HasPrecision(18, 2);
            entity.Property(o => o.KmInicial).HasPrecision(18, 2);
            entity.Property(o => o.KmRecorridos).HasPrecision(18, 2);

            entity.HasOne(o => o.Vehiculo)
                  .WithMany()
                  .HasForeignKey(o => o.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Ruta)
                  .WithMany()
                  .HasForeignKey(o => o.IdRuta)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);

            entity.HasOne(o => o.Conductor)
                  .WithMany()
                  .HasForeignKey(o => o.IdConductor)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class ControlSalidaConfiguracion : IEntityTypeConfiguration<ControlSalida>
    {
        public void Configure(EntityTypeBuilder<ControlSalida> entity)
        {
            entity.ToTable("control_salidas");
            entity.HasKey(e => e.IdControlSalida);

            entity.Property(e => e.OdometroSalida).HasPrecision(12, 2);
            entity.Property(e => e.OdometroEntrada).HasPrecision(12, 2);
            entity.Property(e => e.KmRecorridos).HasPrecision(12, 2);

            entity.HasOne(e => e.Vehiculo)
                  .WithMany()
                  .HasForeignKey(e => e.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Conductor)
                  .WithMany()
                  .HasForeignKey(e => e.IdConductor)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);

            entity.HasOne(e => e.Ruta)
                  .WithMany()
                  .HasForeignKey(e => e.IdRuta)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class OrdenMantenimientoConfiguracion : IEntityTypeConfiguration<OrdenMantenimiento>
    {
        public void Configure(EntityTypeBuilder<OrdenMantenimiento> entity)
        {
            entity.Property(o => o.KmOdometro).HasPrecision(18, 2);
            entity.Property(o => o.MontoManoObra).HasPrecision(18, 2);
            entity.Property(o => o.MontoOtros).HasPrecision(18, 2);
            entity.Property(o => o.MontoRepuestos).HasPrecision(18, 2);
            entity.Property(o => o.Total).HasPrecision(18, 2);

            entity.HasOne(o => o.Vehiculo)
                  .WithMany()
                  .HasForeignKey(o => o.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Taller)
                  .WithMany()
                  .HasForeignKey(o => o.IdTaller)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class PeajeConfiguracion : IEntityTypeConfiguration<Peaje>
    {
        public void Configure(EntityTypeBuilder<Peaje> entity)
        {
            entity.Property(p => p.Monto).HasPrecision(18, 2);
            entity.Property(p => p.KmOdometro).HasPrecision(18, 2);

            entity.HasOne(p => p.Vehiculo)
                  .WithMany()
                  .HasForeignKey(p => p.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.Ruta)
                  .WithMany()
                  .HasForeignKey(p => p.IdRuta)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);

            entity.HasOne(p => p.Conductor)
                  .WithMany()
                  .HasForeignKey(p => p.IdConductor)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class RutaConfiguracion : IEntityTypeConfiguration<Ruta>
    {
        public void Configure(EntityTypeBuilder<Ruta> entity)
        {
            entity.Property(r => r.DistanciaKm).HasPrecision(18, 2);
        }
    }

    public sealed class PolizaSeguroConfiguracion : IEntityTypeConfiguration<PolizaSeguro>
    {
        public void Configure(EntityTypeBuilder<PolizaSeguro> entity)
        {
            entity.Property(p => p.CostoDiario).HasPrecision(18, 2);
            entity.Property(p => p.PrimaTotal).HasPrecision(18, 2);

            entity.HasOne(p => p.Vehiculo)
                  .WithMany()
                  .HasForeignKey(p => p.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class SalarioDiarioConfiguracion : IEntityTypeConfiguration<SalarioDiario>
    {
        public void Configure(EntityTypeBuilder<SalarioDiario> entity)
        {
            entity.Property(s => s.Monto).HasPrecision(18, 2);

            entity.HasOne(s => s.Vehiculo)
                  .WithMany()
                  .HasForeignKey(s => s.IdVehiculo)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Persona)
                  .WithMany()
                  .HasForeignKey(s => s.IdPersona)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
