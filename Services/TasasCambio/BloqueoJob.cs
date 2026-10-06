using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Bloqueo de "una sola ejecución a la vez" de un job, aunque haya varios procesos (reciclado de IIS que solapa
    /// el proceso viejo y el nuevo, disparador externo y programado al mismo tiempo).
    /// </summary>
    public interface IBloqueoJob
    {
        /// <summary>
        /// Toma el bloqueo sin esperar. Devuelve nulo si otra ejecución ya lo tiene; si no, un objeto que lo libera al
        /// desecharlo.
        /// </summary>
        Task<IAsyncDisposable?> IntentarTomarAsync(string recurso, CancellationToken ct = default);
    }

    /// <summary>
    /// Bloqueo con sp_getapplock (@LockOwner = 'Session', @LockTimeout = 0) en una conexión propia, abierta mientras
    /// dure la ejecución: así el bloqueo no depende de las transacciones del job. Se libera explícitamente con
    /// sp_releaseapplock antes de cerrar, porque con el pool de conexiones la sesión física sigue viva.
    /// </summary>
    public sealed class BloqueoJobSqlServer : IBloqueoJob
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<BloqueoJobSqlServer> _log;

        public BloqueoJobSqlServer(ApplicationDbContext db, ILogger<BloqueoJobSqlServer> log)
        {
            _db = db;
            _log = log;
        }

        public async Task<IAsyncDisposable?> IntentarTomarAsync(string recurso, CancellationToken ct = default)
        {
            var cadena = _db.Database.GetConnectionString()
                         ?? throw new InvalidOperationException("No hay cadena de conexión para el bloqueo del job.");

            var conexion = new SqlConnection(cadena);
            try
            {
                await conexion.OpenAsync(ct);
                await using var comando = conexion.CreateCommand();
                comando.CommandText =
                    "DECLARE @r int; " +
                    "EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; " +
                    "SELECT @r;";
                comando.Parameters.Add(new SqlParameter("@recurso", System.Data.SqlDbType.NVarChar, 255) { Value = recurso });

                var resultado = Convert.ToInt32(await comando.ExecuteScalarAsync(ct));
                if (resultado < 0)
                {
                    await conexion.DisposeAsync();
                    return null;   // -1: lo tiene otra sesión (con @LockTimeout = 0 no se espera)
                }
                return new Liberador(conexion, recurso, _log);
            }
            catch
            {
                await conexion.DisposeAsync();
                throw;
            }
        }

        private sealed class Liberador : IAsyncDisposable
        {
            private readonly SqlConnection _conexion;
            private readonly string _recurso;
            private readonly ILogger _log;

            public Liberador(SqlConnection conexion, string recurso, ILogger log)
            {
                _conexion = conexion;
                _recurso = recurso;
                _log = log;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using var comando = _conexion.CreateCommand();
                    comando.CommandText = "EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';";
                    comando.Parameters.Add(new SqlParameter("@recurso", System.Data.SqlDbType.NVarChar, 255) { Value = _recurso });
                    await comando.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    // Cerrar la conexión también lo suelta cuando el pool la reinicia; solo se registra.
                    _log.LogWarning(ex, "No se pudo liberar el bloqueo {Recurso}.", _recurso);
                }
                finally
                {
                    await _conexion.DisposeAsync();
                }
            }
        }
    }

    /// <summary>Bloqueo dentro del proceso, para las pruebas (SQLite no tiene sp_getapplock). Registrarlo como singleton.</summary>
    public sealed class BloqueoJobEnMemoria : IBloqueoJob
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaforos = new(StringComparer.OrdinalIgnoreCase);

        public Task<IAsyncDisposable?> IntentarTomarAsync(string recurso, CancellationToken ct = default)
        {
            var semaforo = _semaforos.GetOrAdd(recurso, _ => new SemaphoreSlim(1, 1));
            return Task.FromResult<IAsyncDisposable?>(semaforo.Wait(0) ? new Liberador(semaforo) : null);
        }

        private sealed class Liberador : IAsyncDisposable
        {
            private SemaphoreSlim? _semaforo;

            public Liberador(SemaphoreSlim semaforo) => _semaforo = semaforo;

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _semaforo, null)?.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
