using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.SqlServer.Tenancy
{
    /// <summary>
    /// Paso F1.2 contra SQL Server real (LocalDB): el interceptor del tenant junto con el de auditoría, con transacciones y
    /// claves generadas por la base. Lo que SQLite no puede comprobar: que el resolvedor, con su propia conexión, no se bloquee
    /// contra la transacción del guardado (el de auditoría abre una para registrar las altas) y que las filas queden con tenant.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    public class TenantAlGuardarSqlServerTests
    {
        private static readonly TimeSpan Limite = TimeSpan.FromSeconds(45);

        private sealed class Entorno : IAsyncDisposable
        {
            public required string Base { get; init; }
            public required string Cadena { get; init; }
            public required ServiceProvider Servicios { get; init; }
            public TenantContextFijo Tenant { get; } = new() { IdTenant = DatosBase.TenantA, IdEmpresa = DatosBase.EmpresaA };
            public ContextoAuditoriaFijo Auditoria { get; } = new() { Usuario = "pruebas", IdEmpresa = DatosBase.EmpresaA };

            public ApplicationDbContext Crear()
            {
                var resolvedor = new ResolvedorDeTenantEf(Servicios.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()));
                var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Cadena)
                    .AddInterceptors(new AuditoriaCambiosInterceptor(Auditoria), new TenantAlGuardarInterceptor(Tenant, resolvedor)).Options;
                return new ApplicationDbContext(opciones);
            }

            public async ValueTask DisposeAsync()
            {
                await Servicios.DisposeAsync();
                await ServidorSqlDePrueba.BorrarBaseAsync(Base);
            }
        }

        private static async Task<Entorno> PrepararAsync(string proposito)
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync(proposito);
            var cadena = ServidorSqlDePrueba.Cadena(nombre);
            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena).Options));

            await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena).Options))
            {
                await db.Database.EnsureCreatedAsync();
                Sembrar(db);
            }

            return new Entorno { Base = nombre, Cadena = cadena, Servicios = servicios.BuildServiceProvider() };
        }

        /// <summary>Lo mínimo que necesitan estas pruebas, sin ids explícitos (en SQL Server son identidades): tenants 1 y 2, empresas 1 a 4.</summary>
        private static void Sembrar(ApplicationDbContext db)
        {
            var ahora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            db.Paises.Add(new Pais { CodigoIso = "HN", CodigoIso3 = "HND", CodigoNumerico = "340", Nombre = "Honduras", NombreIngles = "Honduras", FechaCreacion = ahora });
            db.CatalogoTiposDocumento.Add(new CatalogoTipoDocumento { Codigo = "DNI", Nombre = "Documento Nacional de Identificación", PaisIso = "HN", Patron = "^[0-9]{13}$", LargoMin = 13, LargoMax = 13, EsIdentidad = true, FechaCreacion = ahora });
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "TENANT_A", Nombre = "Tenant de prueba A", Tipo = TipoTenant.Interno, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.Tenants.Add(new Tenant { UidPublico = Guid.NewGuid(), Codigo = "TENANT_B", Nombre = "Tenant de prueba B", Tipo = TipoTenant.Cliente, Estado = EstadoTenant.Activo, FechaEstado = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();

            // Cuatro empresas para que la B tenga el id 4 de DatosBase (en SQL Server el id lo genera la base): 1 a 3 son del tenant A.
            for (var n = 1; n <= 4; n++)
                db.Empresas.Add(new Empresa { IdTenant = n == DatosBase.EmpresaB ? DatosBase.TenantB : DatosBase.TenantA, Codigo = "E" + n, RazonSocial = "Empresa de prueba " + n, PaisIso = "HN", MonedaIso = "HNL", ZonaHoraria = "America/Tegucigalpa", FechaActivacion = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();
        }

        private static async Task ConLimite(Task tarea)
        {
            var terminada = await Task.WhenAny(tarea, Task.Delay(Limite));
            Assert.True(terminada == tarea, "El guardado no terminó: el resolvedor probablemente quedó bloqueado contra la transacción.");
            await tarea;
        }

        [FactSqlServer]
        public async Task Una_persona_con_vinculo_de_otra_empresa_queda_con_su_tenant_y_su_bitacora_tambien_sin_bloqueos()
        {
            await using var entorno = await PrepararAsync("tenant_persona");
            entorno.Tenant.IdTenant = DatosBase.TenantA;   // el administrador de la empresa A da de alta a una persona de la B
            entorno.Auditoria.IdEmpresa = DatosBase.EmpresaB;

            int idPersona;
            await using (var db = entorno.Crear())
            {
                var persona = PersonasDePrueba.Armar(DatosBase.EmpresaB, "Luis", "Pérez", dni: "0801199012345");
                db.Personas.Add(persona);
                await ConLimite(db.SaveChangesAsync());
                idPersona = persona.IdPersona;
            }

            await using var lectura = entorno.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.PersonaEmpresas.AsNoTracking().SingleAsync()).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.Empleados.AsNoTracking().SingleAsync()).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.Set<eGestion360Web.Models.Personas.PersonaDocumento>().AsNoTracking().SingleAsync()).IdTenant);

            var bitacora = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();
            Assert.NotEmpty(bitacora);
            Assert.All(bitacora, f => Assert.Equal(DatosBase.TenantB, f.IdTenant));
        }

        [FactSqlServer]
        public async Task Editar_una_persona_registra_la_bitacora_en_su_tenant_y_el_tenant_no_se_puede_cambiar()
        {
            await using var entorno = await PrepararAsync("tenant_editar");
            entorno.Tenant.IdTenant = DatosBase.TenantB;
            entorno.Auditoria.IdEmpresa = DatosBase.EmpresaB;

            int idPersona;
            await using (var db = entorno.Crear())
            {
                var persona = PersonasDePrueba.Armar(DatosBase.EmpresaB, "Luis", "Pérez");
                db.Personas.Add(persona);
                await ConLimite(db.SaveChangesAsync());
                idPersona = persona.IdPersona;
            }

            entorno.Tenant.IdTenant = DatosBase.TenantA;   // otra sesión edita
            await using (var db = entorno.Crear())
            {
                (await db.Personas.SingleAsync(p => p.IdPersona == idPersona)).PrimerNombre = "Luisa";
                await ConLimite(db.SaveChangesAsync());
            }

            await using (var db = entorno.Crear())
            {
                (await db.Personas.SingleAsync(p => p.IdPersona == idPersona)).IdTenant = DatosBase.TenantA;
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }

            await using var lectura = entorno.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdTenant);
            var cambio = await lectura.BitacoraCambios.AsNoTracking().SingleAsync(b => b.Operacion == "UPDATE");
            Assert.Equal(DatosBase.TenantB, cambio.IdTenant);
        }

        [FactSqlServer]
        public async Task Una_fila_de_catalogo_toma_el_tenant_de_su_empresa_desde_la_base()
        {
            await using var entorno = await PrepararAsync("tenant_catalogo");
            entorno.Tenant.IdTenant = null;   // administrador general sin empresa en sesión

            await using (var db = entorno.Crear())
            {
                db.Cargos.Add(new Cargo { IdEmpresa = DatosBase.EmpresaB, Codigo = "NUEVO", Nombre = "Nuevo", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                await ConLimite(db.SaveChangesAsync());
            }

            await using var lectura = entorno.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "NUEVO")).IdTenant);
        }
    }
}
