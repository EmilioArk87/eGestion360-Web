using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Contabilidad;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Facturacion;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Data
{
    public class ApplicationDbContext : DbContext
    {
        /// <summary>Nombre del filtro global por tenant (EF 10 admite varios filtros con nombre por entidad).</summary>
        public const string FiltroTenant = "tenant";

        /// <summary>Nombre del filtro global de borrado lógico: las filas con <c>eliminado = 1</c> no aparecen en las consultas.</summary>
        public const string FiltroBorrado = "borrado";

        private readonly ITenantContext? _tenant;

        /// <param name="options">Opciones de EF.</param>
        /// <param name="tenant">
        /// Tenant de la petición. La aplicación siempre lo recibe por inyección de dependencias y entonces todas las
        /// entidades de tenant llevan el filtro <see cref="FiltroTenant"/> (F1.3). Sin él (un contexto armado a mano, como en
        /// algunas pruebas) el filtro no se aplica.
        /// </param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenant = null)
            : base(options)
        {
            _tenant = tenant;
        }

        /// <summary>Verdadero si las consultas de este contexto se limitan al tenant de la petición.</summary>
        public bool FiltraPorTenant => _tenant != null && !_tenant.EsPlataforma;

        /// <summary>Tenant al que se limitan las consultas; 0 (ningún tenant) cuando la petición no trae uno: así no ve nada.</summary>
        public int IdTenantFiltro => _tenant?.IdTenant ?? 0;

        public DbSet<User> Users { get; set; }
        public DbSet<PasswordResetCode> PasswordResetCodes { get; set; }
        public DbSet<EmailConfiguration> EmailConfigurations { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
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
        public DbSet<TasaCambioConfiguracion> TasasCambioConfiguracion { get; set; }

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

            // La configuración de cada entidad vive en Data/Configuracion, un archivo por módulo (paso F0.8 del plan de
            // arquitectura). Una entidad nueva se configura con su clase IEntityTypeConfiguration en el archivo de su módulo.
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly,
                tipo => tipo.Namespace == typeof(ApplicationDbContext).Namespace + ".Configuracion");

            // Filtro global por tenant (paso F1.3): primera barrera del aislamiento; la segunda será RLS (F1.4). Las tablas
            // mixtas (ADR-015) dejan ver además las filas globales, las de id_tenant nulo. En ámbito de plataforma
            // (ITenantContext.EsPlataforma) el filtro no se aplica; una petición sin tenant no ve ninguna fila de tenant.
            foreach (var entidad in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType == null && !e.IsOwned()).ToList())
            {
                if (typeof(ITenantEntity).IsAssignableFrom(entidad.ClrType))
                    MetodoFiltroDeTenant.MakeGenericMethod(entidad.ClrType).Invoke(this, new object[] { modelBuilder });
                else if (typeof(ITenantOpcionalEntity).IsAssignableFrom(entidad.ClrType))
                    MetodoFiltroDeTenantMixto.MakeGenericMethod(entidad.ClrType).Invoke(this, new object[] { modelBuilder });
            }

            // Filtro global de borrado lógico (F1.3c, ADR-022): aparte del de tenant, para poder ignorar uno sin el otro. Solo en las
            // entidades con Eliminado que NINGUNA otra referencia (hojas: cargas, peajes, documentos...). Una entidad que otras
            // referencian (vehículo, persona, cliente...) no lo lleva: con él, el historial que apunta a una fila borrada
            // desaparecería de las consultas que la incluyen. Esas siguen filtrando con !Eliminado donde corresponde.
            foreach (var entidad in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType == null && !e.IsOwned()).ToList())
            {
                var eliminado = entidad.FindProperty("Eliminado");
                if (eliminado != null && eliminado.ClrType == typeof(bool) && !entidad.GetReferencingForeignKeys().Any())
                    MetodoFiltroDeBorrado.MakeGenericMethod(entidad.ClrType).Invoke(this, new object[] { modelBuilder });
            }
        }

        private static readonly System.Reflection.MethodInfo MetodoFiltroDeTenant =
            typeof(ApplicationDbContext).GetMethod(nameof(AplicarFiltroDeTenant), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        private static readonly System.Reflection.MethodInfo MetodoFiltroDeTenantMixto =
            typeof(ApplicationDbContext).GetMethod(nameof(AplicarFiltroDeTenantMixto), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        private void AplicarFiltroDeTenant<T>(ModelBuilder modelBuilder) where T : class, ITenantEntity
            => modelBuilder.Entity<T>().HasQueryFilter(FiltroTenant, e => !FiltraPorTenant || e.IdTenant == IdTenantFiltro);

        private static readonly System.Reflection.MethodInfo MetodoFiltroDeBorrado =
            typeof(ApplicationDbContext).GetMethod(nameof(AplicarFiltroDeBorrado), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        private void AplicarFiltroDeBorrado<T>(ModelBuilder modelBuilder) where T : class
            => modelBuilder.Entity<T>().HasQueryFilter(FiltroBorrado, e => !EF.Property<bool>(e, "Eliminado"));

        private void AplicarFiltroDeTenantMixto<T>(ModelBuilder modelBuilder) where T : class, ITenantOpcionalEntity
            => modelBuilder.Entity<T>().HasQueryFilter(FiltroTenant, e => !FiltraPorTenant || e.IdTenant == null || e.IdTenant == IdTenantFiltro);
    }
}