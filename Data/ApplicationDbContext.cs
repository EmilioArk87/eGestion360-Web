using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Contabilidad;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<PasswordResetCode> PasswordResetCodes { get; set; }
        public DbSet<EmailConfiguration> EmailConfigurations { get; set; }
        public DbSet<Empresa> Empresas { get; set; }
        public DbSet<Modulo> Modulos { get; set; }
        public DbSet<EmpresaModulo> EmpresaModulos { get; set; }
        public DbSet<EmpresaRol> EmpresaRoles { get; set; }
        public DbSet<EmpresaRolPermiso> EmpresaRolPermisos { get; set; }
        public DbSet<Pais> Paises { get; set; }
        public DbSet<Moneda> Monedas { get; set; }
        public DbSet<Vehiculo> Vehiculos { get; set; }
        public DbSet<TipoVehiculo> TiposVehiculo { get; set; }
        public DbSet<Cargo> Cargos { get; set; }
        public DbSet<Ruta> Rutas { get; set; }
        public DbSet<CargaCombustible> CargasCombustible { get; set; }
        public DbSet<CategoriaRepuesto> CategoriasRepuesto { get; set; }
        public DbSet<GastoRepuesto> GastosRepuesto { get; set; }
        public DbSet<OdometroDiario> OdometrosDiarios { get; set; }
        public DbSet<OrdenMantenimiento> OrdenesMantenimiento { get; set; }
        public DbSet<Peaje> Peajes { get; set; }
        public DbSet<Persona> Personas { get; set; }

        // Persona maestra (scripts 014 a 018): vínculos por empresa, roles, documentos y bitácora
        public DbSet<PersonaDocumento> PersonaDocumentos { get; set; }
        public DbSet<PersonaEmpresa> PersonaEmpresas { get; set; }
        public DbSet<Empleado> Empleados { get; set; }
        public DbSet<BitacoraCambio> BitacoraCambios { get; set; }

        // Catálogos globales de Honduras (scripts 014 y 015), sin id_empresa
        public DbSet<CatalogoDepartamento> CatalogoDepartamentos { get; set; }
        public DbSet<CatalogoMunicipio> CatalogoMunicipios { get; set; }
        public DbSet<CatalogoTipoDocumento> CatalogoTiposDocumento { get; set; }
        public DbSet<CatalogoTipoLicencia> CatalogoTiposLicencia { get; set; }
        public DbSet<PolizaSeguro> PolizasSeguros { get; set; }
        public DbSet<SalarioDiario> SalariosDiarios { get; set; }
        public DbSet<Taller> Talleres { get; set; }
        public DbSet<ControlSalida> ControlSalidas { get; set; }

        // Catálogos transversales (Fase 0)
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<ProductoServicio> ProductosServicios { get; set; }
        public DbSet<Impuesto> Impuestos { get; set; }
        public DbSet<FormaPago> FormasPago { get; set; }
        public DbSet<CondicionPago> CondicionesPago { get; set; }
        public DbSet<CondicionPagoCuota> CondicionesPagoCuotas { get; set; }

        // Tasas de cambio y bitácora del job que las obtiene (script 020)
        public DbSet<TasaCambio> TasasCambio { get; set; }
        public DbSet<TasaCambioEjecucion> TasasCambioEjecuciones { get; set; }
        public DbSet<TasaCambioEjecucionDetalle> TasasCambioEjecucionesDetalle { get; set; }

        // Outbox de eventos de dominio (Fase 0)
        public DbSet<DomainEvent> DomainEvents { get; set; }

        // Facturación (Fase 1)
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<FacturaDetalle> FacturaDetalles { get; set; }
        public DbSet<FacturaSecuencia> FacturaSecuencias { get; set; }
        public DbSet<Pago> Pagos { get; set; }
        public DbSet<PagoAplicacion> PagoAplicaciones { get; set; }
        public DbSet<Nota> Notas { get; set; }

        // Contabilidad (Fase 2) — script 010_ct_nucleo_contable.sql
        public DbSet<CuentaContable> CuentasContables { get; set; }
        public DbSet<EjercicioFiscal> EjerciciosFiscales { get; set; }
        public DbSet<PeriodoContable> PeriodosContables { get; set; }
        public DbSet<CentroCosto> CentrosCosto { get; set; }
        public DbSet<Asiento> Asientos { get; set; }
        public DbSet<AsientoMovimiento> AsientoMovimientos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
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
            });

            // Configure Modulo entity
            modelBuilder.Entity<Modulo>(entity =>
            {
                entity.HasKey(e => e.IdModulo);
                entity.Property(e => e.Codigo).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.Codigo).IsUnique();
            });

            // Seed Modulos
            modelBuilder.Entity<Modulo>().HasData(
                new Modulo { IdModulo = 1, Codigo = "flota",       Nombre = "Control de Flota",   Descripcion = "Gestión de vehículos, operación, gastos y KPIs",   Icono = "fa-truck",         Orden = 1, Activo = true },
                new Modulo { IdModulo = 2, Codigo = "inventario",  Nombre = "Inventario",          Descripcion = "Control de productos, stock y movimientos",         Icono = "fa-boxes",         Orden = 2, Activo = true },
                new Modulo { IdModulo = 3, Codigo = "ventas",      Nombre = "Ventas",              Descripcion = "Registro y seguimiento de ventas realizadas",       Icono = "fa-shopping-cart", Orden = 3, Activo = true },
                new Modulo { IdModulo = 4, Codigo = "reportes",    Nombre = "Reportes",            Descripcion = "Generación de reportes y análisis de datos",        Icono = "fa-chart-bar",     Orden = 4, Activo = true },
                new Modulo { IdModulo = 5, Codigo = "catalogos",   Nombre = "Catálogos",           Descripcion = "Clientes, proveedores, productos, impuestos, formas y condiciones de pago", Icono = "fa-book", Orden = 5, Activo = true },
                new Modulo { IdModulo = 6, Codigo = "facturacion", Nombre = "Facturación",         Descripcion = "Emisión de facturas contado/crédito, notas de crédito/débito, CxC",         Icono = "fa-file-invoice-dollar", Orden = 6, Activo = true },
                new Modulo { IdModulo = 7, Codigo = "bancos",      Nombre = "Bancos",              Descripcion = "Cuentas bancarias, depósitos, cheques y conciliación",                       Icono = "fa-university",    Orden = 7, Activo = true },
                new Modulo { IdModulo = 8, Codigo = "contabilidad",Nombre = "Contabilidad",        Descripcion = "Plan de cuentas, asientos, libros y estados financieros (opcional por empresa)", Icono = "fa-calculator", Orden = 8, Activo = true }
            );

            // Configure EmpresaModulo entity
            modelBuilder.Entity<EmpresaModulo>(entity =>
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
            });

            // Configure EmpresaRol entity
            modelBuilder.Entity<EmpresaRol>(entity =>
            {
                entity.HasKey(e => e.IdRol);
                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.Empresa)
                      .WithMany()
                      .HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure Flota FK relationships (Id prefix doesn't match EF convention)
            modelBuilder.Entity<Vehiculo>(entity =>
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
            });

            modelBuilder.Entity<CargaCombustible>(entity =>
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
            });

            modelBuilder.Entity<GastoRepuesto>(entity =>
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
            });

            modelBuilder.Entity<OdometroDiario>(entity =>
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
            });

            modelBuilder.Entity<ControlSalida>(entity =>
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
            });

            modelBuilder.Entity<OrdenMantenimiento>(entity =>
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
            });

            modelBuilder.Entity<Peaje>(entity =>
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
            });

            modelBuilder.Entity<Ruta>(entity =>
            {
                entity.Property(r => r.DistanciaKm).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Persona>(entity =>
            {
                // Persona maestra (script 014). Todas las referencias son opcionales.
                entity.HasOne(p => p.Nacionalidad)
                      .WithMany()
                      .HasForeignKey(p => p.PaisNacionalidad)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);

                entity.HasOne(p => p.MunicipioNacimiento)
                      .WithMany()
                      .HasForeignKey(p => p.IdMunicipioNacimiento)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);

                entity.HasOne(p => p.MunicipioResidencia)
                      .WithMany()
                      .HasForeignKey(p => p.IdMunicipioResidencia)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);

                entity.HasOne(p => p.TipoLicencia)
                      .WithMany()
                      .HasForeignKey(p => p.LicenciaTipo)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);

                entity.HasOne(p => p.PersonaPrincipal)
                      .WithMany()
                      .HasForeignKey(p => p.IdPersonaPrincipal)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);
            });

            modelBuilder.Entity<PersonaDocumento>(entity =>
            {
                entity.HasOne(d => d.Persona)
                      .WithMany(p => p.Documentos)
                      .HasForeignKey(d => d.IdPersona)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Tipo)
                      .WithMany()
                      .HasForeignKey(d => d.TipoDocumento)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PersonaEmpresa>(entity =>
            {
                entity.HasOne(v => v.Empresa)
                      .WithMany()
                      .HasForeignKey(v => v.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(v => v.Persona)
                      .WithMany(p => p.Vinculos)
                      .HasForeignKey(v => v.IdPersona)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Empleado>(entity =>
            {
                entity.Property(e => e.TarifaDiaria).HasPrecision(18, 2);

                // La BD tiene una clave foránea compuesta (id_persona_empresa, id_empresa, tipo_vinculo)
                // que garantiza la misma empresa y el tipo 'empleado'. A EF le basta la parte simple.
                entity.HasOne(e => e.Vinculo)
                      .WithOne(v => v.Empleado)
                      .HasForeignKey<Empleado>(e => e.IdPersonaEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // El cliente natural se enlaza con su vínculo de tipo 'cliente' (script 014).
            modelBuilder.Entity<Cliente>()
                .HasOne(c => c.Vinculo)
                .WithMany()
                .HasForeignKey(c => c.IdPersonaEmpresa)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            modelBuilder.Entity<BitacoraCambio>(entity =>
            {
                // La tabla tiene un disparador INSTEAD OF UPDATE, DELETE. Declararlo hace que EF recupere
                // el id generado sin la cláusula OUTPUT directa, que SQL Server no admite con disparadores.
                entity.ToTable(t => t.HasTrigger("TR_bitacora_cambios_inmutable"));
            });

            modelBuilder.Entity<CatalogoMunicipio>(entity =>
            {
                entity.HasOne(m => m.Departamento)
                      .WithMany(d => d.Municipios)
                      .HasForeignKey(m => m.IdDepartamento)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PolizaSeguro>(entity =>
            {
                entity.Property(p => p.CostoDiario).HasPrecision(18, 2);
                entity.Property(p => p.PrimaTotal).HasPrecision(18, 2);

                entity.HasOne(p => p.Vehiculo)
                      .WithMany()
                      .HasForeignKey(p => p.IdVehiculo)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalarioDiario>(entity =>
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
            });

            // Configure EmpresaRolPermiso entity (PK compuesto)
            modelBuilder.Entity<EmpresaRolPermiso>(entity =>
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
            });

            // Configure PasswordResetCode entity
            modelBuilder.Entity<PasswordResetCode>(entity =>
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
            });

            // Configure EmailConfiguration entity
            modelBuilder.Entity<EmailConfiguration>(entity =>
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
            });

            // Configure Moneda entity
            modelBuilder.Entity<Moneda>(entity =>
            {
                entity.HasKey(e => e.CodigoIso);
                entity.Property(e => e.CodigoIso).IsRequired().HasMaxLength(3);
                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Simbolo).IsRequired().HasMaxLength(10);
            });

            // Seed Monedas
            modelBuilder.Entity<Moneda>().HasData(
                new Moneda { CodigoIso = "AED", Nombre = "Dírham de los EAU", Simbolo = "د.إ" },
                new Moneda { CodigoIso = "AFN", Nombre = "Afgani afgano", Simbolo = "؋" },
                new Moneda { CodigoIso = "ALL", Nombre = "Lek albanés", Simbolo = "L" },
                new Moneda { CodigoIso = "AMD", Nombre = "Dram armenio", Simbolo = "֏" },
                new Moneda { CodigoIso = "ANG", Nombre = "Florín antillano neerlandés", Simbolo = "ƒ" },
                new Moneda { CodigoIso = "AOA", Nombre = "Kwanza angoleño", Simbolo = "Kz" },
                new Moneda { CodigoIso = "ARS", Nombre = "Peso argentino", Simbolo = "$" },
                new Moneda { CodigoIso = "AUD", Nombre = "Dólar australiano", Simbolo = "A$" },
                new Moneda { CodigoIso = "AWG", Nombre = "Florín arubeño", Simbolo = "ƒ" },
                new Moneda { CodigoIso = "AZN", Nombre = "Manat azerbaiyano", Simbolo = "₼" },
                new Moneda { CodigoIso = "BAM", Nombre = "Marco bosnio convertible", Simbolo = "KM" },
                new Moneda { CodigoIso = "BBD", Nombre = "Dólar de Barbados", Simbolo = "Bds$" },
                new Moneda { CodigoIso = "BDT", Nombre = "Taka bangladesí", Simbolo = "৳" },
                new Moneda { CodigoIso = "BGN", Nombre = "Lev búlgaro", Simbolo = "лв" },
                new Moneda { CodigoIso = "BHD", Nombre = "Dinar bareiní", Simbolo = ".د.ب" },
                new Moneda { CodigoIso = "BIF", Nombre = "Franco burundés", Simbolo = "Fr" },
                new Moneda { CodigoIso = "BMD", Nombre = "Dólar de Bermudas", Simbolo = "$" },
                new Moneda { CodigoIso = "BND", Nombre = "Dólar de Brunéi", Simbolo = "B$" },
                new Moneda { CodigoIso = "BOB", Nombre = "Boliviano", Simbolo = "Bs" },
                new Moneda { CodigoIso = "BRL", Nombre = "Real brasileño", Simbolo = "R$" },
                new Moneda { CodigoIso = "BSD", Nombre = "Dólar bahameño", Simbolo = "B$" },
                new Moneda { CodigoIso = "BTN", Nombre = "Ngultrum butanés", Simbolo = "Nu" },
                new Moneda { CodigoIso = "BWP", Nombre = "Pula botsuanesa", Simbolo = "P" },
                new Moneda { CodigoIso = "BYN", Nombre = "Rublo bielorruso", Simbolo = "Br" },
                new Moneda { CodigoIso = "BZD", Nombre = "Dólar de Belice", Simbolo = "BZ$" },
                new Moneda { CodigoIso = "CAD", Nombre = "Dólar canadiense", Simbolo = "CA$" },
                new Moneda { CodigoIso = "CDF", Nombre = "Franco congoleño", Simbolo = "Fr" },
                new Moneda { CodigoIso = "CHF", Nombre = "Franco suizo", Simbolo = "Fr" },
                new Moneda { CodigoIso = "CLP", Nombre = "Peso chileno", Simbolo = "$" },
                new Moneda { CodigoIso = "CNY", Nombre = "Yuan chino", Simbolo = "¥" },
                new Moneda { CodigoIso = "COP", Nombre = "Peso colombiano", Simbolo = "$" },
                new Moneda { CodigoIso = "CRC", Nombre = "Colón costarricense", Simbolo = "₡" },
                new Moneda { CodigoIso = "CUP", Nombre = "Peso cubano", Simbolo = "$" },
                new Moneda { CodigoIso = "CVE", Nombre = "Escudo caboverdiano", Simbolo = "$" },
                new Moneda { CodigoIso = "CZK", Nombre = "Corona checa", Simbolo = "Kč" },
                new Moneda { CodigoIso = "DJF", Nombre = "Franco yibutiano", Simbolo = "Fr" },
                new Moneda { CodigoIso = "DKK", Nombre = "Corona danesa", Simbolo = "kr" },
                new Moneda { CodigoIso = "DOP", Nombre = "Peso dominicano", Simbolo = "RD$" },
                new Moneda { CodigoIso = "DZD", Nombre = "Dinar argelino", Simbolo = "دج" },
                new Moneda { CodigoIso = "EGP", Nombre = "Libra egipcia", Simbolo = "E£" },
                new Moneda { CodigoIso = "ERN", Nombre = "Nakfa eritreo", Simbolo = "Nfk" },
                new Moneda { CodigoIso = "ETB", Nombre = "Birr etíope", Simbolo = "Br" },
                new Moneda { CodigoIso = "EUR", Nombre = "Euro", Simbolo = "€" },
                new Moneda { CodigoIso = "FJD", Nombre = "Dólar fiyiano", Simbolo = "FJ$" },
                new Moneda { CodigoIso = "FKP", Nombre = "Libra malvinense", Simbolo = "£" },
                new Moneda { CodigoIso = "GBP", Nombre = "Libra esterlina", Simbolo = "£" },
                new Moneda { CodigoIso = "GEL", Nombre = "Lari georgiano", Simbolo = "₾" },
                new Moneda { CodigoIso = "GHS", Nombre = "Cedi ghanés", Simbolo = "₵" },
                new Moneda { CodigoIso = "GIP", Nombre = "Libra gibraltareña", Simbolo = "£" },
                new Moneda { CodigoIso = "GMD", Nombre = "Dalasi gambiano", Simbolo = "D" },
                new Moneda { CodigoIso = "GNF", Nombre = "Franco guineano", Simbolo = "Fr" },
                new Moneda { CodigoIso = "GTQ", Nombre = "Quetzal guatemalteco", Simbolo = "Q" },
                new Moneda { CodigoIso = "GYD", Nombre = "Dólar de Guyana", Simbolo = "GY$" },
                new Moneda { CodigoIso = "HKD", Nombre = "Dólar de Hong Kong", Simbolo = "HK$" },
                new Moneda { CodigoIso = "HNL", Nombre = "Lempira hondureño", Simbolo = "L" },
                new Moneda { CodigoIso = "HTG", Nombre = "Gourde haitiano", Simbolo = "G" },
                new Moneda { CodigoIso = "HUF", Nombre = "Forinto húngaro", Simbolo = "Ft" },
                new Moneda { CodigoIso = "IDR", Nombre = "Rupia indonesia", Simbolo = "Rp" },
                new Moneda { CodigoIso = "ILS", Nombre = "Nuevo séquel israelí", Simbolo = "₪" },
                new Moneda { CodigoIso = "INR", Nombre = "Rupia india", Simbolo = "₹" },
                new Moneda { CodigoIso = "IQD", Nombre = "Dinar iraquí", Simbolo = "ع.د" },
                new Moneda { CodigoIso = "IRR", Nombre = "Rial iraní", Simbolo = "﷼" },
                new Moneda { CodigoIso = "ISK", Nombre = "Corona islandesa", Simbolo = "kr" },
                new Moneda { CodigoIso = "JMD", Nombre = "Dólar jamaicano", Simbolo = "J$" },
                new Moneda { CodigoIso = "JOD", Nombre = "Dinar jordano", Simbolo = "JD" },
                new Moneda { CodigoIso = "JPY", Nombre = "Yen japonés", Simbolo = "¥" },
                new Moneda { CodigoIso = "KES", Nombre = "Chelín keniata", Simbolo = "KSh" },
                new Moneda { CodigoIso = "KGS", Nombre = "Som kirguís", Simbolo = "с" },
                new Moneda { CodigoIso = "KHR", Nombre = "Riel camboyano", Simbolo = "៛" },
                new Moneda { CodigoIso = "KMF", Nombre = "Franco comorense", Simbolo = "Fr" },
                new Moneda { CodigoIso = "KPW", Nombre = "Won norcoreano", Simbolo = "₩" },
                new Moneda { CodigoIso = "KRW", Nombre = "Won surcoreano", Simbolo = "₩" },
                new Moneda { CodigoIso = "KWD", Nombre = "Dinar kuwaití", Simbolo = "KD" },
                new Moneda { CodigoIso = "KZT", Nombre = "Tenge kazajo", Simbolo = "₸" },
                new Moneda { CodigoIso = "LAK", Nombre = "Kip laosiano", Simbolo = "₭" },
                new Moneda { CodigoIso = "LBP", Nombre = "Libra libanesa", Simbolo = "L£" },
                new Moneda { CodigoIso = "LKR", Nombre = "Rupia de Sri Lanka", Simbolo = "Rs" },
                new Moneda { CodigoIso = "LRD", Nombre = "Dólar liberiano", Simbolo = "L$" },
                new Moneda { CodigoIso = "LSL", Nombre = "Loti lesotense", Simbolo = "L" },
                new Moneda { CodigoIso = "LYD", Nombre = "Dinar libio", Simbolo = "LD" },
                new Moneda { CodigoIso = "MAD", Nombre = "Dírham marroquí", Simbolo = "MAD" },
                new Moneda { CodigoIso = "MDL", Nombre = "Leu moldavo", Simbolo = "L" },
                new Moneda { CodigoIso = "MGA", Nombre = "Ariary malgache", Simbolo = "Ar" },
                new Moneda { CodigoIso = "MKD", Nombre = "Denar macedonio", Simbolo = "ден" },
                new Moneda { CodigoIso = "MMK", Nombre = "Kyat birmano", Simbolo = "K" },
                new Moneda { CodigoIso = "MNT", Nombre = "Tugrik mongol", Simbolo = "₮" },
                new Moneda { CodigoIso = "MOP", Nombre = "Pataca macaense", Simbolo = "P" },
                new Moneda { CodigoIso = "MRU", Nombre = "Uguiya mauritana", Simbolo = "UM" },
                new Moneda { CodigoIso = "MUR", Nombre = "Rupia mauriciana", Simbolo = "Rs" },
                new Moneda { CodigoIso = "MVR", Nombre = "Rufiyaa maldiva", Simbolo = "Rf" },
                new Moneda { CodigoIso = "MWK", Nombre = "Kwacha malauí", Simbolo = "MK" },
                new Moneda { CodigoIso = "MXN", Nombre = "Peso mexicano", Simbolo = "$" },
                new Moneda { CodigoIso = "MYR", Nombre = "Ringgit malayo", Simbolo = "RM" },
                new Moneda { CodigoIso = "MZN", Nombre = "Metical mozambiqueño", Simbolo = "MT" },
                new Moneda { CodigoIso = "NAD", Nombre = "Dólar namibio", Simbolo = "N$" },
                new Moneda { CodigoIso = "NGN", Nombre = "Naira nigeriana", Simbolo = "₦" },
                new Moneda { CodigoIso = "NIO", Nombre = "Córdoba nicaragüense", Simbolo = "C$" },
                new Moneda { CodigoIso = "NOK", Nombre = "Corona noruega", Simbolo = "kr" },
                new Moneda { CodigoIso = "NPR", Nombre = "Rupia nepalesa", Simbolo = "Rs" },
                new Moneda { CodigoIso = "NZD", Nombre = "Dólar neozelandés", Simbolo = "NZ$" },
                new Moneda { CodigoIso = "OMR", Nombre = "Rial omaní", Simbolo = "ر.ع." },
                new Moneda { CodigoIso = "PAB", Nombre = "Balboa panameño", Simbolo = "B/." },
                new Moneda { CodigoIso = "PEN", Nombre = "Sol peruano", Simbolo = "S/" },
                new Moneda { CodigoIso = "PGK", Nombre = "Kina de Papúa Nueva Guinea", Simbolo = "K" },
                new Moneda { CodigoIso = "PHP", Nombre = "Peso filipino", Simbolo = "₱" },
                new Moneda { CodigoIso = "PKR", Nombre = "Rupia pakistaní", Simbolo = "Rs" },
                new Moneda { CodigoIso = "PLN", Nombre = "Esloti polaco", Simbolo = "zł" },
                new Moneda { CodigoIso = "PYG", Nombre = "Guaraní paraguayo", Simbolo = "₲" },
                new Moneda { CodigoIso = "QAR", Nombre = "Riyal catarí", Simbolo = "QR" },
                new Moneda { CodigoIso = "RON", Nombre = "Leu rumano", Simbolo = "lei" },
                new Moneda { CodigoIso = "RSD", Nombre = "Dinar serbio", Simbolo = "din" },
                new Moneda { CodigoIso = "RUB", Nombre = "Rublo ruso", Simbolo = "₽" },
                new Moneda { CodigoIso = "RWF", Nombre = "Franco ruandés", Simbolo = "Fr" },
                new Moneda { CodigoIso = "SAR", Nombre = "Riyal saudí", Simbolo = "SR" },
                new Moneda { CodigoIso = "SBD", Nombre = "Dólar de las Islas Salomón", Simbolo = "SI$" },
                new Moneda { CodigoIso = "SCR", Nombre = "Rupia de Seychelles", Simbolo = "Rs" },
                new Moneda { CodigoIso = "SDG", Nombre = "Libra sudanesa", Simbolo = "£" },
                new Moneda { CodigoIso = "SEK", Nombre = "Corona sueca", Simbolo = "kr" },
                new Moneda { CodigoIso = "SGD", Nombre = "Dólar de Singapur", Simbolo = "S$" },
                new Moneda { CodigoIso = "SHP", Nombre = "Libra de Santa Elena", Simbolo = "£" },
                new Moneda { CodigoIso = "SLE", Nombre = "Leone de Sierra Leona", Simbolo = "Le" },
                new Moneda { CodigoIso = "SOS", Nombre = "Chelín somalí", Simbolo = "Sh" },
                new Moneda { CodigoIso = "SRD", Nombre = "Dólar surinamés", Simbolo = "$" },
                new Moneda { CodigoIso = "STN", Nombre = "Dobra de Santo Tomé", Simbolo = "Db" },
                new Moneda { CodigoIso = "SVC", Nombre = "Colón salvadoreño", Simbolo = "₡" },
                new Moneda { CodigoIso = "SYP", Nombre = "Libra siria", Simbolo = "£" },
                new Moneda { CodigoIso = "SZL", Nombre = "Lilangeni suazi", Simbolo = "L" },
                new Moneda { CodigoIso = "THB", Nombre = "Baht tailandés", Simbolo = "฿" },
                new Moneda { CodigoIso = "TJS", Nombre = "Somoni tayiko", Simbolo = "SM" },
                new Moneda { CodigoIso = "TMT", Nombre = "Manat turcomano", Simbolo = "T" },
                new Moneda { CodigoIso = "TND", Nombre = "Dinar tunecino", Simbolo = "DT" },
                new Moneda { CodigoIso = "TOP", Nombre = "Pa'anga tongano", Simbolo = "T$" },
                new Moneda { CodigoIso = "TRY", Nombre = "Lira turca", Simbolo = "₺" },
                new Moneda { CodigoIso = "TTD", Nombre = "Dólar de Trinidad y Tobago", Simbolo = "TT$" },
                new Moneda { CodigoIso = "TWD", Nombre = "Nuevo dólar taiwanés", Simbolo = "NT$" },
                new Moneda { CodigoIso = "TZS", Nombre = "Chelín tanzano", Simbolo = "Sh" },
                new Moneda { CodigoIso = "UAH", Nombre = "Grivna ucraniana", Simbolo = "₴" },
                new Moneda { CodigoIso = "UGX", Nombre = "Chelín ugandés", Simbolo = "Sh" },
                new Moneda { CodigoIso = "USD", Nombre = "Dólar estadounidense", Simbolo = "$" },
                new Moneda { CodigoIso = "UYU", Nombre = "Peso uruguayo", Simbolo = "$U" },
                new Moneda { CodigoIso = "UZS", Nombre = "Som uzbeko", Simbolo = "лв" },
                new Moneda { CodigoIso = "VES", Nombre = "Bolívar venezolano", Simbolo = "Bs" },
                new Moneda { CodigoIso = "VND", Nombre = "Dong vietnamita", Simbolo = "₫" },
                new Moneda { CodigoIso = "VUV", Nombre = "Vatu de Vanuatu", Simbolo = "Vt" },
                new Moneda { CodigoIso = "WST", Nombre = "Tālā samoano", Simbolo = "T" },
                new Moneda { CodigoIso = "XAF", Nombre = "Franco CFA de África Central", Simbolo = "Fr" },
                new Moneda { CodigoIso = "XCD", Nombre = "Dólar del Caribe Oriental", Simbolo = "EC$" },
                new Moneda { CodigoIso = "XOF", Nombre = "Franco CFA de África Occidental", Simbolo = "Fr" },
                new Moneda { CodigoIso = "XPF", Nombre = "Franco CFP", Simbolo = "Fr" },
                new Moneda { CodigoIso = "YER", Nombre = "Rial yemení", Simbolo = "﷼" },
                new Moneda { CodigoIso = "ZAR", Nombre = "Rand sudafricano", Simbolo = "R" },
                new Moneda { CodigoIso = "ZMW", Nombre = "Kwacha zambiano", Simbolo = "ZK" },
                new Moneda { CodigoIso = "ZWL", Nombre = "Dólar zimbabuense", Simbolo = "$" }
            );

            // Configure Pais entity
            modelBuilder.Entity<Pais>(entity =>
            {
                entity.HasKey(e => e.CodigoIso);
                entity.Property(e => e.CodigoIso).IsRequired().HasMaxLength(2);
                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
            });

            // PaÃ­ses: sin semilla aquÃ­. La tabla catalogo_paises (249 filas, ISO 3166-1) la carga el script 016 (decisiÃ³n D12).

            // ─────────────────────────────────────────────────────────────────
            // Catálogos transversales (Fase 0) — todos multitenant por id_empresa
            // ─────────────────────────────────────────────────────────────────

            modelBuilder.Entity<Cliente>(entity =>
            {
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
            });

            modelBuilder.Entity<Proveedor>(entity =>
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
            });

            modelBuilder.Entity<Impuesto>(entity =>
            {
                entity.HasKey(e => e.IdImpuesto);
                entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

                entity.HasOne(e => e.Empresa)
                      .WithMany()
                      .HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductoServicio>(entity =>
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
            });

            modelBuilder.Entity<FormaPago>(entity =>
            {
                entity.HasKey(e => e.IdFormaPago);
                entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

                entity.HasOne(e => e.Empresa)
                      .WithMany()
                      .HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CondicionPago>(entity =>
            {
                entity.HasKey(e => e.IdCondicionPago);
                entity.HasIndex(e => new { e.IdEmpresa, e.Codigo }).IsUnique();

                entity.HasOne(e => e.Empresa)
                      .WithMany()
                      .HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CondicionPagoCuota>(entity =>
            {
                entity.HasKey(e => e.IdCuota);
                entity.HasIndex(e => new { e.IdCondicionPago, e.NumeroCuota }).IsUnique();

                entity.HasOne(e => e.CondicionPago)
                      .WithMany(c => c.Cuotas)
                      .HasForeignKey(e => e.IdCondicionPago)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Tasas de cambio (script 020). Una sola tasa VIGENTE por par, tipo, fecha y empresa:
            // lo garantiza el índice único filtrado. En SQL Server los NULL de id_empresa cuentan como
            // iguales (una sola tasa oficial por clave); SQLite los trata como distintos, así que las
            // pruebas en memoria no cubren el caso de dos tasas oficiales duplicadas.
            modelBuilder.Entity<TasaCambio>(entity =>
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
            });

            modelBuilder.Entity<TasaCambioEjecucion>(entity =>
            {
                entity.HasKey(e => e.IdEjecucion);
                entity.HasIndex(e => new { e.Job, e.FechaObjetivo, e.InicioUtc })
                      .HasDatabaseName("IX_tce_job_fecha");

                entity.HasOne(e => e.EjecucionOrigen)
                      .WithMany()
                      .HasForeignKey(e => e.IdEjecucionOrigen)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired(false);
            });

            modelBuilder.Entity<TasaCambioEjecucionDetalle>(entity =>
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
            });

            // ─────────────────────────────────────────────────────────────────
            // Facturación (Fase 1 Sprint 3)
            // ─────────────────────────────────────────────────────────────────
            modelBuilder.Entity<Factura>(entity =>
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
            });

            modelBuilder.Entity<FacturaDetalle>(entity =>
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
            });

            modelBuilder.Entity<FacturaSecuencia>(entity =>
            {
                entity.HasKey(e => e.IdSecuencia);
                entity.HasIndex(e => new { e.IdEmpresa, e.TipoDocumento, e.Serie })
                      .IsUnique()
                      .HasDatabaseName("UX_factura_secuencias_tenant_tipo_serie");
            });

            // ─────────────────────────────────────────────────────────────────
            // Pagos y Notas (Fase 1 Sprint 4)
            // ─────────────────────────────────────────────────────────────────
            modelBuilder.Entity<Pago>(entity =>
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
            });

            modelBuilder.Entity<PagoAplicacion>(entity =>
            {
                entity.HasKey(e => e.IdAplicacion);
                entity.HasIndex(e => e.IdFactura).HasDatabaseName("IX_pago_aplicaciones_factura");

                entity.HasOne(e => e.Pago).WithMany(p => p.Aplicaciones).HasForeignKey(e => e.IdPago).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Factura).WithMany().HasForeignKey(e => e.IdFactura).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Nota>(entity =>
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
            });

            // ─────────────────────────────────────────────────────────────────
            // Outbox de eventos de dominio (Fase 0 Sprint 2)
            // ─────────────────────────────────────────────────────────────────
            modelBuilder.Entity<DomainEvent>(entity =>
            {
                entity.HasKey(e => e.IdEvento);
                entity.Property(e => e.IdEvento).ValueGeneratedOnAdd();

                // Índice para el worker: trae eventos elegibles ordenados
                entity.HasIndex(e => new { e.Status, e.ProximoIntentoEn })
                      .HasDatabaseName("IX_domain_events_status_proximo");

                // Índice para idempotencia de handlers y trazabilidad por agregado
                entity.HasIndex(e => new { e.IdEmpresa, e.AggregateType, e.AggregateId })
                      .HasDatabaseName("IX_domain_events_aggregate");

                entity.HasIndex(e => new { e.IdEmpresa, e.EventType, e.Status })
                      .HasDatabaseName("IX_domain_events_tenant_type_status");
            });

            // ─────────────────────────────────────────────────────────────────
            // Contabilidad (Fase 2) — mapea las tablas creadas por
            // 2 - Script SQL/010_ct_nucleo_contable.sql
            //
            // Aislamiento entre empresas: cada tabla referida tiene una clave alterna (id, id_empresa) y las
            // relaciones internas del módulo usan las dos columnas, así la BD rechaza un movimiento con la cuenta de
            // otra empresa (o un asiento con su período, etc.). Nada se borra en cascada: los asientos se anulan.
            // ─────────────────────────────────────────────────────────────────
            modelBuilder.Entity<CuentaContable>(entity =>
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
            });

            modelBuilder.Entity<EjercicioFiscal>(entity =>
            {
                entity.HasKey(e => e.IdEjercicio);
                entity.HasAlternateKey(e => new { e.IdEjercicio, e.IdEmpresa })
                      .HasName("UQ_ct_ejercicios_ejercicio_empresa");
                entity.HasIndex(e => new { e.IdEmpresa, e.Anio })
                      .IsUnique()
                      .HasDatabaseName("UX_ct_ejercicios_empresa_anio");

                entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_ejercicios_empresa");
            });

            modelBuilder.Entity<PeriodoContable>(entity =>
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
            });

            modelBuilder.Entity<CentroCosto>(entity =>
            {
                entity.HasKey(e => e.IdCentroCosto);
                entity.HasAlternateKey(e => new { e.IdCentroCosto, e.IdEmpresa })
                      .HasName("UQ_ct_centros_costo_centro_empresa");
                entity.HasIndex(e => new { e.IdEmpresa, e.Codigo })
                      .IsUnique()
                      .HasDatabaseName("UX_ct_centros_costo_empresa_codigo");

                entity.HasOne<Empresa>().WithMany().HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_ct_centros_costo_empresa");
            });

            modelBuilder.Entity<Asiento>(entity =>
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
            });

            modelBuilder.Entity<AsientoMovimiento>(entity =>
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
            });

            // Seed data
            modelBuilder.Entity<User>().HasData(
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
}