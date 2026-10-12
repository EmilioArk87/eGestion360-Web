using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages.Empresas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Paso F1.1: toda empresa nace con un tenant válido elegido por el administrador y no cambia de tenant desde la
    /// pantalla de edición (ADR-002). Las pantallas muestran el tenant de cada empresa.
    /// </summary>
    public class EmpresasTenantTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static PageContext Contexto(string rol = "admin")
        {
            var http = new DefaultHttpContext
            {
                Session = new SesionFalsa(new Dictionary<string, string> { ["UserId"] = "1", ["Role"] = rol, ["Username"] = "admin_pruebas" })
            };
            return new PageContext
            {
                HttpContext = http,
                ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new ModelStateDictionary())
            };
        }

        /// <summary>Guarda los mensajes de TempData solo en memoria, para las páginas que los escriben al terminar.</summary>
        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static T ConTempData<T>(T pagina) where T : PageModel
        {
            pagina.TempData = new TempDataDictionary(pagina.PageContext.HttpContext, new TempDataEnMemoria());
            return pagina;
        }

        private static Empresa EmpresaNueva(int idTenant, string codigo = "NUEVA") => new()
        {
            Codigo = codigo, RazonSocial = "Empresa nueva SA", PaisIso = "HN", MonedaIso = "HNL",
            ZonaHoraria = "America/Tegucigalpa", Activa = true, IdTenant = idTenant
        };

        private int ContarEmpresas()
        {
            using var db = _bd.Crear();
            return db.Empresas.Count();
        }

        private static void AssertErrorDeTenant(PageModel pagina)
        {
            var error = Assert.Single(pagina.ModelState["Empresa.IdTenant"]!.Errors);
            Assert.Equal("Selecciona el tenant al que pertenece la empresa.", error.ErrorMessage);
        }

        // ── Crear ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(999)]
        public async Task Crear_una_empresa_sin_un_tenant_valido_se_rechaza_y_no_guarda_nada(int idTenant)
        {
            using var db = _bd.Crear();
            var pagina = new CreateModel(db) { PageContext = Contexto(), Empresa = EmpresaNueva(idTenant) };
            var antes = ContarEmpresas();

            var resultado = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(resultado);
            AssertErrorDeTenant(pagina);
            Assert.Equal(antes, ContarEmpresas());
            Assert.NotNull(pagina.ViewData["Tenants"]);   // el formulario se vuelve a mostrar con la lista de tenants
        }

        [Fact]
        public async Task Crear_una_empresa_en_un_tenant_cancelado_se_rechaza()
        {
            int cancelado;
            using (var db = _bd.Crear())
            {
                var tenant = new Tenant
                {
                    UidPublico = Guid.NewGuid(), Codigo = "CANCELADO", Nombre = "Cuenta cancelada", Tipo = TipoTenant.Cliente,
                    Estado = EstadoTenant.Cancelado, FechaEstado = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };
                db.Tenants.Add(tenant);
                db.SaveChanges();
                cancelado = tenant.IdTenant;
            }

            using var contexto = _bd.Crear();
            var pagina = new CreateModel(contexto) { PageContext = Contexto(), Empresa = EmpresaNueva(cancelado) };
            var antes = ContarEmpresas();

            var resultado = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(resultado);
            AssertErrorDeTenant(pagina);
            Assert.Equal(antes, ContarEmpresas());
        }

        [Fact]
        public async Task Crear_una_empresa_con_un_tenant_valido_la_guarda_con_ese_tenant()
        {
            using (var db = _bd.Crear())
            {
                var pagina = ConTempData(new CreateModel(db) { PageContext = Contexto(), Empresa = EmpresaNueva(DatosBase.TenantB, "EN_B") });

                var resultado = await pagina.OnPostAsync();

                Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            }

            using var verificar = _bd.Crear();
            var guardada = Assert.Single(verificar.Empresas.Where(e => e.Codigo == "EN_B"));
            Assert.Equal(DatosBase.TenantB, guardada.IdTenant);
            Assert.True(verificar.CategoriasRepuesto.Any(c => c.IdEmpresa == guardada.IdEmpresa), "se siembran las categorías de repuesto");
        }

        [Fact]
        public async Task Crear_una_empresa_exige_ser_administrador_general_antes_de_mirar_el_tenant()
        {
            using var db = _bd.Crear();
            var pagina = new CreateModel(db) { PageContext = Contexto("empresa_admin"), Empresa = EmpresaNueva(DatosBase.TenantA) };
            var antes = ContarEmpresas();

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            Assert.Equal(antes, ContarEmpresas());
        }

        [Fact]
        public void El_formulario_de_crear_ofrece_los_tenants_que_no_estan_cancelados()
        {
            using (var db = _bd.Crear())
            {
                db.Tenants.Add(new Tenant
                {
                    UidPublico = Guid.NewGuid(), Codigo = "CANCELADO", Nombre = "Cuenta cancelada", Tipo = TipoTenant.Cliente,
                    Estado = EstadoTenant.Cancelado, FechaEstado = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });
                db.Tenants.Add(new Tenant
                {
                    UidPublico = Guid.NewGuid(), Codigo = "PAUSADO", Nombre = "Cuenta pausada", Tipo = TipoTenant.Cliente,
                    Estado = EstadoTenant.Suspendido, FechaEstado = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            using var contexto = _bd.Crear();
            var pagina = new CreateModel(contexto) { PageContext = Contexto() };

            pagina.OnGet();

            var opciones = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>(pagina.ViewData["Tenants"]).Select(o => o.Text).ToList();
            Assert.Contains(opciones, o => o.StartsWith("TENANT_A - "));
            Assert.Contains(opciones, o => o.StartsWith("TENANT_B - "));
            Assert.Contains(opciones, o => o == "PAUSADO - Cuenta pausada (suspendido)");
            Assert.DoesNotContain(opciones, o => o.StartsWith("CANCELADO"));
        }

        // ── Editar ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Editar_no_cambia_el_tenant_aunque_el_formulario_traiga_otro()
        {
            using (var db = _bd.Crear())
            {
                var pagina = ConTempData(new EditModel(db)
                {
                    PageContext = Contexto(),
                    Empresa = new Empresa
                    {
                        IdEmpresa = DatosBase.EmpresaA, Codigo = "E1", RazonSocial = "Razón social nueva", PaisIso = "HN", MonedaIso = "HNL",
                        ZonaHoraria = "America/Tegucigalpa", Activa = true,
                        IdTenant = DatosBase.TenantB   // alguien manipuló el formulario para pasarla al otro tenant
                    }
                });

                var resultado = await pagina.OnPostAsync();

                Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            }

            using var verificar = _bd.Crear();
            var empresa = verificar.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaA);
            Assert.Equal("Razón social nueva", empresa.RazonSocial);
            Assert.Equal(DatosBase.TenantA, empresa.IdTenant);
        }

        [Fact]
        public async Task La_pantalla_de_editar_muestra_el_tenant_de_la_empresa()
        {
            using var db = _bd.Crear();
            var pagina = new EditModel(db) { PageContext = Contexto() };

            var resultado = await pagina.OnGetAsync(DatosBase.EmpresaB);

            Assert.IsType<PageResult>(resultado);
            Assert.Equal("TENANT_B", pagina.TenantActual?.Codigo);
        }

        // ── Detalle y lista ────────────────────────────────────────────────

        [Fact]
        public async Task El_detalle_muestra_el_tenant_de_la_empresa()
        {
            using var db = _bd.Crear();
            var pagina = new DetailsModel(db) { PageContext = Contexto() };

            await pagina.OnGetAsync(DatosBase.EmpresaA);

            Assert.Equal("TENANT_A", pagina.TenantActual?.Codigo);
        }

        [Fact]
        public async Task La_lista_ofrece_el_codigo_del_tenant_de_cada_empresa()
        {
            using var db = _bd.Crear();
            var pagina = new IndexModel(db) { PageContext = Contexto() };

            await pagina.OnGetAsync();

            Assert.Equal("TENANT_A", pagina.CodigosDeTenant[pagina.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaA).IdTenant]);
            Assert.Equal("TENANT_B", pagina.CodigosDeTenant[pagina.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaB).IdTenant]);
        }
    }
}
