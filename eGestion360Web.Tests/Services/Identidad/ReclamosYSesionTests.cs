using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Identidad
{
    internal static class DatosDeIdentidadDePrueba
    {
        public static DatosDeIdentidad Empresa(string rol = "empresa_user", int idEmpresa = 1, int idTenant = 1, int? idRol = 7, bool debeCambiarClave = false) => new(
            IdCuenta: 10, IdUserLegado: 4, NombreUsuario: "egaray", Correo: "egaray@prueba.local", NombrePersona: "Emilio Garay", Rol: rol,
            EsOperador: false, DebeCambiarClave: debeCambiarClave, IdMembresia: 20, IdTenant: idTenant, CodigoTenant: "SIP", IdEmpresa: idEmpresa,
            IdRolEmpresa: idRol, MfaVerificada: false, Sello: "sello-1");

        public static DatosDeIdentidad Plataforma() => new(
            IdCuenta: 10, IdUserLegado: 1, NombreUsuario: "admin", Correo: "admin@prueba.local", NombrePersona: "Emilio Garay", Rol: "admin",
            EsOperador: true, DebeCambiarClave: false, IdMembresia: null, IdTenant: null, CodigoTenant: null, IdEmpresa: null,
            IdRolEmpresa: null, MfaVerificada: true, Sello: "sello-2");
    }

    /// <summary>Lo que viaja en la cookie y cómo se lee (F1.5, D50).</summary>
    public class ReclamosDeIdentidadTests
    {
        [Fact]
        public void Lo_que_se_guarda_en_la_cookie_se_lee_igual()
        {
            var datos = DatosDeIdentidadDePrueba.Empresa();

            Assert.Equal(datos, ReclamosDeIdentidad.Leer(ReclamosDeIdentidad.Crear(datos)));
        }

        [Fact]
        public void El_contexto_de_plataforma_no_lleva_membresia_tenant_ni_empresa()
        {
            var datos = DatosDeIdentidadDePrueba.Plataforma();
            var principal = ReclamosDeIdentidad.Crear(datos);

            Assert.Null(principal.FindFirst(ReclamosDeIdentidad.Membresia));
            Assert.Null(principal.FindFirst(ReclamosDeIdentidad.Tenant));
            Assert.Null(principal.FindFirst(ReclamosDeIdentidad.Empresa));
            Assert.Equal("1", principal.FindFirst(ReclamosDeIdentidad.Operador)?.Value);
            Assert.Equal(datos, ReclamosDeIdentidad.Leer(principal));
        }

        [Fact]
        public void Quien_no_esta_autenticado_o_no_trae_cuenta_no_tiene_identidad()
        {
            Assert.Null(ReclamosDeIdentidad.Leer(null));
            Assert.Null(ReclamosDeIdentidad.Leer(new ClaimsPrincipal(new ClaimsIdentity())));
            Assert.Null(ReclamosDeIdentidad.Leer(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "x") }, "otro"))));
        }

        [Fact]
        public void La_identidad_usa_el_esquema_de_la_cookie_y_los_tipos_estandar_de_nombre_y_rol()
        {
            var identidad = (ClaimsIdentity)ReclamosDeIdentidad.Crear(DatosDeIdentidadDePrueba.Empresa()).Identity!;

            Assert.Equal(ReclamosDeIdentidad.Esquema, identidad.AuthenticationType);
            Assert.Equal("egaray", identidad.Name);
            Assert.True(identidad.HasClaim(ClaimTypes.Role, "empresa_user"));
            Assert.Equal("10", identidad.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }

        [Fact]
        public void El_sello_cambia_con_cada_cosa_que_decide_el_acceso()
        {
            string Sello(string cuenta = "activa", bool op = false, bool cambiar = false, string? m = "activa", int? p = 5, string? rol = "empresa_user", int? idRol = 7, bool activo = true)
                => ReclamosDeIdentidad.CalcularSello(cuenta, op, cambiar, m, p, rol, idRol, activo);

            var basico = Sello();
            Assert.Equal(basico, Sello());
            Assert.NotEqual(basico, Sello(cuenta: "inactiva"));
            Assert.NotEqual(basico, Sello(op: true));
            Assert.NotEqual(basico, Sello(cambiar: true));
            Assert.NotEqual(basico, Sello(m: "suspendida"));
            Assert.NotEqual(basico, Sello(p: 6));
            Assert.NotEqual(basico, Sello(rol: "empresa_admin"));
            Assert.NotEqual(basico, Sello(idRol: 8));
            Assert.NotEqual(basico, Sello(activo: false));
        }
    }

    /// <summary>AuthHelper lee la cookie, no la sesión (F1.5, D50).</summary>
    public class AuthHelperConIdentidadTests
    {
        [Fact]
        public void Con_una_identidad_de_empresa_se_leen_rol_empresa_y_rol_de_empresa()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: 4, idTenant: 2, idRol: 9));

            Assert.True(AuthHelper.IsAuthenticated(http));
            Assert.True(AuthHelper.IsEmpresaAdmin(http));
            Assert.True(AuthHelper.IsAnyAdmin(http));
            Assert.False(AuthHelper.IsAdmin(http));
            Assert.Equal(4, AuthHelper.GetEmpresaId(http));
            Assert.Equal(9, AuthHelper.GetEmpresaRolId(http));
            Assert.Equal(2, AuthHelper.GetTenantId(http));
            Assert.Equal(10, AuthHelper.GetCuentaId(http));
            Assert.Equal(4, AuthHelper.GetUserLegadoId(http));
            Assert.Equal("egaray", AuthHelper.GetNombreUsuario(http));
            Assert.Equal("Emilio Garay", AuthHelper.NombreVisible(http));
        }

        [Fact]
        public void El_operador_en_plataforma_es_admin_sin_empresa()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Plataforma());

            Assert.True(AuthHelper.IsAdmin(http));
            Assert.True(AuthHelper.IsOperador(http));
            Assert.Null(AuthHelper.GetEmpresaId(http));
            Assert.Null(AuthHelper.GetTenantId(http));
        }

        [Fact]
        public void La_sesion_sola_no_da_identidad_aunque_diga_que_eres_admin()
        {
            // Una sesión con UserId y Role (como la dejaba el Login anterior) no autentica a nadie: manda la cookie.
            var http = new DefaultHttpContext
            {
                Session = new SesionFalsa(new Dictionary<string, string> { ["UserId"] = "1", ["Role"] = "admin", ["EmpresaId"] = "1" })
            };

            Assert.False(AuthHelper.IsAuthenticated(http));
            Assert.False(AuthHelper.HayCuenta(http));
            Assert.False(AuthHelper.IsAdmin(http));
            Assert.Null(AuthHelper.GetEmpresaId(http));
            Assert.Null(AuthHelper.GetCuentaId(http));
        }

        [Fact]
        public void Con_un_cambio_de_clave_pendiente_hay_cuenta_pero_no_se_esta_autenticado_para_el_resto()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa(debeCambiarClave: true));

            Assert.True(AuthHelper.HayCuenta(http));
            Assert.True(AuthHelper.MustChangePassword(http));
            Assert.False(AuthHelper.IsAuthenticated(http));
        }

        [Fact]
        public void El_rol_de_un_acceso_se_resuelve_como_siempre()
        {
            Assert.Equal("admin", AuthHelper.RolDeAcceso(true, "empresa_user"));
            Assert.Equal("empresa_admin", AuthHelper.RolDeAcceso(false, "EMPRESA_ADMIN"));
            Assert.Equal("empresa_user", AuthHelper.RolDeAcceso(false, ""));
            Assert.Equal("empresa_user", AuthHelper.RolDeAcceso(false, null));
        }
    }

    /// <summary>El espejo de la identidad en la sesión para el código anterior a F1.5 (D50).</summary>
    public class SesionDesdeIdentidadMiddlewareTests
    {
        private sealed class CargadorFalso : ICargadorDePermisos
        {
            public int Llamadas { get; private set; }
            public int? UltimaEmpresa { get; private set; }

            public Task<PermisosDeEmpresa> CargarAsync(int idEmpresa, int? idRol, string rol, CancellationToken ct = default)
            {
                Llamadas++;
                UltimaEmpresa = idEmpresa;
                return Task.FromResult(new PermisosDeEmpresa(new[] { "flota" },
                    new Dictionary<string, HashSet<string>> { ["flota"] = new() { "ver" } }));
            }
        }

        private static async Task Pasar(HttpContext http, CargadorFalso cargador)
        {
            var middleware = new SesionDesdeIdentidadMiddleware(_ => Task.CompletedTask);
            await middleware.InvokeAsync(http, cargador, NullLogger<SesionDesdeIdentidadMiddleware>.Instance);
        }

        [Fact]
        public async Task Refleja_la_identidad_de_la_cookie_en_las_claves_que_lee_el_codigo_anterior()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: 4));
            http.Session.Clear();

            await Pasar(http, new CargadorFalso());

            Assert.Equal("4", http.Session.GetString("UserId"));            // el usuario de dbo.Users de la cuenta
            Assert.Equal("egaray", http.Session.GetString("Username"));
            Assert.Equal("egaray@prueba.local", http.Session.GetString("Email"));
            Assert.Equal("empresa_admin", http.Session.GetString("Role"));
            Assert.Equal("4", http.Session.GetString("EmpresaId"));
            Assert.Equal("7", http.Session.GetString("EmpresaRolId"));
            Assert.Equal("Emilio Garay", http.Session.GetString(AuthHelper.ClaveNombrePersona));
            Assert.Null(http.Session.GetString("MustChangePassword"));
        }

        [Fact]
        public async Task Si_la_cuenta_no_viene_de_un_usuario_el_UserId_es_la_cuenta()
        {
            var datos = DatosDeIdentidadDePrueba.Empresa() with { IdUserLegado = null };
            var http = SesionFalsa.ConIdentidad(datos);
            http.Session.Clear();

            await Pasar(http, new CargadorFalso());

            Assert.Equal("10", http.Session.GetString("UserId"));
        }

        [Fact]
        public async Task Sin_cookie_vacia_el_espejo_pero_deja_el_resto_de_la_sesion()
        {
            var http = new DefaultHttpContext
            {
                Session = new SesionFalsa(new Dictionary<string, string>
                {
                    ["UserId"] = "1", ["Role"] = "admin", ["EmpresaId"] = "1", ["Modulos"] = "[\"flota\"]", ["Permisos"] = "{}",
                    [AuthHelper.ClaveTenantDeSoporte] = "2", ["Entrada.Cuenta"] = "5"
                })
            };

            await Pasar(http, new CargadorFalso());

            Assert.Null(http.Session.GetString("UserId"));
            Assert.Null(http.Session.GetString("Role"));
            Assert.Null(http.Session.GetString("EmpresaId"));
            Assert.Null(http.Session.GetString("Modulos"));
            Assert.Null(http.Session.GetString("Permisos"));
            Assert.Equal("2", http.Session.GetString(AuthHelper.ClaveTenantDeSoporte));   // estado de pantalla: no es del espejo
            Assert.Equal("5", http.Session.GetString("Entrada.Cuenta"));                  // un inicio de sesión a medias tampoco
        }

        [Fact]
        public async Task Lee_modulos_y_permisos_una_vez_y_los_vuelve_a_leer_solo_si_cambia_la_empresa_o_el_rol()
        {
            var cargador = new CargadorFalso();
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa(idEmpresa: 1));
            http.Session.Clear();

            await Pasar(http, cargador);
            await Pasar(http, cargador);
            Assert.Equal(1, cargador.Llamadas);
            Assert.Equal("[\"flota\"]", http.Session.GetString("Modulos"));

            // Misma sesión, otro sitio (cambió de empresa): se rehace.
            http.User = ReclamosDeIdentidad.Crear(DatosDeIdentidadDePrueba.Empresa(idEmpresa: 3));
            await Pasar(http, cargador);
            Assert.Equal(2, cargador.Llamadas);
            Assert.Equal(3, cargador.UltimaEmpresa);

            // Otro rol en la misma empresa: también.
            http.User = ReclamosDeIdentidad.Crear(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: 3));
            await Pasar(http, cargador);
            Assert.Equal(3, cargador.Llamadas);
        }

        [Fact]
        public async Task Una_identidad_renovada_fuerza_la_relectura_de_los_permisos()
        {
            var cargador = new CargadorFalso();
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa());
            http.Session.Clear();
            await Pasar(http, cargador);

            http.Items[EventosDeCookieDeIdentidad.ClaveIdentidadRenovada] = true;
            await Pasar(http, cargador);

            Assert.Equal(2, cargador.Llamadas);
        }

        [Fact]
        public async Task Con_el_cambio_de_clave_pendiente_no_se_leen_permisos()
        {
            var cargador = new CargadorFalso();
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa(debeCambiarClave: true));

            await Pasar(http, cargador);

            Assert.Equal(0, cargador.Llamadas);
            Assert.Equal("1", http.Session.GetString("MustChangePassword"));
        }

        [Fact]
        public async Task El_operador_en_plataforma_no_tiene_empresa_ni_permisos_de_empresa()
        {
            var cargador = new CargadorFalso();
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Plataforma());
            http.Session.SetString("Modulos", "[\"flota\"]");

            await Pasar(http, cargador);

            Assert.Equal(0, cargador.Llamadas);
            Assert.Equal("admin", http.Session.GetString("Role"));
            Assert.Null(http.Session.GetString("EmpresaId"));
            Assert.Null(http.Session.GetString("Modulos"));
        }

        [Fact]
        public async Task Si_no_se_pueden_leer_los_permisos_la_persona_ve_menos_nunca_mas()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa());
            http.Session.SetString("Modulos", "[\"facturacion\"]");   // restos de otra sesión
            var middleware = new SesionDesdeIdentidadMiddleware(_ => Task.CompletedTask);

            await middleware.InvokeAsync(http, new CargadorQueFalla(), NullLogger<SesionDesdeIdentidadMiddleware>.Instance);

            Assert.Null(http.Session.GetString("Modulos"));
            Assert.Null(http.Session.GetString("Permisos"));
        }

        private sealed class CargadorQueFalla : ICargadorDePermisos
        {
            public Task<PermisosDeEmpresa> CargarAsync(int idEmpresa, int? idRol, string rol, CancellationToken ct = default)
                => throw new InvalidOperationException("base caída");
        }
    }
}
