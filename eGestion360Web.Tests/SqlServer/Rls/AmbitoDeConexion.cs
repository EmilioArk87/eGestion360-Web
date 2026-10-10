using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Para quién trabaja una conexión: un tenant (las peticiones de usuarios y los jobs que procesan a ese tenant) o la
    /// plataforma (jobs que escriben filas globales, como la tasa oficial). La plataforma no ve datos de tenants: si
    /// necesita los de uno, abre una conexión con el contexto de ese tenant. Se fija en el contexto de sesión de SQL
    /// Server al abrir la conexión, con <c>@read_only = 1</c>: dentro de esa conexión nadie puede cambiarlo, ni siquiera
    /// un SQL inyectado. Es el prototipo de lo que en F1 hará la aplicación (ADR-001 y ADR-015).
    /// </summary>
    public sealed record AmbitoDeConexion(int? IdTenant, string Ambito)
    {
        public const string AmbitoTenant = "tenant";
        public const string AmbitoPlataforma = "plataforma";

        private const string Sql =
            "EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @id_tenant, @read_only = 1; " +
            "EXEC sys.sp_set_session_context @key = N'ambito', @value = @ambito, @read_only = 1;";

        public static AmbitoDeConexion DeTenant(int idTenant) => new(idTenant, AmbitoTenant);

        public static AmbitoDeConexion DePlataforma() => new(null, AmbitoPlataforma);

        public bool EsPlataforma => Ambito == AmbitoPlataforma;

        public void Fijar(DbConnection conexion)
        {
            using var comando = Comando(conexion);
            comando.ExecuteNonQuery();
        }

        public async Task FijarAsync(DbConnection conexion, CancellationToken ct = default)
        {
            await using var comando = Comando(conexion);
            await comando.ExecuteNonQueryAsync(ct);
        }

        private DbCommand Comando(DbConnection conexion)
        {
            var comando = conexion.CreateCommand();
            comando.CommandText = Sql;

            var tenant = comando.CreateParameter();
            tenant.ParameterName = "@id_tenant";
            tenant.DbType = DbType.Int32;
            tenant.Value = IdTenant.HasValue ? IdTenant.Value : DBNull.Value;
            comando.Parameters.Add(tenant);

            var ambito = comando.CreateParameter();
            ambito.ParameterName = "@ambito";
            ambito.DbType = DbType.String;
            ambito.Size = 20;
            ambito.Value = Ambito;
            comando.Parameters.Add(ambito);

            return comando;
        }
    }

    /// <summary>
    /// Fija el <see cref="AmbitoDeConexion"/> cada vez que EF abre una conexión. EF abre y cierra la conexión en cada
    /// consulta, y el pool la limpia al devolverla (sp_reset_connection), así que el contexto se fija en cada apertura.
    /// </summary>
    public sealed class InterceptorAmbitoDeConexion : DbConnectionInterceptor
    {
        private readonly AmbitoDeConexion _ambito;

        public InterceptorAmbitoDeConexion(AmbitoDeConexion ambito) => _ambito = ambito;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            => _ambito.Fijar(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
            => _ambito.FijarAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Al guardar: asigna el tenant de la sesión a las filas nuevas que no lo traen, rechaza filas nuevas de otro tenant
    /// y rechaza cambiar el tenant de una fila existente. Es la barrera de la aplicación; la base (RLS) es la segunda.
    /// </summary>
    public sealed class InterceptorTenantAlGuardar : SaveChangesInterceptor
    {
        public const string Propiedad = "IdTenant";

        private readonly AmbitoDeConexion _ambito;

        public InterceptorTenantAlGuardar(AmbitoDeConexion ambito) => _ambito = ambito;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Aplicar(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Aplicar(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private void Aplicar(DbContext? db)
        {
            if (db == null) return;

            foreach (var entrada in db.ChangeTracker.Entries())
            {
                if (entrada.Metadata.FindProperty(Propiedad) == null) continue;
                var propiedad = entrada.Property(Propiedad);

                if (entrada.State == EntityState.Added)
                {
                    // La plataforma solo crea filas globales; si intenta otra cosa, la base la rechaza.
                    if (_ambito.EsPlataforma) continue;

                    var actual = propiedad.CurrentValue as int?;
                    if (actual is null or 0)
                        propiedad.CurrentValue = _ambito.IdTenant;
                    else if (actual != _ambito.IdTenant)
                        throw new InvalidOperationException(
                            $"Se intentó crear un registro del tenant {actual} desde una sesión del tenant {_ambito.IdTenant}.");
                }
                else if (entrada.State == EntityState.Modified && propiedad.IsModified
                         && !Equals(propiedad.OriginalValue, propiedad.CurrentValue))
                {
                    throw new InvalidOperationException("No se puede cambiar el tenant de un registro existente.");
                }
            }
        }
    }
}
