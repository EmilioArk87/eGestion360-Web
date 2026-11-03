using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Las comprobaciones de solo lectura de la salida de F1 a producción (<c>1 - Documetacion/Operacion/F1</c>) llevan escritas
    /// dentro la lista de las 41 tablas con <c>id_tenant</c> y los nombres de las 18 claves únicas. Estas pruebas vigilan que esas
    /// listas no se desvíen de lo que clasifica el arnés y de lo que crean los scripts 031 a 035, y que los archivos sigan siendo
    /// de solo lectura. No ejecutan SQL: las comprobaciones se corren contra LocalDB y producción con <c>ejecutar_verificacion.ps1</c>.
    /// Las verificaciones de la identidad global (F1.5: <c>verificar_despues_038.sql</c> y <c>verificar_despues_f15.sql</c>) comparten
    /// carpeta y las mismas garantías de solo lectura, pero sus listas las vigila <see cref="ScriptsIdentidadGlobalTests"/>.
    /// </summary>
    public class VerificacionesDeSalidaF1Tests
    {
        /// <summary>Las cuatro verificaciones de la salida de F1 (scripts 025 a 037): describen el estado exacto de su ventana y no se editan.</summary>
        private static readonly string[] Historicas =
            { "verificar_antes_ventana_1.sql", "verificar_despues_ventana_1.sql", "verificar_despues_ventana_2.sql", "verificar_despues_ventana_3.sql" };

        /// <summary>Las de F1.5 (scripts 038 y 039) y F1.7 (script 040), posteriores a la salida de F1.</summary>
        private static readonly string[] DeF15 = { "verificar_despues_038.sql", "verificar_despues_f15.sql", "verificar_despues_f17.sql", "verificar_despues_041.sql" };

        private static readonly Regex TablaDeLaLista = new(@"^\s*\((\d+), N'([a-z_]+)', N'([TMI])'\)", RegexOptions.Multiline);
        private static readonly Regex ClaveUnicaDeLaLista = new(@"\(N'([a-z_]+)', N'(UX_[A-Za-z0-9_]+)'\)");
        private static readonly Regex PalabrasDeEscritura = new(
            @"\b(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY)\b", RegexOptions.IgnoreCase);

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Verificacion(string archivo)
            => File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", archivo), Encoding.UTF8);

        [Fact]
        public void Las_41_tablas_de_la_verificacion_son_las_que_clasifica_el_arnes()
        {
            var enElSql = TablaDeLaLista.Matches(Verificacion("verificar_despues_ventana_1.sql"))
                .Select(m => (Tabla: m.Groups[2].Value, Tipo: m.Groups[3].Value))
                .OrderBy(t => t.Tabla, StringComparer.Ordinal)
                .ToList();

            // Las listas de estos archivos son las de la salida de F1: no incluyen las tablas que crean scripts posteriores (038 en
            // adelante, p. ej. accesos_empresa), que tienen su propia verificación. Por eso se compara con DeLaSalidaDeF1 y no con todas.
            var esperadas = ClasificacionDeTablas.DeLaSalidaDeF1
                .Where(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria))
                .Select(t => (Tabla: t.Tabla, Tipo: t.Categoria switch
                {
                    CategoriaTabla.Tenant => "T",
                    CategoriaTabla.Mixta => "M",
                    _ => "I"
                }))
                .OrderBy(t => t.Tabla, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(41, esperadas.Count);
            Assert.Equal(esperadas, enElSql);

            // Ninguna tabla posterior a la salida de F1 se cuela en la lista histórica.
            var colada = ClasificacionDeTablas.PosterioresASalidaDeF1.Select(t => t.Tabla).Intersect(enElSql.Select(t => t.Tabla)).ToList();
            Assert.True(colada.Count == 0, "Tablas posteriores a F1 dentro de una verificación histórica: " + string.Join(", ", colada));
        }

        [Fact]
        public void Las_18_claves_unicas_de_la_verificacion_existen_en_los_scripts_031_a_035()
        {
            var claves = ClaveUnicaDeLaLista.Matches(Verificacion("verificar_despues_ventana_2.sql"))
                .Select(m => m.Groups[2].Value).Distinct().ToList();
            Assert.Equal(18, claves.Count);

            var scripts = Directory.GetFiles(Path.Combine(Raiz(), "2 - Script SQL"), "03*_*.sql")
                .Where(a => Regex.IsMatch(Path.GetFileName(a), @"^03[1-5]_")).ToArray();
            Assert.Equal(5, scripts.Length);
            var texto = string.Concat(scripts.Select(File.ReadAllText));

            var sinCrear = claves.Where(c => !texto.Contains(c, StringComparison.Ordinal)).ToList();
            Assert.True(sinCrear.Count == 0, "La verificación espera claves únicas que los scripts 031 a 035 no crean: " + string.Join(", ", sinCrear));
        }

        [Fact]
        public void Las_verificaciones_son_ASCII_y_solo_leen()
        {
            var carpeta = Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1");
            var archivos = Directory.GetFiles(carpeta, "*.sql");

            // Cada archivo de la carpeta está nombrado aquí: uno nuevo sin registrar (y sin pasar estas garantías) hace fallar la prueba.
            Assert.Equal(Historicas.Concat(DeF15).Order(StringComparer.Ordinal).ToList(),
                         archivos.Select(a => Path.GetFileName(a)!).Order(StringComparer.Ordinal).ToList());

            foreach (var archivo in archivos)
            {
                var texto = File.ReadAllText(archivo, Encoding.UTF8);
                Assert.True(texto.All(c => c < 128), Path.GetFileName(archivo) + " tiene caracteres que no son ASCII.");

                // Sin comentarios ni literales de texto: lo que queda es código ejecutable.
                var codigo = Regex.Replace(texto, @"/\*[\s\S]*?\*/", " ");
                codigo = Regex.Replace(codigo, @"--[^\r\n]*", " ");
                codigo = Regex.Replace(codigo, @"N?'(?:[^']|'')*'", "''");
                var prohibidas = PalabrasDeEscritura.Matches(codigo).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
                Assert.True(prohibidas.Count == 0, Path.GetFileName(archivo) + " usa sentencias de escritura: " + string.Join(", ", prohibidas));
            }
        }

        [Fact]
        public void Cada_verificacion_termina_con_una_fila_de_resumen()
        {
            foreach (var archivo in Historicas.Concat(DeF15))
            {
                var texto = Verificacion(archivo);
                Assert.Contains("RESUMEN", texto);
                Assert.Contains("estado", texto);
                Assert.Contains("esperado", texto);
                Assert.Contains("obtenido", texto);
            }
        }
    }
}
