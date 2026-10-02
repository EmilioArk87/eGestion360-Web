using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;

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
    /// </summary>
    public sealed class BaseDeDatosDePrueba : IDisposable
    {
        private readonly SqliteConnection _conexion;

        public BaseDeDatosDePrueba()
        {
            _conexion = new SqliteConnection("DataSource=:memory:");
            _conexion.Open();

            using var db = Crear();
            db.Database.EnsureCreated();
            DatosBase.Sembrar(db);
        }

        public ApplicationDbContext Crear(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptores)
        {
            var constructor = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conexion);
            if (interceptores.Length > 0) constructor.AddInterceptors(interceptores);
            return new ContextoDePrueba(constructor.Options);
        }

        public void Dispose() => _conexion.Dispose();
    }
}
