using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Eventos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.SqlServer.Tenancy
{
    /// <summary>
    /// Paso F1.3 contra SQL Server (LocalDB): el despachador del outbox recorre los eventos de todos los tenants (D20) y, para
    /// cada uno, los manejadores corren con el tenant de su empresa: con el filtro global de EF solo ven lo de ese tenant.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    public class OutboxTenantSqlServerTests
    {
        /// <summary>Anota con qué tenant corrió y cuántos cargos veía a través del filtro.</summary>
        private sealed class ManejadorQueMide : IDomainEventHandler
        {
            private readonly ITenantContext _tenant;
            private readonly ApplicationDbContext _db;
            public static readonly List<(long Evento, int? Tenant, bool Plataforma, int Cargos, int TenantsVistos)> Vistos = new();

            public ManejadorQueMide(ITenantContext tenant, ApplicationDbContext db) { _tenant = tenant; _db = db; }
            public string Name => "mide";
            public bool CanHandle(string eventType) => eventType == "prueba.tenant";

            public async Task HandleAsync(DomainEventDispatch evt, CancellationToken ct)
            {
                var cargos = await _db.Cargos.AsNoTracking().Select(c => c.IdTenant).ToListAsync(ct);
                lock (Vistos) Vistos.Add((evt.IdEvento, _tenant.IdTenant, _tenant.EsPlataforma, cargos.Count, cargos.Distinct().Count()));
            }
        }

        [FactSqlServer]
        public async Task Cada_evento_corre_con_el_tenant_de_su_empresa_y_su_estado_se_guarda()
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync("outbox_tenant");
            try
            {
                var cadena = ServidorSqlDePrueba.Cadena(nombre);
                var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena).Options;

                var servicios = new ServiceCollection();
                servicios.AddLogging();
                servicios.AddHttpContextAccessor();
                servicios.AddScoped<TenantContext>();
                servicios.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
                servicios.AddScoped<IResolvedorDeTenant>(sp => new ResolvedorDeTenantEf(
                    sp.GetRequiredService<IServiceScopeFactory>(), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
                servicios.AddScoped<TenantAlGuardarInterceptor>();
                servicios.AddScoped<ApplicationDbContext>(sp => new ApplicationDbContext(
                    new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena)
                        .AddInterceptors(sp.GetRequiredService<TenantAlGuardarInterceptor>()).Options,
                    sp.GetRequiredService<ITenantContext>()));
                servicios.AddScoped<IDomainEventHandler, ManejadorQueMide>();
                await using var proveedor = servicios.BuildServiceProvider();

                long idA, idB;
                await using (var db = new ApplicationDbContext(opciones))
                {
                    await db.Database.EnsureCreatedAsync();
                    Sembrar(db);
                    var ahora = DateTime.UtcNow;
                    var a = NuevoEvento(DatosBase.EmpresaA, DatosBase.TenantA, ahora);
                    var b = NuevoEvento(DatosBase.EmpresaB, DatosBase.TenantB, ahora);
                    db.AddRange(a, b);
                    await db.SaveChangesAsync();
                    (idA, idB) = (a.IdEvento, b.IdEvento);
                }

                var despachador = new OutboxDispatcherBackgroundService(
                    proveedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcherBackgroundService>.Instance);
                await despachador.StartAsync(CancellationToken.None);
                try
                {
                    var limite = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < limite)
                    {
                        await using var lectura = new ApplicationDbContext(opciones);
                        if (await lectura.Set<DomainEvent>().CountAsync(e => e.Status == DomainEventStatus.Processed) == 2) break;
                        await Task.Delay(500);
                    }
                }
                finally { await despachador.StopAsync(CancellationToken.None); }

                await using var final = new ApplicationDbContext(opciones);
                Assert.Equal(2, await final.Set<DomainEvent>().CountAsync(e => e.Status == DomainEventStatus.Processed));

                (long Evento, int? Tenant, bool Plataforma, int Cargos, int TenantsVistos) deA, deB;
                lock (ManejadorQueMide.Vistos)
                {
                    deA = ManejadorQueMide.Vistos.Single(v => v.Evento == idA);
                    deB = ManejadorQueMide.Vistos.Single(v => v.Evento == idB);
                }
                Assert.Equal(DatosBase.TenantA, deA.Tenant);
                Assert.Equal(DatosBase.TenantB, deB.Tenant);
                Assert.False(deA.Plataforma);
                Assert.False(deB.Plataforma);
                Assert.Equal(1, deA.TenantsVistos);   // el filtro del tenant del evento: nunca mezcla
                Assert.Equal(1, deB.TenantsVistos);
                Assert.Equal(2, deA.Cargos);
                Assert.Equal(3, deB.Cargos);
            }
            finally
            {
                await ServidorSqlDePrueba.BorrarBaseAsync(nombre);
            }
        }

        private static DomainEvent NuevoEvento(int idEmpresa, int idTenant, DateTime ahora) => new()
        {
            IdEmpresa = idEmpresa, IdTenant = idTenant, EventType = "prueba.tenant", AggregateType = "Prueba", AggregateId = "1",
            OccurredAt = ahora, CreadoPor = "pruebas", FechaCreacion = ahora
        };

        /// <summary>Tenants 1 y 2, empresas 1 a 4 (la B es la 4) y cargos: 2 en la empresa A y 3 en la B.</summary>
        private static void Sembrar(ApplicationDbContext db)
        {
            var ahora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            db.Paises.Add(new Pais { CodigoIso = "HN", CodigoIso3 = "HND", CodigoNumerico = "340", Nombre = "Honduras", NombreIngles = "Honduras", FechaCreacion = ahora });
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "TENANT_A", Nombre = "Tenant de prueba A", Tipo = TipoTenant.Interno, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "TENANT_B", Nombre = "Tenant de prueba B", Tipo = TipoTenant.Cliente, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();

            for (var n = 1; n <= 4; n++)
                db.Empresas.Add(new Empresa { IdTenant = n == DatosBase.EmpresaB ? DatosBase.TenantB : DatosBase.TenantA, Codigo = "E" + n, RazonSocial = "Empresa de prueba " + n, PaisIso = "HN", MonedaIso = "HNL", ZonaHoraria = "America/Tegucigalpa", FechaActivacion = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();

            foreach (var (empresa, tenant, codigo) in new[] { (DatosBase.EmpresaA, DatosBase.TenantA, "A1"), (DatosBase.EmpresaA, DatosBase.TenantA, "A2"),
                                                              (DatosBase.EmpresaB, DatosBase.TenantB, "B1"), (DatosBase.EmpresaB, DatosBase.TenantB, "B2"), (DatosBase.EmpresaB, DatosBase.TenantB, "B3") })
                db.Cargos.Add(new Cargo { IdEmpresa = empresa, IdTenant = tenant, Codigo = codigo, Nombre = codigo, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();
        }
    }
}
