using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Data.Configuracion
{
    // Catálogos transversales de negocio (Fase 0): todos por empresa (id_empresa).

    public sealed class ClienteConfiguracion : IEntityTypeConfiguration<Cliente>
    {
        public void Configure(EntityTypeBuilder<Cliente> entity)
        {
            // El cliente natural se enlaza con su vínculo de tipo 'cliente' (script 014).
            entity.HasOne(c => c.Vinculo)
                  .WithMany()
                  .HasForeignKey(c => c.IdPersonaEmpresa)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasKey(e => e.IdCliente);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();
            entity.HasIndex(e => new { e.IdEmpresa, e.RazonSocial });

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CondicionPagoDefault)
                  .WithMany()
                  .HasForeignKey(e => e.IdCondicionPagoDefault)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class ProveedorConfiguracion : IEntityTypeConfiguration<Proveedor>
    {
        public void Configure(EntityTypeBuilder<Proveedor> entity)
        {
            entity.HasKey(e => e.IdProveedor);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();
            entity.HasIndex(e => new { e.IdEmpresa, e.RazonSocial });

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CondicionPagoDefault)
                  .WithMany()
                  .HasForeignKey(e => e.IdCondicionPagoDefault)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class ImpuestoConfiguracion : IEntityTypeConfiguration<Impuesto>
    {
        public void Configure(EntityTypeBuilder<Impuesto> entity)
        {
            entity.HasKey(e => e.IdImpuesto);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class ProductoServicioConfiguracion : IEntityTypeConfiguration<ProductoServicio>
    {
        public void Configure(EntityTypeBuilder<ProductoServicio> entity)
        {
            entity.HasKey(e => e.IdProducto);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();
            entity.HasIndex(e => new { e.IdEmpresa, e.Descripcion });

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ImpuestoDefault)
                  .WithMany()
                  .HasForeignKey(e => e.IdImpuestoDefault)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        }
    }

    public sealed class FormaPagoConfiguracion : IEntityTypeConfiguration<FormaPago>
    {
        public void Configure(EntityTypeBuilder<FormaPago> entity)
        {
            entity.HasKey(e => e.IdFormaPago);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class CondicionPagoConfiguracion : IEntityTypeConfiguration<CondicionPago>
    {
        public void Configure(EntityTypeBuilder<CondicionPago> entity)
        {
            entity.HasKey(e => e.IdCondicionPago);
            entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class CondicionPagoCuotaConfiguracion : IEntityTypeConfiguration<CondicionPagoCuota>
    {
        public void Configure(EntityTypeBuilder<CondicionPagoCuota> entity)
        {
            entity.HasKey(e => e.IdCuota);
            entity.HasIndex(e => new { e.IdCondicionPago, e.NumeroCuota }).IsUnique();

            entity.HasOne(e => e.CondicionPago)
                  .WithMany(c => c.Cuotas)
                  .HasForeignKey(e => e.IdCondicionPago)
                  .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
