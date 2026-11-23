using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Motor de reportes versionados (ADR-031, Fase 1): el script 047 crea 4 tablas <c>rep_</c> (3 mixtas y 1 de tenant) con sus predicados,
    /// disparadores y la semilla; la clasificación, el índice de scripts, el ADR y los verificadores diarios lo conocen.
    /// </summary>
    public class ScriptsReportesTests
    {
        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz() }.Concat(partes).ToArray()), Encoding.UTF8);

        private static string S047() => Leer("2 - Script SQL", "047_motor_reportes_versionados.sql");

        private static string SinComentarios(string sql) => Regex.Replace(Regex.Replace(sql, @"/\*[\s\S]*?\*/", " "), @"--[^\r\n]*", " ");

        private static string SinRollback(string sql) => sql[..sql.LastIndexOf("/*", StringComparison.Ordinal)];

        private static readonly string[] Mixtas = { "dbo.rep_reportes", "dbo.rep_reporte_versiones", "dbo.rep_reporte_bitacora" };
        private const string DeTenant = "dbo.rep_ejecuciones";

        [Fact]
        public void El_script_es_ascii_y_tiene_precheck_cambio_postcheck_y_rollback()
        {
            var s = S047();
            Assert.True(s.All(c => c < 128), "El script 047 debe ser ASCII puro.");
            Assert.Contains("PRECHECK 047", s);
            Assert.Contains("CAMBIO 047 aplicado", s);
            Assert.Contains("POSTCHECK", s);
            Assert.Contains("ROLLBACK 047 detenido", s);
            Assert.Contains("BEGIN TRANSACTION;", s);
            Assert.Contains("/alerta-bd", s);
        }

        [Fact]
        public void Cada_tabla_entra_a_la_politica_con_las_funciones_de_su_tipo()
        {
            var codigo = SinComentarios(SinRollback(S047()));
            foreach (var tabla in Mixtas)
            {
                Assert.Single(Regex.Matches(codigo, @"ADD FILTER PREDICATE seg\.fn_mixta_lectura\(id_tenant\) ON " + Regex.Escape(tabla) + @","));
                Assert.Equal(4, Regex.Matches(codigo, @"ADD BLOCK PREDICATE seg\.fn_mixta_escritura\(id_tenant\) ON " + Regex.Escape(tabla) + @" ").Count);
            }
            Assert.Single(Regex.Matches(codigo, @"ADD FILTER PREDICATE seg\.fn_tenant_actual\(id_tenant\) ON " + Regex.Escape(DeTenant) + @","));
            Assert.Equal(4, Regex.Matches(codigo, @"ADD BLOCK PREDICATE seg\.fn_tenant_actual\(id_tenant\) ON " + Regex.Escape(DeTenant) + @" ").Count);
            Assert.Equal(20, Regex.Matches(codigo, @"ADD (FILTER|BLOCK) PREDICATE").Count);
            Assert.DoesNotContain("STATE = ON", codigo);   // la politica queda como estaba
        }

        [Fact]
        public void Las_versiones_publicadas_la_bitacora_y_las_ejecuciones_son_inmutables()
        {
            var codigo = SinComentarios(SinRollback(S047()));
            Assert.Matches(@"CREATE TRIGGER dbo\.TR_rep_reporte_bitacora_inmutable\s+ON dbo\.rep_reporte_bitacora\s+INSTEAD OF UPDATE, DELETE", codigo);
            Assert.Matches(@"CREATE TRIGGER dbo\.TR_rep_ejecuciones_inmutable\s+ON dbo\.rep_ejecuciones\s+INSTEAD OF UPDATE, DELETE", codigo);
            Assert.Matches(@"CREATE TRIGGER dbo\.TR_rep_reporte_versiones_reglas\s+ON dbo\.rep_reporte_versiones\s+AFTER INSERT, UPDATE, DELETE", codigo);
            Assert.Matches(@"CREATE TRIGGER dbo\.TR_rep_reportes_reglas\s+ON dbo\.rep_reportes\s+AFTER INSERT, UPDATE", codigo);
            Assert.Matches(@"CREATE TRIGGER dbo\.TR_rep_ejecuciones_version\s+ON dbo\.rep_ejecuciones\s+AFTER INSERT", codigo);
            // RP6: la huella la calcula la base
            Assert.Matches(@"hash_sha256\s+AS CONVERT\(CHAR\(64\), HASHBYTES\('SHA2_256', contenido_plantilla \+ N'\|' \+ ISNULL\(configuracion_json, N''\)\), 2\) PERSISTED NOT NULL", codigo);
        }

        [Fact]
        public void La_semilla_global_se_escribe_con_ambito_de_plataforma_y_lo_restituye()
        {
            var codigo = SinComentarios(SinRollback(S047()));
            Assert.Contains("@key = N'ambito', @value = N'plataforma'", codigo);
            Assert.Equal(2, Regex.Matches(codigo, @"@key = N'ambito', @value = @ambitoOriginal").Count);   // al terminar y en el CATCH
            Assert.Contains("'psi_constancia_licencia'", codigo);
        }

        [Fact]
        public void La_clasificacion_el_indice_el_adr_y_los_verificadores_conocen_el_047()
        {
            var clasificacion = ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla);
            foreach (var tabla in Mixtas)
                Assert.Equal((CategoriaTabla.Mixta, "047"), (clasificacion[tabla[4..]].Categoria, clasificacion[tabla[4..]].CreadaPorScript));
            Assert.Equal((CategoriaTabla.Tenant, "047"), (clasificacion[DeTenant[4..]].Categoria, clasificacion[DeTenant[4..]].CreadaPorScript));

            Assert.Contains("| 047 | 047_motor_reportes_versionados.sql |", Leer("1 - Documetacion", "INDICE_SCRIPTS_SQL.md"));
            Assert.True(File.Exists(Path.Combine(Raiz(), "1 - Documetacion", "Arquitectura", "ADR-031-reportes-versionados-en-bd.md")));

            // Los verificadores de la salida de F1 cuentan tablas, claves y predicados de toda la base: con el 047 aplicado deben aceptarlos.
            foreach (var verificador in new[] { "verificar_despues_f15.sql", "verificar_despues_f17.sql" })
            {
                var v = Leer("1 - Documetacion", "Operacion", "F1", verificador);
                Assert.Contains("OBJECT_ID(N'dbo.rep_reportes', N'U') IS NOT NULL THEN 1 ELSE 0 END) AS con047", v);
                Assert.Contains("20 * c.con047", v);   // los 20 predicados
            }
        }
    }
}
