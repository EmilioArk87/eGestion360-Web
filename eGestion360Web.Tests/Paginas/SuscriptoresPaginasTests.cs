using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Plataforma;
using eGestion360Web.Tests.Infra;
using DetalleModel = eGestion360Web.Pages.Admin.Suscriptores.DetalleModel;
using ListaModel = eGestion360Web.Pages.Admin.Suscriptores.IndexModel;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// F1.9a: las pantallas de suscriptores. Lo que se puede hacer lo prueba <c>ServicioDeSuscriptoresTests</c>; aquí, que la pantalla carga,
    /// responde 404 por un suscriptor que no existe, vuelve a su dirección al guardar y, si el cambio se rechaza, conserva lo escrito.
    /// </summary>
    public class SuscriptoresPaginasTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static T Preparar<T>(T pagina) where T : PageModel
        {
            var http = SesionFalsa.Contexto("admin", null);
            pagina.PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor()))
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        private DetalleModel Detalle(eGestion360Web.Data.ApplicationDbContext db, int id)
        {
            var pagina = Preparar(new DetalleModel(new ServicioDeSuscriptores(db)));
            pagina.Id = id;
            return pagina;
        }

        [Fact]
        public async Task La_lista_muestra_los_suscriptores()
        {
            using var db = _bd.Crear();
            var pagina = Preparar(new ListaModel(new ServicioDeSuscriptores(db)));

            Assert.IsType<PageResult>(await pagina.OnGetAsync(CancellationToken.None));

            Assert.Equal(2, pagina.Suscriptores.Count);
        }

        [Fact]
        public async Task Un_suscriptor_que_no_existe_responde_404()
        {
            using var db = _bd.Crear();

            Assert.IsType<NotFoundResult>(await Detalle(db, 999).OnGetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Un_cambio_rechazado_vuelve_a_la_pantalla_con_el_error_y_lo_escrito()
        {
            using var db = _bd.Crear();
            var pagina = Detalle(db, DatosBase.TenantB);

            var resultado = await pagina.OnPostEstadoAsync(AccionDeEstado.Suspender, ModoDeSuspension.Bloqueo, "Falta de pago", "otro-codigo", "",
                CancellationToken.None);

            Assert.IsType<PageResult>(resultado);
            Assert.Contains("TENANT_B", pagina.ErrorDeEstado);
            Assert.Equal("Falta de pago", pagina.MotivoDeEstado);
            Assert.Equal(AccionDeEstado.Suspender, pagina.AccionElegida);
            Assert.Equal(ModoDeSuspension.Bloqueo, pagina.ModoElegido);
            Assert.Equal(EstadoTenant.Activo, pagina.Detalle.Resumen.Estado);
        }

        [Fact]
        public async Task Un_cambio_aceptado_redirige_al_mismo_suscriptor_con_su_aviso()
        {
            using var db = _bd.Crear();
            var pagina = Detalle(db, DatosBase.TenantB);

            var resultado = await pagina.OnPostEstadoAsync(AccionDeEstado.Suspender, ModoDeSuspension.Lectura, "Falta de pago", "TENANT_B", "",
                CancellationToken.None);

            var redireccion = Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Equal(DatosBase.TenantB, redireccion.RouteValues!["id"]);
            Assert.NotNull(pagina.TempData["SuscriptoresMensaje"]);
        }
    }
}
