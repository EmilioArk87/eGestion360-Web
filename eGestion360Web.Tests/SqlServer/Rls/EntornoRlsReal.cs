using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Tests.Arquitectura;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.Infra.FilasDePrueba;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Base desechable con el esquema que crea EF, las tablas de la base real que EF no mapea y los scripts 036 y 037 aplicados,
    /// con filas de dos tenants (A y B) en cada tabla de tenant y una global en las mixtas. La usan las pruebas de la política
    /// (<see cref="RlsRealSqlServerTests"/>) y las de la aplicación con la política encendida.
    /// </summary>
    public static class EntornoRlsReal
    {
        internal const int TenantA = DatosBase.TenantA;
        internal const int TenantB = DatosBase.TenantB;

        /// <summary>Tablas de la base que el modelo de EF no mapea: se crean mínimas para que el PRECHECK del 036 las encuentre.</summary>
        internal const string TablasFueraDeEf = @"
CREATE TABLE dbo.ingresos_operativos (id INT IDENTITY PRIMARY KEY, id_empresa INT NOT NULL, id_tenant INT NOT NULL);
CREATE TABLE dbo.precios_combustible (id INT IDENTITY PRIMARY KEY, id_empresa INT NOT NULL, id_tenant INT NOT NULL);
CREATE TABLE dbo.sen_boletines (id INT IDENTITY PRIMARY KEY, id_empresa INT NOT NULL, id_tenant INT NULL);
GO";

        internal static string LeerScript(string nombre)
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !Directory.Exists(Path.Combine(carpeta.FullName, "2 - Script SQL"))) carpeta = carpeta.Parent;
            if (carpeta == null) throw new FileNotFoundException("No se encontró la carpeta '2 - Script SQL'.");
            return File.ReadAllText(Path.Combine(carpeta.FullName, "2 - Script SQL", nombre));
        }

        /// <summary>Corre un lote con el contexto de sesión fijado como lo hará la aplicación (de solo lectura).</summary>
        internal static async Task<object?> ConContexto(string cadena, int? tenant, string ambito, string sql)
        {
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            await using (var fijar = new SqlCommand(
                "EXEC sp_set_session_context @key = N'id_tenant', @value = @t, @read_only = 1; " +
                "EXEC sp_set_session_context @key = N'ambito', @value = @a, @read_only = 1;", conexion))
            {
                fijar.Parameters.AddWithValue("@t", (object?)tenant ?? DBNull.Value);
                fijar.Parameters.AddWithValue("@a", ambito);
                await fijar.ExecuteNonQueryAsync();
            }

            await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
            var r = await comando.ExecuteScalarAsync();
            return r is DBNull ? null : r;
        }

        internal static async Task EjecutarAsync(string cadena, string sql)
        {
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }

        public sealed record Entorno(string Base, string Cadena, List<string> TablasConFiltro);

        internal const string QuitarRestricciones = @"
