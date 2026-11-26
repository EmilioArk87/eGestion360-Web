using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Services.Navegacion;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El menú principal (<c>Pages/MainMenu.cshtml</c>), que desde el menú configurable es el hub: buscador, accesos frecuentes, filtros y una tarjeta
    /// por módulo. Hasta el ensayo general de F8 (2026-11-16) tenía una rama aparte para el administrador de empresa (hallazgo D22); desde F1.6e no
    /// decide por el rol de nadie. Ahora tampoco decide tarjeta por tarjeta: todo sale del catálogo de navegación, que pregunta por el módulo y por
    /// el permiso de cada pantalla (sus reglas se prueban en <c>CatalogoDeNavegacionTests</c>).
    /// </summary>
    public class MenuPrincipalTests
    {
        [Fact]
        public void El_menu_ya_no_decide_por_el_rol()
        {
            var vista = Vista();

            Assert.DoesNotMatch(new Regex(@"Model\.(IsAdmin|IsEmpresaAdmin)\b"), vista);
            Assert.Null(typeof(MainMenuModel).GetProperty("IsAdmin"));
            Assert.Null(typeof(MainMenuModel).GetProperty("IsEmpresaAdmin"));
        }

        [Fact]
        public void Las_tarjetas_salen_del_catalogo_de_navegacion_y_no_de_una_lista_propia()
        {
            var vista = Vista();

            Assert.Contains("@foreach (var m in Model.Menu.Modulos)", vista, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"#\"", vista, StringComparison.Ordinal);                 // nada de enlaces vacíos («Próximamente» no es un enlace)
            Assert.DoesNotContain("MostrarModulo(", vista, StringComparison.Ordinal);
            Assert.Null(typeof(MainMenuModel).GetMethod("MostrarModulo"));
        }

        [Fact]
        public void El_catalogo_tiene_todos_los_modulos_y_pantallas_de_administracion_que_tenia_el_menu()
        {
            foreach (var modulo in new[] { "flota", "catalogos", "facturacion", "psicologia", "bancos", "contabilidad", "inventario", "ventas", "reportes" })
                Assert.Equal(modulo, ModeloNavegacion.Modulo(modulo)?.CodigoModulo);

            var rutas = ModeloNavegacion.Modulos.SelectMany(m => m.Pantallas).Select(p => p.Ruta).ToHashSet();
            foreach (var pantalla in new[]
                     {
                         "/Admin/Invitaciones/Index", "/Admin/RolesPermisos/Index", "/Empresas/Index", "/Admin/Soporte/Index", "/Admin/Personas/Index",
                         "/Admin/Modulos/Index", "/Admin/Outbox/Index", "/Admin/EmailConfig", "/UserManagement",
                     })
                Assert.Contains(pantalla, rutas);
        }

        [Fact]
        public void Las_invitaciones_las_abre_cualquier_administrador_de_empresa_sin_rol()
        {
            // La tarjeta se muestra a quien tiene el permiso; el administrador de empresa lo recibe sin rol y la pantalla no puede depender de un rol ni de un módulo.
            var atributo = typeof(eGestion360Web.Pages.Admin.Invitaciones.IndexModel).GetCustomAttribute<RequierePermisoAttribute>();

            Assert.NotNull(atributo);
            var permiso = CatalogoDePermisos.Buscar(atributo!.Codigo);
            Assert.NotNull(permiso);
            Assert.Equal(AmbitoDePermiso.Empresa, permiso!.Ambito);
            Assert.Equal(GrupoDeNavegacion.Empresa, ModeloNavegacion.Modulos.Single(m => m.Pantallas.Any(p => p.Ruta == "/Admin/Invitaciones/Index")).Grupo);
        }

        // ── El modelo ──────────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class NavegacionFija : INavegacionService
        {
            private readonly MenuDeNavegacion _menu;
            public NavegacionFija(MenuDeNavegacion menu) => _menu = menu;
            public Task<MenuDeNavegacion> ObtenerAsync() => Task.FromResult(_menu);
        }

        [Fact]
        public async Task El_modelo_entrega_el_menu_del_catalogo()
        {
            var menu = new MenuDeNavegacion(new[] { ModeloNavegacion.Modulo("flota")! });
            var modelo = new MainMenuModel(new NavegacionFija(menu))
            {
                PageContext = new PageContext(new ActionContext(SesionFalsa.Contexto("empresa_user", "1"), new RouteData(), new CompiledPageActionDescriptor())),
            };

            Assert.IsType<PageResult>(await modelo.OnGetAsync());
            Assert.Same(menu, modelo.Menu);
        }

        [Fact]
        public async Task Sin_sesion_el_menu_manda_a_entrar()
        {
            var modelo = new MainMenuModel(new NavegacionFija(MenuDeNavegacion.Vacio))
            {
                PageContext = new PageContext(new ActionContext(SesionFalsa.Contexto(autenticado: false), new RouteData(), new CompiledPageActionDescriptor())),
            };

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await modelo.OnGetAsync()).PageName);
        }

        [Fact]
        public void El_filtro_del_hub_encuentra_un_modulo_por_sus_pantallas_y_sus_palabras()
        {
            var texto = MainMenuModel.TextoDeBusqueda(ModeloNavegacion.Modulo("flota")!);

            Assert.Contains("Control de salidas", texto, StringComparison.Ordinal);
            Assert.Contains("garita", texto, StringComparison.Ordinal);
            Assert.Equal("empresa", MainMenuModel.Categoria(GrupoDeNavegacion.Empresa));
            Assert.Equal("sistema", MainMenuModel.Categoria(GrupoDeNavegacion.Plataforma));
            Assert.Equal("operacion", MainMenuModel.Categoria(GrupoDeNavegacion.Operacion));
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
