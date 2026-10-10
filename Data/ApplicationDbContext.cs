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

            // La configuración de cada entidad vive en Data/Configuracion, un archivo por módulo (paso F0.8 del plan de
            // arquitectura). Una entidad nueva se configura con su clase IEntityTypeConfiguration en el archivo de su módulo.
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly,
                tipo => tipo.Namespace == typeof(ApplicationDbContext).Namespace + ".Configuracion");
        }
    }
}