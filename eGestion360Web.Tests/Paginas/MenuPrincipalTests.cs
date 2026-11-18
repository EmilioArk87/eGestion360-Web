using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Pages;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El menú principal (<c>Pages/MainMenu.cshtml</c>). Hasta el ensayo general de F8 (2026-11-16) tenía una rama aparte para el administrador
    /// de empresa que escondía las tarjetas de los módulos (hallazgo D22). Desde F1.6e el menú ya no decide por el rol de nadie: cada tarjeta
    /// pregunta lo que pregunta la pantalla a la que lleva (<c>Puede</c>) y las de módulo, además, que el módulo esté activo. Estas pruebas leen
    /// la vista y ejercitan el modelo.
    /// </summary>
    public class MenuPrincipalTests
    {
        private static readonly string[] Modulos = { "flota", "catalogos", "facturacion", "psicologia", "bancos", "contabilidad", "inventario", "ventas", "reportes" };

        [Fact]
        public void El_menu_ya_no_decide_por_el_rol()
        {
            var vista = Vista();

            Assert.DoesNotMatch(new Regex(@"Model\.(IsAdmin|IsEmpresaAdmin)\b"), vista);
            Assert.Null(typeof(MainMenuModel).GetProperty("IsAdmin"));
            Assert.Null(typeof(MainMenuModel).GetProperty("IsEmpresaAdmin"));
        }

        [Theory]
        [InlineData("flota")]
        [InlineData("catalogos")]
        [InlineData("facturacion")]
        [InlineData("psicologia")]
        public void La_tarjeta_de_cada_modulo_pregunta_por_el_modulo_y_por_los_permisos(string modulo)
        {
            Assert.Contains($"@if (Model.MostrarModulo(\"{modulo}\"))", Vista(), StringComparison.Ordinal);
        }

        [Fact]
        public void Todos_los_modulos_del_menu_pasan_por_la_misma_pregunta()
        {
            var vista = Vista();

            foreach (var modulo in Modulos)
                Assert.Contains($"Model.MostrarModulo(\"{modulo}\")", vista, StringComparison.Ordinal);
            Assert.DoesNotContain("Model.TieneModulo(", vista, StringComparison.Ordinal);
        }

        [Fact]
        public void Cada_tarjeta_de_administracion_lleva_la_pantalla_que_decide_si_se_ve()
        {
            var vista = Vista();

            foreach (var pantalla in new[]
                     {
                         "/Admin/Invitaciones/Index", "/Admin/RolesPermisos/Index", "/Empresas/Index", "/Admin/Soporte/Index", "/Admin/Personas/Index",
                         "/Admin/Modulos/Index", "/Admin/Outbox/Index", "/Admin/EmailConfig", "/UserManagement",
                     })
                Assert.Contains($"eg-si-puede-abrir=\"{pantalla}\"", vista, StringComparison.Ordinal);
        }

        [Fact]
        public void La_administracion_de_la_empresa_tiene_la_tarjeta_de_invitaciones_sin_enlaces_vacios()
        {
            var vista = Vista();
            var inicio = vista.IndexOf("@if (Model.PuedeAdministrarLaEmpresa)", StringComparison.Ordinal);

            Assert.True(inicio >= 0, "El menú no tiene la sección de administración de la empresa.");
            var fin = vista.IndexOf("@if (Model.PuedeAdministrarElSistema)", inicio, StringComparison.Ordinal);
            var seccion = vista[inicio..fin];
            Assert.Contains("asp-page=\"/Admin/Invitaciones/Index\"", seccion, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"#\"", seccion, StringComparison.Ordinal);
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
        }

        // ── El modelo ──────────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class PermisosFalsos : IPermisosDeLaPeticion
        {
            private readonly PermisosEfectivos _permisos;
            public PermisosFalsos(params string[] codigos) => _permisos = new PermisosEfectivos(new HashSet<string>(codigos, StringComparer.Ordinal));
            public Task<PermisosEfectivos> ObtenerAsync() => Task.FromResult(_permisos);
        }

        private static async Task<MainMenuModel> Abrir(string[] permisos, params string[] modulosActivos)
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Session.SetString("Modulos", System.Text.Json.JsonSerializer.Serialize(modulosActivos));
            var modelo = new MainMenuModel(new PermisosFalsos(permisos))
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor())),
            };
            Assert.IsType<PageResult>(await modelo.OnGetAsync());
            return modelo;
        }

        [Fact]
        public async Task Un_modulo_activo_se_muestra_a_quien_puede_hacer_algo_en_el_y_se_oculta_a_quien_no()
        {
            var conPermiso = await Abrir(new[] { "flota.vehiculos.ver" }, "flota", "catalogos");

            Assert.True(conPermiso.MostrarModulo("flota"));
            Assert.False(conPermiso.MostrarModulo("catalogos"));   // el módulo está activo, pero su rol no hace nada en él
        }

        [Fact]
        public async Task Un_modulo_inactivo_no_se_muestra_aunque_la_persona_tenga_permisos_de_el()
        {
            var modelo = await Abrir(new[] { "facturacion.factura.ver" }, "flota");

            Assert.False(modelo.MostrarModulo("facturacion"));
        }

        [Fact]
        public async Task Un_modulo_sin_permisos_en_el_catalogo_se_muestra_por_estar_activo()
        {
            // Bancos, contabilidad… todavía no tienen pantallas ni permisos: su tarjeta («Próximamente») depende solo del módulo.
            var modelo = await Abrir(Array.Empty<string>(), "bancos");

            Assert.True(modelo.MostrarModulo("bancos"));
        }

        [Fact]
        public async Task Los_encabezados_de_administracion_salen_solo_a_quien_tiene_permisos_de_ese_ambito()
        {
            var deEmpresa = await Abrir(new[] { "administracion.roles.ver" });
            var deSistema = await Abrir(new[] { "plataforma.soporte.ver" });
            var ninguno = await Abrir(new[] { "flota.vehiculos.ver" }, "flota");

            Assert.True(deEmpresa.PuedeAdministrarLaEmpresa);
            Assert.False(deEmpresa.PuedeAdministrarElSistema);
            Assert.True(deSistema.PuedeAdministrarElSistema);
            Assert.False(deSistema.PuedeAdministrarLaEmpresa);
            Assert.False(ninguno.PuedeAdministrarLaEmpresa);
            Assert.False(ninguno.PuedeAdministrarElSistema);
        }

        [Fact]
        public async Task El_modelo_responde_la_misma_pregunta_que_el_filtro_de_permisos()
        {
            var modelo = await Abrir(new[] { "plataforma.usuarios.ver" });

            Assert.True(modelo.Puede("plataforma.usuarios.ver"));
            Assert.False(modelo.Puede("plataforma.empresas.ver"));
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
