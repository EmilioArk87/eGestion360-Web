using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Data.Configuracion
{
    // Catálogo de permisos, roles plantilla y permisos de los roles (F1.6a; script 041; ADR-029).

    public sealed class PermisoConfiguracion : IEntityTypeConfiguration<Permiso>
    {
        public void Configure(EntityTypeBuilder<Permiso> entity)
        {
            entity.HasKey(p => p.IdPermiso).HasName("PK_permisos");
            entity.Property(p => p.Codigo).IsUnicode(false);
            entity.Property(p => p.Modulo).IsUnicode(false);
            entity.Property(p => p.Recurso).IsUnicode(false);
            entity.Property(p => p.Accion).IsUnicode(false);
            entity.Property(p => p.Ambito).IsUnicode(false);
            entity.Property(p => p.AccionLegada).IsUnicode(false);
            entity.HasIndex(p => p.Codigo).IsUnique().HasDatabaseName("UX_permisos_codigo");
            // Claves de apoyo para las claves foráneas compuestas: mismo módulo en las plantillas, mismo ámbito en los roles.
            entity.HasAlternateKey(p => new { p.IdPermiso, p.Modulo }).HasName("UX_permisos_id_modulo");
            entity.HasAlternateKey(p => new { p.IdPermiso, p.Ambito }).HasName("UX_permisos_id_ambito");
        }
    }

    public sealed class RolPlantillaConfiguracion : IEntityTypeConfiguration<RolPlantilla>
    {
        public void Configure(EntityTypeBuilder<RolPlantilla> entity)
        {
            entity.HasKey(r => r.IdRolPlantilla).HasName("PK_roles_plantilla");
            entity.Property(r => r.Codigo).IsUnicode(false);
            entity.Property(r => r.Modulo).IsUnicode(false);
            entity.HasIndex(r => r.Codigo).IsUnique().HasDatabaseName("UX_roles_plantilla_codigo");
            entity.HasAlternateKey(r => new { r.IdRolPlantilla, r.Modulo }).HasName("UX_roles_plantilla_id_modulo");
        }
    }

    public sealed class RolPlantillaPermisoConfiguracion : IEntityTypeConfiguration<RolPlantillaPermiso>
    {
        public void Configure(EntityTypeBuilder<RolPlantillaPermiso> entity)
        {
            entity.HasKey(l => new { l.IdRolPlantilla, l.IdPermiso }).HasName("PK_roles_plantilla_permisos");
            entity.Property(l => l.Modulo).IsUnicode(false);
            entity.HasOne<RolPlantilla>().WithMany().HasForeignKey(l => new { l.IdRolPlantilla, l.Modulo })
                  .HasPrincipalKey(r => new { r.IdRolPlantilla, r.Modulo })
                  .HasConstraintName("FK_roles_plantilla_permisos_plantilla").OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Permiso>().WithMany().HasForeignKey(l => new { l.IdPermiso, l.Modulo })
                  .HasPrincipalKey(p => new { p.IdPermiso, p.Modulo })
                  .HasConstraintName("FK_roles_plantilla_permisos_permisos").OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(l => l.IdPermiso).HasDatabaseName("IX_roles_plantilla_permisos_permiso");
        }
    }

    /// <summary>Permisos de cada rol de empresa. Tabla de tenant (id_tenant y RLS); clave primaria (rol, permiso).</summary>
    public sealed class RolPermisoConfiguracion : IEntityTypeConfiguration<RolPermiso>
    {
        public void Configure(EntityTypeBuilder<RolPermiso> entity)
        {
            entity.HasKey(r => new { r.IdRol, r.IdPermiso }).HasName("PK_rol_permisos");
            entity.Property(r => r.AmbitoPermiso).IsUnicode(false);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(r => r.IdTenant)
                  .HasConstraintName("FK_rol_permisos_tenants").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EmpresaRol>().WithMany().HasForeignKey(r => r.IdRol)
                  .HasConstraintName("FKT_rol_permisos_empresa_roles_id_rol").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Permiso>().WithMany().HasForeignKey(r => new { r.IdPermiso, r.AmbitoPermiso })
                  .HasPrincipalKey(p => new { p.IdPermiso, p.Ambito })
                  .HasConstraintName("FK_rol_permisos_permisos").OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.IdTenant).HasDatabaseName("IX_rol_permisos_tenant");
            entity.HasIndex(r => r.IdPermiso).HasDatabaseName("IX_rol_permisos_permiso");
        }
    }
}
