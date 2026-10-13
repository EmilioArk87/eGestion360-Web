using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using eGestion360Web.Data;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Tests.SqlServer.Tenancy
{
    /// <summary>
    /// Paso F1.1 contra SQL Server real (LocalDB): el modelo de EF y el script 025 tienen que describir la misma tabla
    /// <c>saas.tenants</c>, y el resolutor tiene que tolerar una empresa creada antes de F1.1 (con <c>id_tenant</c> nulo, como
    /// la deja la versión anterior de la aplicación mientras la columna admite nulos). Datos inventados; nunca eBD_SPD.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    public class TenantsSqlServerTests
    {
        /// <summary>Lo mínimo de <c>dbo.empresas</c> que usa el script 025, con las 4 empresas reales (D18).</summary>
        private const string EmpresasMinimas = @"
CREATE TABLE dbo.empresas (
    id_empresa INT NOT NULL PRIMARY KEY, codigo NVARCHAR(20) NOT NULL, razon_social NVARCHAR(200) NOT NULL,
    nombre_comercial NVARCHAR(150) NULL, eliminado BIT NOT NULL DEFAULT 0);
GO
INSERT INTO dbo.empresas (id_empresa, codigo, razon_social, nombre_comercial) VALUES
    (1, N'00000000000000000001', N'SIP Tecnologia', N'SIP Tecnologia'),
    (2, N'0000000000000000002', N'Demo', N'Demo S. de R.L.'),
    (3, N'0000000000000000003', N'IISE', N'Ingenieria importaciones y soluciones energeticas'),
    (4, N'TRANSGAR', N'Transportes Garay', N'Transgar');
GO";

        private static string LeerScript(string nombre)
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !Directory.Exists(Path.Combine(carpeta.FullName, "2 - Script SQL"))) carpeta = carpeta.Parent;
            if (carpeta == null) throw new FileNotFoundException("No se encontró la carpeta '2 - Script SQL' subiendo desde " + AppContext.BaseDirectory);
            return File.ReadAllText(Path.Combine(carpeta.FullName, "2 - Script SQL", nombre));
        }

        private static ApplicationDbContext Contexto(string cadena)
            => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena).Options);

        private static async Task EjecutarAsync(string cadena, string sql)
        {
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(sql, conexion);
            await comando.ExecuteNonQueryAsync();
        }

        /// <summary>Forma de saas.tenants y de lo que cuelga de ella, en un texto comparable (sin el orden de las columnas).</summary>
        private static async Task<List<string>> DescribirAsync(string cadena)
        {
            const string sql = @"
SELECT 'col ' + c.name + ' ' + TYPE_NAME(c.user_type_id) + '(' + CAST(c.max_length AS VARCHAR(10)) + ') '
       + CASE WHEN c.is_nullable = 1 THEN 'NULL' ELSE 'NOT NULL' END + CASE WHEN c.is_identity = 1 THEN ' IDENTITY' ELSE '' END
FROM sys.columns c WHERE c.object_id = OBJECT_ID('saas.tenants')
UNION ALL
SELECT 'indice ' + i.name + ' unico=' + CAST(i.is_unique AS VARCHAR(1)) + ' filtro=' + ISNULL(i.filter_definition, '')
FROM sys.indexes i WHERE i.object_id = OBJECT_ID('saas.tenants') AND i.name IS NOT NULL
UNION ALL
SELECT 'default ' + c.name + ' ' + dc.definition
FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('saas.tenants') AND c.name = 'uid_publico'
UNION ALL
SELECT 'check ' + cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id = OBJECT_ID('saas.tenants')
UNION ALL
SELECT 'fk ' + fk.name + ' -> ' + OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id)
FROM sys.foreign_keys fk WHERE fk.name = 'FK_empresas_tenants'
UNION ALL
SELECT 'indice-empresas ' + i.name FROM sys.indexes i WHERE i.object_id = OBJECT_ID('dbo.empresas') AND i.name = 'IX_empresas_tenant'
UNION ALL
SELECT 'esquema ' + s.name FROM sys.schemas s WHERE s.name IN ('saas')";

            var filas = new List<string>();
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(sql, conexion);
            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync()) filas.Add(lector.GetString(0));
            return filas.OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        [FactSqlServer]
        public async Task El_modelo_de_EF_y_el_script_025_describen_la_misma_tabla_de_tenants()
        {
            var baseEf = await ServidorSqlDePrueba.CrearBaseAsync("tenants_ef");
            var baseScript = await ServidorSqlDePrueba.CrearBaseAsync("tenants_025");
            try
            {
                var cadenaEf = ServidorSqlDePrueba.Cadena(baseEf);
                var cadenaScript = ServidorSqlDePrueba.Cadena(baseScript);

                await using (var db = Contexto(cadenaEf)) await db.Database.EnsureCreatedAsync();
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadenaScript, EmpresasMinimas);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadenaScript, LeerScript("025_tenants.sql"));

                var delModelo = await DescribirAsync(cadenaEf);
                var delScript = await DescribirAsync(cadenaScript);

                Assert.NotEmpty(delModelo);
                Assert.Equal(delModelo, delScript);
            }
            finally
            {
                await ServidorSqlDePrueba.BorrarBaseAsync(baseEf);
                await ServidorSqlDePrueba.BorrarBaseAsync(baseScript);
            }
        }

        [FactSqlServer]
        public async Task Un_tenant_creado_desde_EF_sin_uid_recibe_uno_de_la_base()
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync("tenants_uid");
            try
            {
                await using var db = Contexto(ServidorSqlDePrueba.Cadena(nombre));
                await db.Database.EnsureCreatedAsync();
                var tenant = new Models.Plataforma.Tenant
                {
                    Codigo = "SIN_UID", Nombre = "Sin uid", Tipo = Models.Plataforma.TipoTenant.Cliente,
                    Estado = Models.Plataforma.EstadoTenant.Activo, FechaEstado = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };

                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();

                Assert.NotEqual(Guid.Empty, tenant.UidPublico);
                Assert.True(tenant.IdTenant > 0);
            }
            finally
            {
                await ServidorSqlDePrueba.BorrarBaseAsync(nombre);
            }
        }

        [FactSqlServer]
        public async Task El_script_025_deja_los_tenants_de_D18_con_ids_que_siguen_el_orden_de_las_empresas()
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync("tenants_d18");
            try
            {
                var cadena = ServidorSqlDePrueba.Cadena(nombre);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, EmpresasMinimas);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, LeerScript("025_tenants.sql"));

                await using var conexion = new SqlConnection(cadena);
                await conexion.OpenAsync();
                await using var comando = new SqlCommand(
                    @"SELECT STRING_AGG(CONCAT(t.id_tenant, ':', t.codigo, '/', t.tipo, '/', t.estado, '/e', e.id_empresa), ' ')
                             WITHIN GROUP (ORDER BY t.id_tenant)
                      FROM saas.tenants t JOIN dbo.empresas e ON e.id_tenant = t.id_tenant", conexion);
                var mapa = (string?)await comando.ExecuteScalarAsync();

                Assert.Equal("1:SIP/interno/activo/e1 2:DEMO/demo/activo/e2 3:IISE/cliente/suspendido/e3 4:TRANSGAR/cliente/activo/e4", mapa);
            }
            finally
            {
                await ServidorSqlDePrueba.BorrarBaseAsync(nombre);
            }
        }

        [FactSqlServer]
        public async Task El_resolutor_devuelve_nulo_sin_fallar_con_una_empresa_creada_antes_de_F1_1()
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync("tenants_nulo");
            try
            {
                var cadena = ServidorSqlDePrueba.Cadena(nombre);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, EmpresasMinimas);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, LeerScript("025_tenants.sql"));
                // La versión anterior de la aplicación crea empresas sin tocar la columna nueva: id_tenant queda nulo.
                await EjecutarAsync(cadena, "INSERT INTO dbo.empresas (id_empresa, codigo, razon_social) VALUES (5, N'E5', N'Empresa 5')");

                var servicios = new ServiceCollection();
                servicios.AddScoped<ApplicationDbContext>(_ => Contexto(cadena));
                await using var proveedor = servicios.BuildServiceProvider();
                var resolvedor = new ResolvedorDeTenantEf(
                    proveedor.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()));

                Assert.Null(await resolvedor.ResolverPorEmpresaAsync(5));
                Assert.Equal(1, await resolvedor.ResolverPorEmpresaAsync(1));
                Assert.Equal(4, await resolvedor.ResolverPorEmpresaAsync(4));
            }
            finally
            {
                await ServidorSqlDePrueba.BorrarBaseAsync(nombre);
            }
        }
    }
}
