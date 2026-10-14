using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Tests.Arquitectura;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.SqlServer.Rls.EntornoRlsReal;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Paso F1.4a contra SQL Server (LocalDB): los scripts 036 y 037, aplicados sobre el esquema que crea el modelo de EF, hacen que la
    /// base entregue a cada conexión solo las filas de su tenant, TABLA POR TABLA y por SQL directo (sin pasar por EF ni por sus
    /// filtros). La política ya probada con datos inventados en <see cref="RlsPocTests"/> (F0.5) se repite aquí con las tablas
    /// reales. Los scripts sobre la copia real de eBD_SPD se ensayan aparte.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    public class RlsRealSqlServerTests
    {

        private static async Task<int> Contar(string cadena, int? tenant, string ambito, string sql)
            => Convert.ToInt32(await ConContexto(cadena, tenant, ambito, sql));

        [FactSqlServer]
        public async Task Cada_tabla_de_tenant_entrega_solo_las_filas_de_su_tenant_por_SQL_directo()
        {
            var entorno = await PrepararAsync();
            try
            {
                var mixtas = ClasificacionDeTablas.Todas.Where(t => t.Categoria == CategoriaTabla.Mixta).Select(t => t.Tabla).ToHashSet();
                var problemas = new List<string>();

                foreach (var tabla in entorno.TablasConFiltro)
                {
                    foreach (var tenant in new[] { TenantA, TenantB })
                    {
                        var ajenas = await Contar(entorno.Cadena, tenant, "tenant", $"SELECT COUNT(*) FROM dbo.{tabla} WHERE id_tenant IS NOT NULL AND id_tenant <> {tenant}");
                        var propias = await Contar(entorno.Cadena, tenant, "tenant", $"SELECT COUNT(*) FROM dbo.{tabla} WHERE id_tenant = {tenant}");
                        if (ajenas != 0) problemas.Add($"{tabla}: el tenant {tenant} ve {ajenas} fila(s) de otro");
                        if (propias == 0) problemas.Add($"{tabla}: el tenant {tenant} no ve las suyas");
                    }

                    var sinContexto = await Contar(entorno.Cadena, null, "tenant", $"SELECT COUNT(*) FROM dbo.{tabla}");
                    var globales = mixtas.Contains(tabla)
                        ? await Contar(entorno.Cadena, null, "plataforma", $"SELECT COUNT(*) FROM dbo.{tabla} WHERE id_tenant IS NULL")
                        : 0;
                    if (sinContexto != globales) problemas.Add($"{tabla}: sin contexto ve {sinContexto} fila(s) y debería ver {globales}");
                }

                Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
                Assert.True(entorno.TablasConFiltro.Count >= 39, $"Solo se comprobaron {entorno.TablasConFiltro.Count} tablas.");
            }
            finally { await ServidorSqlDePrueba.BorrarBaseAsync(entorno.Base); }
        }

        [FactSqlServer]
        public async Task El_directorio_de_empresas_se_lee_sin_contexto_pero_no_se_escribe_en_otro_tenant_y_domain_events_no_tiene_politica()
        {
            var entorno = await PrepararAsync();
            try
            {
                Assert.True(await Contar(entorno.Cadena, null, "tenant", "SELECT COUNT(*) FROM dbo.empresas") >= 2,
                    "El directorio de empresas debe poder leerse sin contexto (ADR-023).");
                Assert.True(await Contar(entorno.Cadena, null, "tenant", "SELECT COUNT(*) FROM dbo.domain_events") >= 2,
                    "domain_events no lleva política (D20).");

                var error = await Assert.ThrowsAsync<SqlException>(() => ConContexto(entorno.Cadena, TenantA, "tenant",
                    $"UPDATE dbo.empresas SET id_tenant = {TenantB} WHERE id_tenant = {TenantA}"));
                Assert.Contains("block predicate", error.Message);

                var propias = await Contar(entorno.Cadena, TenantA, "tenant",
                    $"UPDATE dbo.empresas SET id_tenant = id_tenant WHERE id_tenant = {TenantA}; SELECT @@ROWCOUNT");
                Assert.True(propias >= 1);
            }
            finally { await ServidorSqlDePrueba.BorrarBaseAsync(entorno.Base); }
        }

        [FactSqlServer]
        public async Task Un_tenant_no_escribe_ni_toca_filas_de_otro_y_el_contexto_no_se_puede_cambiar()
        {
            var entorno = await PrepararAsync();
            try
            {
                Assert.Equal(0, await Contar(entorno.Cadena, TenantA, "tenant",
                    $"UPDATE dbo.cargos SET id_tenant = id_tenant WHERE id_tenant = {TenantB}; SELECT @@ROWCOUNT"));
                Assert.Equal(0, await Contar(entorno.Cadena, TenantA, "tenant",
                    $"DELETE FROM dbo.cargos WHERE id_tenant = {TenantB}; SELECT @@ROWCOUNT"));

                var error = await Assert.ThrowsAsync<SqlException>(() => ConContexto(entorno.Cadena, TenantA, "tenant",
                    $"UPDATE dbo.cargos SET id_tenant = {TenantB} WHERE id_tenant = {TenantA}"));
                Assert.Contains("block predicate", error.Message);

                var cambio = await Assert.ThrowsAsync<SqlException>(() => ConContexto(entorno.Cadena, TenantA, "tenant",
                    "EXEC sp_set_session_context @key = N'id_tenant', @value = 2; SELECT 1"));
                Assert.Contains("read_only", cambio.Message);

                var global = await Assert.ThrowsAsync<SqlException>(() => ConContexto(entorno.Cadena, TenantA, "tenant",
                    "UPDATE dbo.sen_boletines SET id_empresa = id_empresa WHERE id_tenant IS NULL; SELECT @@ROWCOUNT"));
                Assert.Contains("block predicate", global.Message);
            }
            finally { await ServidorSqlDePrueba.BorrarBaseAsync(entorno.Base); }
        }
    }
}
