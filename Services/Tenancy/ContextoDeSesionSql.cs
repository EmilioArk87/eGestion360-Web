using System.Data;
using System.Data.Common;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Para quién trabaja una conexión de SQL Server: un tenant (las peticiones de los usuarios y los procesos que atienden a un
    /// tenant) o la plataforma (procesos que escriben filas globales, como la tasa oficial). Se fija en el contexto de sesión
    /// (<c>SESSION_CONTEXT</c>) al abrir la conexión, con <c>@read_only = 1</c>: dentro de esa conexión nadie puede cambiarlo,
    /// ni siquiera un SQL inyectado. La política <c>seg.pol_tenant</c> (scripts 036 y 037, ADR-023) lo lee para filtrar y
    /// bloquear por tenant. El ámbito de plataforma no ve filas de tenants: solo permite escribir las filas globales de las
    /// tablas mixtas.
    /// </summary>
    public sealed record ContextoDeSesionSql(int? IdTenant, string Ambito)
    {
        public const string AmbitoTenant = "tenant";
        public const string AmbitoPlataforma = "plataforma";

        private const string Sql =
            "EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @id_tenant, @read_only = 1; " +
            "EXEC sys.sp_set_session_context @key = N'ambito', @value = @ambito, @read_only = 1;";

        /// <summary>El contexto que corresponde a lo que <see cref="ITenantContext"/> dice en este momento.</summary>
        public static ContextoDeSesionSql Desde(ITenantContext tenant)
            => new(tenant.IdTenant is > 0 ? tenant.IdTenant : null, tenant.EsPlataforma ? AmbitoPlataforma : AmbitoTenant);

        /// <summary>Comando que fija este contexto en la conexión (hay que ejecutarlo en una conexión abierta).</summary>
        public DbCommand CrearComando(DbConnection conexion)
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

        public override string ToString() => $"tenant {IdTenant?.ToString() ?? "-"}, ámbito {Ambito}";
    }
}
