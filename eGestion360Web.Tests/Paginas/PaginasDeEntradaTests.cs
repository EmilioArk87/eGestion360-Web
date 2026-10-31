using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El inicio de sesión de punta a punta, en tres pasos (F1.5): clave, segundo factor y elegir dónde trabajar. La identidad solo existe
    /// cuando se emite la cookie al final; antes de eso, lo que hay es una entrada en curso que no da acceso a nada.
    /// </summary>
    public class PaginasDeEntradaTests : IDisposable
    {
        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ApplicationDbContext _db;
        private readonly ServiceProvider _proveedor;
        // La misma protección en toda la prueba: el secreto de MFA se guarda y se lee con la misma llave.
        private readonly EphemeralDataProtectionProvider _proteccion = new();
        private readonly Microsoft.Extensions.Options.IOptions<OpcionesDeIdentidad> _opciones = IdentidadDePrueba.Opciones();

        public PaginasDeEntradaTests()
        {
            _db = _bd.Crear();
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddDataProtection();
            servicios.AddAuthentication(ReclamosDeIdentidad.Esquema).AddCookie(ReclamosDeIdentidad.Esquema, o => o.Cookie.Name = ".eg360.auth");
            _proveedor = servicios.BuildServiceProvider();
        }

        public void Dispose()
        {
            _db.Dispose();
            _proveedor.Dispose();
            _bd.Dispose();
        }

        private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

        private HttpContext NuevoHttp() => new DefaultHttpContext { Session = new SesionFalsa(), RequestServices = _proveedor };

        private DirectorioDeCuentas Directorio() => new(_db, IdentidadDePrueba.Tenant());
        private BloqueoDeCuentas Bloqueo() => new(_db, _opciones, _reloj);
        private ServicioMfa Mfa() => new(_db, _proteccion, _opciones, _reloj);

        private Autenticador ArmarAutenticador()
            => new(_db, Directorio(), Bloqueo(), Mfa(), new PasswordService(), _opciones, NullLogger<Autenticador>.Instance, _reloj);

        private T Preparar<T>(T pagina, HttpContext http) where T : PageModel
        {
            pagina.PageContext = new PageContext { HttpContext = http };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        private LoginModel Login(HttpContext http, string usuario, string clave)
        {
            var pagina = Preparar(new LoginModel(ArmarAutenticador(), Directorio(), NullLogger<LoginModel>.Instance, _reloj), http);
            (pagina.Username, pagina.Password) = (usuario, clave);
            return pagina;
        }

        private static string Mensaje(PageModel pagina) => Assert.Single(Assert.Single(pagina.ModelState.Values).Errors).ErrorMessage;

        private static string Pagina(IActionResult resultado) => Assert.IsType<RedirectToPageResult>(resultado).PageName!;

        private string CodigoTotp(string secreto, DateTime? en = null)
            => new Totp(Base32Encoding.ToBytes(secreto)).ComputeTotp(en ?? Ahora);

        /// <summary>Activa la verificación en dos pasos de una cuenta y devuelve su secreto, para escribir códigos válidos.</summary>
        private async Task<string> ActivarMfaAsync(int idCuenta)
        {
            var servicio = Mfa();
            var alta = await servicio.IniciarEnrolamientoAsync(idCuenta, "x@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(idCuenta, CodigoTotp(alta.Secreto));
            return alta.Secreto;
        }

        // ── PASO 1: LA CLAVE ──────────────────────────────────────────────────

        [Fact]
        public async Task Una_clave_correcta_deja_una_entrada_en_curso_y_no_emite_ninguna_identidad()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", "Clave-Segura-1", usuario: "ana");
            var http = NuevoHttp();

            var resultado = await Login(http, "ana", "Clave-Segura-1").OnPostAsync();

            Assert.Equal("/SeleccionarContexto", Pagina(resultado));
            var entrada = EntradaEnCurso.Leer(http.Session, Ahora);
            Assert.NotNull(entrada);
            Assert.Equal(cuenta.IdCuenta, entrada!.IdCuenta);
            Assert.Equal("ana", entrada.Nombre);
            Assert.Equal(ExigenciaDeMfa.NoRequerida, entrada.Exigencia);
            Assert.False(entrada.MfaVerificada);
            Assert.False(AuthHelper.HayCuenta(http));   // la clave sola no da acceso a nada
        }

        [Fact]
        public async Task Una_clave_equivocada_no_deja_entrada_y_el_mensaje_no_dice_si_la_cuenta_existe()
        {
            IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", "Clave-Segura-1", usuario: "ana");
            var existente = Login(NuevoHttp(), "ana", "mala");
            var inexistente = Login(NuevoHttp(), "nadie", "mala");

            Assert.IsType<PageResult>(await existente.OnPostAsync());
            Assert.IsType<PageResult>(await inexistente.OnPostAsync());

            Assert.Equal(LoginModel.MensajeIncorrectas, Mensaje(existente));
            Assert.Equal(Mensaje(existente), Mensaje(inexistente));
            Assert.Null(EntradaEnCurso.Leer(existente.HttpContext.Session, Ahora));
        }

        [Fact]
        public async Task Una_cuenta_bloqueada_da_el_mismo_mensaje_que_una_clave_equivocada()
        {
            IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", "Clave-Segura-1", usuario: "ana");
            for (var i = 0; i < 5; i++) await Login(NuevoHttp(), "ana", "mala").OnPostAsync();

            var bloqueada = Login(NuevoHttp(), "ana", "Clave-Segura-1");
            var equivocada = Login(NuevoHttp(), "nadie", "mala");
            await bloqueada.OnPostAsync();
            await equivocada.OnPostAsync();

            Assert.Equal(Mensaje(equivocada), Mensaje(bloqueada));
            Assert.Null(EntradaEnCurso.Leer(bloqueada.HttpContext.Session, Ahora));
        }

        [Fact]
        public async Task Sin_persona_vinculada_no_entra_pero_el_mensaje_solo_aparece_con_la_clave_correcta()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "sinpersona@prueba.local", "Clave-Segura-1", usuario: "sinpersona");
            var m = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, null);
            IdentidadDePrueba.NuevoAcceso(_db, m, DatosBase.EmpresaA);

            var conClave = Login(NuevoHttp(), "sinpersona", "Clave-Segura-1");
            var sinClave = Login(NuevoHttp(), "sinpersona", "otra");
            await conClave.OnPostAsync();
            await sinClave.OnPostAsync();

            Assert.Equal(LoginModel.MensajeSinPersona, Mensaje(conClave));
            Assert.Equal(LoginModel.MensajeIncorrectas, Mensaje(sinClave));   // quien no sabe la clave no se entera
        }

        [Fact]
        public async Task Una_cuenta_sin_ninguna_empresa_recibe_el_mensaje_de_sin_acceso()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "nada@prueba.local", "Clave-Segura-1", usuario: "nada");
            var pagina = Login(NuevoHttp(), "nada", "Clave-Segura-1");

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.Equal(LoginModel.MensajeSinAcceso, Mensaje(pagina));
        }

        [Fact]
        public async Task Al_operador_se_le_manda_a_activar_la_verificacion_y_si_ya_la_tiene_a_escribir_el_codigo()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", "Clave-Segura-1", usuario: "op", operador: true);

            var http1 = NuevoHttp();
            Assert.Equal("/MfaAlta", Pagina(await Login(http1, "op", "Clave-Segura-1").OnPostAsync()));
            Assert.Equal(ExigenciaDeMfa.Alta, EntradaEnCurso.Leer(http1.Session, Ahora)!.Exigencia);

            await ActivarMfaAsync(cuenta.IdCuenta);
            var http2 = NuevoHttp();
            Assert.Equal("/Mfa", Pagina(await Login(http2, "op", "Clave-Segura-1").OnPostAsync()));
            Assert.Equal(ExigenciaDeMfa.Verificar, EntradaEnCurso.Leer(http2.Session, Ahora)!.Exigencia);
        }

        [Fact]
        public void Quien_ya_tiene_sesion_no_ve_el_formulario()
        {
            LoginModel Pagina(HttpContext http) => Preparar(new LoginModel(ArmarAutenticador(), Directorio(), NullLogger<LoginModel>.Instance, _reloj), http);

            Assert.Equal("/MainMenu", PaginasDeEntradaTests.Pagina(Pagina(SesionFalsa.Contexto("empresa_user", "1")).OnGet()));
            Assert.Equal("/ChangePassword", PaginasDeEntradaTests.Pagina(Pagina(SesionFalsa.Contexto("empresa_user", "1", debeCambiarClave: true)).OnGet()));
            Assert.IsType<PageResult>(Pagina(NuevoHttp()).OnGet());
        }

        // ── PASO 2: EL SEGUNDO FACTOR ─────────────────────────────────────────

        private (Cuenta Cuenta, string Secreto, HttpContext Http) EntradaConMfaActiva(bool conExigencia = true)
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", "Clave-Segura-1", usuario: "op", operador: true);
            var secreto = ActivarMfaAsync(cuenta.IdCuenta).GetAwaiter().GetResult();
            var http = NuevoHttp();
            new EntradaEnCurso(cuenta.IdCuenta, "op", conExigencia ? ExigenciaDeMfa.Verificar : ExigenciaDeMfa.NoRequerida, false).Guardar(http.Session, Ahora);
            return (cuenta, secreto, http);
        }

        private MfaModel PaginaMfa(HttpContext http) => Preparar(new MfaModel(Mfa(), Bloqueo(), _reloj), http);

        [Fact]
        public void Sin_entrada_en_curso_el_segundo_factor_manda_al_inicio_de_sesion()
        {
            Assert.Equal("/Login", Pagina(PaginaMfa(NuevoHttp()).OnGet()));
        }

        [Fact]
        public void Quien_no_debe_verificar_no_se_queda_en_la_pagina_del_codigo()
        {
            var (_, _, http) = EntradaConMfaActiva(conExigencia: false);

            Assert.Equal("/SeleccionarContexto", Pagina(PaginaMfa(http).OnGet()));
        }

        [Fact]
        public async Task Un_codigo_correcto_marca_la_entrada_como_verificada_y_sigue_con_el_sitio()
        {
            var (cuenta, secreto, http) = EntradaConMfaActiva();
            var pagina = PaginaMfa(http);
            pagina.Codigo = CodigoTotp(secreto, Ahora.AddSeconds(30));   // el del paso siguiente: el actual se gastó al activar

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("/SeleccionarContexto", Pagina(resultado));
            var entrada = EntradaEnCurso.Leer(http.Session, Ahora)!;
            Assert.True(entrada.MfaVerificada);
            Assert.Equal(cuenta.IdCuenta, entrada.IdCuenta);
        }

        [Fact]
        public async Task Un_codigo_equivocado_no_avanza()
        {
            var (_, _, http) = EntradaConMfaActiva();
            var pagina = PaginaMfa(http);
            pagina.Codigo = "000000";

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.False(pagina.ModelState.IsValid);
            Assert.False(EntradaEnCurso.Leer(http.Session, Ahora)!.MfaVerificada);
        }

        [Fact]
        public async Task Cinco_codigos_equivocados_bloquean_la_cuenta_y_cierran_la_entrada()
        {
            var (cuenta, _, http) = EntradaConMfaActiva();

            IActionResult? ultimo = null;
            for (var i = 0; i < 5; i++)
            {
                var pagina = PaginaMfa(http);
                pagina.Codigo = $"00000{i}";
                ultimo = await pagina.OnPostAsync();
            }

            Assert.Equal("/Login", Pagina(ultimo!));
            Assert.Null(EntradaEnCurso.Leer(http.Session, Ahora));
            Assert.NotNull(await Bloqueo().BloqueadaHastaAsync(cuenta.IdCuenta));
        }

        [Fact]
        public async Task Un_codigo_de_recuperacion_sirve_en_lugar_del_codigo_de_la_aplicacion()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", "Clave-Segura-1", usuario: "op", operador: true);
            var alta = await Mfa().IniciarEnrolamientoAsync(cuenta.IdCuenta, "op@prueba.local");
            var recuperacion = (await Mfa().ConfirmarEnrolamientoAsync(cuenta.IdCuenta, CodigoTotp(alta.Secreto)))!;
            var http = NuevoHttp();
            new EntradaEnCurso(cuenta.IdCuenta, "op", ExigenciaDeMfa.Verificar, false).Guardar(http.Session, Ahora);
            var pagina = PaginaMfa(http);
            pagina.Codigo = recuperacion[0];

            Assert.Equal("/SeleccionarContexto", Pagina(await pagina.OnPostAsync()));
        }

        [Fact]
        public void Una_entrada_vencida_obliga_a_escribir_la_clave_otra_vez()
        {
            var (_, _, http) = EntradaConMfaActiva();
            _reloj.Avanzar(EntradaEnCurso.Vigencia + TimeSpan.FromSeconds(1));

            Assert.Equal("/Login", Pagina(PaginaMfa(http).OnGet()));
            Assert.Null(http.Session.GetString("Entrada.Cuenta"));
        }

        // ── ACTIVAR LA VERIFICACIÓN EN DOS PASOS ──────────────────────────────

        private MfaAltaModel PaginaAlta(HttpContext http) => Preparar(new MfaAltaModel(_db, Mfa(), _reloj), http);

        private (Cuenta Cuenta, HttpContext Http) EntradaDeAlta()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", "Clave-Segura-1", usuario: "op", operador: true);
            var http = NuevoHttp();
            new EntradaEnCurso(cuenta.IdCuenta, "op", ExigenciaDeMfa.Alta, false).Guardar(http.Session, Ahora);
            return (cuenta, http);
        }

        [Fact]
        public async Task Al_pedirsela_la_pagina_de_alta_muestra_el_QR_y_la_clave()
        {
            var (_, http) = EntradaDeAlta();
            var pagina = PaginaAlta(http);

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.NotNull(pagina.Enrolamiento);
            Assert.Contains("<svg", pagina.Enrolamiento!.QrSvg);
            Assert.False(pagina.EsVoluntaria);
            Assert.Null(pagina.CodigosDeRecuperacion);
        }

        [Fact]
        public async Task Confirmar_el_alta_muestra_los_diez_codigos_y_deja_la_entrada_lista_para_seguir_sin_pedir_otro_codigo()
        {
            var (cuenta, http) = EntradaDeAlta();
            var primera = PaginaAlta(http);
            await primera.OnGetAsync();
            var pagina = PaginaAlta(http);
            pagina.Codigo = CodigoTotp(primera.Enrolamiento!.Secreto);

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.Equal(10, pagina.CodigosDeRecuperacion!.Count);
            Assert.True(await Mfa().EstaActivaAsync(cuenta.IdCuenta));
            var entrada = EntradaEnCurso.Leer(http.Session, Ahora)!;
            Assert.True(entrada.MfaVerificada);
            Assert.Equal("/SeleccionarContexto", entrada.SiguientePagina);
            Assert.Equal("/SeleccionarContexto", Pagina(PaginaAlta(http).OnPostContinuar()));
        }

        [Fact]
        public async Task Un_codigo_que_no_vale_deja_el_alta_a_medias_con_el_mismo_QR()
        {
            var (cuenta, http) = EntradaDeAlta();
            var primera = PaginaAlta(http);
            await primera.OnGetAsync();
            var pagina = PaginaAlta(http);
            pagina.Codigo = "000000";

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.False(pagina.ModelState.IsValid);
            Assert.Null(pagina.CodigosDeRecuperacion);
            Assert.Equal(primera.Enrolamiento!.Secreto, pagina.Enrolamiento!.Secreto);   // el QR ya escaneado sigue sirviendo
            Assert.False(await Mfa().EstaActivaAsync(cuenta.IdCuenta));
            Assert.False(EntradaEnCurso.Leer(http.Session, Ahora)!.MfaVerificada);
        }

        [Fact]
        public async Task Quien_ya_la_tiene_activa_no_vuelve_a_darse_de_alta()
        {
            var (cuenta, http) = EntradaDeAlta();
            await ActivarMfaAsync(cuenta.IdCuenta);
            // Con la verificación ya activa, la entrada pasa a pedir el código en lugar del alta.
            new EntradaEnCurso(cuenta.IdCuenta, "op", ExigenciaDeMfa.Verificar, false).Guardar(http.Session, Ahora);

            Assert.Equal("/Mfa", Pagina(await PaginaAlta(http).OnGetAsync()));
        }

        [Fact]
        public async Task Quien_tiene_sesion_puede_activarla_cuando_quiera_y_al_terminar_vuelve_al_menu()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", "Clave-Segura-1", usuario: "ana");
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa() with { IdCuenta = cuenta.IdCuenta });
            http.RequestServices = _proveedor;
            var pagina = PaginaAlta(http);

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.True(pagina.EsVoluntaria);
            Assert.Equal("/MainMenu", Pagina(PaginaAlta(http).OnPostContinuar()));
        }

        [Fact]
        public async Task Sin_entrada_ni_sesion_el_alta_manda_al_inicio_de_sesion()
        {
            Assert.Equal("/Login", Pagina(await PaginaAlta(NuevoHttp()).OnGetAsync()));
            Assert.Equal("/Login", Pagina(await PaginaAlta(NuevoHttp()).OnPostAsync()));
            Assert.Equal("/Login", Pagina(PaginaAlta(NuevoHttp()).OnPostContinuar()));
        }

        // ── PASO 3: ELEGIR DÓNDE TRABAJAR Y EMITIR LA COOKIE ──────────────────

        private SeleccionarContextoModel PaginaContexto(HttpContext http)
            => Preparar(new SeleccionarContextoModel(_db, Directorio(), ArmarAutenticador(), _reloj), http);

        private HttpContext EntradaLista(Cuenta cuenta, string nombre = "ana", ExigenciaDeMfa exigencia = ExigenciaDeMfa.NoRequerida, bool mfa = false)
        {
            var http = NuevoHttp();
            new EntradaEnCurso(cuenta.IdCuenta, nombre, exigencia, mfa).Guardar(http.Session, Ahora);
            return http;
        }

        /// <summary>Una cuenta con un sitio en el tenant A (empresa A) y otro en el tenant B (empresa B).</summary>
        private (Cuenta Cuenta, Membresia MembresiaA, Membresia MembresiaB) CuentaConDosSitios()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", usuario: "emilio");
            var mA = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA));
            var mB = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantB, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaB));
            IdentidadDePrueba.NuevoAcceso(_db, mA, DatosBase.EmpresaA);
            IdentidadDePrueba.NuevoAcceso(_db, mB, DatosBase.EmpresaB);
            return (cuenta, mA, mB);
        }

        [Fact]
        public async Task Sin_entrada_ni_cookie_se_vuelve_al_inicio_de_sesion()
        {
            var pagina = PaginaContexto(NuevoHttp());

            Assert.Equal("/Login", Pagina(await pagina.OnGetAsync()));
            Assert.Equal("/Login", Pagina(await pagina.OnPostAsync()));
        }

        [Fact]
        public async Task No_se_puede_saltar_el_segundo_factor_yendo_directo_a_elegir_sitio()
        {
            var (cuenta, _, _) = EntradaConMfaActiva();
            var http = EntradaLista(cuenta, exigencia: ExigenciaDeMfa.Verificar, mfa: false);
            var pagina = PaginaContexto(http);
            pagina.Clave = "plataforma";

            Assert.Equal("/Mfa", Pagina(await pagina.OnGetAsync()));
            Assert.Equal("/Mfa", Pagina(await pagina.OnPostAsync()));
            Assert.False(AuthHelper.HayCuenta(http));
        }

        [Fact]
        public async Task Con_un_solo_sitio_se_entra_directo_y_se_emite_la_cookie_con_ese_sitio()
        {
            var (cuenta, membresia, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", idEmpresa: DatosBase.EmpresaB, rolLegado: "empresa_admin");
            var http = EntradaLista(cuenta, "ana");
            http.Session.SetString(AuthHelper.ClaveTenantDeSoporte, "9");   // restos de una sesión anterior

            var resultado = await PaginaContexto(http).OnGetAsync();

            Assert.Equal("/MainMenu", Pagina(resultado));
            Assert.True(AuthHelper.IsAuthenticated(http));
            Assert.Equal(cuenta.IdCuenta, AuthHelper.GetCuentaId(http));
            Assert.Equal(DatosBase.EmpresaB, AuthHelper.GetEmpresaId(http));
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantId(http));
            Assert.Equal(membresia.IdMembresia, AuthHelper.GetMembresiaId(http));
            Assert.True(AuthHelper.IsEmpresaAdmin(http));
            Assert.Equal("ana", AuthHelper.GetNombreUsuario(http));
            Assert.Equal("Emilio Garay", AuthHelper.NombreVisible(http));
            Assert.Null(EntradaEnCurso.Leer(http.Session, Ahora));                       // la entrada en curso se cierra
            Assert.Null(http.Session.GetString(AuthHelper.ClaveTenantDeSoporte));         // el soporte de otra sesión no se hereda
            Assert.Contains(".eg360.auth", http.Response.Headers.SetCookie.ToString());
            Assert.Equal(Ahora, _db.Cuentas.Single(c => c.IdCuenta == cuenta.IdCuenta).FechaUltimoAcceso);
        }

        [Fact]
        public async Task La_cookie_recuerda_si_se_probo_el_segundo_factor()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local");
            var http = EntradaLista(cuenta, exigencia: ExigenciaDeMfa.Verificar, mfa: true);

            await PaginaContexto(http).OnGetAsync();

            Assert.Equal("1", http.User.FindFirst(ReclamosDeIdentidad.MfaVerificada)?.Value);
        }

        [Fact]
        public async Task Con_varios_sitios_se_muestra_la_lista_y_no_se_emite_nada_hasta_elegir()
        {
            var (cuenta, _, _) = CuentaConDosSitios();
            var http = EntradaLista(cuenta);
            var pagina = PaginaContexto(http);

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.Equal(2, pagina.Contextos.Count);
            Assert.False(pagina.EsCambio);
            Assert.False(AuthHelper.HayCuenta(http));
        }

        [Fact]
        public async Task Elegir_un_sitio_de_la_lista_entra_a_ese_sitio_y_no_a_otro()
        {
            var (cuenta, _, mB) = CuentaConDosSitios();
            var http = EntradaLista(cuenta);
            var pagina = PaginaContexto(http);
            pagina.Clave = ContextoDeAcceso.ClaveDe(mB.IdMembresia, DatosBase.EmpresaB);

            Assert.Equal("/MainMenu", Pagina(await pagina.OnPostAsync()));

            Assert.Equal(DatosBase.EmpresaB, AuthHelper.GetEmpresaId(http));
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantId(http));
        }

        [Theory]
        [InlineData("")]
        [InlineData("9999:9999")]
        [InlineData("plataforma")]          // una cuenta que no es operador no puede elegir la plataforma
        [InlineData("1:1; DROP TABLE x")]
        public async Task Un_sitio_que_no_es_de_la_cuenta_se_rechaza_aunque_el_formulario_lo_nombre(string clave)
        {
            var (cuenta, _, _) = CuentaConDosSitios();
            var http = EntradaLista(cuenta);
            var pagina = PaginaContexto(http);
            pagina.Clave = clave;

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.False(pagina.ModelState.IsValid);
            Assert.False(AuthHelper.HayCuenta(http));
        }

        [Fact]
        public async Task El_acceso_de_otra_persona_tampoco_se_puede_elegir()
        {
            var (cuenta, _, _) = CuentaConDosSitios();
            var ajena = IdentidadDePrueba.CuentaDeEmpresa(_db, "ajena@prueba.local", idEmpresa: DatosBase.EmpresaA2);
            var http = EntradaLista(cuenta);
            var pagina = PaginaContexto(http);
            pagina.Clave = ContextoDeAcceso.ClaveDe(ajena.Membresia.IdMembresia, DatosBase.EmpresaA2);

            Assert.IsType<PageResult>(await pagina.OnPostAsync());

            Assert.False(AuthHelper.HayCuenta(http));
        }

        [Fact]
        public async Task El_operador_puede_elegir_la_plataforma_y_entra_como_admin_sin_tenant_ni_empresa()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", usuario: "op", operador: true);
            var m = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA));
            IdentidadDePrueba.NuevoAcceso(_db, m, DatosBase.EmpresaA);
            var http = EntradaLista(cuenta, "op", ExigenciaDeMfa.Verificar, mfa: true);
            var pagina = PaginaContexto(http);
            pagina.Clave = ContextoDeAcceso.ClaveDePlataforma;

            Assert.Equal("/MainMenu", Pagina(await pagina.OnPostAsync()));

            Assert.True(AuthHelper.IsAdmin(http));
            Assert.True(AuthHelper.IsOperador(http));
            Assert.Null(AuthHelper.GetEmpresaId(http));
            Assert.Null(AuthHelper.GetTenantId(http));
            Assert.Null(AuthHelper.GetMembresiaId(http));
        }

        [Fact]
        public async Task El_operador_tambien_puede_trabajar_como_persona_de_una_empresa()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "op@prueba.local", usuario: "op", operador: true);
            var m = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA));
            IdentidadDePrueba.NuevoAcceso(_db, m, DatosBase.EmpresaA, "empresa_admin");
            var http = EntradaLista(cuenta, "op", ExigenciaDeMfa.Verificar, mfa: true);
            var pagina = PaginaContexto(http);
            pagina.Clave = ContextoDeAcceso.ClaveDe(m.IdMembresia, DatosBase.EmpresaA);

            await pagina.OnPostAsync();

            Assert.True(AuthHelper.IsEmpresaAdmin(http));
            Assert.False(AuthHelper.IsAdmin(http));
            Assert.Equal(DatosBase.EmpresaA, AuthHelper.GetEmpresaId(http));
        }

        [Fact]
        public async Task Con_el_cambio_de_clave_pendiente_se_emite_la_cookie_y_se_va_a_cambiarla()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local");
            cuenta.DebeCambiarClave = true;
            _db.SaveChanges();
            var http = EntradaLista(cuenta);

            Assert.Equal("/ChangePassword", Pagina(await PaginaContexto(http).OnGetAsync()));

            Assert.True(AuthHelper.MustChangePassword(http));
            Assert.False(AuthHelper.IsAuthenticated(http));
        }

        [Fact]
        public async Task En_la_ventana_de_gracia_se_avisa_de_la_verificacion_al_entrar()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "jefe@prueba.local", rolLegado: "empresa_admin");
            var http = EntradaLista(cuenta, exigencia: ExigenciaDeMfa.Aviso);
            var pagina = PaginaContexto(http);

            Assert.Equal("/MainMenu", Pagina(await pagina.OnGetAsync()));

            Assert.NotNull(pagina.TempData["WarningMessage"]);
        }

        [Fact]
        public async Task Una_cuenta_que_se_desactiva_a_mitad_de_la_entrada_no_entra()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local");
            var http = EntradaLista(cuenta);
            cuenta.Estado = EstadoCuenta.Inactiva;
            _db.SaveChanges();

            Assert.Equal("/Login", Pagina(await PaginaContexto(http).OnGetAsync()));

            Assert.False(AuthHelper.HayCuenta(http));
            Assert.Null(EntradaEnCurso.Leer(http.Session, Ahora));
        }

        // ── Cambiar de sitio con la sesión iniciada ───────────────────────────

        [Fact]
        public async Task Con_sesion_iniciada_se_puede_cambiar_de_sitio_revalidando_la_cuenta_y_conservando_el_segundo_factor()
        {
            var (cuenta, mA, mB) = CuentaConDosSitios();
            var actual = DatosDeIdentidadDePrueba.Empresa(idEmpresa: DatosBase.EmpresaA, idTenant: DatosBase.TenantA) with
            {
                IdCuenta = cuenta.IdCuenta, IdMembresia = mA.IdMembresia, MfaVerificada = true, NombreUsuario = "emilio"
            };
            var http = SesionFalsa.ConIdentidad(actual);
            http.RequestServices = _proveedor;

            var pagina = PaginaContexto(http);
            Assert.IsType<PageResult>(await pagina.OnGetAsync());      // con sesión no entra solo: deja elegir
            Assert.True(pagina.EsCambio);

            pagina = PaginaContexto(http);
            pagina.Clave = ContextoDeAcceso.ClaveDe(mB.IdMembresia, DatosBase.EmpresaB);
            Assert.Equal("/MainMenu", Pagina(await pagina.OnPostAsync()));

            Assert.Equal(DatosBase.EmpresaB, AuthHelper.GetEmpresaId(http));
            Assert.Equal(DatosBase.TenantB, AuthHelper.GetTenantId(http));
            Assert.Equal("1", http.User.FindFirst(ReclamosDeIdentidad.MfaVerificada)?.Value);
            Assert.Equal("emilio", AuthHelper.GetNombreUsuario(http));
        }

        // ── Salir ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Salir_borra_la_cookie_y_vacia_la_sesion()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa());
            http.RequestServices = _proveedor;
            http.Session.SetString("Cualquier", "cosa");

            await ArmarAutenticador().SalirAsync(http);

            Assert.Empty(http.Session.Keys);
            Assert.Contains(".eg360.auth=;", http.Response.Headers.SetCookie.ToString());   // la cookie se expira
        }
    }
}
