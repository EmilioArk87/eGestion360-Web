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
            // F1.7 (script 040): una suspensión es de solo lectura salvo que el tenant diga otra cosa (D53).
            entity.Property(t => t.ModoSuspension).IsUnicode(false).HasDefaultValue(ModoDeSuspension.Lectura);

            entity.HasIndex(t => t.Codigo).IsUnique().HasDatabaseName("UX_tenants_codigo");
            entity.HasIndex(t => t.UidPublico).IsUnique().HasDatabaseName("UX_tenants_uid_publico");
            // Como mucho un tenant interno (el de la plataforma, ADR-006).
            entity.HasIndex(t => t.Tipo).IsUnique().HasDatabaseName("UX_tenants_interno")
                  .HasFilter("[tipo] = 'interno'");

            entity.ToTable(tabla =>
            {
                tabla.HasCheckConstraint("CK_tenants_tipo", "[tipo] IN ('interno', 'cliente', 'demo')");
                tabla.HasCheckConstraint("CK_tenants_estado", "[estado] IN ('activo', 'suspendido', 'cancelado')");
                tabla.HasCheckConstraint("CK_tenants_modo_suspension", "[modo_suspension] IN ('lectura', 'bloqueo')");
            });
        }
    }

    // ── Planes, derechos de uso y plan de cada tenant (F1.7; script 040) ────────────────────────────────────────────

    public sealed class PlanConfiguracion : IEntityTypeConfiguration<Plan>
    {
        public void Configure(EntityTypeBuilder<Plan> entity)
        {
            entity.HasKey(p => p.IdPlan).HasName("PK_planes");
            entity.Property(p => p.Codigo).IsUnicode(false);
            entity.HasIndex(p => p.Codigo).IsUnique().HasDatabaseName("UX_planes_codigo");
        }
    }

    public sealed class PlanVersionConfiguracion : IEntityTypeConfiguration<PlanVersion>
    {
        public void Configure(EntityTypeBuilder<PlanVersion> entity)
        {
            entity.HasKey(v => v.IdPlanVersion).HasName("PK_plan_versiones");
            entity.HasOne<Plan>().WithMany().HasForeignKey(v => v.IdPlan)
                  .HasConstraintName("FK_plan_versiones_planes").OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(v => new { v.IdPlan, v.Numero }).IsUnique().HasDatabaseName("UX_plan_versiones_plan_numero");
        }
    }

    public sealed class DerechoConfiguracion : IEntityTypeConfiguration<Derecho>
    {
        public void Configure(EntityTypeBuilder<Derecho> entity)
        {
            entity.HasKey(d => d.IdDerecho).HasName("PK_derechos");
            entity.Property(d => d.Codigo).IsUnicode(false);
            entity.Property(d => d.Tipo).IsUnicode(false);
            entity.HasIndex(d => d.Codigo).IsUnique().HasDatabaseName("UX_derechos_codigo");
            // Clave de apoyo para que plan_version_derechos compruebe el tipo con una clave foránea compuesta.
            entity.HasAlternateKey(d => new { d.IdDerecho, d.Tipo }).HasName("UX_derechos_id_tipo");
        }
    }

    public sealed class PlanVersionDerechoConfiguracion : IEntityTypeConfiguration<PlanVersionDerecho>
    {
        public void Configure(EntityTypeBuilder<PlanVersionDerecho> entity)
        {
            entity.HasKey(d => new { d.IdPlanVersion, d.IdDerecho }).HasName("PK_plan_version_derechos");
            entity.Property(d => d.TipoDelDerecho).IsUnicode(false);
            entity.HasOne<PlanVersion>().WithMany().HasForeignKey(d => d.IdPlanVersion)
                  .HasConstraintName("FK_plan_version_derechos_plan_versiones").OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Derecho>().WithMany().HasForeignKey(d => new { d.IdDerecho, d.TipoDelDerecho })
                  .HasPrincipalKey(x => new { x.IdDerecho, x.Tipo })
                  .HasConstraintName("FK_plan_version_derechos_derechos").OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class TenantPlanConfiguracion : IEntityTypeConfiguration<TenantPlan>
    {
        public void Configure(EntityTypeBuilder<TenantPlan> entity)
        {
            entity.HasKey(p => p.IdTenantPlan).HasName("PK_tenant_planes");
            entity.HasOne<Tenant>().WithMany().HasForeignKey(p => p.IdTenant)
                  .HasConstraintName("FK_tenant_planes_tenants").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PlanVersion>().WithMany().HasForeignKey(p => p.IdPlanVersion)
                  .HasConstraintName("FK_tenant_planes_plan_versiones").OnDelete(DeleteBehavior.Restrict);
            // Un solo plan vigente por tenant.
            entity.HasIndex(p => p.IdTenant).IsUnique().HasDatabaseName("UX_tenant_planes_vigente")
                  .HasFilter("[vigente_hasta] IS NULL");
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
