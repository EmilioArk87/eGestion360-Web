using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>
    /// Paso F1.3: quién corre en ámbito de plataforma (sin el filtro global por tenant de EF). La lista es corta y revisada;
    /// estas pruebas la fijan para que ampliarla sea una decisión visible, y vigilan que nadie se salte el filtro por otro
    /// camino (<c>IgnoreQueryFilters</c> o un contexto armado a mano en el código de la aplicación).
    /// </summary>
    public class AmbitoDePlataformaTests
    {
        [Fact]
        public void La_lista_de_pantallas_sin_sesion_es_la_revisada()
            => Assert.Equal(new[] { "/Login", "/ForgotPassword", "/ResetPassword", "/Register" }, AmbitoDePlataformaPageFilter.RutasSinSesion);

        [Fact]
        public void La_lista_de_pantallas_del_administrador_general_es_la_revisada()
            => Assert.Equal(
                new[]
                {
                    "/Empresas", "/UserManagement", "/Catalogos/TiposCambio",
                    "/Admin/Soporte", "/Admin/Modulos", "/Admin/Outbox", "/Admin/EmailConfig", "/Admin/ResetPassword"
                },
                AmbitoDePlataformaPageFilter.RutasDelAdministradorGeneral);

        [Fact]
        public void Las_pantallas_que_trabajan_con_datos_de_un_tenant_no_son_de_plataforma()
        {
            // Con la política RLS encendida la plataforma no ve datos de tenant (ADR-023, D36): esas pantallas trabajan dentro del
            // tenant de soporte y conservan el filtro de EF de ese tenant.
            foreach (var ruta in TenantDeSoporteRequeridoPageFilter.RutasQueExigenTenantDeSoporte)
            {
                Assert.False(AmbitoDePlataformaPageFilter.EsRutaDelAdministradorGeneral(ruta + "/Index"), ruta);
                Assert.False(AmbitoDePlataformaPageFilter.EsRutaSinSesion(ruta + "/Index"), ruta);
            }
        }

        [Fact]
        public void Cada_ruta_de_la_lista_existe_en_Pages()
        {
            var pages = Path.Combine(RaizDelRepositorio(), "Pages");
            foreach (var ruta in AmbitoDePlataformaPageFilter.RutasSinSesion.Concat(AmbitoDePlataformaPageFilter.RutasDelAdministradorGeneral))
            {
                var relativa = ruta.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                Assert.True(File.Exists(Path.Combine(pages, relativa + ".cshtml")) || Directory.Exists(Path.Combine(pages, relativa)),
                    $"La ruta {ruta} de la lista de plataforma no existe en Pages.");
            }
        }

        [Theory]
        [InlineData("/Empresas/Index", true)]
        [InlineData("/Empresas", true)]
        [InlineData("/Admin/Personas/Index", false)]
        [InlineData("/Admin/EmpresaRoles/Permisos", false)]
        [InlineData("/Admin/Soporte/Index", true)]
        [InlineData("/Admin/Outbox/Index", true)]
        [InlineData("/Admin/Modulos/Index", true)]
        [InlineData("/UserManagement", true)]
        [InlineData("/Catalogos/TiposCambio/Configuracion", true)]
        [InlineData("/empresas/index", true)]
        [InlineData("/Catalogos/Clientes/Index", false)]
        [InlineData("/Catalogos/Index", false)]
        [InlineData("/Flota/Operacion/Peajes/Index", false)]
        [InlineData("/EmpresasOtraCosa", false)]
        [InlineData("/AdminX/Index", false)]
        [InlineData("/MainMenu", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Solo_las_rutas_de_la_lista_son_del_administrador_general(string? ruta, bool esperado)
            => Assert.Equal(esperado, AmbitoDePlataformaPageFilter.EsRutaDelAdministradorGeneral(ruta));

        [Theory]
        [InlineData("/Login", false, null, true)]
        [InlineData("/ForgotPassword", false, null, true)]
        [InlineData("/Empresas/Index", true, "admin", true)]
        [InlineData("/Admin/Soporte/Index", true, "admin", true)]
        [InlineData("/Admin/Personas/Index", true, "admin", false)]
        [InlineData("/Admin/Soporte/Index", true, "empresa_admin", false)]
        [InlineData("/Empresas/Index", true, "empresa_admin", false)]
        [InlineData("/Admin/EmpresaRoles/Index", true, "empresa_admin", false)]
        [InlineData("/Admin/EmpresaRoles/Index", true, "empresa_user", false)]
        [InlineData("/Empresas/Index", false, null, false)]
        [InlineData("/Flota/Catalogos/Vehiculos/Index", true, "admin", false)]
        [InlineData("/Catalogos/Clientes/Index", true, "admin", false)]
        public void El_filtro_activa_el_ambito_solo_donde_corresponde(string ruta, bool autenticado, string? rol, bool esperado)
        {
            var http = SesionFalsa.Contexto(rol, autenticado: autenticado);

            Ejecutar(ruta, http);

            Assert.Equal(esperado, http.Items.TryGetValue(TenantMiddleware.ClaveEsPlataforma, out var valor) && valor is true);
        }

        [Fact]
        public void El_contexto_de_tenant_lee_la_marca_de_plataforma_de_la_peticion()
        {
            var http = new DefaultHttpContext();
            var contexto = new TenantContext(new HttpContextAccessor { HttpContext = http });
            Assert.False(contexto.EsPlataforma);

            http.Items[TenantMiddleware.ClaveEsPlataforma] = true;

            Assert.True(contexto.EsPlataforma);
        }

        [Fact]
        public void Un_proceso_de_fondo_fija_su_tenant_o_la_plataforma_a_mano()
        {
            var http = new DefaultHttpContext();
            http.Items[TenantMiddleware.ClaveIdTenant] = 7;
            var contexto = new TenantContext(new HttpContextAccessor { HttpContext = http });
            Assert.Equal(7, contexto.IdTenant);

            contexto.FijarPlataforma();
            Assert.True(contexto.EsPlataforma);
            Assert.Null(contexto.IdTenant);

            contexto.Fijar(3, 12);
            Assert.False(contexto.EsPlataforma);
            Assert.Equal(3, contexto.IdTenant);
            Assert.Equal(12, contexto.IdEmpresa);
        }

        [Fact]
        public void El_codigo_de_la_aplicacion_no_usa_IgnoreQueryFilters_ni_arma_su_propio_contexto()
        {
            var raiz = RaizDelRepositorio();
            var carpetas = new[] { "Pages", "Services", "Data", "Models" };
            var archivos = carpetas.SelectMany(c => Directory.EnumerateFiles(Path.Combine(raiz, c), "*.cs", SearchOption.AllDirectories))
                .Append(Path.Combine(raiz, "Program.cs")).ToList();

            // Solo se admite ignorar el filtro de BORRADO (ADR-022), nombrándolo; ignorar el de tenant o todos a la vez, nunca.
            var permitido = new Regex(@"IgnoreQueryFilters\(\s*(\[|new\[\]\s*\{)\s*ApplicationDbContext\.FiltroBorrado\s*(\]|\})\s*\)");
            var conIgnorar = archivos.Where(f => File.ReadAllText(f) is var texto
                    && Regex.Matches(texto, "IgnoreQueryFilters").Count != permitido.Matches(texto).Count)
                .Select(f => Path.GetRelativePath(raiz, f)).ToList();
            var conContextoPropio = archivos.Where(f => File.ReadAllText(f).Contains("new ApplicationDbContext(")).Select(f => Path.GetRelativePath(raiz, f)).ToList();

            Assert.True(conIgnorar.Count == 0, "El ámbito de plataforma se declara en AmbitoDePlataformaPageFilter; solo se puede ignorar el filtro de borrado (ApplicationDbContext.FiltroBorrado): " + string.Join(", ", conIgnorar));
            Assert.True(conContextoPropio.Count == 0, "Un contexto armado a mano no lleva el filtro por tenant; pídelo por inyección: " + string.Join(", ", conContextoPropio));
        }

        private static void Ejecutar(string ruta, HttpContext http)
        {
            var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = ruta, RelativePath = ruta + ".cshtml" };
            var pageContext = new PageContext(new ActionContext(http, new RouteData(), descriptor));
            var contexto = new PageHandlerExecutingContext(
                pageContext, Array.Empty<IFilterMetadata>(), new HandlerMethodDescriptor(),
                new Dictionary<string, object?>(), new object());

            new AmbitoDePlataformaPageFilter().OnPageHandlerExecuting(contexto);
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
