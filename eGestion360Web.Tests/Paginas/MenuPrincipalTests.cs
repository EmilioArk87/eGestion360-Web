using System.Reflection;
using System.Text.RegularExpressions;
using eGestion360Web.Pages.Admin.Invitaciones;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El menú principal (<c>Pages/MainMenu.cshtml</c>). Hasta el ensayo general de F8 (2026-11-16) tenía una rama aparte para el
    /// administrador de empresa que solo mostraba la tarjeta provisional «Pago de Servicio» y escondía las tarjetas de los módulos: el
    /// administrador de CAPAE entraba a un menú vacío aunque Psicología le abría escribiendo la dirección (hallazgo D22). Estas pruebas leen la
    /// vista y exigen que las tarjetas de módulo dependan solo del módulo y que el administrador de empresa tenga su sección de administración.
    /// </summary>
    public class MenuPrincipalTests
    {
        private static readonly Regex CondicionDeRol = new(
            @"@if\s*\(\s*Model\.(?<rol>IsAdmin|IsEmpresaAdmin)\s*\)", RegexOptions.CultureInvariant);

        /// <summary>Una rama por rol: <c>Inicio</c>/<c>Fin</c> son sus llaves; <c>FinElse</c>, la llave que cierra su <c>else</c> si lo tiene.</summary>
        private sealed record BloqueDeRol(string Rol, int Inicio, int Fin, int? FinElse)
        {
            public bool TieneElse => FinElse.HasValue;

            /// <summary>Lo que depende del rol: el cuerpo y, si lo hay, el <c>else</c> (lo que el rol NO ve).</summary>
            public bool Contiene(int posicion) => posicion > Inicio && posicion < (FinElse ?? Fin);
        }

        [Fact]
        public void Ninguna_rama_por_rol_reemplaza_al_menu_de_modulos()
        {
            var conElse = Bloques(Vista()).Where(b => b.TieneElse).Select(b => b.Rol).ToList();

            Assert.True(conElse.Count == 0,
                "Una rama @if (Model.<rol>) con else le cambia el menú entero a ese rol; las tarjetas de módulo deben verse para todos: "
                + string.Join(", ", conElse));
        }

        [Theory]
        [InlineData("flota")]
        [InlineData("catalogos")]
        [InlineData("facturacion")]
        [InlineData("psicologia")]
        public void La_tarjeta_de_cada_modulo_solo_depende_del_modulo(string modulo)
        {
            var vista = Vista();
            var marca = $"Model.TieneModulo(\"{modulo}\")";
            var posicion = vista.IndexOf(marca, StringComparison.Ordinal);

            Assert.True(posicion >= 0, $"El menú no tiene la tarjeta del módulo {modulo} ({marca}).");
            var dentro = Bloques(vista).Where(b => b.Contiene(posicion)).Select(b => b.Rol).ToList();
            Assert.True(dentro.Count == 0,
                $"La tarjeta de {modulo} está dentro de una rama por rol ({string.Join(", ", dentro)}): un rol no la vería aunque su empresa tenga el módulo.");
        }

        [Fact]
        public void El_administrador_de_empresa_tiene_la_tarjeta_de_invitaciones_sin_enlaces_vacios()
        {
            var vista = Vista();
            var deEmpresa = Bloques(vista).Where(b => b.Rol == "IsEmpresaAdmin")
                                          .Select(b => vista[b.Inicio..b.Fin]).ToList();

            Assert.Contains(deEmpresa, cuerpo => cuerpo.Contains("asp-page=\"/Admin/Invitaciones/Index\"", StringComparison.Ordinal));
            Assert.DoesNotContain(deEmpresa, cuerpo => cuerpo.Contains("href=\"#\"", StringComparison.Ordinal));
        }

        [Fact]
        public void Las_invitaciones_las_abre_cualquier_administrador_de_empresa_sin_rol()
        {
            // La tarjeta se muestra a todo administrador de empresa; la pantalla tiene que dejarlo entrar sin depender de un rol ni de un módulo.
            var atributo = typeof(IndexModel).GetCustomAttribute<RequierePermisoAttribute>();

            Assert.NotNull(atributo);
            var permiso = CatalogoDePermisos.Buscar(atributo!.Codigo);
            Assert.NotNull(permiso);
            Assert.Equal(AmbitoDePermiso.Empresa, permiso!.Ambito);
        }

        /// <summary>Cada <c>@if (Model.IsAdmin)</c> o <c>@if (Model.IsEmpresaAdmin)</c> con el tramo entre sus llaves y el de su <c>else</c>, si lo tiene.</summary>
        private static List<BloqueDeRol> Bloques(string vista)
        {
            var bloques = new List<BloqueDeRol>();
            foreach (Match m in CondicionDeRol.Matches(vista))
            {
                var (abre, cierra) = Llaves(vista, m.Index + m.Length, m.Value);
                int? finElse = null;
                var siguiente = Regex.Match(vista[(cierra + 1)..], @"^\s*else\b");
                if (siguiente.Success)
                    finElse = Llaves(vista, cierra + 1 + siguiente.Length, m.Value + " else").Cierra;
                bloques.Add(new BloqueDeRol(m.Groups["rol"].Value, abre, cierra, finElse));
            }
            return bloques;
        }

        /// <summary>La primera llave que abre desde <paramref name="desde"/> y la que la cierra, contando las de adentro.</summary>
        private static (int Abre, int Cierra) Llaves(string vista, int desde, string donde)
        {
            var abre = vista.IndexOf('{', desde);
            Assert.True(abre >= 0, "Rama sin llave de apertura en " + donde);
            var nivel = 0;
            for (var i = abre; i < vista.Length; i++)
            {
                if (vista[i] == '{') nivel++;
                else if (vista[i] == '}' && --nivel == 0) return (abre, i);
            }
            Assert.Fail("Rama sin llave de cierre en " + donde);
            return default;
        }

        private static string Vista() => File.ReadAllText(Path.Combine(RaizDelRepositorio(), "Pages", "MainMenu.cshtml"));

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
