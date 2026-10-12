using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Data.Configuracion
{
    // Plano de control de la plataforma (esquema saas, ADR-013). Paso F1.1; script 025.

    public sealed class TenantConfiguracion : IEntityTypeConfiguration<Tenant>
    {
        public void Configure(EntityTypeBuilder<Tenant> entity)
        {
            entity.HasKey(t => t.IdTenant).HasName("PK_tenants");
            entity.Property(t => t.IdTenant).ValueGeneratedOnAdd();

            // La base le da un valor (NEWSEQUENTIALID, como el script 025); si el código ya trae uno (pruebas, importaciones),
            // se respeta.
            entity.Property(t => t.UidPublico).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(t => t.Codigo).IsUnicode(false);
            entity.Property(t => t.Tipo).IsUnicode(false);
            entity.Property(t => t.Estado).IsUnicode(false);

            entity.HasIndex(t => t.Codigo).IsUnique().HasDatabaseName("UX_tenants_codigo");
            entity.HasIndex(t => t.UidPublico).IsUnique().HasDatabaseName("UX_tenants_uid_publico");
            // Como mucho un tenant interno (el de la plataforma, ADR-006).
            entity.HasIndex(t => t.Tipo).IsUnique().HasDatabaseName("UX_tenants_interno")
                  .HasFilter("[tipo] = 'interno'");

            entity.ToTable(tabla =>
            {
                tabla.HasCheckConstraint("CK_tenants_tipo", "[tipo] IN ('interno', 'cliente', 'demo')");
                tabla.HasCheckConstraint("CK_tenants_estado", "[estado] IN ('activo', 'suspendido', 'cancelado')");
            });
        }
    }

    /// <summary>Cada empresa pertenece a un tenant (ADR-002). La columna nace nulable en la base (expandir) y el modelo la
    /// declara obligatoria; el script de contraer de F1.3 la deja NOT NULL.</summary>
    public sealed class EmpresaTenantConfiguracion : IEntityTypeConfiguration<Empresa>
    {
        public void Configure(EntityTypeBuilder<Empresa> entity)
        {
            entity.HasOne<Tenant>().WithMany().HasForeignKey(e => e.IdTenant)
                  .HasConstraintName("FK_empresas_tenants")
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.IdTenant).HasDatabaseName("IX_empresas_tenant");
        }
    }
}
