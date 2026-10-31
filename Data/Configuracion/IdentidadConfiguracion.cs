using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Data.Configuracion
{
    // Identidad global (F1.5; scripts 038 y 039; ADR-003). Los nombres de claves e índices son los del script 038. Las claves foráneas
    // entre estas tablas y a dbo.personas / dbo.empresas las exige la base; el modelo no navega entre ellas.

    public sealed class CuentaConfiguracion : IEntityTypeConfiguration<Cuenta>
    {
        public void Configure(EntityTypeBuilder<Cuenta> entity)
        {
            entity.HasKey(c => c.IdCuenta).HasName("PK_cuentas");
            entity.Property(c => c.IdCuenta).ValueGeneratedOnAdd();
            entity.Property(c => c.UidPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(c => c.Estado).IsUnicode(false);
            entity.HasIndex(c => c.UidPublico).IsUnique().HasDatabaseName("UX_cuentas_uid_publico");
            entity.HasIndex(c => c.Correo).IsUnique().HasDatabaseName("UX_cuentas_correo");
            entity.HasIndex(c => c.NombreUsuario).IsUnique().HasDatabaseName("UX_cuentas_nombre_usuario")
                  .HasFilter("[nombre_usuario] IS NOT NULL");
            entity.HasIndex(c => c.IdUserLegado).IsUnique().HasDatabaseName("UX_cuentas_id_user_legado")
                  .HasFilter("[id_user_legado] IS NOT NULL");
        }
    }

    public sealed class CuentaAliasConfiguracion : IEntityTypeConfiguration<CuentaAlias>
    {
        public void Configure(EntityTypeBuilder<CuentaAlias> entity)
        {
            entity.HasKey(a => a.IdAlias).HasName("PK_cuenta_alias");
            entity.Property(a => a.Tipo).IsUnicode(false);
            entity.HasIndex(a => a.Alias).IsUnique().HasDatabaseName("UX_cuenta_alias_alias");
            entity.HasIndex(a => new { a.IdUserLegado, a.Tipo }).IsUnique().HasDatabaseName("UX_cuenta_alias_user_legado_tipo")
                  .HasFilter("[id_user_legado] IS NOT NULL");
            entity.HasIndex(a => a.IdCuenta).HasDatabaseName("IX_cuenta_alias_cuenta");
        }
    }

    public sealed class MembresiaConfiguracion : IEntityTypeConfiguration<Membresia>
    {
        public void Configure(EntityTypeBuilder<Membresia> entity)
        {
            entity.HasKey(m => m.IdMembresia).HasName("PK_membresias");
            entity.Property(m => m.Estado).IsUnicode(false);
            entity.HasIndex(m => new { m.IdCuenta, m.IdTenant }).IsUnique().HasDatabaseName("UX_membresias_cuenta_tenant");
            entity.HasIndex(m => new { m.IdTenant, m.IdMembresia }).IsUnique().HasDatabaseName("UX_membresias_tenant_id_membresia");
        }
    }

    public sealed class AccesoEmpresaConfiguracion : IEntityTypeConfiguration<AccesoEmpresa>
    {
        public void Configure(EntityTypeBuilder<AccesoEmpresa> entity)
        {
            entity.HasKey(a => a.IdAccesoEmpresa).HasName("PK_accesos_empresa");
            entity.Property(a => a.RolLegado).IsUnicode(false);
            entity.HasIndex(a => new { a.IdMembresia, a.IdEmpresa }).IsUnique().HasDatabaseName("UX_accesos_empresa_membresia_empresa");
            entity.HasIndex(a => a.IdMembresia).IsUnique().HasDatabaseName("UX_accesos_empresa_principal")
                  .HasFilter("[es_principal] = 1");
            entity.HasIndex(a => a.IdTenant).HasDatabaseName("IX_accesos_empresa_tenant");
        }
    }

    public sealed class CuentaIntentosConfiguracion : IEntityTypeConfiguration<CuentaIntentos>
    {
        public void Configure(EntityTypeBuilder<CuentaIntentos> entity)
        {
            entity.HasKey(i => i.IdCuenta).HasName("PK_cuenta_intentos");
            entity.Property(i => i.IdCuenta).ValueGeneratedNever();
        }
    }

    public sealed class CuentaMfaConfiguracion : IEntityTypeConfiguration<CuentaMfa>
    {
        public void Configure(EntityTypeBuilder<CuentaMfa> entity)
        {
            entity.HasKey(m => m.IdCuenta).HasName("PK_cuenta_mfa");
            entity.Property(m => m.IdCuenta).ValueGeneratedNever();
        }
    }

    public sealed class CuentaMfaRecuperacionConfiguracion : IEntityTypeConfiguration<CuentaMfaRecuperacion>
    {
        public void Configure(EntityTypeBuilder<CuentaMfaRecuperacion> entity)
        {
            entity.HasKey(r => r.Id).HasName("PK_cuenta_mfa_recuperacion");
            entity.Property(r => r.HashCodigo).HasColumnType("char(64)");
            entity.HasIndex(r => new { r.IdCuenta, r.HashCodigo }).IsUnique().HasDatabaseName("UX_cuenta_mfa_recuperacion_cuenta_codigo");
        }
    }

    public sealed class InvitacionConfiguracion : IEntityTypeConfiguration<Invitacion>
    {
        public void Configure(EntityTypeBuilder<Invitacion> entity)
        {
            entity.HasKey(i => i.IdInvitacion).HasName("PK_invitaciones");
            entity.Property(i => i.TokenHash).HasColumnType("char(64)");
            entity.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName("UX_invitaciones_token_hash");
            entity.HasIndex(i => i.IdTenant).HasDatabaseName("IX_invitaciones_tenant");
            entity.HasIndex(i => i.Correo).HasDatabaseName("IX_invitaciones_correo");
        }
    }

    public sealed class LlaveProteccionDatosConfiguracion : IEntityTypeConfiguration<LlaveProteccionDatos>
    {
        public void Configure(EntityTypeBuilder<LlaveProteccionDatos> entity)
        {
            entity.HasKey(l => l.IdLlave).HasName("PK_llaves_proteccion_datos");
        }
    }
}
