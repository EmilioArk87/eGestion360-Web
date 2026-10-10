using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace eGestion360Web.Tests.SqlServer
{
    /// <summary>
    /// Servidor SQL Server para las pruebas que SQLite no puede hacer: Row-Level Security, claves foráneas compuestas
    /// reales y planes de ejecución. Por defecto es LocalDB (<c>(localdb)\MSSQLLocalDB</c>) con autenticación de Windows,
    /// sin usuarios ni claves. La variable de entorno <see cref="VariableEntorno"/> permite usar otro servidor de pruebas.
    ///
    /// Cada prueba trabaja en una base propia con nombre único (<c>egestion_prueba_…</c>) que se crea al empezar y se
    /// borra al terminar, con datos inventados. Nunca se conecta a eBD_SPD.
    /// </summary>
    public static class ServidorSqlDePrueba
    {
        /// <summary>Cadena de conexión opcional a otro servidor de pruebas (sin base: la elige cada prueba).</summary>
        public const string VariableEntorno = "EGESTION_PRUEBAS_SQLSERVER";

        private const string PrefijoBases = "egestion_prueba_";
        private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60";

        private static readonly Lazy<string?> _motivoParaOmitir = new(CalcularMotivoParaOmitir);

        /// <summary>null si hay un servidor de pruebas; si no, el motivo por el que se omiten las pruebas.</summary>
        public static string? MotivoParaOmitir => _motivoParaOmitir.Value;

        /// <summary>Cadena a una base concreta del servidor de pruebas, con ajustes opcionales (pool, nombre de app…).</summary>
        public static string Cadena(string baseDeDatos, Action<SqlConnectionStringBuilder>? ajustar = null)
        {
            var constructor = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(VariableEntorno) ?? LocalDb)
            {
                InitialCatalog = baseDeDatos
            };
            ajustar?.Invoke(constructor);
            return constructor.ConnectionString;
        }

        /// <summary>Crea una base vacía con nombre único y nivel de compatibilidad 160, el de eBD_SPD.</summary>
        public static async Task<string> CrearBaseAsync(string proposito)
        {
            var nombre = $"{PrefijoBases}{proposito}_{Guid.NewGuid():N}";
            await using var conexion = new SqlConnection(Cadena("master"));
            await conexion.OpenAsync();
            await EjecutarAsync(conexion, $"CREATE DATABASE [{nombre}];");
            await EjecutarAsync(conexion, $"ALTER DATABASE [{nombre}] SET COMPATIBILITY_LEVEL = 160;");
            return nombre;
        }

        /// <summary>Borra una base creada por <see cref="CrearBaseAsync"/>; se niega a borrar cualquier otra.</summary>
        public static async Task BorrarBaseAsync(string nombre)
        {
            if (!nombre.StartsWith(PrefijoBases, StringComparison.Ordinal))
                throw new InvalidOperationException($"Solo se borran bases de prueba ({PrefijoBases}…), no '{nombre}'.");

            SqlConnection.ClearAllPools();
            await using var conexion = new SqlConnection(Cadena("master"));
            await conexion.OpenAsync();
            await EjecutarAsync(conexion,
                $"IF DB_ID(N'{nombre}') IS NOT NULL BEGIN ALTER DATABASE [{nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nombre}]; END");
        }

        /// <summary>Ejecuta un script con lotes separados por líneas <c>GO</c>, como lo haría SSMS o DBeaver.</summary>
        public static async Task EjecutarScriptAsync(string cadena, string script)
        {
            await using var conexion = new SqlConnection(cadena);
            await conexion.OpenAsync();
            foreach (var lote in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(lote)) await EjecutarAsync(conexion, lote);
            }
        }

        private static async Task EjecutarAsync(SqlConnection conexion, string sql)
        {
            await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }

        private static string? CalcularMotivoParaOmitir()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableEntorno))) return null;
            if (!OperatingSystem.IsWindows())
                return $"Sin SQL Server de pruebas: LocalDB solo existe en Windows y {VariableEntorno} no está definida.";

            using var versiones = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions");
            return versiones is { SubKeyCount: > 0 }
                ? null
                : "Sin SQL Server de pruebas: LocalDB no está instalado (ver 1 - Documetacion/PRUEBAS_AUTOMATIZADAS.md).";
        }
    }

    /// <summary>
    /// Prueba que necesita SQL Server. Si la máquina no tiene LocalDB ni <see cref="ServidorSqlDePrueba.VariableEntorno"/>,
    /// la prueba aparece como omitida con el motivo, en lugar de fallar.
    /// </summary>
    public sealed class FactSqlServerAttribute : FactAttribute
    {
        public FactSqlServerAttribute()
        {
            var motivo = ServidorSqlDePrueba.MotivoParaOmitir;
            if (motivo != null) Skip = motivo;
        }
    }
}
