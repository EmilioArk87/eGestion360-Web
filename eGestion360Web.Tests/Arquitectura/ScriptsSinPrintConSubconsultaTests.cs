using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// SQL Server no admite una subconsulta dentro de <c>PRINT</c> («Subqueries are not allowed in this context»): el ensayo del script 046
    /// lo encontró en su PRECHECK, que habría abortado contra producción. El analizador de sintaxis (ScriptDom) no lo detecta, porque es una
    /// regla semántica, así que esta prueba lo vigila en los scripts de migración a partir del 040: el valor se calcula antes en una variable
    /// y el <c>PRINT</c> lleva la variable. No necesita base de datos.
    /// </summary>
    public class ScriptsSinPrintConSubconsultaTests
    {
        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        /// <summary>Sin comentarios ni textos entre comillas (así un «(SELECT» dentro de un mensaje no cuenta).</summary>
        private static string SoloCodigo(string sql)
        {
            var t = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ");
            t = Regex.Replace(t, @"--[^\r\n]*", " ");
            return Regex.Replace(t, @"N?'(?:[^']|'')*'", "''");
        }

        /// <summary>Los <c>PRINT</c> de un texto: cada uno llega hasta el «;» o hasta el fin de línea cuando no queda ningún paréntesis abierto.</summary>
        private static IEnumerable<string> Prints(string codigo)
        {
            foreach (Match m in Regex.Matches(codigo, @"\bPRINT\b", RegexOptions.IgnoreCase))
            {
                var sentencia = new StringBuilder();
                var profundidad = 0;
                for (var i = m.Index; i < codigo.Length; i++)
                {
                    var c = codigo[i];
                    if (c == '(') profundidad++;
                    else if (c == ')') profundidad--;
                    else if (c == ';') break;
                    else if (c == '\n' && profundidad <= 0) break;
                    sentencia.Append(c);
                }
                yield return sentencia.ToString();
            }
        }

        [Fact]
        public void Ningun_PRINT_de_los_scripts_de_migracion_lleva_una_subconsulta()
        {
            var infractores = new List<string>();
            foreach (var archivo in Directory.GetFiles(Path.Combine(Raiz(), "2 - Script SQL"), "*.sql").OrderBy(a => a, StringComparer.Ordinal))
            {
                var nombre = Path.GetFileName(archivo);
                if (!Regex.IsMatch(nombre, @"^0[4-9]\d_")) continue;   // del 040 en adelante

                var codigo = SoloCodigo(File.ReadAllText(archivo, Encoding.UTF8));
                foreach (var print in Prints(codigo))
                    if (Regex.IsMatch(print, @"\(\s*SELECT\b", RegexOptions.IgnoreCase))
                        infractores.Add($"{nombre}: {Regex.Replace(print, @"\s+", " ").Trim()}");
            }

            Assert.True(infractores.Count == 0,
                "Un PRINT con subconsulta falla al ejecutarse; calcule el valor antes en una variable:\n" + string.Join("\n", infractores));
        }

        [Theory]
        [InlineData("PRINT CONCAT(N'a', (SELECT COUNT(*) FROM t), N'.');", true)]
        [InlineData("PRINT CONCAT(N'a', @n,\n   (SELECT COUNT(*) FROM t), N'.');", true)]
        [InlineData("PRINT CONCAT(N'a', @n, N'.');", false)]
        [InlineData("PRINT N'(SELECT en un mensaje)';", false)]
        [InlineData("-- PRINT (SELECT 1)\nSELECT 1;", false)]
        public void El_detector_distingue_un_PRINT_con_subconsulta_de_uno_sin_ella(string sql, bool debeDetectar)
        {
            var detectado = Prints(SoloCodigo(sql)).Any(p => Regex.IsMatch(p, @"\(\s*SELECT\b", RegexOptions.IgnoreCase));
            Assert.Equal(debeDetectar, detectado);
        }
    }
}
