using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages.Preferencias;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El selector «Estilo de menú» (etapa 4 del plan secundario): guarda la preferencia SOLO en la cuenta de la cookie (saas.cuentas no tiene RLS),
    /// solo acepta los estilos de la lista blanca, vuelve a emitir la cookie con el estilo nuevo sin cerrar la sesión y nunca redirige fuera del sitio.
    /// </summary>
    public sealed class EstiloMenuTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private sealed class AutenticacionEspia : IAuthenticationService
        {
            public ClaimsPrincipal? Emitida { get; private set; }
            public AuthenticationProperties? Propiedades { get; private set; }

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
            public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            {
                Emitida = principal;
                Propiedades = properties;
                return Task.CompletedTask;
            }
        }

        private sealed class TempDataLocal : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private (EstiloMenuModel Pagina, AutenticacionEspia Autenticacion, HttpContext Http) Pagina(int idCuenta, string estiloEnCookie = EstilosDeMenu.Hub)
        {
            var autenticacion = new AutenticacionEspia();
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa() with { IdCuenta = idCuenta, EstiloMenu = estiloEnCookie });
            http.RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(autenticacion).BuildServiceProvider();
            var pagina = new EstiloMenuModel(_bd.Crear(), new RelojFijo(IdentidadDePrueba.Ahora))
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor())),
            };
            pagina.TempData = new TempDataDictionary(http, new TempDataLocal());
            return (pagina, autenticacion, http);
        }

        private string EstiloGuardado(int idCuenta)
        {
            using var db = _bd.Crear();
            return db.Cuentas.AsNoTracking().Single(c => c.IdCuenta == idCuenta).EstiloMenu;
        }

        [Fact]
        public async Task Cambiar_el_estilo_actualiza_solo_la_cuenta_de_la_cookie_y_renueva_la_cookie()
        {
            int propia, ajena;
            using (var db = _bd.Crear())
            {
                propia = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local", usuario: "ana").IdCuenta;
                ajena = IdentidadDePrueba.NuevaCuenta(db, "beto@prueba.local", usuario: "beto").IdCuenta;
            }
            var (pagina, autenticacion, http) = Pagina(propia);

            var resultado = await pagina.OnPostAsync(EstilosDeMenu.Sidebar, "/Flota/Index?pagina=2");

            Assert.Equal("/Flota/Index?pagina=2", Assert.IsType<LocalRedirectResult>(resultado).Url);
            Assert.Equal(EstilosDeMenu.Sidebar, EstiloGuardado(propia));
            Assert.Equal(EstilosDeMenu.Hub, EstiloGuardado(ajena));

            // La cookie nueva trae el estilo nuevo y todo lo demás igual: no se cierra la sesión.
            var emitida = ReclamosDeIdentidad.Leer(autenticacion.Emitida);
            Assert.NotNull(emitida);
            Assert.Equal(EstilosDeMenu.Sidebar, emitida!.EstiloMenu);
            Assert.Equal(DatosDeIdentidadDePrueba.Empresa() with { IdCuenta = propia, EstiloMenu = EstilosDeMenu.Sidebar }, emitida);
            Assert.Equal(EstilosDeMenu.Sidebar, eGestion360Web.Services.AuthHelper.GetEstiloMenu(http));
        }

        [Theory]
        [InlineData("rojo")]
        [InlineData("SIDEBAR")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("hub'); DROP TABLE saas.cuentas;--")]
        public async Task Un_estilo_fuera_de_la_lista_no_se_guarda(string? estilo)
        {
            int propia;
            using (var db = _bd.Crear())
                propia = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local", usuario: "ana").IdCuenta;
            var (pagina, autenticacion, _) = Pagina(propia);

            var resultado = await pagina.OnPostAsync(estilo, "/MainMenu");

            Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Equal(EstilosDeMenu.Hub, EstiloGuardado(propia));
            Assert.Null(autenticacion.Emitida);
            Assert.NotNull(pagina.Error);
        }

        [Theory]
        [InlineData("https://otro.sitio/robar")]
        [InlineData("//otro.sitio")]
        [InlineData("/\\otro.sitio")]
        [InlineData(null)]
        public async Task Nunca_redirige_fuera_del_sitio(string? volver)
        {
            int propia;
            using (var db = _bd.Crear())
                propia = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local", usuario: "ana").IdCuenta;
            var (pagina, _, _) = Pagina(propia);

            var resultado = await pagina.OnPostAsync(EstilosDeMenu.Topbar, volver);

            Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Equal(EstilosDeMenu.Topbar, EstiloGuardado(propia));
        }

        [Fact]
        public async Task Sin_sesion_no_se_guarda_nada()
        {
            var http = SesionFalsa.Contexto(autenticado: false);
            var pagina = new EstiloMenuModel(_bd.Crear())
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor())),
            };

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync(EstilosDeMenu.Sidebar, null)).PageName);
            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(pagina.OnGet()).PageName);
        }

        [Theory]
        [InlineData("/", true)]
        [InlineData("/Flota/Index", true)]
        [InlineData("/Flota/Index?x=1#a", true)]
        [InlineData("~/MainMenu", true)]
        [InlineData("//evil.com", false)]
        [InlineData("/\\evil.com", false)]
        [InlineData("~//evil.com", false)]
        [InlineData("https://evil.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Una_ruta_de_vuelta_es_local_solo_si_es_de_este_sitio(string? url, bool esperado)
        {
            Assert.Equal(esperado, EstiloMenuModel.EsRutaLocal(url));
        }
    }
}
