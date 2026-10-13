using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.2: los scripts 026 a 030 agregan <c>id_tenant</c> a las tablas de tenant de la base. Esta prueba mantiene
    /// ordenados los scripts y la clasificación del arnés: si una tabla de tenant nueva llega a la base sin que un script
    /// le agregue la columna (o un script toca una tabla que ya no es de tenant), falla aquí. No necesita base de datos.
    /// El comportamiento de los scripts lo ensayan, contra una restauración de la copia real, los casos de la sesión de F1.2.
    /// </summary>
    public class ScriptsIdTenantTests
    {
        private static readonly string[] Numeros = { "026", "027", "028", "029", "030" };

        private static string CarpetaScripts()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !Directory.Exists(Path.Combine(carpeta.FullName, "2 - Script SQL"))) carpeta = carpeta.Parent;
            if (carpeta == null) throw new FileNotFoundException("No se encontró la carpeta '2 - Script SQL' subiendo desde " + AppContext.BaseDirectory);
            return Path.Combine(carpeta.FullName, "2 - Script SQL");
        }

        private static string Leer(string numero)
        {
            var archivo = Directory.GetFiles(CarpetaScripts(), numero + "_*.sql").Single();
            return File.ReadAllText(archivo);
        }

        private static readonly Regex AgregaColumna = new(@"ALTER TABLE dbo\.(\w+) ADD id_tenant INT NULL;", RegexOptions.Compiled);

        private static string[] TablasQueAgrega(string texto)
            => AgregaColumna.Matches(texto).Select(m => m.Groups[1].Value).ToArray();

        [Fact]
        public void Los_scripts_agregan_id_tenant_a_las_tablas_de_tenant_de_la_base_y_a_ninguna_otra()
        {
            // empresas la agrega el script 025 (F1.1); las tablas contables solo existen en el modelo de EF (script 010 en espera).
            var esperadas = ClasificacionDeTablas.Todas
                .Where(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria) && t.EnBaseReal && t.Tabla != "empresas")
                .Select(t => t.Tabla)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            var agregadas = Numeros.SelectMany(n => TablasQueAgrega(Leer(n))).ToList();

            var repetidas = agregadas.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(repetidas.Count == 0, $"Tablas que dos scripts agregan por segunda vez: {string.Join(", ", repetidas)}");

            var faltan = esperadas.Except(agregadas).ToList();
            var sobran = agregadas.Except(esperadas).ToList();
            Assert.True(faltan.Count == 0, $"Tablas de tenant de la base que ningún script 026-030 prepara: {string.Join(", ", faltan)}. Agrégalas al generador de scripts.");
            Assert.True(sobran.Count == 0, $"Tablas que un script prepara pero el arnés no clasifica como de tenant en la base: {string.Join(", ", sobran)}.");
        }

        [Theory]
        [InlineData("026")]
        [InlineData("027")]
        [InlineData("028")]
        [InlineData("029")]
        [InlineData("030")]
        public void Cada_script_es_ASCII_y_su_ROLLBACK_cubre_las_mismas_tablas_que_el_cambio(string numero)
        {
            var texto = Leer(numero);

            Assert.DoesNotMatch(@"[^\x00-\x7F]", texto);

            var inicioRollback = texto.LastIndexOf("/*", StringComparison.Ordinal);
            Assert.True(inicioRollback > 0, "No se encontró el bloque ROLLBACK comentado.");

            var rollback = texto[inicioRollback..];
            var enRollback = Regex.Matches(rollback, @"ALTER TABLE dbo\.(\w+) DROP COLUMN id_tenant;").Select(m => m.Groups[1].Value).OrderBy(t => t).ToList();
            var enCambio = TablasQueAgrega(texto).OrderBy(t => t).ToList();

            Assert.Equal(enCambio, enRollback);
        }

        [Fact]
        public void El_script_027_solo_desactiva_el_disparador_de_la_bitacora_dentro_de_su_transaccion_y_lo_reactiva()
        {
            var texto = Leer("027");

            Assert.Contains("DISABLE TRIGGER dbo.TR_bitacora_cambios_inmutable", texto);
            Assert.Contains("ENABLE TRIGGER dbo.TR_bitacora_cambios_inmutable", texto);
            Assert.True(texto.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal) < texto.IndexOf("DISABLE TRIGGER", StringComparison.Ordinal));
            Assert.True(texto.IndexOf("DISABLE TRIGGER", StringComparison.Ordinal) < texto.IndexOf("ENABLE TRIGGER", StringComparison.Ordinal));
            Assert.True(texto.IndexOf("ENABLE TRIGGER", StringComparison.Ordinal) < texto.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal));
        }
    }
}
