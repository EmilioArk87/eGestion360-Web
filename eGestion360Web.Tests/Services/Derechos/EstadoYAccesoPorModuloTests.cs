using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Derechos
{
    /// <summary>
    /// Lo que el estado del tenant y su plan hacen en cada petición (paso F1.7): el middleware que bloquea a un tenant suspendido o
    /// cancelado, el filtro que exige el derecho de cada módulo y la lectura de <see cref="AuthHelper.HasModulo"/>.
    /// </summary>
    public class EstadoYAccesoPorModuloTests
    {
        // ── Quién se bloquea: la tabla completa ───────────────────────────────

        private static DerechosDelTenant Tenant(string estado, string modo = ModoDeSuspension.Lectura, params string[] derechos) => new(
            2, "TRANSGAR", TipoTenant.Cliente, estado, modo, null, derechos.Length == 0 ? null : "flota",
            new HashSet<string>(derechos), new Dictionary<string, int?>());

        [Theory]
        // Activo: nada se bloquea.
        [InlineData(EstadoTenant.Activo, ModoDeSuspension.Lectura, "GET", "/Flota/Index", false)]
        [InlineData(EstadoTenant.Activo, ModoDeSuspension.Lectura, "POST", "/Flota/Catalogos/Vehiculos/Create", false)]
        // Suspendido en lectura: consultar sí, guardar no.
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "GET", "/Flota/Index", false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "HEAD", "/Flota/Index", false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "OPTIONS", "/Flota/Index", false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "POST", "/Flota/Catalogos/Vehiculos/Create", true)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "PUT", "/api/algo", true)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "DELETE", "/api/algo", true)]
        // Suspendido en bloqueo: nada, ni consultar.
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "GET", "/Flota/Index", true)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "POST", "/Flota/Index", true)]
        // Cancelado: nada, sea cual sea el modo.
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, "GET", "/MainMenu", true)]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Bloqueo, "GET", "/MainMenu", true)]
        // Siempre disponibles: entrar, salir, la página del motivo y el error.
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "POST", "/Login", false)]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, "GET", "/Logout", false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "GET", "/SinAcceso", false)]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, "GET", "/Error", false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "POST", "/login", false)]
        // Una ruta que solo empieza igual no es la exenta.
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "GET", "/LoginOtraCosa", true)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "GET", "/Flota/Login", true)]
        public void La_tabla_de_quien_se_bloquea(string estado, string modo, string metodo, string ruta, bool bloquea)
            => Assert.Equal(bloquea, EstadoDelTenantMiddleware.Bloquea(Tenant(estado, modo), metodo, new PathString(ruta), esOperador: false));

        [Theory]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "POST")]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "GET")]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, "GET")]
        public void El_operador_de_la_plataforma_no_se_bloquea_nunca(string estado, string modo, string metodo)
            => Assert.False(EstadoDelTenantMiddleware.Bloquea(Tenant(estado, modo), metodo, new PathString("/Flota/Index"), esOperador: true));

        // ── El middleware en una petición ─────────────────────────────────────

        private sealed class DerechosFalsos : IDerechosDeUso
        {
            public DerechosDelTenant? Tenant { get; init; }
            public IReadOnlySet<string> Modulos { get; init; } = new HashSet<string>();
            public int LlamadasATenant { get; private set; }

            public Task<DerechosDelTenant?> DelTenantAsync(int idTenant, CancellationToken ct = default)
            {
                LlamadasATenant++;
                return Task.FromResult(Tenant);
            }

            public Task<IReadOnlySet<string>> ModulosActivosDeLaEmpresaAsync(int idEmpresa, CancellationToken ct = default) => Task.FromResult(Modulos);

            public Task<ResultadoDeLimite> PuedeAgregarAsync(int idTenant, string codigoDeLimite, CancellationToken ct = default)
                => Task.FromResult(new ResultadoDeLimite(true, null, 0));

            public void Invalidar(int idTenant, int? idEmpresa = null) { }
        }

        private static async Task<(HttpContext Http, bool SiguioAdelante)> Pasar(DerechosFalsos derechos, string metodo, string ruta,
            string rol = "empresa_user", bool conTenant = true, string? aceptar = null)
        {
            var http = SesionFalsa.Contexto(rol, empresaId: "1");
            http.Request.Method = metodo;
            http.Request.Path = ruta;
            if (aceptar != null) http.Request.Headers.Accept = aceptar;
            if (conTenant)
            {
                http.Items[TenantMiddleware.ClaveIdTenant] = 2;
                http.Items[TenantMiddleware.ClaveIdEmpresa] = 1;
            }

            var siguio = false;
            var middleware = new EstadoDelTenantMiddleware(_ => { siguio = true; return Task.CompletedTask; });
            await middleware.InvokeAsync(http, derechos, NullLogger<EstadoDelTenantMiddleware>.Instance);
            return (http, siguio);
        }

        [Fact]
        public async Task Un_tenant_activo_pasa_y_deja_sus_derechos_en_la_peticion()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Activo, derechos: "modulo.flota"), Modulos = new HashSet<string> { "flota" } };

            var (http, siguio) = await Pasar(derechos, "POST", "/Flota/Catalogos/Vehiculos/Create");

            Assert.True(siguio);
            var guardado = Assert.IsType<DerechosDeLaPeticion>(http.Items[EstadoDelTenantMiddleware.ClaveDerechos]);
            Assert.True(guardado.TieneModulo("flota"));
        }

        [Fact]
        public async Task Un_tenant_suspendido_en_lectura_puede_consultar_pero_no_guardar()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "modulo.flota") };

            var consulta = await Pasar(derechos, "GET", "/Flota/Index");
            Assert.True(consulta.SiguioAdelante);

            var guardado = await Pasar(derechos, "POST", "/Flota/Catalogos/Vehiculos/Create");
            Assert.False(guardado.SiguioAdelante);
            Assert.Equal(StatusCodes.Status302Found, guardado.Http.Response.StatusCode);
            Assert.Equal(EstadoDelTenantMiddleware.PaginaSinAcceso, guardado.Http.Response.Headers.Location.ToString());
        }

        [Fact]
        public async Task Un_tenant_cancelado_no_entra_a_nada()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Cancelado) };

            var (http, siguio) = await Pasar(derechos, "GET", "/MainMenu");

            Assert.False(siguio);
            Assert.Equal(EstadoDelTenantMiddleware.PaginaSinAcceso, http.Response.Headers.Location.ToString());
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("text/html, application/json;q=0.9")]
        public async Task Una_llamada_de_datos_bloqueada_recibe_403_y_no_una_redireccion(string aceptar)
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo) };

            var (http, siguio) = await Pasar(derechos, "GET", "/Flota/Catalogos/Personas/Index", aceptar: aceptar);

            Assert.False(siguio);
            Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
            Assert.False(http.Response.Headers.ContainsKey("Location"));
        }

        [Fact]
        public async Task El_administrador_general_entra_a_un_tenant_suspendido_para_atenderlo()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo) };

            var (_, siguio) = await Pasar(derechos, "POST", "/Flota/Catalogos/Vehiculos/Create", rol: "admin");

            Assert.True(siguio);
        }

        [Fact]
        public async Task Sin_tenant_en_la_peticion_no_se_consulta_nada_y_la_peticion_sigue()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Cancelado) };

            var (http, siguio) = await Pasar(derechos, "GET", "/Login", conTenant: false);

            Assert.True(siguio);
            Assert.Equal(0, derechos.LlamadasATenant);
            Assert.False(http.Items.ContainsKey(EstadoDelTenantMiddleware.ClaveDerechos));
        }

        [Fact]
        public async Task Un_tenant_que_la_base_no_conoce_no_bloquea_y_no_deja_derechos()
        {
            var derechos = new DerechosFalsos { Tenant = null };

            var (http, siguio) = await Pasar(derechos, "POST", "/Flota/Index");

            Assert.True(siguio);
            Assert.False(http.Items.ContainsKey(EstadoDelTenantMiddleware.ClaveDerechos));
        }

        [Fact]
        public async Task Un_tenant_sin_plan_no_se_bloquea_pero_no_concede_ningun_modulo()
        {
            var derechos = new DerechosFalsos { Tenant = Tenant(EstadoTenant.Activo), Modulos = new HashSet<string> { "flota" } };

            var (http, siguio) = await Pasar(derechos, "GET", "/Flota/Index");

            Assert.True(siguio);
            Assert.False(Assert.IsType<DerechosDeLaPeticion>(http.Items[EstadoDelTenantMiddleware.ClaveDerechos]).TieneModulo("flota"));
        }

        // ── Qué módulo exige cada carpeta ─────────────────────────────────────

        [Theory]
        [InlineData("/Flota/Index", "flota")]
        [InlineData("/Flota/Catalogos/Vehiculos/Create", "flota")]
        [InlineData("/flota/kpi/dashboard", "flota")]
        [InlineData("/Facturacion/Index", "facturacion")]
        [InlineData("/Facturacion/Pagos/Create", "facturacion")]
        [InlineData("/Catalogos/Clientes/Index", "catalogos")]
        [InlineData("/Contabilidad/Index", "contabilidad")]
        public void Cada_carpeta_de_modulo_exige_su_modulo(string ruta, string modulo)
            => Assert.Equal(modulo, DerechoDeModuloPageFilter.ModuloDe(ruta));

        [Theory]
        [InlineData("/Flota/SinEmpresa")]
        [InlineData("/MainMenu")]
        [InlineData("/Login")]
        [InlineData("/SinAcceso")]
        [InlineData("/Admin/Soporte/Index")]
        [InlineData("/Empresas/Index")]
        [InlineData("/FlotaOtraCosa")]
        [InlineData("/Catalogos")]         // sin barra: la carpeta, no una pantalla suya
        [InlineData("")]
        [InlineData(null)]
        public void Lo_que_no_es_de_un_modulo_no_exige_ninguno(string? ruta)
            => Assert.Null(DerechoDeModuloPageFilter.ModuloDe(ruta));

        // ── Qué hace el filtro con cada sesión ────────────────────────────────

        private static DerechosDeLaPeticion Peticion(string[] derechos, string[] modulosDeLaEmpresa)
            => new(Tenant(EstadoTenant.Activo, ModoDeSuspension.Lectura, derechos), new HashSet<string>(modulosDeLaEmpresa));

        private static PageHandlerExecutingContext Ejecutar(string ruta, HttpContext http)
        {
            var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = ruta, RelativePath = ruta + ".cshtml" };
            var pageContext = new PageContext(new ActionContext(http, new RouteData(), descriptor));
            var contexto = new PageHandlerExecutingContext(
                pageContext, Array.Empty<IFilterMetadata>(), new HandlerMethodDescriptor(),
                new Dictionary<string, object?>(), new object());

            new DerechoDeModuloPageFilter().OnPageHandlerExecuting(contexto);
            return contexto;
        }

        [Fact]
        public void Quien_tiene_el_modulo_en_su_plan_y_en_su_empresa_pasa()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(new[] { "modulo.flota" }, new[] { "flota" });

            Assert.Null(Ejecutar("/Flota/Catalogos/Vehiculos/Index", http).Result);
        }

        [Fact]
        public void Si_el_plan_no_concede_el_modulo_la_pantalla_responde_403_aunque_la_empresa_lo_tenga_activo()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(new[] { "modulo.flota" }, new[] { "flota", "facturacion" });

            var resultado = Assert.IsType<ContentResult>(Ejecutar("/Facturacion/Index", http).Result);

            Assert.Equal(StatusCodes.Status403Forbidden, resultado.StatusCode);
            Assert.Contains("facturacion", resultado.Content);
        }

        [Fact]
        public void Si_la_empresa_no_tiene_activado_el_modulo_la_pantalla_responde_403_aunque_el_plan_lo_conceda()
        {
            var http = SesionFalsa.Contexto("empresa_admin", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(new[] { "modulo.flota", "modulo.catalogos" }, new[] { "flota" });

            var resultado = Assert.IsType<ContentResult>(Ejecutar("/Catalogos/Clientes/Index", http).Result);

            Assert.Equal(StatusCodes.Status403Forbidden, resultado.StatusCode);
        }

        [Fact]
        public void El_administrador_general_entra_a_todas_las_carpetas_de_modulo()
        {
            var http = SesionFalsa.Contexto("admin", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(Array.Empty<string>(), Array.Empty<string>());

            Assert.Null(Ejecutar("/Flota/Index", http).Result);
        }

        [Fact]
        public void Quien_no_inicio_sesion_no_recibe_el_403_sino_lo_que_decida_la_pantalla()
        {
            var http = SesionFalsa.Contexto(rol: null, autenticado: false);

            Assert.Null(Ejecutar("/Flota/Index", http).Result);
        }

        [Fact]
        public void Las_pantallas_que_no_son_de_un_modulo_y_la_de_sin_empresa_no_se_tocan()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(Array.Empty<string>(), Array.Empty<string>());

            Assert.Null(Ejecutar("/MainMenu", http).Result);
            Assert.Null(Ejecutar("/Flota/SinEmpresa", http).Result);
            Assert.Null(Ejecutar("/Admin/Usuarios/Index", http).Result);
        }

        // ── AuthHelper.HasModulo: la petición manda, la sesión es el respaldo ─

        [Fact]
        public void HasModulo_lee_los_derechos_de_la_peticion_y_no_la_copia_de_la_sesion()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Session.SetString("Modulos", "[\"flota\",\"facturacion\"]");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(new[] { "modulo.flota" }, new[] { "flota" });

            Assert.True(AuthHelper.HasModulo(http, "flota"));
            Assert.False(AuthHelper.HasModulo(http, "facturacion"));   // la sesión lo tiene, el plan ya no
        }

        [Fact]
        public void HasModulo_sin_derechos_de_la_peticion_usa_la_copia_de_la_sesion_como_antes()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Session.SetString("Modulos", "[\"flota\"]");

            Assert.True(AuthHelper.HasModulo(http, "flota"));
            Assert.True(AuthHelper.HasModulo(http, "FLOTA"));
            Assert.False(AuthHelper.HasModulo(http, "facturacion"));
        }

        [Fact]
        public void HasModulo_para_el_administrador_general_es_siempre_verdadero()
        {
            var http = SesionFalsa.Contexto("admin");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(Array.Empty<string>(), Array.Empty<string>());

            Assert.True(AuthHelper.HasModulo(http, "contabilidad"));
        }

        [Fact]
        public void El_empresa_admin_pierde_los_permisos_de_un_modulo_que_el_plan_ya_no_concede()
        {
            var http = SesionFalsa.Contexto("empresa_admin", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = Peticion(new[] { "modulo.flota" }, new[] { "flota", "facturacion" });

            Assert.True(AuthHelper.PuedeCrear(http, "flota"));
            Assert.False(AuthHelper.PuedeCrear(http, "facturacion"));
        }

        // ── Que ninguna carpeta de módulo se escape del filtro ────────────────

        private static string RaizDelRepositorio()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static readonly Regex PideModulo = new(@"HasModulo\(\s*[A-Za-z_.]+\s*,\s*""(?<modulo>[a-z_]+)""", RegexOptions.Compiled);

        [Fact]
        public void Todo_modulo_que_una_pantalla_exige_tiene_su_carpeta_en_el_filtro()
        {
            var raiz = RaizDelRepositorio();
            var mapeados = DerechoDeModuloPageFilter.ModulosPorRuta.Select(m => m.Modulo).ToHashSet();

            var sinMapear = Directory.EnumerateFiles(Path.Combine(raiz, "Pages"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => PideModulo.Matches(File.ReadAllText(f)).Select(m => (Archivo: Path.GetRelativePath(raiz, f), Modulo: m.Groups["modulo"].Value)))
                .Where(x => !mapeados.Contains(x.Modulo))
                .Select(x => $"{x.Archivo} pide «{x.Modulo}»")
                .Distinct()
                .ToList();

            Assert.True(sinMapear.Count == 0,
                "Estas pantallas exigen un módulo que no está en DerechoDeModuloPageFilter.ModulosPorRuta (agrega su carpeta): " + string.Join("; ", sinMapear));
        }

        [Fact]
        public void Una_pantalla_dentro_de_una_carpeta_de_modulo_no_exige_otro_modulo()
        {
            var raiz = RaizDelRepositorio();
            var culpables = new List<string>();

            foreach (var (prefijo, modulo) in DerechoDeModuloPageFilter.ModulosPorRuta)
            {
                // Contabilidad aún no tiene pantallas: queda en el filtro para que su carpeta nazca ya protegida.
                var carpeta = Path.Combine(raiz, "Pages", prefijo.Trim('/'));
                if (!Directory.Exists(carpeta)) continue;

                foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.cs", SearchOption.AllDirectories))
                    foreach (Match m in PideModulo.Matches(File.ReadAllText(archivo)))
                        if (m.Groups["modulo"].Value != modulo)
                            culpables.Add($"{Path.GetRelativePath(raiz, archivo)} pide «{m.Groups["modulo"].Value}» dentro de {prefijo}");
            }

            Assert.True(culpables.Count == 0, "Una carpeta de módulo mezcla módulos: " + string.Join("; ", culpables.Distinct()));
        }

        [Fact]
        public void Los_modulos_del_filtro_existen_en_el_catalogo_de_modulos_del_modelo()
        {
            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();
            var catalogo = db.Modulos.Select(m => m.Codigo).ToHashSet();

            foreach (var (prefijo, modulo) in DerechoDeModuloPageFilter.ModulosPorRuta)
                Assert.True(catalogo.Contains(modulo), $"El módulo «{modulo}» de {prefijo} no está en el catálogo de módulos.");
        }

        [Fact]
        public void Program_registra_el_servicio_el_filtro_y_el_middleware_justo_despues_del_de_tenant()
        {
            var program = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "Program.cs"));

            Assert.Contains("AddScoped<IDerechosDeUso, DerechosDeUso>()", program);
            Assert.Contains("options.Filters.Add<DerechoDeModuloPageFilter>()", program);

            var tenant = program.IndexOf("app.UseMiddleware<TenantMiddleware>()", StringComparison.Ordinal);
            var estado = program.IndexOf("app.UseMiddleware<EstadoDelTenantMiddleware>()", StringComparison.Ordinal);
            Assert.True(tenant >= 0 && estado > tenant, "El middleware de estado debe ir después del de tenant: de él lee el tenant de la petición.");

            // Entre los dos no debe haber otro middleware.
            var entre = program.Substring(tenant, estado - tenant);
            Assert.Equal(1, Regex.Matches(entre, @"app\.Use\w*[<(]").Count);
        }
    }
}
