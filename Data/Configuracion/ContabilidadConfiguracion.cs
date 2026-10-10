using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;
using eGestion360Web.Models.Contabilidad;

namespace eGestion360Web.Data.Configuracion
{
    // Contabilidad (Fase 2): mapea las tablas del script 2 - Script SQL/010_ct_nucleo_contable.sql (en espera, ADR-014).
    // 
    // Aislamiento entre empresas: cada tabla referida tiene una clave alterna (id, id_empresa) y las relaciones internas
    // del módulo usan las dos columnas, así la BD rechaza un movimiento con la cuenta de otra empresa (o un asiento con su
    // período, etc.). Nada se borra en cascada: los asientos se anulan.

    public sealed class CuentaContableConfiguracion : IEntityTypeConfiguration<CuentaContable>
    {
        public void Configure(EntityTypeBuilder<CuentaContable> entity)
        {
            entity.HasKey(e => e.IdCuenta);
            entity.HasAlternateKey(e => new { e.IdCuenta, e.IdEmpresa })
                  .HasName("UQ_ct_cuentas_cuenta_empresa");
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo })
                  .IsUnique()
                  .HasDatabaseName("UX_ct_cuentas_empresa_codigo");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdCuentaPadre })
                  .HasDatabaseName("IX_ct_cuentas_empresa_padre");

            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_cuentas_empresa");
            entity.HasOne<Moneda>().WithMany().HasForeignKey(e => e.Moneda)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_cuentas_moneda");
            entity.HasOne(e => e.CuentaPadre)
                  .WithMany(e => e.SubCuentas)
                  .HasForeignKey(e => new { e.IdCuentaPadre, e.IdEmpresa })
                  .HasPrincipalKey(e => new { e.IdCuenta, e.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false)
                  .HasConstraintName("FK_ct_cuentas_padre");
        }
    }

    public sealed class EjercicioFiscalConfiguracion : IEntityTypeConfiguration<EjercicioFiscal>
    {
        public void Configure(EntityTypeBuilder<EjercicioFiscal> entity)
        {
            entity.HasKey(e => e.IdEjercicio);
            entity.HasAlternateKey(e => new { e.IdEjercicio, e.IdEmpresa })
                  .HasName("UQ_ct_ejercicios_ejercicio_empresa");
            entity.HasIndex(e => new { e.IdEmpresa, e.Anio })
                  .IsUnique()
                  .HasDatabaseName("UX_ct_ejercicios_empresa_anio");

            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_ejercicios_empresa");
        }
    }

    public sealed class PeriodoContableConfiguracion : IEntityTypeConfiguration<PeriodoContable>
    {
        public void Configure(EntityTypeBuilder<PeriodoContable> entity)
        {
            entity.HasKey(e => e.IdPeriodo);
            entity.HasAlternateKey(e => new { e.IdPeriodo, e.IdEmpresa })
                  .HasName("UQ_ct_periodos_periodo_empresa");
            entity.HasIndex(e => new { e.IdEjercicio, e.Numero })
                  .IsUnique()
                  .HasDatabaseName("UX_ct_periodos_ejercicio_numero");

            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_periodos_empresa");
            entity.HasOne(e => e.Ejercicio)
                  .WithMany(e => e.Periodos)
                  .HasForeignKey(e => new { e.IdEjercicio, e.IdEmpresa })
                  .HasPrincipalKey(e => new { e.IdEjercicio, e.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_ct_periodos_ejercicio");
        }
    }

    public sealed class CentroCostoConfiguracion : IEntityTypeConfiguration<CentroCosto>
    {
        public void Configure(EntityTypeBuilder<CentroCosto> entity)
        {
            entity.HasKey(e => e.IdCentroCosto);
            entity.HasAlternateKey(e => new { e.IdCentroCosto, e.IdEmpresa })
                  .HasName("UQ_ct_centros_costo_centro_empresa");
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo })
                  .IsUnique()
                  .HasDatabaseName("UX_ct_centros_costo_empresa_codigo");

            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_centros_costo_empresa");
        }
    }

    public sealed class AsientoConfiguracion : IEntityTypeConfiguration<Asiento>
    {
        public void Configure(EntityTypeBuilder<Asiento> entity)
        {
            entity.HasKey(e => e.IdAsiento);
            // Idempotencia del outbox: un evento => a lo sumo un asiento por empresa
            entity.HasIndex(e => new { e.IdEmpresa, e.IdEventoOrigen })
                  .IsUnique()
                  .HasFilter("[id_evento_origen] IS NOT NULL")
                  .HasDatabaseName("UX_ct_asientos_empresa_evento");
            // Correlativo único de asientos ya mayorizados
            entity.HasIndex(e => new { e.IdEmpresa, e.Numero })
                  .IsUnique()
                  .HasFilter("[numero] IS NOT NULL")
                  .HasDatabaseName("UX_ct_asientos_empresa_numero");
            entity.HasIndex(e => new { e.IdEmpresa, e.Fecha })
                  .HasDatabaseName("IX_ct_asientos_empresa_fecha");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdPeriodo, e.Estado })
                  .HasDatabaseName("IX_ct_asientos_empresa_periodo_estado");
            entity.HasAlternateKey(e => new { e.IdAsiento, e.IdEmpresa })
                  .HasName("UQ_ct_asientos_asiento_empresa");

            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_asientos_empresa");
            entity.HasOne(e => e.Periodo)
                  .WithMany()
                  .HasForeignKey(e => new { e.IdPeriodo, e.IdEmpresa })
                  .HasPrincipalKey(e => new { e.IdPeriodo, e.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_ct_asientos_periodo");
        }
    }

    public sealed class AsientoMovimientoConfiguracion : IEntityTypeConfiguration<AsientoMovimiento>
    {
        public void Configure(EntityTypeBuilder<AsientoMovimiento> entity)
        {
            entity.HasKey(e => e.IdMovimiento);
            entity.HasIndex(e => new { e.IdAsiento, e.NumeroLinea })
                  .IsUnique()
                  .HasDatabaseName("UX_ct_mov_asiento_linea");
            entity.HasIndex(e => e.IdAsiento)
                  .HasDatabaseName("IX_ct_mov_asiento");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdCuenta })
                  .HasDatabaseName("IX_ct_mov_empresa_cuenta");

            // Restrict (no Cascade): los asientos no se borran, se anulan; la BD tampoco borra en cascada.
            entity.HasOne(e => e.Asiento)
                  .WithMany(a => a.Movimientos)
                  .HasForeignKey(e => new { e.IdAsiento, e.IdEmpresa })
                  .HasPrincipalKey(a => new { a.IdAsiento, a.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_ct_mov_asiento");
            entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_mov_empresa");
            entity.HasOne(e => e.Cuenta)
                  .WithMany()
                  .HasForeignKey(e => new { e.IdCuenta, e.IdEmpresa })
                  .HasPrincipalKey(c => new { c.IdCuenta, c.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_ct_mov_cuenta");
            entity.HasOne(e => e.CentroCosto)
                  .WithMany()
                  .HasForeignKey(e => new { e.IdCentroCosto, e.IdEmpresa })
                  .HasPrincipalKey(c => new { c.IdCentroCosto, c.IdEmpresa })
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false)
                  .HasConstraintName("FK_ct_mov_centro");
        }
    }
}
