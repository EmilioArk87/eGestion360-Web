using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Lo que se retiró en F1.6e-3 (ADR-029, D63) no vuelve: ninguna pantalla ni servicio decide por el texto del rol (<c>IsAdmin</c> y
    /// compañía), por las cuatro casillas viejas (<c>AuthHelper.PuedeX</c>) ni por una copia de los permisos en la sesión, y el filtro
    /// <c>AdminOnlyPageFilter</c> y el cargador de permisos ya no existen. Quién puede qué lo decide el catálogo de permisos.
    /// </summary>
    public class RolesRetiradosTests
    {
        private static readonly string Raiz = RaizDelRepositorio();

        // Lo que ya no debe aparecer en el código (los comentarios que cuentan la historia no cuentan).
        private static readonly (string Nombre, Regex Patron)[] Prohibido =
        {
            ("IsAdmin / IsEmpresaAdmin / IsAnyAdmin / IsEmpresaUser (el rol de texto decidía)", new Regex(@"\bIs(Admin|EmpresaAdmin|AnyAdmin|EmpresaUser)\(", RegexOptions.CultureInvariant)),
            ("AuthHelper.PuedeVer/Crear/Editar/Eliminar (las casillas viejas)", new Regex(@"\bPuede(Ver|Crear|Editar|Eliminar)\(\s*(HttpContext|http|context)\b", RegexOptions.CultureInvariant)),
            ("GetPermisos / GetModulos (la copia de la sesión)", new Regex(@"AuthHelper\.(GetPermisos|GetModulos)\(", RegexOptions.CultureInvariant)),
            ("Session Modulos / Permisos (la copia de la sesión)", new Regex(@"Session\.(Get|Set)String\(""(Modulos|Permisos)""", RegexOptions.CultureInvariant)),
            ("AdminOnlyPageFilter", new Regex(@"\bAdminOnlyPageFilter\b", RegexOptions.CultureInvariant)),
            ("CargadorDePermisos", new Regex(@"\bI?CargadorDePermisos\b", RegexOptions.CultureInvariant)),
        };

        private static string[] CodigoDeProduccion() => new[] { "Pages", "Services", "Data", "Models" }
            .Select(c => Path.Combine(Raiz, c))
            .Where(Directory.Exists)
            .SelectMany(c => Directory.EnumerateFiles(c, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
            .Append(Path.Combine(Raiz, "Program.cs"))
            .ToArray();

        /// <summary>Las líneas de código: sin los comentarios de línea completa (// y ///), que pueden recordar lo que se retiró.</summary>
        private static IEnumerable<(int Numero, string Linea)> Codigo(string archivo)
        {
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                var recortada = lineas[i].TrimStart();
                if (recortada.StartsWith("//", StringComparison.Ordinal) || recortada.StartsWith("@*", StringComparison.Ordinal)) continue;
                yield return (i + 1, lineas[i]);
            }
        }

        [Fact]
        public void Ninguna_pantalla_ni_servicio_decide_por_el_texto_del_rol_las_casillas_viejas_o_la_copia_de_la_sesion()
        {
            var encontrados = new List<string>();
            foreach (var archivo in CodigoDeProduccion())
                foreach (var (numero, linea) in Codigo(archivo))
                    foreach (var (nombre, patron) in Prohibido)
                        if (patron.IsMatch(linea))
                            encontrados.Add($"{Path.GetRelativePath(Raiz, archivo)}:{numero}  {nombre}");

            Assert.True(encontrados.Count == 0,
                "Esto se retiró en F1.6e-3; pregunta con el catálogo de permisos (RequierePermiso, this.Puede) o con AuthHelper.EsOperador / PuedeActuarSobreEmpresa:\n  "
                + string.Join("\n  ", encontrados));
        }

        [Theory]
        [InlineData("Services/AdminOnlyPageFilter.cs")]
        [InlineData("Services/Identidad/CargadorDePermisos.cs")]
        public void Los_archivos_retirados_no_existen(string relativa)
            => Assert.False(File.Exists(Path.Combine(Raiz, relativa.Replace('/', Path.DirectorySeparatorChar))), relativa + " se retiró en F1.6e-3");

        [Fact]
        public void Las_cuatro_pantallas_de_diagnostico_que_cerraba_AdminOnlyPageFilter_exigen_permiso_de_plataforma()
        {
            // Sin sesión las cierra PermisosPageFilter (una pantalla que exige permiso exige sesión): por eso cada una tiene que declarar uno.
            foreach (var (archivo, permiso) in new[]
                     {
                         ("Pages/DebugUsers.cshtml.cs", "plataforma.diagnostico.ver"), ("Pages/ResetCodesHistory.cshtml.cs", "plataforma.diagnostico.ver"),
                         ("Pages/ValidarEmails.cshtml.cs", "plataforma.diagnostico.ver"), ("Pages/Admin/EmailConfig.cshtml.cs", "plataforma.correo.ver"),
                     })
            {
                var texto = File.ReadAllText(Path.Combine(Raiz, archivo.Replace('/', Path.DirectorySeparatorChar)));
                Assert.Contains($"[RequierePermiso(\"{permiso}\")]", texto);
            }
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
