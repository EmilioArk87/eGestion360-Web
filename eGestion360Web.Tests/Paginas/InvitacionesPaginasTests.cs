using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Pages.Admin.Invitaciones;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Las pantallas de invitaciones (ADR-028): quien invita y la lista (administradores) y quien acepta (sin sesión, solo con el token).
    /// </summary>
    public class InvitacionesPaginasTests : IDisposable
    {
        private const string ClaveSegura = "Clave-Muy-Segura-2026!";

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        /// <summary>Arma el enlace como lo haría el sitio: esquema, host y el token como parámetro.</summary>
        private sealed class UrlHelperFalso : IUrlHelper
        {
            public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            public string? Action(UrlActionContext actionContext) => null;
            public string? Content(string? contentPath) => contentPath;
            public bool IsLocalUrl(string? url) => true;
            public string? Link(string? routeName, object? values) => null;
            public string? RouteUrl(UrlRouteContext routeContext)
            {
                var valores = new RouteValueDictionary(routeContext.Values);
                return $"{routeContext.Protocol}://{routeContext.Host}{valores["page"]}?token={valores["token"]}";
            }
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly CorreoEspia _correo = new();
        private readonly DerechosConTope _derechos = new();
        private readonly ApplicationDbContext _db;

        public InvitacionesPaginasTests() => _db = _bd.Crear();

        public void Dispose()
        {
            _db.Dispose();
            _bd.Dispose();
        }

        private ServicioDeInvitaciones Servicio(ApplicationDbContext? db = null) => InvitacionesDePrueba.Servicio(db ?? _bd.Crear(), _reloj, _correo, _derechos);

        private T Preparar<T>(T pagina, HttpContext http) where T : PageModel
        {
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("sitio.prueba");
            pagina.PageContext = new PageContext { HttpContext = http };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            pagina.Url = new UrlHelperFalso();
            return pagina;
        }

        private static HttpContext AdminDeEmpresa(int empresa = DatosBase.EmpresaA) => SesionFalsa.Contexto("empresa_admin", empresa.ToString());

        private Pages.Admin.Invitaciones.CreateModel PaginaCrear(HttpContext http)
            => Preparar(new Pages.Admin.Invitaciones.CreateModel(_bd.Crear(), Servicio(), InvitacionesDePrueba.Personas(_bd.Crear(), _reloj)), http);

        private Pages.Admin.Invitaciones.IndexModel PaginaLista(HttpContext http) => Preparar(new Pages.Admin.Invitaciones.IndexModel(Servicio()), http);

        private AceptarInvitacionModel PaginaAceptar(HttpContext? http = null) => Preparar(new AceptarInvitacionModel(Servicio()), http ?? new DefaultHttpContext());

        private (int IdPersona, int IdRol) PersonaYRol(int empresa = DatosBase.EmpresaA)
        {
            var persona = PersonasDePrueba.Insertar(_db, empresa, "Maria", "Lopez", codigoInterno: "E-" + Guid.NewGuid().ToString("N")[..6]).IdPersona;
            var rol = new EmpresaRol { IdEmpresa = empresa, IdTenant = DatosBase.TenantDe(empresa), Nombre = "Flota" };
            _db.EmpresaRoles.Add(rol);
            _db.SaveChanges();
            return (persona, rol.IdRol);
        }

        /// <summary>Invita desde la pantalla de invitar y devuelve el token que viajó en el correo.</summary>
        private async Task<string> InvitarDesdeLaPantalla(string correo = "maria@x.hn")
        {
            var (persona, rol) = PersonaYRol();
            var pagina = PaginaCrear(AdminDeEmpresa());
            (pagina.Correo, pagina.IdPersonaElegida, pagina.RolLegado, pagina.IdRol) = (correo, persona, "empresa_user", rol);

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            var enlace = System.Net.WebUtility.HtmlDecode(Assert.Single(_correo.Enviados).Html);
            var inicio = enlace.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
            return enlace[inicio..].Split('\'', '"', '<', ' ', '\n')[0];
        }

        // ── Aceptar (sin sesión) ──────────────────────────────────────────────

        [Fact]
        public async Task Abrir_el_enlace_muestra_a_que_empresa_se_invita_y_protege_el_token()
        {
            var token = await InvitarDesdeLaPantalla();
            var http = new DefaultHttpContext();
            var pagina = PaginaAceptar(http);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(token));

            Assert.Equal(EstadoDeConsulta.Valida, pagina.Consulta.Estado);
            Assert.Equal("maria@x.hn", pagina.Consulta.Correo);
            Assert.Equal(token, pagina.Token);
            Assert.Equal("no-referrer", http.Response.Headers["Referrer-Policy"].ToString());   // el token no sale en el encabezado Referer
            Assert.Equal("no-store", http.Response.Headers["Cache-Control"].ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no-existe")]
        public async Task Un_enlace_que_no_sirve_muestra_el_aviso_y_no_el_formulario(string? token)
        {
            var pagina = PaginaAceptar();

            Assert.IsType<PageResult>(await pagina.OnGetAsync(token));

            Assert.Equal(EstadoDeConsulta.NoEncontrada, pagina.Consulta.Estado);
            Assert.Contains("no es válido", pagina.MensajeDeEstado);
        }

        [Fact]
        public async Task Elegir_la_contrasena_activa_la_cuenta_y_lleva_al_inicio_de_sesion()
        {
            var token = await InvitarDesdeLaPantalla();
            var pagina = PaginaAceptar();
            (pagina.Token, pagina.Password, pagina.ConfirmPassword) = (token, ClaveSegura, ClaveSegura);

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            Assert.Contains("activada", pagina.TempData["SuccessMessage"]!.ToString());
            using var db = _bd.Crear();
            var usuario = db.Users.Single(u => u.Email == "maria@x.hn");
            Assert.True(usuario.IsActive);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveSegura, usuario.Password));
            Assert.Equal(EstadoCuenta.Activa, db.Cuentas.Single().Estado);
        }

        [Fact]
        public async Task Una_contrasena_debil_vuelve_al_formulario_con_el_motivo_y_el_enlace_sigue_sirviendo()
        {
            var token = await InvitarDesdeLaPantalla();
            var pagina = PaginaAceptar();
            (pagina.Token, pagina.Password, pagina.ConfirmPassword) = (token, "123456", "123456");

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.NotEmpty(pagina.ModelState.Values.SelectMany(v => v.Errors));
            Assert.Equal(EstadoDeConsulta.Valida, pagina.Consulta.Estado);
            using var db = _bd.Crear();
            Assert.False(db.Users.Single(u => u.Email == "maria@x.hn").IsActive);
        }

        [Fact]
        public async Task Con_el_formulario_invalido_no_se_llama_al_servicio()
        {
            var token = await InvitarDesdeLaPantalla();
            var pagina = PaginaAceptar();
            (pagina.Token, pagina.Password, pagina.ConfirmPassword) = (token, ClaveSegura, "otra");
            pagina.ModelState.AddModelError(nameof(pagina.ConfirmPassword), "Las contraseñas no coinciden");

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            using var db = _bd.Crear();
            Assert.False(db.Users.Single(u => u.Email == "maria@x.hn").IsActive);
            Assert.Null(db.Invitaciones.Single().UsadaEn);
        }

        [Fact]
        public async Task El_formulario_exige_la_contrasena_segura_y_su_confirmacion()
        {
            Assert.NotNull(typeof(AceptarInvitacionModel).GetProperty(nameof(AceptarInvitacionModel.Password))!.GetCustomAttribute<PasswordSeguroAttribute>());
            Assert.NotNull(typeof(AceptarInvitacionModel).GetProperty(nameof(AceptarInvitacionModel.Password))!.GetCustomAttribute<RequiredAttribute>());
            Assert.NotNull(typeof(AceptarInvitacionModel).GetProperty(nameof(AceptarInvitacionModel.ConfirmPassword))!.GetCustomAttribute<CompareAttribute>());
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Un_enlace_ya_usado_no_deja_activar_otra_vez()
        {
            var token = await InvitarDesdeLaPantalla();
            var primera = PaginaAceptar();
            (primera.Token, primera.Password, primera.ConfirmPassword) = (token, ClaveSegura, ClaveSegura);
            Assert.IsType<RedirectToPageResult>(await primera.OnPostAsync());

            var segunda = PaginaAceptar();
            (segunda.Token, segunda.Password, segunda.ConfirmPassword) = (token, "Otra-Clave-Segura-9!", "Otra-Clave-Segura-9!");
            Assert.IsType<PageResult>(await segunda.OnPostAsync());

            Assert.Equal(EstadoDeConsulta.Usada, segunda.Consulta.Estado);
            using var db = _bd.Crear();
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveSegura, db.Users.Single(u => u.Email == "maria@x.hn").Password));
        }

        // ── Invitar ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Invitar_exige_sesion()
        {
            var sinSesion = PaginaCrear(SesionFalsa.Contexto(autenticado: false));
            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await sinSesion.OnGetAsync()).PageName);
            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await sinSesion.OnPostAsync()).PageName);

            // Quién puede invitar lo decide desde F1.6c el filtro de permisos (administracion.invitaciones.crear); ver PermisosPageFilterTests.
            Assert.Empty(_correo.Enviados);
        }

        [Fact]
        public async Task Un_administrador_de_empresa_ve_su_empresa_ya_elegida_y_sus_roles()
        {
            PersonaYRol();
            var pagina = PaginaCrear(AdminDeEmpresa());

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.Equal(DatosBase.EmpresaA, pagina.IdEmpresa);
            Assert.False(pagina.EsAdministradorGeneral);
            Assert.Contains(pagina.Roles, r => r.Text == "Flota");
        }

        [Fact]
        public async Task Un_administrador_de_empresa_no_puede_cambiar_la_empresa_desde_el_formulario()
        {
            var (persona, rol) = PersonaYRol();
            var pagina = PaginaCrear(AdminDeEmpresa());
            (pagina.Correo, pagina.IdEmpresa, pagina.IdPersonaElegida, pagina.RolLegado, pagina.IdRol) = ("x@x.hn", DatosBase.EmpresaB, persona, "empresa_user", rol);

            await pagina.OnPostAsync();

            using var db = _bd.Crear();
            Assert.Equal(DatosBase.EmpresaA, db.Users.Single(u => u.Email == "x@x.hn").EmpresaId);   // el formulario mandó la B; manda la sesión
            Assert.Equal(DatosBase.EmpresaA, db.Invitaciones.Single().IdEmpresa);
        }
        [Fact]
        public async Task Invitar_envia_el_correo_con_el_enlace_del_sitio_y_avisa_en_la_lista()
        {
            var token = await InvitarDesdeLaPantalla("Maria@X.hn");

            var (para, _, html) = Assert.Single(_correo.Enviados);
            Assert.Equal("maria@x.hn", para);
            Assert.Contains("https://sitio.prueba/AceptarInvitacion?token=" + token, System.Net.WebUtility.HtmlDecode(html));
        }

        [Fact]
        public async Task Si_el_servicio_rechaza_la_invitacion_el_formulario_vuelve_con_el_motivo()
        {
            var (persona, _) = PersonaYRol();
            var pagina = PaginaCrear(AdminDeEmpresa());
            (pagina.Correo, pagina.IdPersonaElegida, pagina.RolLegado, pagina.IdRol) = ("x@x.hn", persona, "empresa_user", null);   // un usuario sin rol

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.Contains(pagina.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("rol"));
            Assert.Empty(_correo.Enviados);
        }

        [Fact]
        public async Task Invitar_sin_elegir_persona_o_empresa_vuelve_al_formulario()
        {
            var (_, rol) = PersonaYRol();
            var sinPersona = PaginaCrear(AdminDeEmpresa());
            (sinPersona.Correo, sinPersona.RolLegado, sinPersona.IdRol) = ("x@x.hn", "empresa_user", rol);
            Assert.IsType<PageResult>(await sinPersona.OnPostAsync());
            Assert.True(sinPersona.ModelState.ContainsKey(nameof(sinPersona.IdPersonaElegida)));

            var sinEmpresa = PaginaCrear(SesionFalsa.Contexto("admin", null));
            (sinEmpresa.Correo, sinEmpresa.RolLegado, sinEmpresa.IdPersonaElegida, sinEmpresa.IdRol) = ("x@x.hn", "empresa_user", 1, rol);
            Assert.IsType<PageResult>(await sinEmpresa.OnPostAsync());
            Assert.True(sinEmpresa.ModelState.ContainsKey(nameof(sinEmpresa.IdEmpresa)));
            Assert.Empty(_correo.Enviados);
        }

        [Fact]
        public async Task Si_el_correo_no_sale_se_avisa_y_la_invitacion_queda_para_reenviar()
        {
            _correo.Responde = false;
            var (persona, rol) = PersonaYRol();
            var pagina = PaginaCrear(AdminDeEmpresa());
            (pagina.Correo, pagina.IdPersonaElegida, pagina.RolLegado, pagina.IdRol) = ("x@x.hn", persona, "empresa_user", rol);

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("Index", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            Assert.Contains("no se pudo enviar", pagina.TempData["WarningMessage"]!.ToString());
            Assert.Null(pagina.TempData["SuccessMessage"]);
            using var db = _bd.Crear();
            Assert.Single(db.Invitaciones);
        }

        [Fact]
        public async Task La_busqueda_de_personas_exige_sesion()
        {
            var sinSesion = PaginaCrear(SesionFalsa.Contexto(autenticado: false));
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(await sinSesion.OnGetBuscarPersonasAsync("maria")).StatusCode);
        }

        [Fact]
        public async Task Los_roles_que_se_ofrecen_son_los_de_la_empresa_y_un_administrador_de_empresa_no_ve_los_de_otra()
        {
            PersonaYRol();
            PersonaYRol(DatosBase.EmpresaB);

            var general = PaginaCrear(SesionFalsa.Contexto("admin", null));
            var deB = Assert.IsType<JsonResult>(await general.OnGetRolesAsync(DatosBase.EmpresaB));
            Assert.Single((System.Collections.IEnumerable)deB.Value!);

            var deA = PaginaCrear(AdminDeEmpresa());
            var ajenos = Assert.IsType<JsonResult>(await deA.OnGetRolesAsync(DatosBase.EmpresaB));
            Assert.Empty((System.Collections.IEnumerable)ajenos.Value!);
        }

        // ── Lista ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task La_lista_exige_sesion()
        {
            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(await PaginaLista(SesionFalsa.Contexto(autenticado: false)).OnGetAsync()).PageName);
        }

        [Fact]
        public async Task La_lista_muestra_las_invitaciones_de_la_empresa()
        {
            await InvitarDesdeLaPantalla();
            var pagina = PaginaLista(AdminDeEmpresa());

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            var fila = Assert.Single(pagina.Filas);
            Assert.Equal("maria@x.hn", fila.Correo);
            Assert.Equal(EstadoDeInvitacion.Pendiente, fila.Estado);
        }

        [Fact]
        public async Task Reenviar_desde_la_lista_manda_un_enlace_nuevo_y_el_viejo_deja_de_servir()
        {
            var viejo = await InvitarDesdeLaPantalla();
            int id;
            using (var db = _bd.Crear()) id = db.Invitaciones.Single().IdInvitacion;
            var pagina = PaginaLista(AdminDeEmpresa());

            Assert.IsType<RedirectToPageResult>(await pagina.OnPostReenviarAsync(id));

            Assert.Contains("reenviada", pagina.TempData["SuccessMessage"]!.ToString());
            Assert.Equal(2, _correo.Enviados.Count);
            using var db2 = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoEncontrada, (await Servicio(db2).ConsultarAsync(viejo)).Estado);
        }

        [Fact]
        public async Task Anular_desde_la_lista_y_los_avisos_cuando_no_se_puede()
        {
            await InvitarDesdeLaPantalla();
            int id;
            using (var db = _bd.Crear()) id = db.Invitaciones.Single().IdInvitacion;

            var anular = PaginaLista(AdminDeEmpresa());
            Assert.IsType<RedirectToPageResult>(await anular.OnPostAnularAsync(id));
            Assert.Contains("anulada", anular.TempData["SuccessMessage"]!.ToString());

            var otra = PaginaLista(AdminDeEmpresa());
            await otra.OnPostAnularAsync(9999);
            Assert.NotNull(otra.TempData["ErrorMessage"]);

            var reenviar = PaginaLista(AdminDeEmpresa(DatosBase.EmpresaB));
            await reenviar.OnPostReenviarAsync(id);                              // otra empresa: no la ve
            Assert.NotNull(reenviar.TempData["ErrorMessage"]);
        }

        // ── Cómo está puesto en el sitio ──────────────────────────────────────

        [Fact]
        public void Aceptar_una_invitacion_es_una_pantalla_sin_sesion_y_la_lista_de_invitaciones_exige_tenant_de_soporte()
        {
            Assert.True(AmbitoDePlataformaPageFilter.EsRutaSinSesion("/AceptarInvitacion"));
            Assert.True(TenantDeSoporteRequeridoPageFilter.Aplica("/Admin/Invitaciones/Index"));
            Assert.True(TenantDeSoporteRequeridoPageFilter.Aplica("/Admin/Invitaciones/Create"));
            Assert.False(AmbitoDePlataformaPageFilter.EsRutaDelAdministradorGeneral("/Admin/Invitaciones/Index"));
        }
    }
}
