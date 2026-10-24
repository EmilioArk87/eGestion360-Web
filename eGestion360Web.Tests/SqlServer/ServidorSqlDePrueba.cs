using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace eGestion360Web.Tests.SqlServer
{
    /// <summary>
    /// Servidor SQL Server para las pruebas que SQLite no puede hacer: Row-Level Security, claves foráneas compuestas
    /// reales y planes de ejecución. Por defecto es el SQL Server de esta máquina en <c>localhost,1433</c> con autenticación
    /// de Windows, sin usuarios ni claves. La variable de entorno <see cref="VariableEntorno"/> permite usar otro servidor de pruebas.
    /// Cada base de prueba se crea con nivel de compatibilidad 160, el de eBD_SPD en Somee (SQL Server 2022), aunque el servidor local
    /// sea más nuevo.
    ///
    /// Cada prueba trabaja en una base propia con nombre único (<c>egestion_prueba_…</c>) que se crea al empezar y se
    /// borra al terminar, con datos inventados. Nunca se conecta a eBD_SPD.
    /// </summary>
    public static class ServidorSqlDePrueba
    {
        /// <summary>Cadena de conexión opcional a otro servidor de pruebas (sin base: la elige cada prueba).</summary>
        public const string VariableEntorno = "EGESTION_PRUEBAS_SQLSERVER";

        private const string PrefijoBases = "egestion_prueba_";
        private const string ServidorLocal = @"Server=localhost,1433;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60";

        // Respaldo para máquinas sin servidor local (por ejemplo el runner de GitHub Actions, que trae LocalDB).
        private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60";

        private static readonly Lazy<(string Cadena, string? Motivo)> _servidor = new(ElegirServidor);

        /// <summary>null si hay un servidor de pruebas; si no, el motivo por el que se omiten las pruebas.</summary>
        public static string? MotivoParaOmitir => _servidor.Value.Motivo;

        /// <summary>Cadena a una base concreta del servidor de pruebas, con ajustes opcionales (pool, nombre de app…).</summary>
        public static string Cadena(string baseDeDatos, Action<SqlConnectionStringBuilder>? ajustar = null)
        {
            var constructor = new SqlConnectionStringBuilder(_servidor.Value.Cadena)
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

        /// <summary>
        /// Elige el servidor una sola vez: la variable de entorno si existe; si no, el SQL Server de esta máquina (localhost,1433);
        /// si tampoco responde, LocalDB cuando está instalado (el caso de GitHub Actions); y si no hay ninguno, el motivo para omitir.
        /// </summary>
        private static (string Cadena, string? Motivo) ElegirServidor()
        {
            var deEntorno = Environment.GetEnvironmentVariable(VariableEntorno);
            if (!string.IsNullOrWhiteSpace(deEntorno)) return (deEntorno, null);

            if (Responde(ServidorLocal)) return (ServidorLocal, null);

            if (OperatingSystem.IsWindows() && Responde(LocalDb)) return (LocalDb, null);

            return (ServidorLocal, $"Sin SQL Server de pruebas: no responde localhost,1433 ni hay LocalDB y {VariableEntorno} no está definida " +
                                   "(ver 1 - Documetacion/PRUEBAS_AUTOMATIZADAS.md).");
        }

        private static bool Responde(string cadenaBase)
        {
            try
            {
                var cadena = new SqlConnectionStringBuilder(cadenaBase) { InitialCatalog = "master", ConnectTimeout = 8 }.ConnectionString;
                using var conexion = new SqlConnection(cadena);
                conexion.Open();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Prueba que necesita SQL Server. Si la máquina no tiene un servidor en localhost,1433 ni <see cref="ServidorSqlDePrueba.VariableEntorno"/>,
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
