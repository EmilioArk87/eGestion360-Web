using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Pages.Empresas;
using eGestion360Web.Services;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.SqlServer.Rls.EntornoRlsReal;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Paso F1.4c con la política RLS ENCENDIDA: el administrador general trabaja en el tenant de soporte que eligió, y las
    /// pantallas de empresas (directorio abierto, escritura aislada por tenant) guardan dentro del contexto del tenant de la
    /// empresa aunque su sesión no tenga ninguno (ADR-023 y ADR-025).
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    [Collection(ColeccionRlsReal.Nombre)]
    public class SoporteConRlsSqlServerTests
    {
        private readonly BaseRlsReal _bd;

        public SoporteConRlsSqlServerTests(BaseRlsReal bd) => _bd = bd;

        private Entorno Entorno => _bd.Entorno ?? throw new InvalidOperationException("Sin servidor de pruebas.");

        private ServiceProvider Servicios(IHttpContextAccessor? acceso = null)
        {
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddMemoryCache();
            servicios.AddSingleton<IHttpContextAccessor>(acceso ?? new HttpContextAccessor());
            servicios.AddScoped<TenantContext>();
            servicios.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            servicios.AddScoped<IResolvedorDeTenant, ResolvedorDeTenantEf>();
            servicios.AddScoped<TenantAlGuardarInterceptor>();
            servicios.AddScoped<ContextoDeSesionInterceptor>();
            servicios.AddScoped<ApplicationDbContext>(sp => new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena)
                    .AddInterceptors(sp.GetRequiredService<TenantAlGuardarInterceptor>(), sp.GetRequiredService<ContextoDeSesionInterceptor>()).Options,
                sp.GetRequiredService<ITenantContext>()));
            return servicios.BuildServiceProvider();
        }

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static T Preparar<T>(T pagina) where T : PageModel
        {
            var http = new DefaultHttpContext
            {
                Session = new SesionFalsa(new Dictionary<string, string> { ["UserId"] = "1", ["Role"] = "admin", ["Username"] = "admin_pruebas" })
            };
            pagina.PageContext = new PageContext
            {
                HttpContext = http,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        private static Empresa EmpresaNueva(string codigo, int idTenant) => new()
        {
            Codigo = codigo, RazonSocial = "Empresa de soporte " + codigo, PaisIso = "HN", MonedaIso = "HNL",
            ZonaHoraria = "America/Tegucigalpa", Activa = true, IdTenant = idTenant
        };

        private async Task<string> EmpresaVista(int tenant, string codigo)
            => Convert.ToString(await ConContexto(Entorno.Cadena, tenant, "tenant",
                $"SELECT CONCAT(id_tenant, '/', eliminado, '/', razon_social) FROM dbo.empresas WHERE codigo = '{codigo}'")) ?? "";

        [FactSqlServer]
        public async Task El_administrador_general_crea_edita_y_elimina_una_empresa_de_un_tenant_aunque_su_sesion_no_tenga_ninguno()
        {
            await using var servicios = Servicios();

            // Crear: la petición de plataforma no tiene tenant; la página fija el elegido antes de guardar.
            int idEmpresa;
            await using (var ambito = servicios.CreateAsyncScope())
            {
                var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
                tenant.FijarPlataforma();
                var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pagina = Preparar(new CreateModel(db, tenant) { Empresa = EmpresaNueva("SOP14C", DatosBase.TenantB) });

                Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());
                idEmpresa = await db.Empresas.AsNoTracking().Where(e => e.Codigo == "SOP14C").Select(e => e.IdEmpresa).SingleAsync();
            }

            Assert.StartsWith($"{DatosBase.TenantB}/0/", await EmpresaVista(DatosBase.TenantB, "SOP14C"));
            Assert.Equal(0, Convert.ToInt32(await ConContexto(Entorno.Cadena, DatosBase.TenantA, "tenant",
                $"SELECT COUNT(*) FROM dbo.categorias_repuesto WHERE id_empresa = {idEmpresa}")));
            Assert.True(Convert.ToInt32(await ConContexto(Entorno.Cadena, DatosBase.TenantB, "tenant",
                $"SELECT COUNT(*) FROM dbo.categorias_repuesto WHERE id_empresa = {idEmpresa} AND id_tenant = {DatosBase.TenantB}")) > 0,
                "Las categorías sembradas deben quedar con el tenant de la empresa.");

            // Editar
            await using (var ambito = servicios.CreateAsyncScope())
            {
                var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
                tenant.FijarPlataforma();
                var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var editada = EmpresaNueva("SOP14C", DatosBase.TenantB);
                editada.IdEmpresa = idEmpresa;
                editada.RazonSocial = "Razón social editada por soporte";
                var pagina = Preparar(new EditModel(db, tenant) { Empresa = editada });

                Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());
            }

            Assert.EndsWith("/Razón social editada por soporte", await EmpresaVista(DatosBase.TenantB, "SOP14C"));

            // Eliminar (baja lógica)
            await using (var ambito = servicios.CreateAsyncScope())
            {
                var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
                tenant.FijarPlataforma();
                var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pagina = Preparar(new DeleteModel(db, tenant));

                Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync(idEmpresa));
            }

            Assert.StartsWith($"{DatosBase.TenantB}/1/", await EmpresaVista(DatosBase.TenantB, "SOP14C"));
        }

        [FactSqlServer]
        public async Task Sin_fijar_el_tenant_de_la_empresa_la_base_rechaza_guardarla()
        {
            await using var servicios = Servicios();
            await using var ambito = servicios.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<TenantContext>().FijarPlataforma();
            var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pagina = Preparar(new CreateModel(db, tenant: null) { Empresa = EmpresaNueva("SOP14X", DatosBase.TenantB) });

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => pagina.OnPostAsync());
            Assert.Equal(33504, Assert.IsType<SqlException>(error.GetBaseException()).Number);
        }

        /// <summary>Corre el middleware real con la sesión dada y, dentro de la petición, cuenta los cargos que entrega la base.</summary>
        private async Task<int[]> TenantsDeLosCargosVistosEnLaPeticion(HttpContext http)
        {
            http.Items.Clear();   // cada llamada es una petición nueva: el tenant de la anterior no se hereda
            var acceso = new HttpContextAccessor { HttpContext = http };
            await using var servicios = Servicios(acceso);
            await using var ambito = servicios.CreateAsyncScope();
            http.RequestServices = ambito.ServiceProvider;

            int[] vistos = Array.Empty<int>();
            var middleware = new TenantMiddleware(async _ =>
            {
                var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // Sin filtro de EF: lo que llega es lo que entrega la base según el tenant de la petición.
                vistos = await db.Cargos.IgnoreQueryFilters().AsNoTracking().Select(c => c.IdTenant).Distinct().ToArrayAsync();
            });

            await middleware.InvokeAsync(http, ambito.ServiceProvider.GetRequiredService<IResolvedorDeTenant>(),
                NullLogger<TenantMiddleware>.Instance);
            return vistos;
        }

        [FactSqlServer]
        public async Task Con_tenant_de_soporte_el_administrador_general_ve_solo_los_datos_de_ese_tenant()
        {
            var http = SesionFalsa.Contexto("admin");
            AuthHelper.EntrarEnSoporte(http, DatosBase.TenantB, "TENANT_B");

            Assert.Equal(new[] { DatosBase.TenantB }, await TenantsDeLosCargosVistosEnLaPeticion(http));
        }

        [FactSqlServer]
        public async Task Sin_tenant_de_soporte_el_administrador_general_no_ve_datos_de_ningun_tenant()
        {
            Assert.Empty(await TenantsDeLosCargosVistosEnLaPeticion(SesionFalsa.Contexto("admin")));
        }

        [FactSqlServer]
        public async Task Cambiar_de_tenant_de_soporte_cambia_lo_que_entrega_la_base()
        {
            var http = SesionFalsa.Contexto("admin");

            AuthHelper.EntrarEnSoporte(http, DatosBase.TenantA, "TENANT_A");
            Assert.Equal(new[] { DatosBase.TenantA }, await TenantsDeLosCargosVistosEnLaPeticion(http));

            AuthHelper.EntrarEnSoporte(http, DatosBase.TenantB, "TENANT_B");
            Assert.Equal(new[] { DatosBase.TenantB }, await TenantsDeLosCargosVistosEnLaPeticion(http));

            AuthHelper.SalirDeSoporte(http);
            Assert.Empty(await TenantsDeLosCargosVistosEnLaPeticion(http));
        }
    }
}
