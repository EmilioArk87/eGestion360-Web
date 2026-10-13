using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>
    /// El mismo modelo de EF que usa la aplicación, adaptado a SQLite en memoria para poder probar sin
    /// tocar la base real. Diferencias con SQL Server:
    ///   * SQLite no genera rowversion: se desactiva token_concurrencia.
    ///   * persona_documentos.numero_normalizado es una columna calculada y persistida en SQL Server; aquí
    ///     se emula con una columna generada de SQLite, con la misma regla.
    /// Lo que SQLite no comprueba (índices únicos filtrados, claves compuestas, disparadores) lo cubren los
    /// scripts SQL y su POSTCHECK, no estas pruebas.
    /// </summary>
    public sealed class ContextoDePrueba : ApplicationDbContext
    {
        public ContextoDePrueba(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var tipo in modelBuilder.Model.GetEntityTypes())
            {
                var token = tipo.FindProperty("TokenConcurrencia");
                if (token == null) continue;
                token.IsConcurrencyToken = false;
                token.ValueGenerated = ValueGenerated.Never;
            }

            modelBuilder.Entity<PersonaDocumento>()
                .Property(d => d.NumeroNormalizado)
                .HasComputedColumnSql("upper(replace(replace(replace(numero, '-', ''), ' ', ''), '.', ''))", stored: true);
        }
    }

    /// <summary>
    /// Una base de datos SQLite en memoria con los catálogos y empresas de <see cref="DatosBase"/>.
    /// Cada <see cref="Crear"/> devuelve un contexto nuevo sobre la misma base, como si fuera otra petición.
    ///
    /// Todo contexto lleva el <see cref="TenantAlGuardarInterceptor"/> real, igual que la aplicación (F1.2): lo que una
    /// prueba inserta sin tenant lo recibe de su empresa, de su padre o de <see cref="Tenant"/>, y si no tiene de dónde,
    /// la prueba falla como fallaría la aplicación. <see cref="Tenant"/> es el tenant de la «petición» de las pruebas:
    /// por defecto el de la empresa A (<see cref="DatosBase.TenantA"/>); una prueba lo cambia o lo deja nulo para
    /// representar a otro tenant o al administrador general sin empresa.
    /// </summary>
    public sealed class BaseDeDatosDePrueba : IDisposable
    {
        private readonly SqliteConnection _conexion;
        private readonly ServiceProvider _servicios;

        /// <summary>El tenant de la petición simulada. Cámbialo antes de <see cref="Crear"/> si la prueba lo necesita.</summary>
        public TenantContextFijo Tenant { get; } = new() { IdTenant = DatosBase.TenantA, IdEmpresa = DatosBase.EmpresaA };

        /// <summary>El resolvedor real de la aplicación, con su propia caché y sus propios contextos sobre esta base.</summary>
        public IResolvedorDeTenant Resolvedor { get; }

        public BaseDeDatosDePrueba()
        {
            _conexion = new SqliteConnection("DataSource=:memory:");
            _conexion.Open();

            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => new ContextoDePrueba(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conexion).Options));
            _servicios = servicios.BuildServiceProvider();
            Resolvedor = new ResolvedorDeTenantEf(
                _servicios.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()));

            using var db = Crear();
            db.Database.EnsureCreated();
            DatosBase.Sembrar(db);
        }

        /// <summary>Un resolvedor nuevo, con caché vacía, sobre esta misma base.</summary>
        public IResolvedorDeTenant NuevoResolvedor(IMemoryCache? cache = null) => new ResolvedorDeTenantEf(
            _servicios.GetRequiredService<IServiceScopeFactory>(), cache ?? new MemoryCache(new MemoryCacheOptions()));

        public ApplicationDbContext Crear(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptores)
        {
            var constructor = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conexion);

            // Igual que en Program.cs: primero los que se pasan (la auditoría) y después el del tenant.
            var todos = interceptores.Append(new TenantAlGuardarInterceptor(Tenant, Resolvedor)).ToArray();
            constructor.AddInterceptors(todos);
            return new ContextoDePrueba(constructor.Options);
        }

        public void Dispose()
        {
            _servicios.Dispose();
            _conexion.Dispose();
        }
    }
}
