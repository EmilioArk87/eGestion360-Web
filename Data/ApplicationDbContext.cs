using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Models.Flota;

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
        public DbSet<Ruta> Rutas { get; set; }
        public DbSet<CargaCombustible> CargasCombustible { get; set; }
        public DbSet<CategoriaRepuesto> CategoriasRepuesto { get; set; }
        public DbSet<GastoRepuesto> GastosRepuesto { get; set; }
        public DbSet<OdometroDiario> OdometrosDiarios { get; set; }
        public DbSet<OrdenMantenimiento> OrdenesMantenimiento { get; set; }
        public DbSet<Persona> Personas { get; set; }
        public DbSet<PolizaSeguro> PolizasSeguros { get; set; }
        public DbSet<SalarioDiario> SalariosDiarios { get; set; }
        public DbSet<Taller> Talleres { get; set; }

        // Catálogos transversales (Fase 0)
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<ProductoServicio> ProductosServicios { get; set; }
        public DbSet<Impuesto> Impuestos { get; set; }
        public DbSet<FormaPago> FormasPago { get; set; }
        public DbSet<CondicionPago> CondicionesPago { get; set; }
        public DbSet<CondicionPagoCuota> CondicionesPagoCuotas { get; set; }
        public DbSet<TipoCambio> TiposCambio { get; set; }

        // Outbox de eventos de dominio (Fase 0)
        public DbSet<DomainEvent> DomainEvents { get; set; }

        // Facturación (Fase 1)
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<FacturaDetalle> FacturaDetalles { get; set; }
        public DbSet<FacturaSecuencia> FacturaSecuencias { get; set; }
        public DbSet<Pago> Pagos { get; set; }
        public DbSet<PagoAplicacion> PagoAplicaciones { get; set; }
        public DbSet<Nota> Notas { get; set; }

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

            modelBuilder.Entity<Ruta>(entity =>
            {
                entity.Property(r => r.DistanciaKm).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Persona>(entity =>
            {
                entity.Property(p => p.TarifaDiaria).HasPrecision(18, 2);
            });

            modelBuilder.Entity<PolizaSeguro>(entity =>
            {
                entity.Property(p => p.CostoDiario).HasPrecision(18, 2);

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

            // Seed Paises
            modelBuilder.Entity<Pais>().HasData(
                new Pais { CodigoIso = "AF", Nombre = "Afganistán" },
                new Pais { CodigoIso = "AL", Nombre = "Albania" },
                new Pais { CodigoIso = "DE", Nombre = "Alemania" },
                new Pais { CodigoIso = "AD", Nombre = "Andorra" },
                new Pais { CodigoIso = "AO", Nombre = "Angola" },
                new Pais { CodigoIso = "AG", Nombre = "Antigua y Barbuda" },
                new Pais { CodigoIso = "SA", Nombre = "Arabia Saudita" },
                new Pais { CodigoIso = "DZ", Nombre = "Argelia" },
                new Pais { CodigoIso = "AR", Nombre = "Argentina" },
                new Pais { CodigoIso = "AM", Nombre = "Armenia" },
                new Pais { CodigoIso = "AU", Nombre = "Australia" },
                new Pais { CodigoIso = "AT", Nombre = "Austria" },
                new Pais { CodigoIso = "AZ", Nombre = "Azerbaiyán" },
                new Pais { CodigoIso = "BS", Nombre = "Bahamas" },
                new Pais { CodigoIso = "BH", Nombre = "Baréin" },
                new Pais { CodigoIso = "BD", Nombre = "Bangladés" },
                new Pais { CodigoIso = "BB", Nombre = "Barbados" },
                new Pais { CodigoIso = "BE", Nombre = "Bélgica" },
                new Pais { CodigoIso = "BZ", Nombre = "Belice" },
                new Pais { CodigoIso = "BJ", Nombre = "Benín" },
                new Pais { CodigoIso = "BY", Nombre = "Bielorrusia" },
                new Pais { CodigoIso = "BO", Nombre = "Bolivia" },
                new Pais { CodigoIso = "BA", Nombre = "Bosnia y Herzegovina" },
                new Pais { CodigoIso = "BW", Nombre = "Botsuana" },
                new Pais { CodigoIso = "BR", Nombre = "Brasil" },
                new Pais { CodigoIso = "BN", Nombre = "Brunéi" },
                new Pais { CodigoIso = "BG", Nombre = "Bulgaria" },
                new Pais { CodigoIso = "BF", Nombre = "Burkina Faso" },
                new Pais { CodigoIso = "BI", Nombre = "Burundi" },
                new Pais { CodigoIso = "BT", Nombre = "Bután" },
                new Pais { CodigoIso = "CV", Nombre = "Cabo Verde" },
                new Pais { CodigoIso = "KH", Nombre = "Camboya" },
                new Pais { CodigoIso = "CM", Nombre = "Camerún" },
                new Pais { CodigoIso = "CA", Nombre = "Canadá" },
                new Pais { CodigoIso = "QA", Nombre = "Catar" },
                new Pais { CodigoIso = "TD", Nombre = "Chad" },
                new Pais { CodigoIso = "CL", Nombre = "Chile" },
                new Pais { CodigoIso = "CN", Nombre = "China" },
                new Pais { CodigoIso = "CY", Nombre = "Chipre" },
                new Pais { CodigoIso = "CO", Nombre = "Colombia" },
                new Pais { CodigoIso = "KM", Nombre = "Comoras" },
                new Pais { CodigoIso = "CG", Nombre = "Congo" },
                new Pais { CodigoIso = "CD", Nombre = "Congo (RDC)" },
                new Pais { CodigoIso = "KP", Nombre = "Corea del Norte" },
                new Pais { CodigoIso = "KR", Nombre = "Corea del Sur" },
                new Pais { CodigoIso = "CI", Nombre = "Costa de Marfil" },
                new Pais { CodigoIso = "CR", Nombre = "Costa Rica" },
                new Pais { CodigoIso = "HR", Nombre = "Croacia" },
                new Pais { CodigoIso = "CU", Nombre = "Cuba" },
                new Pais { CodigoIso = "DK", Nombre = "Dinamarca" },
                new Pais { CodigoIso = "DJ", Nombre = "Yibuti" },
                new Pais { CodigoIso = "DM", Nombre = "Dominica" },
                new Pais { CodigoIso = "EC", Nombre = "Ecuador" },
                new Pais { CodigoIso = "EG", Nombre = "Egipto" },
                new Pais { CodigoIso = "SV", Nombre = "El Salvador" },
                new Pais { CodigoIso = "AE", Nombre = "Emiratos Árabes Unidos" },
                new Pais { CodigoIso = "ER", Nombre = "Eritrea" },
                new Pais { CodigoIso = "SK", Nombre = "Eslovaquia" },
                new Pais { CodigoIso = "SI", Nombre = "Eslovenia" },
                new Pais { CodigoIso = "ES", Nombre = "España" },
                new Pais { CodigoIso = "US", Nombre = "Estados Unidos" },
                new Pais { CodigoIso = "EE", Nombre = "Estonia" },
                new Pais { CodigoIso = "ET", Nombre = "Etiopía" },
                new Pais { CodigoIso = "PH", Nombre = "Filipinas" },
                new Pais { CodigoIso = "FI", Nombre = "Finlandia" },
                new Pais { CodigoIso = "FJ", Nombre = "Fiyi" },
                new Pais { CodigoIso = "FR", Nombre = "Francia" },
                new Pais { CodigoIso = "GA", Nombre = "Gabón" },
                new Pais { CodigoIso = "GM", Nombre = "Gambia" },
                new Pais { CodigoIso = "GE", Nombre = "Georgia" },
                new Pais { CodigoIso = "GH", Nombre = "Ghana" },
                new Pais { CodigoIso = "GD", Nombre = "Granada" },
                new Pais { CodigoIso = "GR", Nombre = "Grecia" },
                new Pais { CodigoIso = "GT", Nombre = "Guatemala" },
                new Pais { CodigoIso = "GN", Nombre = "Guinea" },
                new Pais { CodigoIso = "GQ", Nombre = "Guinea Ecuatorial" },
                new Pais { CodigoIso = "GW", Nombre = "Guinea-Bisáu" },
                new Pais { CodigoIso = "GY", Nombre = "Guyana" },
                new Pais { CodigoIso = "HT", Nombre = "Haití" },
                new Pais { CodigoIso = "HN", Nombre = "Honduras" },
                new Pais { CodigoIso = "HU", Nombre = "Hungría" },
                new Pais { CodigoIso = "IN", Nombre = "India" },
                new Pais { CodigoIso = "ID", Nombre = "Indonesia" },
                new Pais { CodigoIso = "IQ", Nombre = "Irak" },
                new Pais { CodigoIso = "IR", Nombre = "Irán" },
                new Pais { CodigoIso = "IE", Nombre = "Irlanda" },
                new Pais { CodigoIso = "IS", Nombre = "Islandia" },
                new Pais { CodigoIso = "MH", Nombre = "Islas Marshall" },
                new Pais { CodigoIso = "SB", Nombre = "Islas Salomón" },
                new Pais { CodigoIso = "IL", Nombre = "Israel" },
                new Pais { CodigoIso = "IT", Nombre = "Italia" },
                new Pais { CodigoIso = "JM", Nombre = "Jamaica" },
                new Pais { CodigoIso = "JP", Nombre = "Japón" },
                new Pais { CodigoIso = "JO", Nombre = "Jordania" },
                new Pais { CodigoIso = "KZ", Nombre = "Kazajistán" },
                new Pais { CodigoIso = "KE", Nombre = "Kenia" },
                new Pais { CodigoIso = "KG", Nombre = "Kirguistán" },
                new Pais { CodigoIso = "KI", Nombre = "Kiribati" },
                new Pais { CodigoIso = "KW", Nombre = "Kuwait" },
                new Pais { CodigoIso = "LA", Nombre = "Laos" },
                new Pais { CodigoIso = "LS", Nombre = "Lesoto" },
                new Pais { CodigoIso = "LV", Nombre = "Letonia" },
                new Pais { CodigoIso = "LB", Nombre = "Líbano" },
                new Pais { CodigoIso = "LR", Nombre = "Liberia" },
                new Pais { CodigoIso = "LY", Nombre = "Libia" },
                new Pais { CodigoIso = "LI", Nombre = "Liechtenstein" },
                new Pais { CodigoIso = "LT", Nombre = "Lituania" },
                new Pais { CodigoIso = "LU", Nombre = "Luxemburgo" },
                new Pais { CodigoIso = "MK", Nombre = "Macedonia del Norte" },
                new Pais { CodigoIso = "MG", Nombre = "Madagascar" },
                new Pais { CodigoIso = "MY", Nombre = "Malasia" },
                new Pais { CodigoIso = "MW", Nombre = "Malaui" },
                new Pais { CodigoIso = "MV", Nombre = "Maldivas" },
                new Pais { CodigoIso = "ML", Nombre = "Malí" },
                new Pais { CodigoIso = "MT", Nombre = "Malta" },
                new Pais { CodigoIso = "MA", Nombre = "Marruecos" },
                new Pais { CodigoIso = "MU", Nombre = "Mauricio" },
                new Pais { CodigoIso = "MR", Nombre = "Mauritania" },
                new Pais { CodigoIso = "MX", Nombre = "México" },
                new Pais { CodigoIso = "FM", Nombre = "Micronesia" },
                new Pais { CodigoIso = "MD", Nombre = "Moldavia" },
                new Pais { CodigoIso = "MC", Nombre = "Mónaco" },
                new Pais { CodigoIso = "MN", Nombre = "Mongolia" },
                new Pais { CodigoIso = "ME", Nombre = "Montenegro" },
                new Pais { CodigoIso = "MZ", Nombre = "Mozambique" },
                new Pais { CodigoIso = "MM", Nombre = "Myanmar" },
                new Pais { CodigoIso = "NA", Nombre = "Namibia" },
                new Pais { CodigoIso = "NR", Nombre = "Nauru" },
                new Pais { CodigoIso = "NP", Nombre = "Nepal" },
                new Pais { CodigoIso = "NI", Nombre = "Nicaragua" },
                new Pais { CodigoIso = "NE", Nombre = "Níger" },
                new Pais { CodigoIso = "NG", Nombre = "Nigeria" },
                new Pais { CodigoIso = "NO", Nombre = "Noruega" },
                new Pais { CodigoIso = "NZ", Nombre = "Nueva Zelanda" },
                new Pais { CodigoIso = "OM", Nombre = "Omán" },
                new Pais { CodigoIso = "PK", Nombre = "Pakistán" },
                new Pais { CodigoIso = "PW", Nombre = "Palaos" },
                new Pais { CodigoIso = "PA", Nombre = "Panamá" },
                new Pais { CodigoIso = "PG", Nombre = "Papúa Nueva Guinea" },
                new Pais { CodigoIso = "PY", Nombre = "Paraguay" },
                new Pais { CodigoIso = "NL", Nombre = "Países Bajos" },
                new Pais { CodigoIso = "PE", Nombre = "Perú" },
                new Pais { CodigoIso = "PL", Nombre = "Polonia" },
                new Pais { CodigoIso = "PT", Nombre = "Portugal" },
                new Pais { CodigoIso = "GB", Nombre = "Reino Unido" },
                new Pais { CodigoIso = "CF", Nombre = "República Centroafricana" },
                new Pais { CodigoIso = "CZ", Nombre = "República Checa" },
                new Pais { CodigoIso = "DO", Nombre = "República Dominicana" },
                new Pais { CodigoIso = "RW", Nombre = "Ruanda" },
                new Pais { CodigoIso = "RO", Nombre = "Rumanía" },
                new Pais { CodigoIso = "RU", Nombre = "Rusia" },
                new Pais { CodigoIso = "WS", Nombre = "Samoa" },
                new Pais { CodigoIso = "KN", Nombre = "San Cristóbal y Nieves" },
                new Pais { CodigoIso = "SM", Nombre = "San Marino" },
                new Pais { CodigoIso = "VC", Nombre = "San Vicente y las Granadinas" },
                new Pais { CodigoIso = "LC", Nombre = "Santa Lucía" },
                new Pais { CodigoIso = "ST", Nombre = "Santo Tomé y Príncipe" },
                new Pais { CodigoIso = "SN", Nombre = "Senegal" },
                new Pais { CodigoIso = "RS", Nombre = "Serbia" },
                new Pais { CodigoIso = "SC", Nombre = "Seychelles" },
                new Pais { CodigoIso = "SL", Nombre = "Sierra Leona" },
                new Pais { CodigoIso = "SG", Nombre = "Singapur" },
                new Pais { CodigoIso = "SY", Nombre = "Siria" },
                new Pais { CodigoIso = "SO", Nombre = "Somalia" },
                new Pais { CodigoIso = "LK", Nombre = "Sri Lanka" },
                new Pais { CodigoIso = "SZ", Nombre = "Suazilandia" },
                new Pais { CodigoIso = "ZA", Nombre = "Sudáfrica" },
                new Pais { CodigoIso = "SS", Nombre = "Sudán del Sur" },
                new Pais { CodigoIso = "SD", Nombre = "Sudán" },
                new Pais { CodigoIso = "SE", Nombre = "Suecia" },
                new Pais { CodigoIso = "CH", Nombre = "Suiza" },
                new Pais { CodigoIso = "SR", Nombre = "Surinam" },
                new Pais { CodigoIso = "TH", Nombre = "Tailandia" },
                new Pais { CodigoIso = "TZ", Nombre = "Tanzania" },
                new Pais { CodigoIso = "TJ", Nombre = "Tayikistán" },
                new Pais { CodigoIso = "TL", Nombre = "Timor Oriental" },
                new Pais { CodigoIso = "TG", Nombre = "Togo" },
                new Pais { CodigoIso = "TO", Nombre = "Tonga" },
                new Pais { CodigoIso = "TT", Nombre = "Trinidad y Tobago" },
                new Pais { CodigoIso = "TN", Nombre = "Túnez" },
                new Pais { CodigoIso = "TM", Nombre = "Turkmenistán" },
                new Pais { CodigoIso = "TR", Nombre = "Turquía" },
                new Pais { CodigoIso = "TV", Nombre = "Tuvalu" },
                new Pais { CodigoIso = "UA", Nombre = "Ucrania" },
                new Pais { CodigoIso = "UG", Nombre = "Uganda" },
                new Pais { CodigoIso = "UY", Nombre = "Uruguay" },
                new Pais { CodigoIso = "UZ", Nombre = "Uzbekistán" },
                new Pais { CodigoIso = "VU", Nombre = "Vanuatu" },
                new Pais { CodigoIso = "VE", Nombre = "Venezuela" },
                new Pais { CodigoIso = "VN", Nombre = "Vietnam" },
                new Pais { CodigoIso = "YE", Nombre = "Yemen" },
                new Pais { CodigoIso = "ZM", Nombre = "Zambia" },
                new Pais { CodigoIso = "ZW", Nombre = "Zimbabue" }
            );

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

            modelBuilder.Entity<TipoCambio>(entity =>
            {
                entity.HasKey(e => e.IdTipoCambio);
                entity.HasIndex(e => new { e.IdEmpresa, e.MonedaOrigen, e.MonedaDestino, e.Fecha }).IsUnique();

                entity.HasOne(e => e.Empresa)
                      .WithMany()
                      .HasForeignKey(e => e.IdEmpresa)
                      .OnDelete(DeleteBehavior.Restrict);
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