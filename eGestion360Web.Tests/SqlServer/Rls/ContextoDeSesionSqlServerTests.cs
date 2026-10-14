using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Eventos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Arquitectura;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.Infra.FilasDePrueba;
using static eGestion360Web.Tests.SqlServer.Rls.EntornoRlsReal;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>Colección de las pruebas con la política encendida: comparten una base y corren una tras otra.</summary>
    [CollectionDefinition(Nombre)]
    public sealed class ColeccionRlsReal : ICollectionFixture<BaseRlsReal>
    {
        public const string Nombre = "RlsReal";
    }

    /// <summary>Una sola base con la política encendida para todas las pruebas de <see cref="ContextoDeSesionSqlServerTests"/>.</summary>
    public sealed class BaseRlsReal : IAsyncLifetime
    {
        public Entorno? Entorno { get; private set; }

        public async Task InitializeAsync()
        {
            if (ServidorSqlDePrueba.MotivoParaOmitir != null) return;
            Entorno = await PrepararAsync();

            // La plataforma: un tenant interno y un tenant cliente (las filas de las demás tablas llevan los ids 1 y 2).
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena).Options;
            await using var db = new ApplicationDbContext(opciones);
            var ahora = DateTime.UtcNow;
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "T_A", Nombre = "Tenant A", Tipo = TipoTenant.Interno, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            await db.SaveChangesAsync();
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "T_B", Nombre = "Tenant B", Tipo = TipoTenant.Cliente, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            await db.SaveChangesAsync();
        }

        public async Task DisposeAsync()
        {
            if (Entorno != null) await ServidorSqlDePrueba.BorrarBaseAsync(Entorno.Base);
        }
    }

    /// <summary>
    /// Paso F1.4b contra SQL Server (LocalDB) con la política RLS ENCENDIDA (scripts 036 y 037): el interceptor de conexión fija el
    /// tenant de <see cref="ITenantContext"/> en cada apertura, así que la base entrega a cada contexto de EF solo las filas de su
    /// tenant aunque el filtro de EF esté apagado; el contexto no puede quedar viejo; y el resolvedor, el guardado y el despachador
    /// del outbox siguen funcionando con la política encendida.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    [Collection(ColeccionRlsReal.Nombre)]
    public class ContextoDeSesionSqlServerTests
    {
        private static readonly string[] SinFiltrosDeEf = { ApplicationDbContext.FiltroTenant, ApplicationDbContext.FiltroBorrado };

        private readonly BaseRlsReal _bd;

        public ContextoDeSesionSqlServerTests(BaseRlsReal bd) => _bd = bd;

        private Entorno Entorno => _bd.Entorno ?? throw new InvalidOperationException("Sin servidor de pruebas.");

        /// <summary>Un contexto de EF con el interceptor de sesión, sin servicios.</summary>
        private ApplicationDbContext Contexto(ITenantContext tenant, string? cadena = null)
        {
            var constructor = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena ?? Entorno.Cadena);
            constructor.AddInterceptors(new ContextoDeSesionInterceptor(tenant));
            return new ApplicationDbContext(constructor.Options, tenant);
        }

        private static TenantContextFijo DeTenant(int idTenant) => new() { IdTenant = idTenant };

        /// <summary>Las entidades de tenant de la base real que el modelo de EF mapea, menos las que no llevan filtro de lectura.</summary>
        private static List<(Type Tipo, string Tabla, bool Mixta)> EntidadesConPolitica(ApplicationDbContext db)
        {
            var tablas = ClasificacionDeTablas.Todas
                .Where(t => t.EnBaseReal && t.EnModeloEf && ClasificacionDeTablas.LlevaIdTenant(t.Categoria) && t.Tabla is not ("empresas" or "domain_events"))
                .ToDictionary(t => t.Tabla, t => t.Categoria == CategoriaTabla.Mixta);
            return db.Model.GetEntityTypes()
                .Where(e => !e.IsOwned() && e.BaseType == null && e.GetTableName() != null && tablas.ContainsKey(e.GetTableName()!))
                .Select(e => (e.ClrType, e.GetTableName()!, tablas[e.GetTableName()!]))
                .ToList();
        }

        [FactSqlServer]
        public async Task Cada_contexto_de_EF_ve_solo_las_filas_de_su_tenant_aunque_el_filtro_de_EF_este_apagado()
        {
            var problemas = new List<string>();
            foreach (var tenant in new[] { DatosBase.TenantA, DatosBase.TenantB })
            {
                await using var db = Contexto(DeTenant(tenant));
                var entidades = EntidadesConPolitica(db);
                Assert.True(entidades.Count >= 30, $"Solo {entidades.Count} entidades con política.");

                foreach (var (tipo, tabla, mixta) in entidades)
                {
                    var filas = Filas(db, tipo, SinFiltrosDeEf);
                    var ajenas = filas.Count(f => TenantDe(f) is { } t && t != tenant);
                    if (ajenas != 0) problemas.Add($"{tabla}: el tenant {tenant} ve {ajenas} fila(s) de otro tenant");
                    if (!filas.Any(f => TenantDe(f) == tenant)) problemas.Add($"{tabla}: el tenant {tenant} no ve las suyas");
                    if (mixta && !filas.Any(f => TenantDe(f) == null)) problemas.Add($"{tabla}: el tenant {tenant} no ve las filas globales");
                }
            }

            Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
        }

        [FactSqlServer]
        public async Task Sin_tenant_o_en_ambito_de_plataforma_no_se_ve_ninguna_fila_de_tenant_y_si_las_globales()
        {
            var problemas = new List<string>();
            var contextos = new (string Nombre, TenantContextFijo Tenant)[]
            {
                ("sin tenant", new TenantContextFijo()),
                ("plataforma", new TenantContextFijo { EsPlataforma = true })
            };

            foreach (var (nombre, tenant) in contextos)
            {
                await using var db = Contexto(tenant);
                foreach (var (tipo, tabla, mixta) in EntidadesConPolitica(db))
                {
                    var filas = Filas(db, tipo, SinFiltrosDeEf);
                    var deTenant = filas.Count(f => TenantDe(f) != null);
                    if (deTenant != 0) problemas.Add($"{tabla}: {nombre} ve {deTenant} fila(s) de tenant");
                    if (mixta && filas.Count == 0) problemas.Add($"{tabla}: {nombre} no ve las filas globales");
                }
            }

            Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
        }

        [FactSqlServer]
        public async Task Si_el_tenant_cambia_con_la_conexion_abierta_el_siguiente_comando_falla_y_se_arregla_cerrandola()
        {
            var tenant = DeTenant(DatosBase.TenantA);
            await using var db = Contexto(tenant);
            await db.Database.OpenConnectionAsync();
            Assert.True(await db.Cargos.IgnoreQueryFilters().AnyAsync());

            tenant.IdTenant = DatosBase.TenantB;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Cargos.IgnoreQueryFilters().CountAsync());
            Assert.Contains("El tenant cambió con la conexión abierta", error.Message);

            await db.Database.CloseConnectionAsync();
            var cargos = await db.Cargos.IgnoreQueryFilters().Select(c => c.IdTenant).Distinct().ToListAsync();
            Assert.Equal(new[] { DatosBase.TenantB }, cargos);
        }

        [FactSqlServer]
        public async Task Una_conexion_abierta_sin_pasar_por_EF_se_rechaza_en_lugar_de_correr_sin_contexto()
        {
            await using var db = Contexto(DeTenant(DatosBase.TenantA));
            await db.Database.GetDbConnection().OpenAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Cargos.IgnoreQueryFilters().CountAsync());
            Assert.Contains("sin pasar por EF", error.Message);
        }

        [FactSqlServer]
        public async Task El_pool_de_conexiones_no_arrastra_el_tenant_anterior()
        {
            // Pool propio de una sola conexión: cada contexto reutiliza la misma sesión física.
            var cadena = ServidorSqlDePrueba.Cadena(Entorno.Base, b =>
            {
                b.Pooling = true;
                b.MaxPoolSize = 1;
                b.ApplicationName = $"f14b-pool-{Guid.NewGuid():N}";
            });

            foreach (var tenant in new[] { DatosBase.TenantA, DatosBase.TenantB, DatosBase.TenantA })
            {
                await using var db = Contexto(DeTenant(tenant), cadena);
                var vistos = await db.Cargos.IgnoreQueryFilters().Select(c => c.IdTenant).Distinct().ToListAsync();
                Assert.Equal(new[] { tenant }, vistos);
            }

            await using (var sinTenant = Contexto(new TenantContextFijo(), cadena))
                Assert.Empty(await sinTenant.Cargos.IgnoreQueryFilters().ToListAsync());

            SqlConnection.ClearAllPools();
        }
    }
}
