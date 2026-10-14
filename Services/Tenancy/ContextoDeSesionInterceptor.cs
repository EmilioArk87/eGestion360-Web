using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Fija el <see cref="ContextoDeSesionSql"/> cada vez que EF abre una conexión (paso F1.4b, ADR-023). EF abre y cierra la
    /// conexión en cada consulta y el pool la limpia al devolverla (<c>sp_reset_connection</c> borra también el contexto de
    /// sesión), así que se fija en cada apertura, con el tenant que <see cref="ITenantContext"/> tenga en ese momento.
    ///
    /// También vigila que el contexto no quede viejo: si una conexión sigue abierta (una transacción, una apertura explícita) y
    /// el tenant de <see cref="ITenantContext"/> cambia, o si alguien abrió la conexión sin pasar por EF, el siguiente comando
    /// falla en lugar de correr con el tenant equivocado. Un contexto de sesión no se puede cambiar dentro de una conexión
    /// abierta (es de solo lectura), así que el único arreglo es cerrarla y volver a abrirla con el tenant correcto.
    /// </summary>
    public sealed class ContextoDeSesionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor
    {
        private readonly ITenantContext _tenant;
        private readonly ConditionalWeakTable<DbConnection, ContextoDeSesionSql> _fijados = new();

        public ContextoDeSesionInterceptor(ITenantContext tenant) => _tenant = tenant;

        // ── Al abrir y cerrar la conexión ───────────────────────────────────

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            var contexto = ContextoDeSesionSql.Desde(_tenant);
            try
            {
                using var comando = contexto.CrearComando(connection);
                comando.ExecuteNonQuery();
            }
            catch
            {
                // Una conexión abierta sin contexto no debe quedar en uso.
                connection.Close();
                throw;
            }

            _fijados.AddOrUpdate(connection, contexto);
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var contexto = ContextoDeSesionSql.Desde(_tenant);
            try
            {
                await using var comando = contexto.CrearComando(connection);
                await comando.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                await connection.CloseAsync();
                throw;
            }

            _fijados.AddOrUpdate(connection, contexto);
        }

        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
            => _fijados.Remove(connection);

        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        {
            _fijados.Remove(connection);
            return Task.CompletedTask;
        }

        // ── Al ejecutar: el contexto de la conexión tiene que ser el de ahora ──
        // La vigilancia va al ejecutar y no al crear el comando: EF ya abrió la conexión, y si el comando falla aquí la cierra.

        public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Verificar(eventData.Connection);
            return result;
        }

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Verificar(eventData.Connection);
            return ValueTask.FromResult(result);
        }

        public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Verificar(eventData.Connection);
            return result;
        }

        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Verificar(eventData.Connection);
            return ValueTask.FromResult(result);
        }

        public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Verificar(eventData.Connection);
            return result;
        }

        public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Verificar(eventData.Connection);
            return ValueTask.FromResult(result);
        }

        private void Verificar(DbConnection conexion)
        {
            if (!_fijados.TryGetValue(conexion, out var fijado))
                throw new InvalidOperationException(
                    "La conexión se abrió sin pasar por EF y no tiene contexto de tenant. Ábrela con " +
                    "Database.OpenConnectionAsync() para que se le fije el tenant.");

            var actual = ContextoDeSesionSql.Desde(_tenant);
            if (actual != fijado)
                throw new InvalidOperationException(
                    $"El tenant cambió con la conexión abierta (se abrió para {fijado}; ahora es {actual}). " +
                    "Cierra la conexión o la transacción antes de cambiar de tenant.");
        }
    }
}
