using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Tests.Infra;
using SoporteModel = eGestion360Web.Pages.Admin.Soporte.IndexModel;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Paso F1.4c: <c>/Admin/Soporte</c>, donde el administrador general elige el tenant con el que trabaja (ADR-025). Solo él la
    /// usa, solo elige tenants que existen y no están cancelados, y la elección queda en su sesión.
    /// </summary>
    public class SoporteTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private SoporteModel Pagina(ApplicationDbContext db, string rol = "admin", string? empresa = null)
        {
            var http = SesionFalsa.Contexto(rol, empresa);
            http.Session.SetString("Username", "admin_pruebas");
            var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
            return new SoporteModel(db, NullLogger<SoporteModel>.Instance)
            {
                PageContext = new PageContext(actionContext)
                {
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                },
                TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
                Url = new UrlHelper(actionContext)
            };
        }

        private HttpContext Http(SoporteModel pagina) => pagina.PageContext.HttpContext;

        [Fact]
        public async Task La_pagina_lista_los_tenants_con_su_cantidad_de_empresas()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(CancellationToken.None));

            Assert.Equal(2, pagina.Tenants.Count);
            Assert.Equal(DatosBase.TenantA, pagina.Tenants.Single(t => t.Tipo == TipoTenant.Interno).IdTenant);
            Assert.All(pagina.Tenants, t => Assert.True(t.Empresas >= 1, t.Codigo));
            Assert.Null(pagina.TenantDeSoporte);
        }

        [Fact]
        public async Task Los_tenants_cancelados_no_aparecen()
        {
            using (var db = _bd.Crear())
            {
                (await db.Tenants.SingleAsync(t => t.IdTenant == DatosBase.TenantB)).Estado = EstadoTenant.Cancelado;
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            var pagina = Pagina(lectura);
            await pagina.OnGetAsync(CancellationToken.None);

            Assert.Equal(new[] { DatosBase.TenantA }, pagina.Tenants.Select(t => t.IdTenant));
        }

        [Fact]
        public async Task Entrar_guarda_el_tenant_en_la_sesion_y_vuelve_a_la_pantalla_pedida()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            pagina.Volver = "/Admin/Personas/Index?pagina=2";

            var resultado = await pagina.OnPostEntrarAsync(DatosBase.TenantB, CancellationToken.None);

            Assert.Equal("/Admin/Personas/Index?pagina=2", Assert.IsType<LocalRedirectResult>(resultado).Url);
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantDeSoporte(Http(pagina)));
            Assert.Equal("TENANT_B", AuthHelper.GetCodigoTenantDeSoporte(Http(pagina)));
        }

        [Theory]
        [InlineData("https://sitio-ajeno.example/robo")]
        [InlineData("//sitio-ajeno.example/robo")]
        [InlineData("javascript:alert(1)")]
        public async Task Entrar_nunca_redirige_a_un_destino_que_no_es_local(string volver)
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            pagina.Volver = volver;

            var resultado = await pagina.OnPostEntrarAsync(DatosBase.TenantA, CancellationToken.None);

            Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Equal(DatosBase.TenantA, AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(999)]
        public async Task Un_tenant_que_no_existe_no_se_elige(int idTenant)
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            await pagina.OnPostEntrarAsync(idTenant, CancellationToken.None);

            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
            Assert.NotNull(pagina.TempData["SoporteError"]);
        }

        [Fact]
        public async Task Un_tenant_cancelado_no_se_elige()
        {
            using (var db = _bd.Crear())
            {
                (await db.Tenants.SingleAsync(t => t.IdTenant == DatosBase.TenantB)).Estado = EstadoTenant.Cancelado;
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            var pagina = Pagina(lectura);
            await pagina.OnPostEntrarAsync(DatosBase.TenantB, CancellationToken.None);

            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Theory]
        [InlineData("modulos", "/Admin/EmpresaModulos/Index")]
        [InlineData("roles", "/Admin/EmpresaRoles/Index")]
        public async Task Desde_la_lista_de_empresas_entra_al_tenant_de_la_empresa_y_abre_la_pantalla_pedida(string destino, string paginaEsperada)
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            var resultado = Assert.IsType<RedirectToPageResult>(await pagina.OnPostEntrarEnEmpresaAsync(DatosBase.EmpresaB, destino, CancellationToken.None));

            Assert.Equal(paginaEsperada, resultado.PageName);
            Assert.Equal(DatosBase.EmpresaB, resultado.RouteValues!["empresaId"]);
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Una_empresa_que_no_existe_no_cambia_el_tenant_de_soporte()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            AuthHelper.EntrarEnSoporte(Http(pagina), DatosBase.TenantA, "TENANT_A");

            await pagina.OnPostEntrarEnEmpresaAsync(9999, "modulos", CancellationToken.None);

            Assert.Equal(DatosBase.TenantA, AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public void Salir_borra_el_tenant_de_soporte()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            AuthHelper.EntrarEnSoporte(Http(pagina), DatosBase.TenantB, "TENANT_B");

            pagina.OnPostSalir();

            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Theory]
        [InlineData("empresa_admin", null)]
        [InlineData("empresa_user", null)]
        [InlineData("admin", "1")]
        public async Task Solo_el_administrador_general_sin_empresa_usa_el_soporte(string rol, string? empresa)
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db, rol, empresa);

            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(await pagina.OnGetAsync(CancellationToken.None)).PageName);
            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(await pagina.OnPostEntrarAsync(DatosBase.TenantA, CancellationToken.None)).PageName);
            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(pagina.OnPostSalir()).PageName);
            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Sin_sesion_va_al_inicio_de_sesion()
        {
            using var db = _bd.Crear();
            var pagina = new SoporteModel(db, NullLogger<SoporteModel>.Instance)
            {
                PageContext = new PageContext { HttpContext = SesionFalsa.Contexto("admin", autenticado: false) }
            };

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await pagina.OnGetAsync(CancellationToken.None)).PageName);
        }
    }
}
