using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Eventos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.Infra.FilasDePrueba;
using static eGestion360Web.Tests.SqlServer.Rls.EntornoRlsReal;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Paso F1.4b: la aplicación armada como en Program.cs (contexto de tenant por ámbito, interceptor de sesión, interceptor del
    /// tenant al guardar, resolvedor) sobre una base con la política RLS ENCENDIDA. Comprueba que lo que la aplicación hace de
    /// verdad (guardar, resolver el tenant, correr un proceso de fondo) sigue funcionando y que la base rechaza lo demás.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    [Collection(ColeccionRlsReal.Nombre)]
    public class AplicacionConRlsSqlServerTests
    {
        private const int ErrorBloqueoRls = 33504;

        private readonly BaseRlsReal _bd;

        public AplicacionConRlsSqlServerTests(BaseRlsReal bd) => _bd = bd;

        private Entorno Entorno => _bd.Entorno ?? throw new InvalidOperationException("Sin servidor de pruebas.");

        /// <summary>Los servicios de la aplicación que importan aquí, registrados como en Program.cs.</summary>
        private ServiceProvider Servicios(Action<IServiceCollection>? mas = null)
        {
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddHttpContextAccessor();
            servicios.AddMemoryCache();
            servicios.AddScoped<TenantContext>();
            servicios.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            servicios.AddScoped<IResolvedorDeTenant, ResolvedorDeTenantEf>();
            servicios.AddScoped<TenantAlGuardarInterceptor>();
            servicios.AddScoped<ContextoDeSesionInterceptor>();
            servicios.AddScoped<ApplicationDbContext>(sp => new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena)
                    .AddInterceptors(sp.GetRequiredService<TenantAlGuardarInterceptor>(), sp.GetRequiredService<ContextoDeSesionInterceptor>()).Options,
                sp.GetRequiredService<ITenantContext>()));
            mas?.Invoke(servicios);
            return servicios.BuildServiceProvider();
        }

        private static ApplicationDbContext Contexto(IServiceScope ambito, int? idTenant, int? idEmpresa = null, bool plataforma = false)
        {
            var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
            if (plataforma) tenant.FijarPlataforma(); else tenant.Fijar(idTenant, idEmpresa);
            return ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        private async Task<int> ContarPorSql(int? tenant, string sql) => Convert.ToInt32(await ConContexto(Entorno.Cadena, tenant, "tenant", sql));

        [FactSqlServer]
        public async Task Guardar_en_una_peticion_de_un_tenant_asigna_su_tenant_y_la_base_lo_acepta()
        {
            await using var servicios = Servicios();
            await using (var ambito = servicios.CreateAsyncScope())
            {
                var db = Contexto(ambito, DatosBase.TenantA, DatosBase.EmpresaA);
                db.Cargos.Add(new Cargo { IdEmpresa = DatosBase.EmpresaA, Codigo = "F14B", Nombre = "Cargo de la prueba F1.4b", CreadoPor = "pruebas" });
                await db.SaveChangesAsync();
            }

            Assert.Equal(1, await ContarPorSql(DatosBase.TenantA, "SELECT COUNT(*) FROM dbo.cargos WHERE codigo = 'F14B'"));
            Assert.Equal(0, await ContarPorSql(DatosBase.TenantB, "SELECT COUNT(*) FROM dbo.cargos WHERE codigo = 'F14B'"));
        }

        [FactSqlServer]
        public async Task Sin_el_interceptor_de_la_aplicacion_la_base_rechaza_guardar_una_fila_de_otro_tenant()
        {
            var tenant = new TenantContextFijo { IdTenant = DatosBase.TenantA };
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena)
                .AddInterceptors(new ContextoDeSesionInterceptor(tenant)).Options;
            await using var db = new ApplicationDbContext(opciones, tenant);

            var ajena = Fabricar(db.Model.FindEntityType(typeof(Cargo))!, DatosBase.TenantB, DatosBase.EmpresaB, "AJENA", 77);
            db.Add(ajena);

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(ErrorBloqueoRls, Assert.IsType<SqlException>(error.GetBaseException()).Number);
        }

        [FactSqlServer]
        public async Task La_tasa_oficial_global_solo_se_escribe_en_ambito_de_plataforma()
        {
            await using var servicios = Servicios();

            await using (var ambito = servicios.CreateAsyncScope())
            {
                var db = Contexto(ambito, idTenant: null, plataforma: true);
                var tasa = Fabricar(db.Model.FindEntityType(typeof(TasaCambio))!, DatosBase.TenantA, DatosBase.EmpresaA, "O", 91);
                ((ITenantOpcionalEntity)tasa).IdTenant = null;
                tasa.GetType().GetProperty("IdEmpresa")?.SetValue(tasa, null);
                db.Add(tasa);
                await db.SaveChangesAsync();   // sin tenant, en ámbito de plataforma: una fila global
            }

            await using (var ambito = servicios.CreateAsyncScope())
            {
                var db = Contexto(ambito, DatosBase.TenantA, DatosBase.EmpresaA);
                var tasa = Fabricar(db.Model.FindEntityType(typeof(TasaCambio))!, DatosBase.TenantA, DatosBase.EmpresaA, "P", 92);
                ((ITenantOpcionalEntity)tasa).IdTenant = null;
                tasa.GetType().GetProperty("IdEmpresa")?.SetValue(tasa, null);
                db.Add(tasa);

                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());   // un tenant no escribe filas globales
                Assert.Equal(ErrorBloqueoRls, Assert.IsType<SqlException>(error.GetBaseException()).Number);
            }
        }

        [FactSqlServer]
        public async Task La_plataforma_no_ve_datos_de_tenant_por_la_conexion_transaccional_pero_si_el_directorio_de_empresas()
        {
            await using var servicios = Servicios();
            await using var ambito = servicios.CreateAsyncScope();
            var db = Contexto(ambito, idTenant: null, plataforma: true);

            Assert.Empty(await db.Cargos.ToListAsync());      // el filtro de EF está apagado: es la base la que no entrega nada
            Assert.Empty(await db.Personas.ToListAsync());
            Assert.True(await db.Empresas.CountAsync() >= 2);  // ADR-023, D35: el directorio de cuentas es de lectura abierta
        }

        [FactSqlServer]
        public async Task El_resolvedor_resuelve_empresas_sin_contexto_y_personas_solo_desde_su_tenant()
        {
            await using var servicios = Servicios();

            var personaA = Convert.ToInt32(await ConContexto(Entorno.Cadena, DatosBase.TenantA, "tenant", "SELECT MIN(id_persona) FROM dbo.personas"));
            var personaB = Convert.ToInt32(await ConContexto(Entorno.Cadena, DatosBase.TenantB, "tenant", "SELECT MIN(id_persona) FROM dbo.personas"));

            await using (var ambito = servicios.CreateAsyncScope())   // sin tenant: el directorio de empresas es abierto
            {
                var resolvedor = ambito.ServiceProvider.GetRequiredService<IResolvedorDeTenant>();
                Assert.Equal(DatosBase.TenantA, await resolvedor.ResolverPorEmpresaAsync(1));
                Assert.Equal(DatosBase.TenantB, await resolvedor.ResolverPorEmpresaAsync(2));
                Assert.Equal(DatosBase.TenantA, await resolvedor.ResolverInternoAsync());
                Assert.Null(await resolvedor.ResolverPorPersonaAsync(personaA));   // una persona no se ve sin tenant
            }

            await using (var ambito = servicios.CreateAsyncScope())   // con el tenant A: la persona del A, no la del B
            {
                Contexto(ambito, DatosBase.TenantA, DatosBase.EmpresaA);
                var resolvedor = ambito.ServiceProvider.GetRequiredService<IResolvedorDeTenant>();
                Assert.Equal(DatosBase.TenantA, await resolvedor.ResolverPorPersonaAsync(personaA));
                Assert.Null(await resolvedor.ResolverPorPersonaAsync(personaB));
            }
        }

        [FactSqlServer]
        public async Task El_despachador_del_outbox_corre_cada_evento_con_el_contexto_de_su_tenant_y_la_base_solo_le_entrega_lo_suyo()
        {
            ManejadorQueMideConRls.Vistos.Clear();
            await using var servicios = Servicios(s => s.AddScoped<IDomainEventHandler, ManejadorQueMideConRls>());

            long idA, idB;
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena).Options;
            await using (var db = new ApplicationDbContext(opciones))
            {
                var ahora = DateTime.UtcNow;
                var a = NuevoEvento(DatosBase.EmpresaA, DatosBase.TenantA, ahora);
                var b = NuevoEvento(DatosBase.EmpresaB, DatosBase.TenantB, ahora);
                db.AddRange(a, b);
                await db.SaveChangesAsync();
                (idA, idB) = (a.IdEvento, b.IdEvento);
            }

            var despachador = new OutboxDispatcherBackgroundService(
                servicios.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcherBackgroundService>.Instance);
            await despachador.StartAsync(CancellationToken.None);
            try
            {
                var limite = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < limite)
                {
                    await using var lectura = new ApplicationDbContext(opciones);
                    if (await lectura.Set<DomainEvent>().CountAsync(e => e.Status == DomainEventStatus.Processed && (e.IdEvento == idA || e.IdEvento == idB)) == 2) break;
                    await Task.Delay(500);
                }
            }
            finally { await despachador.StopAsync(CancellationToken.None); }

            await using var final = new ApplicationDbContext(opciones);
            Assert.Equal(2, await final.Set<DomainEvent>().CountAsync(e => e.Status == DomainEventStatus.Processed && (e.IdEvento == idA || e.IdEvento == idB)));

            lock (ManejadorQueMideConRls.Vistos)
            {
                var deA = ManejadorQueMideConRls.Vistos.Single(v => v.Evento == idA);
                var deB = ManejadorQueMideConRls.Vistos.Single(v => v.Evento == idB);
                Assert.Equal(new[] { DatosBase.TenantA }, deA.TenantsVistos);   // solo con la base: el filtro de EF está apagado
                Assert.Equal(new[] { DatosBase.TenantB }, deB.TenantsVistos);
            }
        }

        private static DomainEvent NuevoEvento(int idEmpresa, int idTenant, DateTime ahora) => new()
        {
            IdEmpresa = idEmpresa, IdTenant = idTenant, EventType = "prueba.rls", AggregateType = "Prueba", AggregateId = "1",
            OccurredAt = ahora, CreadoPor = "pruebas", FechaCreacion = ahora
        };

        /// <summary>Anota los tenants de los cargos que ve con el filtro de EF apagado: lo que entrega la base.</summary>
        private sealed class ManejadorQueMideConRls : IDomainEventHandler
        {
            private readonly ApplicationDbContext _db;
            public static readonly List<(long Evento, int[] TenantsVistos)> Vistos = new();

            public ManejadorQueMideConRls(ApplicationDbContext db) => _db = db;
            public string Name => "mide-rls";
            public bool CanHandle(string eventType) => eventType == "prueba.rls";

            public async Task HandleAsync(DomainEventDispatch evt, CancellationToken ct)
            {
                var tenants = await _db.Cargos.IgnoreQueryFilters().AsNoTracking().Select(c => c.IdTenant).Distinct().ToArrayAsync(ct);
                lock (Vistos) Vistos.Add((evt.IdEvento, tenants));
            }
        }
    }
}
