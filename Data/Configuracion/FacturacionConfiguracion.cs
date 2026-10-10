using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Facturacion;

namespace eGestion360Web.Data.Configuracion
{
    // Facturación (Fase 1 Sprint 3), pagos y notas (Fase 1 Sprint 4).

    public sealed class FacturaConfiguracion : IEntityTypeConfiguration<Factura>
    {
        public void Configure(EntityTypeBuilder<Factura> entity)
        {
            entity.HasKey(e => e.IdFactura);
            entity.HasIndex(e => new { e.IdEmpresa, e.Serie, e.Numero })
                  .IsUnique()
                  .HasFilter("[numero] IS NOT NULL")
                  .HasDatabaseName("UX_facturas_empresa_serie_numero");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdCliente, e.Estado })
                  .HasDatabaseName("IX_facturas_empresa_cliente_estado");
            entity.HasIndex(e => new { e.IdEmpresa, e.FechaEmision })
                  .HasDatabaseName("IX_facturas_empresa_fecha");

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Cliente)
                  .WithMany()
                  .HasForeignKey(e => e.IdCliente)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.FormaPago)
                  .WithMany()
                  .HasForeignKey(e => e.IdFormaPago)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(e => e.CondicionPago)
                  .WithMany()
                  .HasForeignKey(e => e.IdCondicionPago)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }

    public sealed class FacturaDetalleConfiguracion : IEntityTypeConfiguration<FacturaDetalle>
    {
        public void Configure(EntityTypeBuilder<FacturaDetalle> entity)
        {
            entity.HasKey(e => e.IdFacturaDetalle);
            entity.HasIndex(e => new { e.IdFactura, e.NumeroLinea })
                  .IsUnique()
                  .HasDatabaseName("UX_factura_detalle_factura_linea");

            entity.HasOne(e => e.Factura)
                  .WithMany(f => f.Detalle)
                  .HasForeignKey(e => e.IdFactura)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Producto)
                  .WithMany()
                  .HasForeignKey(e => e.IdProducto)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(e => e.Impuesto)
                  .WithMany()
                  .HasForeignKey(e => e.IdImpuesto)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }

    public sealed class FacturaSecuenciaConfiguracion : IEntityTypeConfiguration<FacturaSecuencia>
    {
        public void Configure(EntityTypeBuilder<FacturaSecuencia> entity)
        {
            entity.HasKey(e => e.IdSecuencia);
            entity.HasIndex(e => new { e.IdEmpresa, e.TipoDocumento, e.Serie })
                  .IsUnique()
                  .HasDatabaseName("UX_factura_secuencias_tenant_tipo_serie");
        }
    }

    public sealed class PagoConfiguracion : IEntityTypeConfiguration<Pago>
    {
        public void Configure(EntityTypeBuilder<Pago> entity)
        {
            entity.HasKey(e => e.IdPago);
            entity.HasIndex(e => new { e.IdEmpresa, e.Serie, e.Numero })
                  .IsUnique()
                  .HasFilter("[numero] IS NOT NULL")
                  .HasDatabaseName("UX_pagos_empresa_serie_numero");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdCliente, e.Estado })
                  .HasDatabaseName("IX_pagos_empresa_cliente_estado");
            entity.HasIndex(e => new { e.IdEmpresa, e.Fecha })
                  .HasDatabaseName("IX_pagos_empresa_fecha");

            entity.HasOne(e => e.Empresa).WithMany().HasForeignKey(e => e.IdEmpresa).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Cliente).WithMany().HasForeignKey(e => e.IdCliente).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FormaPago).WithMany().HasForeignKey(e => e.IdFormaPago).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class PagoAplicacionConfiguracion : IEntityTypeConfiguration<PagoAplicacion>
    {
        public void Configure(EntityTypeBuilder<PagoAplicacion> entity)
        {
            entity.HasKey(e => e.IdAplicacion);
            entity.HasIndex(e => e.IdFactura).HasDatabaseName("IX_pago_aplicaciones_factura");

            entity.HasOne(e => e.Pago).WithMany(p => p.Aplicaciones).HasForeignKey(e => e.IdPago).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Factura).WithMany().HasForeignKey(e => e.IdFactura).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class NotaConfiguracion : IEntityTypeConfiguration<Nota>
    {
        public void Configure(EntityTypeBuilder<Nota> entity)
        {
            entity.HasKey(e => e.IdNota);
            entity.HasIndex(e => new { e.IdEmpresa, e.Tipo, e.Serie, e.Numero })
                  .IsUnique()
                  .HasFilter("[numero] IS NOT NULL")
                  .HasDatabaseName("UX_notas_empresa_tipo_serie_numero");
            entity.HasIndex(e => new { e.IdEmpresa, e.IdFacturaOrigen })
                  .HasDatabaseName("IX_notas_empresa_factura");

            entity.HasOne(e => e.Empresa).WithMany().HasForeignKey(e => e.IdEmpresa).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Cliente).WithMany().HasForeignKey(e => e.IdCliente).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FacturaOrigen).WithMany().HasForeignKey(e => e.IdFacturaOrigen).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
