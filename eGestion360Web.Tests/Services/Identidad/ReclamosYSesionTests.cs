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

        /// <summary>El operador dentro de una empresa: la marca de su cuenta sigue, pero entra con el rol de ese acceso (D66).</summary>
        public static DatosDeIdentidad OperadorEnEmpresa(int idEmpresa = 1, int idTenant = 1) => Empresa("empresa_user", idEmpresa, idTenant) with { EsOperador = true };

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
            Assert.Equal("empresa_admin", AuthHelper.RolLegadoDeAcceso(http));
            Assert.False(AuthHelper.EsOperador(http));
            Assert.False(AuthHelper.EnPlataforma(http));
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

            Assert.True(AuthHelper.EnPlataforma(http));
            Assert.True(AuthHelper.EsOperador(http));
            Assert.True(AuthHelper.IsOperador(http));
            Assert.Null(AuthHelper.GetEmpresaId(http));
            Assert.Null(AuthHelper.GetTenantId(http));
        }

        [Fact]
        public void El_operador_dentro_de_una_empresa_sigue_siendo_operador_pero_ya_no_esta_en_plataforma()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.OperadorEnEmpresa(idEmpresa: 4));

            Assert.True(AuthHelper.EsOperador(http));      // la marca de su cuenta
            Assert.False(AuthHelper.EnPlataforma(http));   // entra con el rol de ese acceso
            Assert.Equal("empresa_user", AuthHelper.RolLegadoDeAcceso(http));
        }

        [Fact]
        public void El_alcance_sobre_una_empresa_es_del_operador_en_cualquiera_y_del_resto_solo_en_la_suya()
        {
            var deLaEmpresa4 = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_user", idEmpresa: 4));
            var admin4 = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: 4));
            var operadorEnEmpresa = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.OperadorEnEmpresa(idEmpresa: 4));
            var operador = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Plataforma());
            var sinSesion = new DefaultHttpContext();

            foreach (var http in new[] { deLaEmpresa4, admin4 })
            {
                Assert.True(AuthHelper.PuedeActuarSobreEmpresa(http, 4));
                Assert.False(AuthHelper.PuedeActuarSobreEmpresa(http, 5));      // otra empresa del tenant
                Assert.False(AuthHelper.PuedeActuarSobreEmpresa(http, null));   // sin empresa: solo el personal de la plataforma
                Assert.False(AuthHelper.PuedeActuarSobreEmpresa(http, 0));
            }
            foreach (var http in new[] { operadorEnEmpresa, operador })
            {
                Assert.True(AuthHelper.PuedeActuarSobreEmpresa(http, 4));
                Assert.True(AuthHelper.PuedeActuarSobreEmpresa(http, 5));
                Assert.True(AuthHelper.PuedeActuarSobreEmpresa(http, null));
            }
            Assert.False(AuthHelper.PuedeActuarSobreEmpresa(sinSesion, 4));
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
            Assert.False(AuthHelper.EsOperador(http));
            Assert.False(AuthHelper.EnPlataforma(http));
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
        private static async Task Pasar(HttpContext http)
        {
            var middleware = new SesionDesdeIdentidadMiddleware(_ => Task.CompletedTask);
            await middleware.InvokeAsync(http);
        }

        [Fact]
        public async Task Refleja_la_identidad_de_la_cookie_en_las_claves_que_lee_el_codigo_anterior()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: 4));
            http.Session.Clear();

            await Pasar(http);

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

            await Pasar(http);

            Assert.Equal("10", http.Session.GetString("UserId"));
        }

        [Fact]
        public async Task Sin_cookie_vacia_el_espejo_pero_deja_el_resto_de_la_sesion()
        {
            var http = new DefaultHttpContext
            {
                Session = new SesionFalsa(new Dictionary<string, string>
                {
                    ["UserId"] = "1", ["Role"] = "admin", ["EmpresaId"] = "1",
                    [AuthHelper.ClaveTenantDeSoporte] = "2", ["Entrada.Cuenta"] = "5"
                })
            };

            await Pasar(http);

            Assert.Null(http.Session.GetString("UserId"));
            Assert.Null(http.Session.GetString("Role"));
            Assert.Null(http.Session.GetString("EmpresaId"));
            Assert.Equal("2", http.Session.GetString(AuthHelper.ClaveTenantDeSoporte));   // estado de pantalla: no es del espejo
            Assert.Equal("5", http.Session.GetString("Entrada.Cuenta"));                  // un inicio de sesión a medias tampoco
        }

        [Fact]
        public async Task Ya_no_guarda_en_la_sesion_los_modulos_ni_los_permisos()
        {
            // Los decide cada petición (plan del tenant y evaluador de permisos), no una copia de la sesión (F1.6e-3).
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa(idEmpresa: 1));
            http.Session.Clear();

            await Pasar(http);
            await Pasar(http);

            Assert.Null(http.Session.GetString("Modulos"));
            Assert.Null(http.Session.GetString("Permisos"));
            Assert.Null(http.Session.GetString("ContextoDePermisos"));
        }

        [Fact]
        public async Task Con_el_cambio_de_clave_pendiente_solo_se_marca_en_el_espejo()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa(debeCambiarClave: true));

            await Pasar(http);

            Assert.Equal("1", http.Session.GetString("MustChangePassword"));
        }

        [Fact]
        public async Task El_operador_en_plataforma_no_tiene_empresa_en_el_espejo()
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Plataforma());

            await Pasar(http);

            Assert.Equal("admin", http.Session.GetString("Role"));
            Assert.Null(http.Session.GetString("EmpresaId"));
        }
    }
}
