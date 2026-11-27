using System.Text.RegularExpressions;
using eGestion360Web.Tests.SqlServer.Rls;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Toda tabla con <c>id_tenant</c> que crea un script posterior a la salida de F1 entra a <c>seg.pol_tenant</c> en su script, con un
    /// filtro y cuatro bloqueos y las funciones de su categoría. No necesita SQL Server: lee las mismas sentencias que
    /// <see cref="EntornoRlsReal"/> aplica a la base de pruebas.
    /// </summary>
    public class RlsDeTablasPosterioresTests
    {
        /// <summary>Los predicados de las altas posteriores a F1: tabla, función y operación («FILTER» para el filtro).</summary>
        private static List<(string Tabla, string Funcion, string Operacion)> Predicados()
            => EntornoRlsReal.AltasEnLaPoliticaPosterioresAF1()
                .SelectMany(alta => Regex.Matches(alta,
                    @"(FILTER|BLOCK) PREDICATE seg\.(\w+)\(id_tenant\) ON \w+\.(\w+)(?:\s+(AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?"))
                .Select(m => (m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value == "FILTER" ? "FILTER" : m.Groups[4].Value))
                .ToList();

        private static List<TablaClasificada> TablasQueDebenEntrar()
            => ClasificacionDeTablas.PosterioresASalidaDeF1.Where(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria)).ToList();

        [Fact]
        public void Cada_tabla_de_tenant_posterior_a_F1_entra_a_la_politica_con_un_filtro_y_cuatro_bloqueos()
        {
            var tablas = TablasQueDebenEntrar();
            Assert.True(tablas.Count >= 18, $"Solo hay {tablas.Count} tablas de tenant posteriores a F1.");
            var predicados = Predicados();
            var problemas = new List<string>();

            foreach (var tabla in tablas)
            {
                var mixta = tabla.Categoria == CategoriaTabla.Mixta;
                var esperados = new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE DELETE", "BEFORE UPDATE", "FILTER" }
                    .Select(operacion => operacion + " " + (!mixta ? "fn_tenant_actual" : operacion == "FILTER" ? "fn_mixta_lectura" : "fn_mixta_escritura"))
                    .ToList();
                var reales = predicados.Where(p => p.Tabla == tabla.Tabla).Select(p => p.Operacion + " " + p.Funcion).Order(StringComparer.Ordinal).ToList();

                if (!esperados.SequenceEqual(reales))
                    problemas.Add($"{tabla.Tabla} (script {tabla.CreadaPorScript}): [{string.Join(", ", reales)}]");
            }

            Assert.True(problemas.Count == 0, "Tablas sin la política que les toca: " + string.Join(" | ", problemas));
        }

        [Fact]
        public void La_politica_no_recibe_despues_de_F1_tablas_sin_clasificar_como_de_tenant_o_mixtas()
        {
            var clasificadas = TablasQueDebenEntrar().Select(t => t.Tabla).ToHashSet();

            Assert.Empty(Predicados().Select(p => p.Tabla).Distinct().Where(t => !clasificadas.Contains(t)));
        }
    }
}
