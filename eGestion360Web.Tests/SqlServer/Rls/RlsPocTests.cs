using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static eGestion360Web.Tests.SqlServer.Rls.EsquemaPocRls;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Base desechable del laboratorio: se crea una vez para todas las pruebas de <see cref="RlsPocTests"/>, con
    /// <see cref="EsquemaPocRls.Script"/>, y se borra al terminar.
    /// </summary>
    public sealed class BasePocRls : IAsyncLifetime
    {
        public string Nombre { get; private set; } = string.Empty;
        public string Cadena { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            if (ServidorSqlDePrueba.MotivoParaOmitir != null) return;
            Nombre = await ServidorSqlDePrueba.CrearBaseAsync("rls");
            Cadena = ServidorSqlDePrueba.Cadena(Nombre);
            await ServidorSqlDePrueba.EjecutarScriptAsync(Cadena, EsquemaPocRls.Script);
        }

        public async Task DisposeAsync()
        {
            if (Nombre.Length > 0) await ServidorSqlDePrueba.BorrarBaseAsync(Nombre);
        }
    }

    /// <summary>
    /// Prueba de concepto de Row-Level Security (paso F0.5, ADR-001) sobre SQL Server real (LocalDB). Demuestra que la
    /// base, y no solo la aplicación, separa a los tenants: lo que una pantalla olvide filtrar, la base no lo entrega, y
    /// lo que intente escribir en otro tenant, la base lo rechaza.
    ///
    /// Las pruebas que escriben usan los tenants 3 y 4, o deshacen lo que hacen, para no mover los conteos de los demás.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    public class RlsPocTests : IClassFixture<BasePocRls>
    {
        private const int ErrorBloqueoRls = 33504;
        private const int ErrorClaveForanea = 547;

        private readonly BasePocRls _bd;

        public RlsPocTests(BasePocRls bd) => _bd = bd;

        // ── Ayudantes ────────────────────────────────────────────────────────────────────────────────────────────

        private SqlConnection Abrir(AmbitoDeConexion? ambito, string? cadena = null)
        {
            var conexion = new SqlConnection(cadena ?? _bd.Cadena);
            conexion.Open();
            ambito?.Fijar(conexion);
            return conexion;
        }

        private static object? Escalar(SqlConnection conexion, string sql)
        {
            using var comando = new SqlCommand(sql, conexion);
            var valor = comando.ExecuteScalar();
            return valor is DBNull ? null : valor;
        }

        private static int Ejecutar(SqlConnection conexion, string sql)
        {
            using var comando = new SqlCommand(sql, conexion);
            return comando.ExecuteNonQuery();
        }

        private static int Contar(SqlConnection conexion, string tabla) => (int)Escalar(conexion, $"SELECT COUNT(*) FROM dbo.{tabla};")!;

        private static void FallaConError(int numero, Action accion)
        {
            var error = Assert.Throws<SqlException>(accion);
            Assert.Equal(numero, error.Number);
        }

        private ContextoPocRls Contexto(AmbitoDeConexion ambito) => ContextoPocRls.Crear(_bd.Cadena, ambito);

        // ── Lectura ──────────────────────────────────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public void Sin_contexto_de_tenant_no_se_ve_ni_se_escribe_nada_salvo_lo_global()
        {
            using var conexion = Abrir(ambito: null);

            Assert.Equal(0, Contar(conexion, "poc_clientes"));
            Assert.Equal(0, Contar(conexion, "poc_facturas"));
            Assert.Equal(TasasGlobales, Contar(conexion, "poc_tasas"));
            FallaConError(ErrorBloqueoRls, () => Ejecutar(conexion,
                $"INSERT INTO dbo.poc_clientes (id_tenant, nombre) VALUES ({TenantEscrituraC}, N'Sin contexto');"));
        }

        [FactSqlServer]
        public async Task Una_consulta_que_ignora_los_filtros_de_EF_solo_recibe_filas_del_tenant()
        {
            await using var db = Contexto(AmbitoDeConexion.DeTenant(TenantA));

            // Simula la pantalla que olvida filtrar: sin el filtro de EF, la base sigue entregando solo el tenant A.
            var clientes = await db.Clientes.IgnoreQueryFilters().ToListAsync();
            var facturas = await db.Facturas.IgnoreQueryFilters().ToListAsync();

            Assert.Equal(ClientesA, clientes.Count);
            Assert.Equal(FacturasA, facturas.Count);
            Assert.Equal(TotalA, facturas.Sum(f => f.Total));
            Assert.All(clientes, c => Assert.Equal(TenantA, c.IdTenant));
            Assert.All(facturas, f => Assert.Equal(TenantA, f.IdTenant));
        }

        [FactSqlServer]
        public async Task Con_los_filtros_de_EF_el_resultado_es_el_mismo()
        {
            await using var db = Contexto(AmbitoDeConexion.DeTenant(TenantB));

            Assert.Equal(ClientesB, await db.Clientes.CountAsync());
            Assert.Equal(FacturasB, await db.Facturas.CountAsync());
            Assert.Equal(TotalB, await db.Facturas.SumAsync(f => f.Total));
        }

        // ── Escritura ────────────────────────────────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public void Insertar_o_mover_filas_a_otro_tenant_falla_en_la_base()
        {
            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraC));
            using var transaccion = conexion.BeginTransaction();
            void Intentar(string sql) => FallaConError(ErrorBloqueoRls, () =>
            {
                using var comando = new SqlCommand(sql, conexion, transaccion);
                comando.ExecuteNonQuery();
            });

            Intentar($"INSERT INTO dbo.poc_clientes (id_tenant, nombre) VALUES ({TenantEscrituraD}, N'Intruso');");
            Intentar($"UPDATE dbo.poc_clientes SET id_tenant = {TenantEscrituraD} WHERE nombre = N'Cliente C1';");
            transaccion.Rollback();
            using var verificacion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraC));
            Assert.Equal(1, (int)Escalar(verificacion, "SELECT COUNT(*) FROM dbo.poc_clientes WHERE nombre = N'Cliente C1';")!);
        }

        [FactSqlServer]
        public void Modificar_o_borrar_filas_de_otro_tenant_no_las_alcanza()
        {
            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraC));

            // Las filas del tenant D no existen para esta sesión: el UPDATE y el DELETE no afectan ninguna.
            Assert.Equal(0, Ejecutar(conexion, $"UPDATE dbo.poc_clientes SET nombre = N'Tocado' WHERE id_tenant = {TenantEscrituraD};"));
            Assert.Equal(0, Ejecutar(conexion, $"DELETE FROM dbo.poc_clientes WHERE id_tenant = {TenantEscrituraD};"));

            using var verificacion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraD));
            Assert.Equal(1, (int)Escalar(verificacion, "SELECT COUNT(*) FROM dbo.poc_clientes WHERE nombre = N'Cliente D1';")!);
        }

        [FactSqlServer]
        public async Task El_interceptor_asigna_el_tenant_al_insertar_y_prohibe_cambiarlo()
        {
            var nombre = $"Interceptor {Guid.NewGuid():N}";
            await using (var db = Contexto(AmbitoDeConexion.DeTenant(TenantEscrituraC)))
            {
                db.Clientes.Add(new ClientePoc { Nombre = nombre });   // sin IdTenant
                await db.SaveChangesAsync();

                var guardado = await db.Clientes.SingleAsync(c => c.Nombre == nombre);
                Assert.Equal(TenantEscrituraC, guardado.IdTenant);

                guardado.IdTenant = TenantEscrituraD;
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

                db.Clientes.Add(new ClientePoc { IdTenant = TenantEscrituraD, Nombre = "Otro tenant" });
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }

            using var verificacion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraC));
            Assert.Equal(TenantEscrituraC, (int)Escalar(verificacion, $"SELECT id_tenant FROM dbo.poc_clientes WHERE nombre = N'{nombre}';")!);
            Ejecutar(verificacion, $"DELETE FROM dbo.poc_clientes WHERE nombre = N'{nombre}';");
        }

        [FactSqlServer]
        public void La_clave_foranea_compuesta_impide_usar_un_cliente_de_otro_tenant()
        {
            int clienteDeD;
            using (var tenantD = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraD)))
                clienteDeD = (int)Escalar(tenantD, "SELECT id_cliente FROM dbo.poc_clientes WHERE nombre = N'Cliente D1';")!;

            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantEscrituraC));
            // La fila es del tenant C (RLS la acepta), pero apunta al cliente del tenant D: la FK (id_tenant, id_cliente) la rechaza.
            FallaConError(ErrorClaveForanea, () => Ejecutar(conexion,
                $"INSERT INTO dbo.poc_facturas (id_tenant, id_cliente, numero, total) VALUES ({TenantEscrituraC}, {clienteDeD}, N'C-999', 1.00);"));
        }

        // ── Contexto de sesión y pool ────────────────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public void El_contexto_de_la_conexion_no_se_puede_cambiar_despues_de_fijarlo()
        {
            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantA));

            Assert.Throws<SqlException>(() => AmbitoDeConexion.DeTenant(TenantB).Fijar(conexion));
            Assert.Throws<SqlException>(() => Ejecutar(conexion,
                "EXEC sys.sp_set_session_context @key = N'ambito', @value = N'plataforma';"));

            Assert.Equal(TenantA, Convert.ToInt32(Escalar(conexion, "SELECT SESSION_CONTEXT(N'id_tenant');")));
            Assert.Equal(ClientesA, Contar(conexion, "poc_clientes"));
        }

        [FactSqlServer]
        public void El_pool_de_conexiones_no_arrastra_el_tenant_anterior()
        {
            // Pool propio de una sola conexión: la segunda apertura reutiliza la misma sesión física.
            var cadena = ServidorSqlDePrueba.Cadena(_bd.Nombre, b =>
            {
                b.Pooling = true;
                b.MaxPoolSize = 1;
                b.ApplicationName = $"poc-rls-pool-{Guid.NewGuid():N}";
            });

            short sesionA;
            using (var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantA), cadena))
            {
                sesionA = (short)Escalar(conexion, "SELECT @@SPID;")!;
                Assert.Equal(ClientesA, Contar(conexion, "poc_clientes"));
            }

            using (var conexion = Abrir(ambito: null, cadena))
            {
                Assert.Equal(sesionA, (short)Escalar(conexion, "SELECT @@SPID;")!);
                Assert.Null(Escalar(conexion, "SELECT SESSION_CONTEXT(N'id_tenant');"));
                Assert.Equal(0, Contar(conexion, "poc_clientes"));

                AmbitoDeConexion.DeTenant(TenantB).Fijar(conexion);   // no falla: el read_only anterior se limpió
                Assert.Equal(ClientesB, Contar(conexion, "poc_clientes"));
            }
        }

        [FactSqlServer]
        public async Task Un_job_recorre_los_tenants_cada_uno_con_su_propio_contexto()
        {
            List<int> tenants;
            using (var conexion = Abrir(ambito: null))
            {
                using var comando = new SqlCommand("SELECT id_tenant FROM dbo.poc_tenants ORDER BY id_tenant;", conexion);
                using var lector = comando.ExecuteReader();
                tenants = new List<int>();
                while (lector.Read()) tenants.Add(lector.GetInt32(0));
            }

            var totales = new Dictionary<int, (int Facturas, decimal Total)>();
            foreach (var tenant in tenants)
            {
                await using var db = Contexto(AmbitoDeConexion.DeTenant(tenant));
                var facturas = await db.Facturas.IgnoreQueryFilters().ToListAsync();
                Assert.All(facturas, f => Assert.Equal(tenant, f.IdTenant));
                totales[tenant] = (facturas.Count, facturas.Sum(f => f.Total));
            }

            Assert.Equal((FacturasA, TotalA), totales[TenantA]);
            Assert.Equal((FacturasB, TotalB), totales[TenantB]);
            for (var t = PrimerTenantDeVolumen; t <= UltimoTenantDeVolumen; t++)
                Assert.Equal((FacturasPorTenantDeVolumen, TotalPorTenantDeVolumen), totales[t]);
        }

        // ── Tabla mixta y ámbito de plataforma ───────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public async Task La_tabla_mixta_muestra_lo_global_y_lo_propio_y_protege_lo_global()
        {
            await using (var db = Contexto(AmbitoDeConexion.DeTenant(TenantA)))
            {
                var tasas = await db.Tasas.IgnoreQueryFilters().ToListAsync();
                Assert.Equal(TasasGlobales + 1, tasas.Count);
                Assert.DoesNotContain(tasas, t => t.IdTenant == TenantB);
            }

            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantA));
            using var transaccion = conexion.BeginTransaction();
            void Intentar(string sql) => FallaConError(ErrorBloqueoRls, () =>
            {
                using var comando = new SqlCommand(sql, conexion, transaccion);
                comando.ExecuteNonQuery();
            });

            Intentar("INSERT INTO dbo.poc_tasas (id_tenant, moneda, valor) VALUES (NULL, 'GTQ', 3.1);");           // crear una global
            Intentar("UPDATE dbo.poc_tasas SET valor = 1 WHERE id_tenant IS NULL AND moneda = 'USD';");           // cambiarla
            Intentar($"UPDATE dbo.poc_tasas SET id_tenant = {TenantA} WHERE id_tenant IS NULL AND moneda = 'EUR';"); // adueñarse de ella
            Intentar("DELETE FROM dbo.poc_tasas WHERE id_tenant IS NULL AND moneda = 'USD';");                   // borrarla
            transaccion.Rollback();

            using var verificacion = Abrir(ambito: null);
            Assert.Equal(TasasGlobales, (int)Escalar(verificacion, "SELECT COUNT(*) FROM dbo.poc_tasas WHERE id_tenant IS NULL;")!);
        }

        [FactSqlServer]
        public void La_plataforma_escribe_filas_globales_pero_no_filas_de_un_tenant()
        {
            using var conexion = Abrir(AmbitoDeConexion.DePlataforma());
            using var transaccion = conexion.BeginTransaction();

            using (var comando = new SqlCommand("INSERT INTO dbo.poc_tasas (id_tenant, moneda, valor) VALUES (NULL, 'GTQ', 3.1);", conexion, transaccion))
                Assert.Equal(1, comando.ExecuteNonQuery());

            FallaConError(ErrorBloqueoRls, () =>
            {
                using var comando = new SqlCommand($"INSERT INTO dbo.poc_tasas (id_tenant, moneda, valor) VALUES ({TenantA}, 'GTQ', 3.1);", conexion, transaccion);
                comando.ExecuteNonQuery();
            });

            transaccion.Rollback();
        }

        [FactSqlServer]
        public void La_plataforma_no_ve_ni_escribe_datos_de_los_tenants()
        {
            using var conexion = Abrir(AmbitoDeConexion.DePlataforma());
            Assert.Equal(0, Contar(conexion, "poc_clientes"));
            Assert.Equal(0, Contar(conexion, "poc_facturas"));
            Assert.Equal(TasasGlobales, Contar(conexion, "poc_tasas"));

            using (var transaccion = conexion.BeginTransaction())
            {
                int Afectadas(string sql)
                {
                    using var comando = new SqlCommand(sql, conexion, transaccion);
                    return comando.ExecuteNonQuery();
                }

                FallaConError(ErrorBloqueoRls, () => Afectadas(
                    $"INSERT INTO dbo.poc_clientes (id_tenant, nombre) VALUES ({TenantA}, N'Desde la plataforma');"));
                // Las filas de los tenants no existen para la plataforma: modificar o borrar no alcanza ninguna.
                Assert.Equal(0, Afectadas("UPDATE dbo.poc_clientes SET nombre = N'Cambiado' WHERE nombre = N'Cliente A1';"));
                Assert.Equal(0, Afectadas("DELETE FROM dbo.poc_facturas WHERE numero = N'A-001';"));
                transaccion.Rollback();
            }

            using var tenantA = Abrir(AmbitoDeConexion.DeTenant(TenantA));
            Assert.Equal(1, (int)Escalar(tenantA, "SELECT COUNT(*) FROM dbo.poc_clientes WHERE nombre = N'Cliente A1';")!);
            Assert.Equal(1, (int)Escalar(tenantA, "SELECT COUNT(*) FROM dbo.poc_facturas WHERE numero = N'A-001';")!);
        }

        // ── Permisos de la aplicación ────────────────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public void El_usuario_de_la_aplicacion_no_puede_apagar_la_politica()
        {
            using var conexion = Abrir(AmbitoDeConexion.DeTenant(TenantA));
            Ejecutar(conexion, "EXECUTE AS USER = N'usr_app';");
            try
            {
                Assert.Equal(ClientesA, Contar(conexion, "poc_clientes"));
                Assert.Throws<SqlException>(() => Ejecutar(conexion, "ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = OFF);"));
                Assert.Throws<SqlException>(() => Ejecutar(conexion, "DROP SECURITY POLICY seg.pol_tenant;"));
            }
            finally
            {
                Ejecutar(conexion, "REVERT;");
            }

            Assert.True((bool)Escalar(conexion, "SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant';")!);
        }

        // ── Rendimiento ──────────────────────────────────────────────────────────────────────────────────────────

        [FactSqlServer]
        public void La_consulta_del_tenant_usa_el_indice_que_empieza_por_id_tenant()
        {
            using var conexion = Abrir(AmbitoDeConexion.DeTenant(PrimerTenantDeVolumen + 2));

            string plan;
            using (var comando = new SqlCommand(
                "SET STATISTICS XML ON; SELECT COUNT(*), SUM(total) FROM dbo.poc_facturas; SET STATISTICS XML OFF;", conexion))
            using (var lector = comando.ExecuteReader())
            {
                Assert.True(lector.Read());
                Assert.Equal(FacturasPorTenantDeVolumen, lector.GetInt32(0));
                Assert.True(lector.NextResult());
                Assert.True(lector.Read());
                plan = lector.GetString(0);
            }

            XNamespace sp = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var operadores = XDocument.Parse(plan).Descendants(sp + "RelOp")
                .Select(r => new
                {
                    Operador = (string?)r.Attribute("PhysicalOp"),
                    Indices = r.Elements().SelectMany(e => e.Elements(sp + "Object")).Select(o => (string?)o.Attribute("Index")).ToList()
                })
                .ToList();

            var resumen = string.Join(" | ", operadores.Select(o => $"{o.Operador} {string.Join(",", o.Indices)}"));
            Assert.True(operadores.Any(o => o.Operador == "Index Seek" && o.Indices.Contains($"[{IndiceFacturas}]")),
                $"El plan no busca por {IndiceFacturas}: {resumen}");
            Assert.False(operadores.Any(o => o.Operador is "Table Scan" or "Clustered Index Scan" or "Index Scan"),
                $"El plan recorre la tabla completa: {resumen}");
        }
    }
}
