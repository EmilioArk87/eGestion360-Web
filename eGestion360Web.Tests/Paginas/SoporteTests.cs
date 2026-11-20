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
using eGestion360Web.Services.Auditoria;
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
        private readonly AuditoriaEspia _auditoria = new();

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
            return new SoporteModel(db, NullLogger<SoporteModel>.Instance, _auditoria)
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
        public async Task Salir_borra_el_tenant_de_soporte()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            AuthHelper.EntrarEnSoporte(Http(pagina), DatosBase.TenantB, "TENANT_B");

            await pagina.OnPostSalirAsync(CancellationToken.None);

            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Entrar_a_un_tenant_deja_primero_su_rastro_estricto_con_el_tenant_y_sin_empresa()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            await pagina.OnPostEntrarAsync(DatosBase.TenantB, CancellationToken.None);

            var evento = Assert.Single(_auditoria.Estrictos);
            Assert.Equal(TiposDeEvento.SoporteEntrada, evento.Tipo);
            Assert.Equal(DatosBase.TenantB, evento.IdTenant);
            Assert.Null(evento.IdEmpresa);
            Assert.Equal("tenant", evento.ObjetoTipo);
            Assert.Equal(DatosBase.TenantB.ToString(), evento.ObjetoId);
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Entrar_desde_una_empresa_deja_el_rastro_con_su_empresa()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            await pagina.OnPostEntrarEnEmpresaAsync(DatosBase.EmpresaB, "roles", CancellationToken.None);

            var evento = Assert.Single(_auditoria.Estrictos);
            Assert.Equal(DatosBase.TenantB, evento.IdTenant);
            Assert.Equal(DatosBase.EmpresaB, evento.IdEmpresa);
        }

        [Fact]
        public async Task Si_no_se_puede_dejar_el_rastro_no_se_entra_y_se_avisa_sin_guardar_nada_en_la_sesion()
        {
            _auditoria.FallaElModoEstricto = true;
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            var resultado = await pagina.OnPostEntrarAsync(DatosBase.TenantB, CancellationToken.None);

            Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
            Assert.Contains("no se entró", pagina.TempData["SoporteError"]!.ToString());
            Assert.Empty(_auditoria.Eventos);
        }

        [Fact]
        public async Task Si_no_se_puede_dejar_el_rastro_tampoco_se_entra_desde_una_empresa()
        {
            _auditoria.FallaElModoEstricto = true;
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            await pagina.OnPostEntrarEnEmpresaAsync(DatosBase.EmpresaB, "modulos", CancellationToken.None);

            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Salir_deja_su_rastro_y_un_fallo_de_la_auditoria_no_impide_salir()
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            AuthHelper.EntrarEnSoporte(Http(pagina), DatosBase.TenantB, "TENANT_B");

            await pagina.OnPostSalirAsync(CancellationToken.None);

            var evento = Assert.Single(_auditoria.Eventos);
            Assert.Equal(TiposDeEvento.SoporteSalida, evento.Tipo);
            Assert.Equal(DatosBase.TenantB, evento.IdTenant);
            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        // Quién entra al soporte lo decide desde F1.6c el filtro de permisos (plataforma.soporte.*, solo el operador; ver PermisosPageFilterTests).
        // La pantalla conserva su propia regla: el soporte se usa sin empresa en la sesión.
        [Theory]
        [InlineData("admin", "1")]
        public async Task Con_una_empresa_en_la_sesion_el_soporte_lleva_al_menu(string rol, string? empresa)
        {
            using var db = _bd.Crear();
            var pagina = Pagina(db, rol, empresa);

            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(await pagina.OnGetAsync(CancellationToken.None)).PageName);
            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(await pagina.OnPostEntrarAsync(DatosBase.TenantA, CancellationToken.None)).PageName);
            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(await pagina.OnPostSalirAsync(CancellationToken.None)).PageName);
            Assert.Null(AuthHelper.GetTenantDeSoporte(Http(pagina)));
        }

        [Fact]
        public async Task Sin_sesion_va_al_inicio_de_sesion()
        {
            using var db = _bd.Crear();
            var pagina = new SoporteModel(db, NullLogger<SoporteModel>.Instance, new AuditoriaEspia())
            {
                PageContext = new PageContext { HttpContext = SesionFalsa.Contexto("admin", autenticado: false) }
            };

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await pagina.OnGetAsync(CancellationToken.None)).PageName);
        }
    }
}
