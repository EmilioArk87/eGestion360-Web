using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Services;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>
    /// Paso F1.4c: el tenant de soporte del administrador general (ADR-025). Lo elige en <c>/Admin/Soporte</c>, vive en su
    /// sesión, el middleware lo convierte en el tenant de la petición y un filtro lleva a elegirlo a quien abre una pantalla de
    /// datos de un tenant sin haberlo hecho.
    /// </summary>
    public class TenantDeSoporteTests
    {
        private static HttpContext Sesion(string rol, string? empresa = null, string? tenantDeSoporte = "2", string? codigo = "DEMO")
        {
            var http = SesionFalsa.Contexto(rol, empresa);
            if (tenantDeSoporte != null) http.Session.SetString(AuthHelper.ClaveTenantDeSoporte, tenantDeSoporte);
            if (codigo != null) http.Session.SetString(AuthHelper.ClaveCodigoTenantDeSoporte, codigo);
            return http;
        }

        // ── Ayudantes de sesión ─────────────────────────────────────────────

        [Fact]
        public void El_tenant_de_soporte_es_solo_del_administrador_general_sin_empresa()
        {
            Assert.Equal(2, AuthHelper.GetTenantDeSoporte(Sesion("admin")));
            Assert.Equal("DEMO", AuthHelper.GetCodigoTenantDeSoporte(Sesion("admin")));

            // Con empresa en la sesión el tenant es el de la empresa; y los demás roles no tienen soporte aunque la clave esté.
            Assert.Null(AuthHelper.GetTenantDeSoporte(Sesion("admin", empresa: "1")));
            Assert.Null(AuthHelper.GetTenantDeSoporte(Sesion("empresa_admin")));
            Assert.Null(AuthHelper.GetTenantDeSoporte(Sesion("empresa_user")));
            Assert.Null(AuthHelper.GetCodigoTenantDeSoporte(Sesion("empresa_admin")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-4")]
        [InlineData("abc")]
        public void Un_valor_invalido_en_la_sesion_no_es_un_tenant_de_soporte(string? valor)
            => Assert.Null(AuthHelper.GetTenantDeSoporte(Sesion("admin", tenantDeSoporte: valor)));

        [Fact]
        public void Entrar_y_salir_de_soporte_guardan_y_borran_la_eleccion()
        {
            var http = SesionFalsa.Contexto("admin");
            Assert.Null(AuthHelper.GetTenantDeSoporte(http));

            AuthHelper.EntrarEnSoporte(http, 4, "TRANSGAR");
            Assert.Equal(4, AuthHelper.GetTenantDeSoporte(http));
            Assert.Equal("TRANSGAR", AuthHelper.GetCodigoTenantDeSoporte(http));

            AuthHelper.SalirDeSoporte(http);
            Assert.Null(AuthHelper.GetTenantDeSoporte(http));
            Assert.Null(AuthHelper.GetCodigoTenantDeSoporte(http));
        }

        // ── Middleware ──────────────────────────────────────────────────────

        private sealed class ResolvedorQueNoSeUsa : IResolvedorDeTenant
        {
            public int Consultas { get; private set; }
            public Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default) { Consultas++; return Task.FromResult<int?>(1); }
            public Task<int?> ResolverPorPersonaAsync(int idPersona, CancellationToken ct = default) => Task.FromResult<int?>(null);
            public Task<int?> ResolverInternoAsync(CancellationToken ct = default) => Task.FromResult<int?>(null);
        }

        private static async Task<(int? IdTenant, int? IdEmpresa, bool Plataforma, string Ambito, int Consultas)> Correr(HttpContext http)
        {
            var resolvedor = new ResolvedorQueNoSeUsa();
            var registro = new RegistroEnMemoria<TenantMiddleware>();
            (int? Tenant, int? Empresa, bool Plataforma) visto = default;
            var middleware = new TenantMiddleware(contexto =>
            {
                var tenant = new TenantContext(new HttpContextAccessor { HttpContext = contexto });
                visto = (tenant.IdTenant, tenant.IdEmpresa, tenant.EsPlataforma);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(http, resolvedor, registro);
            return (visto.Tenant, visto.Empresa, visto.Plataforma, Assert.Single(registro.Ambitos).Texto!, resolvedor.Consultas);
        }

        [Fact]
        public async Task El_administrador_sin_empresa_trabaja_en_el_tenant_de_soporte_y_el_log_lo_dice()
        {
            var http = Sesion("admin", tenantDeSoporte: "4");

            var (tenant, empresa, plataforma, ambito, consultas) = await Correr(http);

            Assert.Equal(4, tenant);
            Assert.Null(empresa);
            Assert.False(plataforma);
            Assert.Equal(0, consultas);   // lo guardó el servidor en la sesión: no hay nada que resolver
            Assert.Equal($"IdTenant:4 IdEmpresa:- IdUsuario:1 IdCorrelacion:{http.TraceIdentifier} Soporte:True", ambito);
        }

        [Fact]
        public async Task Con_empresa_en_la_sesion_el_tenant_es_el_de_la_empresa_y_no_el_de_soporte()
        {
            var (tenant, empresa, _, ambito, consultas) = await Correr(Sesion("admin", empresa: "1", tenantDeSoporte: "4"));

            Assert.Equal(1, tenant);   // el resolvedor de la prueba devuelve el tenant 1 para cualquier empresa
            Assert.Equal(1, empresa);
            Assert.Equal(1, consultas);
            Assert.DoesNotContain("Soporte", ambito);
        }

        [Theory]
        [InlineData("empresa_admin")]
        [InlineData("empresa_user")]
        public async Task Quien_no_es_administrador_general_no_puede_usar_el_soporte_aunque_traiga_la_clave(string rol)
        {
            var (tenant, _, _, ambito, _) = await Correr(Sesion(rol, tenantDeSoporte: "4"));

            Assert.Null(tenant);
            Assert.DoesNotContain("Soporte", ambito);
        }

        [Fact]
        public async Task El_tenant_de_soporte_no_sale_de_la_peticion()
        {
            var http = SesionFalsa.Contexto("admin");
            http.Request.QueryString = new QueryString("?TenantDeSoporte=4&IdTenant=4");
            http.Request.Headers["X-Tenant"] = "4";

            var (tenant, _, _, _, _) = await Correr(http);

            Assert.Null(tenant);
        }

        // ── Filtro de pantallas de datos de un tenant ───────────────────────

        private static PageHandlerExecutingContext Ejecutar(string ruta, HttpContext http)
        {
            http.Request.Path = ruta;
            var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = ruta, RelativePath = ruta + ".cshtml" };
            var pageContext = new PageContext(new ActionContext(http, new RouteData(), descriptor));
            var contexto = new PageHandlerExecutingContext(
                pageContext, Array.Empty<IFilterMetadata>(), new HandlerMethodDescriptor(),
                new Dictionary<string, object?>(), new object());

            new TenantDeSoporteRequeridoPageFilter().OnPageHandlerExecuting(contexto);
            return contexto;
        }

        [Fact]
        public void La_lista_de_pantallas_que_exigen_tenant_de_soporte_es_la_revisada()
            => Assert.Equal(
                new[] { "/Admin/EmpresaModulos", "/Admin/EmpresaRoles", "/Admin/RolesPermisos", "/Admin/Personas", "/Admin/Usuarios" },
                TenantDeSoporteRequeridoPageFilter.RutasQueExigenTenantDeSoporte);

        [Fact]
        public void Cada_ruta_de_la_lista_existe_en_Pages()
        {
            var pages = Path.Combine(RaizDelRepositorio(), "Pages");
            foreach (var ruta in TenantDeSoporteRequeridoPageFilter.RutasQueExigenTenantDeSoporte)
                Assert.True(Directory.Exists(Path.Combine(pages, ruta.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))), ruta);
        }

        [Fact]
        public void El_administrador_sin_tenant_de_soporte_va_a_elegirlo_y_la_pantalla_pedida_queda_para_volver()
        {
            var http = Sesion("admin", tenantDeSoporte: null);
            http.Request.QueryString = new QueryString("?id=7");

            var contexto = Ejecutar("/Admin/Usuarios/Edit", http);

            var redireccion = Assert.IsType<RedirectToPageResult>(contexto.Result);
            Assert.Equal("/Admin/Soporte/Index", redireccion.PageName);
            Assert.Equal("/Admin/Usuarios/Edit?id=7", redireccion.RouteValues!["volver"]);
        }

        [Theory]
        [InlineData("/Admin/Personas/Index")]
        [InlineData("/Admin/EmpresaModulos/Index")]
        [InlineData("/Admin/EmpresaRoles/Permisos")]
        [InlineData("/Admin/RolesPermisos/Index")]
        [InlineData("/Admin/Usuarios/Create")]
        public void Todas_las_pantallas_de_la_lista_llevan_a_elegir_el_tenant(string ruta)
            => Assert.IsType<RedirectToPageResult>(Ejecutar(ruta, Sesion("admin", tenantDeSoporte: null)).Result);

        [Fact]
        public void Con_tenant_de_soporte_la_pantalla_corre()
            => Assert.Null(Ejecutar("/Admin/Personas/Index", Sesion("admin")).Result);

        [Theory]
        [InlineData("/Admin/Soporte/Index")]
        [InlineData("/Admin/Modulos/Index")]
        [InlineData("/Admin/Outbox/Index")]
        [InlineData("/Empresas/Index")]
        [InlineData("/UserManagement")]
        [InlineData("/Flota/Catalogos/Vehiculos/Index")]
        [InlineData("/Admin/PersonasOtraCosa")]
        public void Las_pantallas_fuera_de_la_lista_no_exigen_tenant_de_soporte(string ruta)
            => Assert.Null(Ejecutar(ruta, Sesion("admin", tenantDeSoporte: null)).Result);

        [Fact]
        public void El_administrador_de_empresa_y_la_sesion_con_empresa_no_pasan_por_el_filtro()
        {
            Assert.Null(Ejecutar("/Admin/Usuarios/Create", Sesion("empresa_admin", empresa: "1", tenantDeSoporte: null)).Result);
            Assert.Null(Ejecutar("/Admin/Usuarios/Create", Sesion("admin", empresa: "1", tenantDeSoporte: null)).Result);
        }

        [Fact]
        public void Sin_sesion_la_pagina_decide_y_el_filtro_no_interviene()
            => Assert.Null(Ejecutar("/Admin/Personas/Index", SesionFalsa.Contexto("admin", autenticado: false)).Result);

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
