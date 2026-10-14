using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.4a: el script 036 crea la política de Row-Level Security sobre las tablas que el arnés clasifica como de tenant,
    /// mixtas o de infraestructura. Esta prueba mantiene ordenados el arnés y el script: una tabla de tenant nueva que no quede en
    /// la política (y por tanto sin la segunda barrera) falla aquí. No necesita base de datos; el comportamiento de la política
    /// lo ensayan los casos sobre la copia real y <c>RlsRealSqlServerTests</c>.
    /// </summary>
    public class ScriptsRlsTests
    {
        private static string Leer(string patron)
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !Directory.Exists(Path.Combine(carpeta.FullName, "2 - Script SQL"))) carpeta = carpeta.Parent;
            if (carpeta == null) throw new FileNotFoundException("No se encontró la carpeta '2 - Script SQL'.");
            return File.ReadAllText(Directory.GetFiles(Path.Combine(carpeta.FullName, "2 - Script SQL"), patron).Single());
        }

        private static readonly Regex Predicado = new(
            @"ADD (?<tipo>FILTER|BLOCK) PREDICATE seg\.(?<fn>\w+)\(id_tenant\) ON dbo\.(?<tabla>\w+)(?: (?<op>AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?",
            RegexOptions.Compiled);

        private static List<(string Tipo, string Funcion, string Tabla, string Operacion)> Predicados() =>
            Predicado.Matches(Leer("036_rls_politicas_apagadas.sql"))
                .Select(m => (m.Groups["tipo"].Value, m.Groups["fn"].Value, m.Groups["tabla"].Value, m.Groups["op"].Value))
                .ToList();

        [Fact]
        public void La_politica_cubre_todas_las_tablas_de_tenant_de_la_base_menos_las_excepciones_documentadas()
        {
            // domain_events: solo la lee el despachador, que fija el contexto de cada evento (D20, ADR-019).
            // empresas: directorio de cuentas, lectura abierta y solo bloqueos (ADR-023).
            var esperadas = ClasificacionDeTablas.Todas
                .Where(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria) && t.EnBaseReal && t.Tabla != "domain_events")
                .Select(t => t.Tabla).Order().ToList();

            var enLaPolitica = Predicados().Select(p => p.Tabla).Distinct().Order().ToList();

            Assert.Equal(esperadas, enLaPolitica);
            Assert.DoesNotContain("domain_events", enLaPolitica);
        }

        [Fact]
        public void Cada_tabla_lleva_el_filtro_y_los_cuatro_bloqueos_de_su_tipo()
        {
            var mixtas = ClasificacionDeTablas.Todas.Where(t => t.Categoria == CategoriaTabla.Mixta).Select(t => t.Tabla).ToHashSet();
            var problemas = new List<string>();

            foreach (var grupo in Predicados().GroupBy(p => p.Tabla))
            {
                var tabla = grupo.Key;
                var filtros = grupo.Where(p => p.Tipo == "FILTER").ToList();
                var operaciones = grupo.Where(p => p.Tipo == "BLOCK").Select(p => p.Operacion).Order().ToList();

                if (operaciones.Count != 4 || operaciones.Distinct().Count() != 4)
                    problemas.Add($"{tabla}: debe llevar los cuatro bloqueos (AFTER INSERT, AFTER UPDATE, BEFORE UPDATE, BEFORE DELETE)");

                if (tabla == "empresas")
                {
                    if (filtros.Count != 0) problemas.Add("empresas es el directorio: lectura abierta, sin filtro (ADR-023)");
                    continue;
                }

                if (filtros.Count != 1) problemas.Add($"{tabla}: debe llevar un solo filtro");
                var funcionFiltro = mixtas.Contains(tabla) ? "fn_mixta_lectura" : "fn_tenant_actual";
                var funcionBloqueo = mixtas.Contains(tabla) ? "fn_mixta_escritura" : "fn_tenant_actual";
                if (filtros.Any(f => f.Funcion != funcionFiltro)) problemas.Add($"{tabla}: el filtro debe usar {funcionFiltro}");
                if (grupo.Where(p => p.Tipo == "BLOCK").Any(b => b.Funcion != funcionBloqueo)) problemas.Add($"{tabla}: los bloqueos deben usar {funcionBloqueo}");
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
            Assert.Equal(199, Predicados().Count);
        }

        [Fact]
        public void La_politica_se_crea_apagada_el_037_la_enciende_y_la_emergencia_la_apaga()
        {
            var s036 = Leer("036_rls_politicas_apagadas.sql");
            var s037 = Leer("037_rls_encender.sql");
            var emergencia = Leer("emergencia_rls_apagar.sql");

            Assert.Contains("WITH (STATE = OFF, SCHEMABINDING = ON)", s036);
            Assert.DoesNotContain("STATE = ON", s036.Substring(0, s036.LastIndexOf("/*", StringComparison.Ordinal)));
            Assert.Contains("ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = ON)", s037);
            Assert.Contains("ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = OFF)", emergencia);
            Assert.DoesNotContain("PRECHECK", emergencia.Replace("sin PRECHECK", ""));   // la emergencia no depende de nada
        }

        [Theory]
        [InlineData("036_rls_politicas_apagadas.sql")]
        [InlineData("037_rls_encender.sql")]
        [InlineData("emergencia_rls_apagar.sql")]
        public void Los_scripts_de_RLS_son_ASCII(string archivo) => Assert.DoesNotMatch(@"[^\x00-\x7F]", Leer(archivo));
    }
}
