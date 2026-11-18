using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Menú, tarjetas y botones preguntan lo mismo que cada pantalla (paso F1.6e-2, ADR-029). Estas pruebas leen las vistas y vigilan que el
    /// mecanismo (los tag helpers <c>eg-si-puede-abrir</c> y los enlaces con <c>asp-page</c>) se use bien y que no vuelvan las banderas por rol.
    /// </summary>
    public class MenusYBotonesTests
    {
        private static readonly string Raiz = RaizDelRepositorio();

        private static readonly string[] CentrosDeNavegacion =
        {
            "Pages/Flota/Index.cshtml", "Pages/Flota/Operacion/Index.cshtml", "Pages/Flota/Gastos/Index.cshtml",
            "Pages/Flota/Catalogos/Index.cshtml", "Pages/Flota/MantenimientoDatos/Index.cshtml", "Pages/Catalogos/Index.cshtml",
            "Pages/Psicologia/Index.cshtml", "Pages/MainMenu.cshtml",
        };

        // Una tarjeta de un centro: el contenedor «col» seguido de la tarjeta «menu-card».
        private static readonly Regex Tarjeta = new(@"<div class=""col[^""]*""(?<atributos>[^>]*)>\s*<div class=""card[^""]*menu-card", RegexOptions.CultureInvariant);

        private static string Leer(string relativa) => File.ReadAllText(Path.Combine(Raiz, relativa.Replace('/', Path.DirectorySeparatorChar)));

        [Fact]
        public void Cada_tarjeta_de_un_centro_de_navegacion_decide_si_se_muestra()
        {
            var sinMarca = new List<string>();
            foreach (var archivo in CentrosDeNavegacion)
            {
                // Las tarjetas de módulo del menú principal se deciden con MostrarModulo (un @if), no con eg-si-puede-abrir.
                var texto = Leer(archivo);
                foreach (Match m in Tarjeta.Matches(texto))
                {
                    var antes = texto[..m.Index];
                    var condicionada = archivo.EndsWith("MainMenu.cshtml", StringComparison.Ordinal)
                                       && Regex.IsMatch(antes[Math.Max(0, antes.Length - 400)..], @"@if \(Model\.\w+\([^)]*\)\)\s*\{\s*$");
                    if (!m.Groups["atributos"].Value.Contains("eg-si-puede-abrir", StringComparison.Ordinal) && !condicionada)
                        sinMarca.Add($"{archivo} (tarjeta en el carácter {m.Index})");
                }
            }

            Assert.True(sinMarca.Count == 0, "Estas tarjetas no deciden si se muestran (agrega eg-si-puede-abrir con la pantalla a la que llevan):\n  " + string.Join("\n  ", sinMarca));
        }

        [Fact]
        public void Las_pantallas_que_nombra_eg_si_puede_abrir_existen()
        {
            var rutas = PantallasExistentes();
            var inexistentes = new List<string>();
            foreach (var archivo in Directory.EnumerateFiles(Path.Combine(Raiz, "Pages"), "*.cshtml", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(archivo), @"eg-si-puede-abrir=""(?<valor>[^""@]+)"""))
                    foreach (var destino in m.Groups["valor"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!rutas.Contains(destino) && !rutas.Contains(destino.TrimEnd('/') + "/Index"))
                            inexistentes.Add($"{Path.GetRelativePath(Raiz, archivo)}: {destino}");
            }

            Assert.True(inexistentes.Count == 0, "eg-si-puede-abrir apunta a pantallas que no existen:\n  " + string.Join("\n  ", inexistentes));
        }

        [Fact]
        public void Los_centros_de_navegacion_ya_no_deciden_por_el_rol()
        {
            var culpables = CentrosDeNavegacion.Append("Pages/Shared/_NavigationPartial.cshtml")
                .Where(a => Regex.IsMatch(Leer(a), @"AuthHelper\.(IsAdmin|IsAnyAdmin|IsEmpresaAdmin)\(|Model\.(IsAdmin|IsEmpresaAdmin)\b"))
                .ToList();

            Assert.True(culpables.Count == 0, "Estas vistas deciden qué mostrar por el rol y no por el permiso: " + string.Join(", ", culpables));
        }

        [Fact]
        public void Los_botones_de_las_pantallas_de_modulo_ya_no_leen_las_casillas_viejas()
        {
            // Las banderas PuedeCrear/PuedeEditar/TienePermisoCrear salen del catálogo (this.Puede), no de AuthHelper.PuedeX.
            var culpables = Directory.EnumerateFiles(Path.Combine(Raiz, "Pages"), "*.cs", SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"AuthHelper\.Puede(Ver|Crear|Editar|Eliminar)\("))
                .Select(f => Path.GetRelativePath(Raiz, f))
                .ToList();

            Assert.True(culpables.Count == 0, "Estas pantallas aún leen las casillas viejas para mostrar botones: " + string.Join(", ", culpables));
        }

        /// <summary>Las rutas de Razor Pages que existen: cada <c>.cshtml</c> con <c>@page</c> bajo <c>Pages/</c>.</summary>
        private static HashSet<string> PantallasExistentes()
        {
            var carpeta = Path.Combine(Raiz, "Pages");
            return Directory.EnumerateFiles(carpeta, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_') && File.ReadLines(f).Any(l => l.TrimStart().StartsWith("@page", StringComparison.Ordinal)))
                .Select(f => "/" + Path.GetRelativePath(carpeta, f).Replace(Path.DirectorySeparatorChar, '/')[..^".cshtml".Length])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string RaizDelRepositorio()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }
    }
}
