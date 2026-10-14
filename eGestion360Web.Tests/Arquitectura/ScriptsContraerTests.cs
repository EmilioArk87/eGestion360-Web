using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.3b: los scripts 031 a 035 convierten en COMPUESTAS (id_tenant, ...) las claves foráneas entre tablas de tenant.
    /// Esta prueba mantiene ordenados el modelo de EF y los scripts: una relación nueva entre dos tablas de tenant que no
    /// aparezca como clave compuesta en un script falla aquí, y toda clave compuesta de los scripts empieza por id_tenant en
    /// los dos lados. No necesita base de datos; el comportamiento de los scripts se ensaya sobre la copia real.
    /// </summary>
    public class ScriptsContraerTests
    {
        private static readonly string[] Numeros = { "031", "032", "033", "034", "035" };

        private static readonly Regex Fk = new(
            @"ALTER TABLE dbo\.(?<hijo>\w+) ADD CONSTRAINT (?<nombre>FKT_\w+) FOREIGN KEY \(id_tenant, (?<cols>[\w, ]+)\) REFERENCES dbo\.(?<padre>\w+) \(id_tenant, (?<pcols>[\w, ]+)\)",
            RegexOptions.Compiled);

        private static string Leer(string numero)
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !Directory.Exists(Path.Combine(carpeta.FullName, "2 - Script SQL"))) carpeta = carpeta.Parent;
            if (carpeta == null) throw new FileNotFoundException("No se encontró la carpeta '2 - Script SQL'.");
            return File.ReadAllText(Directory.GetFiles(Path.Combine(carpeta.FullName, "2 - Script SQL"), numero + "_*.sql").Single());
        }

        private static List<(string Hijo, string Padre, string Cols, string Script)> ClavesDeLosScripts() =>
            Numeros.SelectMany(n => Fk.Matches(Leer(n)).Select(m =>
                (m.Groups["hijo"].Value, m.Groups["padre"].Value, string.Join(",", m.Groups["cols"].Value.Split(',').Select(c => c.Trim())), n))).ToList();

        [Fact]
        public void Cada_relacion_de_EF_entre_tablas_de_tenant_de_la_base_es_una_clave_compuesta_en_algun_script()
        {
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=.;Database=modelo").Options;
            using var db = new ApplicationDbContext(opciones);

            var tenant = ClasificacionDeTablas.Todas
                .Where(t => t.EnModeloEf && t.EnBaseReal && ClasificacionDeTablas.LlevaIdTenant(t.Categoria))
                .Select(t => t.Tabla).ToHashSet(StringComparer.Ordinal);

            var delModelo = db.Model.GetEntityTypes()
                .Where(e => !e.IsOwned() && tenant.Contains(e.GetTableName() ?? ""))
                .SelectMany(e => e.GetForeignKeys().Where(k => tenant.Contains(k.PrincipalEntityType.GetTableName() ?? "")))
                .Select(k => (Hijo: k.DeclaringEntityType.GetTableName()!, Padre: k.PrincipalEntityType.GetTableName()!,
                              Cols: string.Join(",", k.Properties.Select(p => p.GetColumnName()))))
                .Distinct().ToList();

            // EF modela algunas relaciones con menos columnas que la base (clientes y empleados apuntan a persona_empresa por
            // una sola; la clave de la base lleva tambien empresa y tipo de vinculo): basta con que la clave compuesta las incluya.
            var delScript = ClavesDeLosScripts();
            bool Cubierta((string Hijo, string Padre, string Cols) r) => delScript.Any(c => c.Hijo == r.Hijo && c.Padre == r.Padre
                && r.Cols.Split(',').All(col => c.Cols.Split(',').Contains(col)));

            var faltan = delModelo.Where(r => !Cubierta(r)).Select(r => $"{r.Hijo}({r.Cols}) -> {r.Padre}").ToList();
            Assert.True(faltan.Count == 0,
                "Estas relaciones de EF entre tablas de tenant no son clave compuesta en los scripts 031 a 035: " + string.Join("; ", faltan) +
                ". Agrégalas al generador de scripts (toda clave foránea entre tablas de tenant incluye id_tenant).");
            Assert.True(delModelo.Count >= 50, $"Se esperaban al menos 50 relaciones en el modelo y hay {delModelo.Count}.");
        }

        [Fact]
        public void Ninguna_clave_compuesta_aparece_en_dos_scripts_ni_dos_veces()
        {
            var repetidas = ClavesDeLosScripts().GroupBy(c => (c.Hijo, c.Padre, c.Cols)).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.Hijo}({g.Key.Cols}) -> {g.Key.Padre}").ToList();

            Assert.True(repetidas.Count == 0, "Claves compuestas repetidas: " + string.Join("; ", repetidas));
            Assert.True(ClavesDeLosScripts().Count == 78, $"Los scripts crean {ClavesDeLosScripts().Count} claves compuestas; se esperaban 78.");
        }

        [Theory]
        [InlineData("031")]
        [InlineData("032")]
        [InlineData("033")]
        [InlineData("034")]
        [InlineData("035")]
        public void Cada_script_es_ASCII_y_su_ROLLBACK_devuelve_las_claves_simples(string numero)
        {
            var texto = Leer(numero);
            Assert.DoesNotMatch(@"[^\x00-\x7F]", texto);

            var rollback = texto[texto.LastIndexOf("/*", StringComparison.Ordinal)..];
            var nuevas = Fk.Matches(texto).Select(m => m.Groups["nombre"].Value).Distinct().ToList();
            Assert.All(nuevas, nombre => Assert.Contains($"DROP CONSTRAINT {nombre};", rollback));
            Assert.Contains("ALTER COLUMN id_tenant INT NULL", rollback);
        }
    }
}
