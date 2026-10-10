using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;

namespace eGestion360Web.Data.Configuracion
{
    // Seguridad y plataforma: usuarios, módulos, roles y permisos, recuperación de contraseña y correo.

    public sealed class UserConfiguracion : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> entity)
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Password).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(20).HasDefaultValue("user");
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.EmpresaId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(e => e.EmpresaRol)
                  .WithMany(r => r.Usuarios)
                  .HasForeignKey(e => e.EmpresaRolId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            // Script 021: la persona que usa la cuenta. Sin cascada; varias cuentas pueden ser de la misma persona.
            entity.HasOne(e => e.Persona)
                  .WithMany()
                  .HasForeignKey(e => e.PersonaId)
                  .HasConstraintName("FK_Users_personas_PersonaId")
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
            entity.HasIndex(e => e.PersonaId).HasDatabaseName("IX_Users_PersonaId");

            // Semilla del administrador. Solo se aplica si alguien corre las migraciones de EF (hoy apagado).
            entity.HasData(
                new User
                {
                    Id = 1,
                    Username = "admin",
                    Email = "admin@siptech.com",
                    Password = "admin123", // In production, this should be hashed
                    CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsActive = true
                }
            );
        }
    }

    public sealed class ModuloConfiguracion : IEntityTypeConfiguration<Modulo>
    {
        public void Configure(EntityTypeBuilder<Modulo> entity)
        {
            entity.HasKey(e => e.IdModulo);
            entity.Property(e => e.Codigo).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Codigo).IsUnique();

            entity.HasData(
                new Modulo { IdModulo = 1, Codigo = "flota",       Nombre = "Control de Flota",   Descripcion = "Gestión de vehículos, operación, gastos y KPIs",   Icono = "fa-truck",         Orden = 1, Activo = true },
                new Modulo { IdModulo = 2, Codigo = "inventario",  Nombre = "Inventario",          Descripcion = "Control de productos, stock y movimientos",         Icono = "fa-boxes",         Orden = 2, Activo = true },
                new Modulo { IdModulo = 3, Codigo = "ventas",      Nombre = "Ventas",              Descripcion = "Registro y seguimiento de ventas realizadas",       Icono = "fa-shopping-cart", Orden = 3, Activo = true },
                new Modulo { IdModulo = 4, Codigo = "reportes",    Nombre = "Reportes",            Descripcion = "Generación de reportes y análisis de datos",        Icono = "fa-chart-bar",     Orden = 4, Activo = true },
                new Modulo { IdModulo = 5, Codigo = "catalogos",   Nombre = "Catálogos",           Descripcion = "Clientes, proveedores, productos, impuestos, formas y condiciones de pago", Icono = "fa-book", Orden = 5, Activo = true },
                new Modulo { IdModulo = 6, Codigo = "facturacion", Nombre = "Facturación",         Descripcion = "Emisión de facturas contado/crédito, notas de crédito/débito, CxC",         Icono = "fa-file-invoice-dollar", Orden = 6, Activo = true },
                new Modulo { IdModulo = 7, Codigo = "bancos",      Nombre = "Bancos",              Descripcion = "Cuentas bancarias, depósitos, cheques y conciliación",                       Icono = "fa-university",    Orden = 7, Activo = true },
                new Modulo { IdModulo = 8, Codigo = "contabilidad",Nombre = "Contabilidad",        Descripcion = "Plan de cuentas, asientos, libros y estados financieros (opcional por empresa)", Icono = "fa-calculator", Orden = 8, Activo = true }
            );
        }
    }

    public sealed class EmpresaModuloConfiguracion : IEntityTypeConfiguration<EmpresaModulo>
    {
        public void Configure(EntityTypeBuilder<EmpresaModulo> entity)
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IdEmpresa, e.IdModulo }).IsUnique();

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Modulo)
                  .WithMany(m => m.EmpresaModulos)
                  .HasForeignKey(e => e.IdModulo)
                  .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class EmpresaRolConfiguracion : IEntityTypeConfiguration<EmpresaRol>
    {
        public void Configure(EntityTypeBuilder<EmpresaRol> entity)
        {
            entity.HasKey(e => e.IdRol);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(50);

            entity.HasOne(e => e.Empresa)
                  .WithMany()
                  .HasForeignKey(e => e.IdEmpresa)
                  .OnDelete(DeleteBehavior.Cascade);
        }
    }

    // Clave primaria compuesta (rol, módulo).
    public sealed class EmpresaRolPermisoConfiguracion : IEntityTypeConfiguration<EmpresaRolPermiso>
    {
        public void Configure(EntityTypeBuilder<EmpresaRolPermiso> entity)
        {
            entity.HasKey(e => new { e.IdRol, e.IdModulo });

            entity.HasOne(e => e.Rol)
                  .WithMany(r => r.Permisos)
                  .HasForeignKey(e => e.IdRol)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Modulo)
                  .WithMany(m => m.RolPermisos)
                  .HasForeignKey(e => e.IdModulo)
                  .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class PasswordResetCodeConfiguracion : IEntityTypeConfiguration<PasswordResetCode>
    {
        public void Configure(EntityTypeBuilder<PasswordResetCode> entity)
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(6);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(100);

            // Foreign key relationship
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Index for performance
            entity.HasIndex(e => new { e.Email, e.Code, e.IsUsed });
            entity.HasIndex(e => e.ExpiresAt);
        }
    }

    public sealed class EmailConfigurationConfiguracion : IEntityTypeConfiguration<EmailConfiguration>
    {
        public void Configure(EntityTypeBuilder<EmailConfiguration> entity)
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProfileName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FromEmail).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FromName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SmtpHost).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(50);

            // Unique constraint
            entity.HasIndex(e => e.ProfileName).IsUnique();

            // Index for performance
            entity.HasIndex(e => new { e.IsActive, e.IsDefault });
            entity.HasIndex(e => e.Provider);
        }
    }
}