DECLARE @s NVARCHAR(MAX) = N'';
SELECT @s += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' NOCHECK CONSTRAINT ALL;' FROM sys.tables;
EXEC (@s);";

        /// <summary>
        /// En eBD_SPD esas columnas son calculadas; EnsureCreated las crea como columnas simples NOT NULL que EF no escribe.
        /// Se les pone un valor por defecto para poder insertar filas de prueba (la política no depende de ellas).
        /// </summary>
        internal static async Task DarValorPorDefectoALasCalculadasAsync(ApplicationDbContext db)
        {
            foreach (var entidad in db.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.GetTableName() != null))
            {
                foreach (var p in entidad.GetProperties().Where(p => !p.IsPrimaryKey() && p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never
                                                                    && p.GetDefaultValueSql() == null && p.GetComputedColumnSql() == null))
                {
                    var literal = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                    if (literal != typeof(decimal) && literal != typeof(int) && literal != typeof(long) && literal != typeof(double)) continue;
                    await db.Database.ExecuteSqlRawAsync($"ALTER TABLE dbo.[{entidad.GetTableName()}] ADD DEFAULT 0 FOR [{p.GetColumnName()}]");
                }
            }
        }

        /// <summary>Crea la base y la arma; si el armado falla, la borra para no dejar bases sueltas en LocalDB.</summary>
        internal static async Task<Entorno> PrepararAsync()
        {
            var nombre = await ServidorSqlDePrueba.CrearBaseAsync("rls_real");
            try { return await ArmarAsync(nombre); }
            catch { await ServidorSqlDePrueba.BorrarBaseAsync(nombre); throw; }
        }

        internal static async Task<Entorno> ArmarAsync(string nombre)
        {
            var cadena = ServidorSqlDePrueba.Cadena(nombre);
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cadena).Options;

            var tablas = ClasificacionDeTablas.Todas
                .Where(t => t.EnBaseReal && ClasificacionDeTablas.LlevaIdTenant(t.Categoria))
                .ToList();
            var conEf = tablas.Where(t => t.EnModeloEf).Select(t => t.Tabla).ToHashSet();
            var mixtas = tablas.Where(t => t.Categoria == CategoriaTabla.Mixta).Select(t => t.Tabla).ToHashSet();
            var fallos = new List<string>();

            await using (var db = new ApplicationDbContext(opciones))
            {
                await db.Database.EnsureCreatedAsync();
                await DarValorPorDefectoALasCalculadasAsync(db);
                await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, "CREATE SCHEMA seg;\nGO\n" + TablasFueraDeEf);
                // Lo que se prueba es la política, no las claves foráneas ni los CHECK: se apagan para armar filas de cualquier tabla.
                await EjecutarAsync(cadena, QuitarRestricciones);

                var entidades = db.Model.GetEntityTypes()
                    .Where(e => !e.IsOwned() && e.BaseType == null && conEf.Contains(e.GetTableName() ?? "")).ToList();
                foreach (var entidad in entidades)
                {
                    try
                    {
                        await InsertarFilasAsync(db, entidad, mixtas.Contains(entidad.GetTableName()!));
                    }
                    catch (Exception ex)
                    {
                        fallos.Add($"{entidad.ClrType.Name}: {ex.GetBaseException().Message}");
                        db.ChangeTracker.Clear();
                    }
                }
            }

            Assert.True(fallos.Count == 0, "No se pudo armar una fila de: " + string.Join(" | ", fallos));
            await EjecutarAsync(cadena, FilasFueraDeEf);
            await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, LeerScript("036_rls_politicas_apagadas.sql"));
            await ServidorSqlDePrueba.EjecutarScriptAsync(cadena, LeerScript("037_rls_encender.sql"));

            var conFiltro = tablas.Where(t => t.Tabla is not ("empresas" or "domain_events")).Select(t => t.Tabla).ToList();
            return new Entorno(nombre, cadena, conFiltro);
        }

        internal const string FilasFueraDeEf = @"
INSERT INTO dbo.ingresos_operativos (id_empresa, id_tenant) VALUES (1, 1), (4, 2);
INSERT INTO dbo.precios_combustible (id_empresa, id_tenant) VALUES (1, 1), (4, 2);
INSERT INTO dbo.sen_boletines (id_empresa, id_tenant) VALUES (1, 1), (4, 2), (1, NULL);";

        /// <summary>Dos filas, una por tenant; en las tablas mixtas, además una global (sin tenant).</summary>
        internal static async Task InsertarFilasAsync(ApplicationDbContext db, Microsoft.EntityFrameworkCore.Metadata.IEntityType entidad, bool mixta)
        {
            var a = Fabricar(entidad, TenantA, DatosBase.EmpresaA, "A", 11);
            var b = Fabricar(entidad, TenantB, DatosBase.EmpresaB, "B", 12);
            var filas = new List<object> { a, b };
            if (mixta)
            {
                var global = Fabricar(entidad, TenantA, DatosBase.EmpresaA, "G", 13);
                ((eGestion360Web.Models.Plataforma.ITenantOpcionalEntity)global).IdTenant = null;
                entidad.ClrType.GetProperty("IdEmpresa")?.SetValue(global, null);
                filas.Add(global);
            }

            var valor = 11;
            foreach (var fila in filas)
            {
                FijarClavesSinGenerar(entidad, fila, valor++);
                // La clave de la propia empresa la genera la base (Fabricar la fija para las pruebas sin SQL Server).
                if (fila is eGestion360Web.Models.Empresa empresa) empresa.IdEmpresa = 0;
            }

            db.AddRange(filas);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }
}
