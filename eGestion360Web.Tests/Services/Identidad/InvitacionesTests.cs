using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Identidad
{
    /// <summary>
    /// Invitaciones por correo (F1.5, ADR-028): invitar deja un usuario sin activar con su persona; aceptar lo activa con la clave que
    /// la persona eligió. Solo se guarda la huella del token, el enlace sirve una vez y vence.
    /// </summary>
    public class InvitacionesTests : IDisposable
    {
        private const string ClaveSegura = "Clave-Muy-Segura-2026!";
        private const int IdAdminSembrado = 1;   // el modelo siembra al usuario admin (sin empresa); no es parte de lo que prueban estas pruebas

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly CorreoEspia _correo = new();
        private readonly DerechosConTope _derechos = new();

        // El administrador de la empresa A y el administrador general (sin empresa).
        private static readonly QuienOperaUsuarios AdminDeA = new(false, DatosBase.EmpresaA, "jefe.a");
        private static readonly QuienOperaUsuarios AdminGeneral = new(true, null, "soporte");

        public void Dispose() => _bd.Dispose();

        private ServicioDeInvitaciones Servicio(ApplicationDbContext db, IDerechosDeUso? derechos = null)
            => InvitacionesDePrueba.Servicio(db, _reloj, _correo, derechos ?? _derechos);

        private int NuevaPersona(int idEmpresa = DatosBase.EmpresaA, string nombre = "Maria", string apellido = "Lopez")
        {
            using var db = _bd.Crear();
            return PersonasDePrueba.Insertar(db, idEmpresa, nombre, apellido, codigoInterno: "E-" + Guid.NewGuid().ToString("N")[..6]).IdPersona;
        }

        private int NuevoRol(int idEmpresa = DatosBase.EmpresaA, string nombre = "Flota", bool activo = true)
        {
            using var db = _bd.Crear();
            var rol = new EmpresaRol { IdEmpresa = idEmpresa, IdTenant = DatosBase.TenantDe(idEmpresa), Nombre = nombre, Activo = activo };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();
            return rol.IdRol;
        }

        private static string Enlace(string token) => "https://sitio.prueba/AceptarInvitacion?token=" + token;

        /// <summary>Invita y devuelve el resultado junto con el token que viajó por el enlace.</summary>
        private async Task<(ResultadoDeInvitar Resultado, string? Token)> Invitar(QuienOperaUsuarios quien, SolicitudDeInvitacion solicitud, IDerechosDeUso? derechos = null)
        {
            string? token = null;
            using var db = _bd.Crear();
            var r = await Servicio(db, derechos).InvitarAsync(quien, solicitud, t => { token = t; return Enlace(t); });
            return (r, token);
        }

        private SolicitudDeInvitacion Solicitud(int idPersona, string correo = "Maria.Lopez@Empresa.hn", int idRol = 0, string rolLegado = "empresa_user", int idEmpresa = DatosBase.EmpresaA)
            => new(correo, idEmpresa, rolLegado, rolLegado == "empresa_admin" ? null : (idRol > 0 ? idRol : null), idPersona);

        private async Task<(int IdPersona, int IdRol, string Token, int IdInvitacion)> InvitacionValida(string correo = "maria.lopez@empresa.hn")
        {
            var idPersona = NuevaPersona();
            var idRol = NuevoRol();
            var (r, token) = await Invitar(AdminDeA, Solicitud(idPersona, correo, idRol));
            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            return (idPersona, idRol, token!, r.IdInvitacion!.Value);
        }

        private static User Invitado(ApplicationDbContext db) => db.Users.Single(u => u.Id != IdAdminSembrado);

        private async Task<T> Leer<T>(Func<ApplicationDbContext, Task<T>> consulta)
        {
            using var db = _bd.Crear();
            return await consulta(db);
        }

        // ── Invitar ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Invitar_deja_un_usuario_sin_activar_con_su_persona_y_una_invitacion_que_guarda_solo_la_huella()
        {
            var idPersona = NuevaPersona();
            var idRol = NuevoRol();

            var (r, token) = await Invitar(AdminDeA, Solicitud(idPersona, "Maria.Lopez@Empresa.hn", idRol));

            Assert.True(r.Ok);
            Assert.True(r.CorreoEnviado);
            Assert.False(string.IsNullOrEmpty(token));

            using var db = _bd.Crear();
            var usuario = Invitado(db);
            Assert.False(usuario.IsActive);
            Assert.Equal("maria.lopez@empresa.hn", usuario.Email);                   // el correo se guarda en minúsculas
            Assert.Equal("maria.lopez", usuario.Username);                           // sale de la parte local del correo
            Assert.Equal("empresa_user", usuario.Role);
            Assert.Equal(DatosBase.EmpresaA, usuario.EmpresaId);
            Assert.Equal(idRol, usuario.EmpresaRolId);
            Assert.Equal(idPersona, usuario.PersonaId);
            Assert.False(usuario.RequirePasswordChange);

            var cuenta = db.Cuentas.Single();
            Assert.Equal(EstadoCuenta.Inactiva, cuenta.Estado);                      // todavía no puede entrar
            Assert.Equal(usuario.Id, cuenta.IdUserLegado);
            Assert.Equal(EstadoMembresia.Suspendida, db.Membresias.Single().Estado);
            Assert.False(db.AccesosEmpresa.Single().Activo);

            var invitacion = db.Invitaciones.Single();
            Assert.Equal(r.IdInvitacion, invitacion.IdInvitacion);
            Assert.Equal(64, invitacion.TokenHash.Length);
            Assert.Equal(ServicioDeInvitaciones.Huella(token!), invitacion.TokenHash);
            Assert.NotEqual(token, invitacion.TokenHash);                            // el token no se guarda
            Assert.DoesNotContain(token!, invitacion.TokenHash);
            Assert.Equal(43, token!.Length);                                         // 256 bits en base64url
            Assert.Equal(DatosBase.TenantA, invitacion.IdTenant);
            Assert.Equal(DatosBase.EmpresaA, invitacion.IdEmpresa);
            Assert.Equal(idRol, invitacion.IdRol);
            Assert.Equal(cuenta.IdCuenta, invitacion.IdCuenta);
            Assert.Equal("jefe.a", invitacion.CreadaPor);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddDays(7), invitacion.VenceEn);
            Assert.Null(invitacion.UsadaEn);
        }

        [Fact]
        public async Task El_correo_lleva_el_enlace_la_empresa_y_quien_invita_escapados_y_sin_contrasenas()
        {
            var idPersona = NuevaPersona();
            using (var db = _bd.Crear())
            {
                var empresa = db.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaA);
                empresa.RazonSocial = "Fletes <b>Rapidos</b> & Cia";
                db.SaveChanges();
            }

            var (r, token) = await Invitar(new QuienOperaUsuarios(false, DatosBase.EmpresaA, "jefe<script>"), Solicitud(idPersona, "ana@x.hn", NuevoRol()));

            Assert.True(r.Ok);
            var (para, asunto, html) = Assert.Single(_correo.Enviados);
            Assert.Equal("ana@x.hn", para);
            Assert.Contains("eGestion360", asunto);
            Assert.Contains(Enlace(token!), System.Net.WebUtility.HtmlDecode(html));           // el enlace viaja completo
            Assert.Contains("Fletes &lt;b&gt;Rapidos&lt;/b&gt; &amp; Cia", html);                 // la empresa va escapada
            Assert.DoesNotContain("<b>Rapidos</b>", html);
            Assert.DoesNotContain("jefe<script>", html);
            Assert.Contains("jefe&lt;script&gt;", html);
            Assert.Contains("una sola vez", html);
        }

        [Fact]
        public async Task Si_el_correo_no_sale_la_invitacion_existe_y_se_avisa()
        {
            _correo.Responde = false;

            var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), "ana@x.hn", NuevoRol()));

            Assert.True(r.Ok);
            Assert.False(r.CorreoEnviado);
            Assert.Single(await Leer(db => db.Invitaciones.ToListAsync()));
        }

        [Fact]
        public async Task Un_administrador_de_empresa_se_invita_sin_rol()
        {
            var idPersona = NuevaPersona();

            var (r, _) = await Invitar(AdminDeA, Solicitud(idPersona, "jefe@x.hn", idRol: NuevoRol(), rolLegado: "empresa_admin"));

            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            using var db = _bd.Crear();
            Assert.Equal("empresa_admin", Invitado(db).Role);
            Assert.Null(Invitado(db).EmpresaRolId);
            Assert.Null(db.Invitaciones.Single().IdRol);
            Assert.Equal("empresa_admin", db.AccesosEmpresa.Single().RolLegado);
        }

        [Fact]
        public async Task El_nombre_de_usuario_se_numera_si_ya_existe()
        {
            using (var db = _bd.Crear())
            {
                db.Users.Add(new User { Username = "Maria.Lopez", Email = "otra@x.hn", Password = "x", Role = "empresa_user", EmpresaId = DatosBase.EmpresaA });
                db.SaveChanges();
            }

            var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), "maria.lopez@empresa.hn", NuevoRol()));

            Assert.True(r.Ok);
            using var verificacion = _bd.Crear();
            Assert.Equal("maria.lopez1", verificacion.Users.Single(u => u.Email == "maria.lopez@empresa.hn").Username);
        }

        // ── Lo que impide invitar: no se crea nada ────────────────────────────

        public static IEnumerable<object[]> CorreosInvalidos() => new[]
        {
            new object[] { "" }, new object[] { "sin-arroba" }, new object[] { "dos@@x.hn" }, new object[] { new string('a', 96) + "@x.hn" }
        };

        [Theory]
        [MemberData(nameof(CorreosInvalidos))]
        public async Task Un_correo_invalido_se_rechaza_sin_crear_nada(string correo)
        {
            var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), correo, NuevoRol()));

            Assert.False(r.Ok);
            await NadaSeCreo();
        }

        private async Task NadaSeCreo()
        {
            using var db = _bd.Crear();
            Assert.Empty(await db.Users.Where(u => u.Id != IdAdminSembrado).ToListAsync());
            Assert.Empty(await db.Cuentas.ToListAsync());
            Assert.Empty(await db.Invitaciones.ToListAsync());
            Assert.Empty(_correo.Enviados);
        }

        [Fact]
        public async Task Un_correo_que_ya_tiene_usuario_cuenta_o_alias_no_se_invita()
        {
            using (var db = _bd.Crear())
            {
                db.Users.Add(new User { Username = "viejo", Email = "viejo@x.hn", Password = "x", Role = "empresa_user", EmpresaId = DatosBase.EmpresaA });
                var cuenta = IdentidadDePrueba.NuevaCuenta(db, "cuenta@x.hn");
                db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "alias@x.hn", Tipo = TipoAlias.Correo, PuedeIniciarSesion = false, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
                db.SaveChanges();
            }
            var rol = NuevoRol();

            foreach (var correo in new[] { "VIEJO@x.hn", "cuenta@x.hn", "alias@x.hn" })
            {
                var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), correo, rol));
                Assert.False(r.Ok, correo);
                Assert.Contains("ya tiene un usuario", string.Join(" ", r.Errores));
            }

            Assert.Empty(await Leer(db => db.Invitaciones.ToListAsync()));
            Assert.Single(await Leer(db => db.Users.Where(u => u.Id != IdAdminSembrado).ToListAsync()));   // solo el que ya estaba
        }

        [Fact]
        public async Task No_se_invita_dos_veces_al_mismo_correo_mientras_la_primera_espera()
        {
            var rol = NuevoRol();
            Assert.True((await Invitar(AdminDeA, Solicitud(NuevaPersona(), "ana@x.hn", rol))).Resultado.Ok);

            var (segunda, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), "ANA@x.hn", rol));

            Assert.False(segunda.Ok);
            Assert.Contains("invitación pendiente", string.Join(" ", segunda.Errores));
            Assert.Single(await Leer(db => db.Invitaciones.ToListAsync()));
        }

        [Fact]
        public async Task Un_administrador_de_empresa_solo_invita_a_su_empresa_y_a_personas_de_su_empresa()
        {
            var rolB = NuevoRol(DatosBase.EmpresaB);
            var personaB = NuevaPersona(DatosBase.EmpresaB, "Otro", "Ajeno");

            // a otra empresa
            var (aOtraEmpresa, _) = await Invitar(AdminDeA, Solicitud(personaB, "x@x.hn", rolB, idEmpresa: DatosBase.EmpresaB));
            Assert.False(aOtraEmpresa.Ok);
            Assert.Contains("propia empresa", string.Join(" ", aOtraEmpresa.Errores));

            // a una persona de otra empresa, aunque la empresa sea la suya
            var (personaAjena, _) = await Invitar(AdminDeA, Solicitud(personaB, "y@x.hn", NuevoRol()));
            Assert.False(personaAjena.Ok);
            Assert.Contains("registrada en tu empresa", string.Join(" ", personaAjena.Errores));

            await NadaSeCreo();
        }

        [Fact]
        public async Task El_administrador_general_invita_a_la_empresa_que_elija()
        {
            var rolB = NuevoRol(DatosBase.EmpresaB);
            var personaB = NuevaPersona(DatosBase.EmpresaB, "Otro", "Ajeno");

            var (r, _) = await Invitar(AdminGeneral, Solicitud(personaB, "otro@x.hn", rolB, idEmpresa: DatosBase.EmpresaB));

            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            using var db = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, db.Invitaciones.Single().IdTenant);
            Assert.Equal(DatosBase.EmpresaB, Invitado(db).EmpresaId);
        }

        [Fact]
        public async Task El_rol_debe_ser_de_la_empresa_estar_activo_y_ser_obligatorio_para_un_usuario()
        {
            var persona = NuevaPersona();
            var rolAjeno = NuevoRol(DatosBase.EmpresaB);
            var rolInactivo = NuevoRol(nombre: "Inactivo", activo: false);

            foreach (var (idRol, esperado) in new[] { (0, "rol que tendrá"), (rolAjeno, "no es de esa empresa"), (rolInactivo, "no es de esa empresa") })
            {
                var (r, _) = await Invitar(AdminDeA, Solicitud(persona, "ana@x.hn", idRol));
                Assert.False(r.Ok);
                Assert.Contains(esperado, string.Join(" ", r.Errores));
            }

            await NadaSeCreo();
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("empresa_root")]
        [InlineData("")]
        public async Task Solo_se_invita_como_administrador_de_empresa_o_como_usuario(string rolLegado)
        {
            var (r, _) = await Invitar(AdminDeA, new SolicitudDeInvitacion("ana@x.hn", DatosBase.EmpresaA, rolLegado, null, NuevaPersona()));

            Assert.False(r.Ok);
            await NadaSeCreo();
        }

        [Fact]
        public async Task Un_tenant_suspendido_no_recibe_invitaciones()
        {
            using (var db = _bd.Crear())
            {
                db.Tenants.Single(t => t.IdTenant == DatosBase.TenantA).Estado = EstadoTenant.Suspendido;
                db.SaveChanges();
            }

            var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), "ana@x.hn", NuevoRol()));

            Assert.False(r.Ok);
            Assert.Contains("no está activo", string.Join(" ", r.Errores));
            await NadaSeCreo();
        }

        [Fact]
        public async Task El_tope_de_usuarios_del_plan_impide_invitar()
        {
            _derechos.Permite = false;

            var (r, _) = await Invitar(AdminDeA, Solicitud(NuevaPersona(), "ana@x.hn", NuevoRol()));

            Assert.False(r.Ok);
            Assert.Contains("permite hasta 3 usuarios activos", string.Join(" ", r.Errores));
            await NadaSeCreo();
        }

        [Fact]
        public async Task Quien_no_es_administrador_no_puede_invitar()
        {
            var (r, _) = await Invitar(new QuienOperaUsuarios(false, null, "nadie"), Solicitud(NuevaPersona(), "ana@x.hn", NuevoRol()));

            Assert.False(r.Ok);
            await NadaSeCreo();
        }

        // ── Consultar y aceptar ───────────────────────────────────────────────

        [Fact]
        public async Task Aceptar_activa_el_usuario_la_cuenta_la_membresia_y_el_acceso_con_la_clave_elegida()
        {
            var (idPersona, idRol, token, idInvitacion) = await InvitacionValida();

            ResultadoDeAceptar r;
            using (var db = _bd.Crear()) r = await Servicio(db).AceptarAsync(token, ClaveSegura);

            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            using var verificacion = _bd.Crear();
            var usuario = Invitado(verificacion);
            Assert.True(usuario.IsActive);
            Assert.False(usuario.RequirePasswordChange);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveSegura, usuario.Password));
            Assert.Equal(idPersona, usuario.PersonaId);

            var cuenta = verificacion.Cuentas.Single();
            Assert.Equal(EstadoCuenta.Activa, cuenta.Estado);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveSegura, cuenta.HashClave));        // la cuenta tiene la misma clave
            Assert.Equal("maria.lopez@empresa.hn", cuenta.Correo);
            Assert.Equal(EstadoMembresia.Activa, verificacion.Membresias.Single().Estado);
            Assert.Equal(idPersona, verificacion.Membresias.Single().IdPersona);
            var acceso = verificacion.AccesosEmpresa.Single();
            Assert.True(acceso.Activo);
            Assert.Equal(idRol, acceso.IdRol);

            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, verificacion.Invitaciones.Single(i => i.IdInvitacion == idInvitacion).UsadaEn);
        }

        [Fact]
        public async Task Aceptar_desde_una_pantalla_sin_sesion_fija_el_tenant_de_la_invitacion_antes_de_escribir()
        {
            // La pantalla de aceptar corre en ámbito de plataforma; la base no deja cambiar de tenant con una transacción abierta, así que
            // el servicio lo fija antes de abrirla (ContextoDeSesionInterceptor lo comprobó en la prueba real con la política encendida).
            var (_, _, token, _) = await InvitacionValida();
            var contexto = IdentidadDePrueba.Tenant();
            contexto.FijarPlataforma();

            ResultadoDeAceptar r;
            using (var db = _bd.Crear()) r = await InvitacionesDePrueba.Servicio(db, _reloj, _correo, _derechos, contextoFijable: contexto).AceptarAsync(token, ClaveSegura);

            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            Assert.Equal(DatosBase.TenantA, contexto.IdTenant);
            Assert.False(contexto.EsPlataforma);
            Assert.Equal(DatosBase.EmpresaA, contexto.IdEmpresa);
        }

        [Fact]
        public async Task El_enlace_sirve_una_sola_vez()
        {
            var (_, _, token, _) = await InvitacionValida();
            using (var db = _bd.Crear()) Assert.True((await Servicio(db).AceptarAsync(token, ClaveSegura)).Ok);

            ResultadoDeAceptar segunda;
            ConsultaDeInvitacion consulta;
            using (var db = _bd.Crear()) segunda = await Servicio(db).AceptarAsync(token, "Otra-Clave-Segura-9!");
            using (var db = _bd.Crear()) consulta = await Servicio(db).ConsultarAsync(token);

            Assert.False(segunda.Ok);
            Assert.Equal(EstadoDeConsulta.Usada, consulta.Estado);
            using var verificacion = _bd.Crear();
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveSegura, Invitado(verificacion).Password));   // la segunda no pisó la clave
        }

        [Fact]
        public async Task Consultar_dice_a_que_empresa_se_invita_y_con_que_correo()
        {
            var (_, _, token, _) = await InvitacionValida();

            ConsultaDeInvitacion consulta;
            using (var db = _bd.Crear()) consulta = await Servicio(db).ConsultarAsync(token);

            Assert.Equal(EstadoDeConsulta.Valida, consulta.Estado);
            Assert.Equal("maria.lopez@empresa.hn", consulta.Correo);
            Assert.Equal("Empresa de prueba 1", consulta.Empresa);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("token-que-no-existe")]
        public async Task Un_token_que_no_existe_no_se_acepta(string? token)
        {
            await InvitacionValida();

            using var db = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoEncontrada, (await Servicio(db).ConsultarAsync(token)).Estado);
            Assert.False((await Servicio(db).AceptarAsync(token, ClaveSegura)).Ok);
            Assert.False(Invitado(db).IsActive);
        }

        [Fact]
        public async Task Un_token_demasiado_largo_ni_se_busca()
        {
            await InvitacionValida();

            using var db = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoEncontrada, (await Servicio(db).ConsultarAsync(new string('a', 5000))).Estado);
        }

        [Fact]
        public async Task La_huella_del_enlace_no_sirve_como_enlace()
        {
            await InvitacionValida();
            string huella;
            using (var db = _bd.Crear()) huella = db.Invitaciones.Single().TokenHash;

            using var consulta = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoEncontrada, (await Servicio(consulta).ConsultarAsync(huella)).Estado);   // robar la base no da acceso
        }

        [Fact]
        public async Task El_enlace_vence_a_los_siete_dias()
        {
            var (_, _, token, _) = await InvitacionValida();

            _reloj.Avanzar(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));
            using (var db = _bd.Crear()) Assert.Equal(EstadoDeConsulta.Valida, (await Servicio(db).ConsultarAsync(token)).Estado);

            _reloj.Avanzar(TimeSpan.FromMinutes(1));
            using var vencida = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.Vencida, (await Servicio(vencida).ConsultarAsync(token)).Estado);
            var r = await Servicio(vencida).AceptarAsync(token, ClaveSegura);
            Assert.False(r.Ok);
            Assert.Contains("venció", string.Join(" ", r.Errores));
            Assert.False(Invitado(vencida).IsActive);
        }

        [Fact]
        public async Task Una_clave_debil_se_rechaza_y_la_invitacion_sigue_valida()
        {
            var (_, _, token, _) = await InvitacionValida();

            ResultadoDeAceptar debil;
            using (var db = _bd.Crear()) debil = await Servicio(db).AceptarAsync(token, "123456");

            Assert.False(debil.Ok);
            using (var db = _bd.Crear())
            {
                Assert.False(Invitado(db).IsActive);
                Assert.Null(db.Invitaciones.Single().UsadaEn);
                Assert.Equal(EstadoDeConsulta.Valida, (await Servicio(db).ConsultarAsync(token)).Estado);
            }

            // la persona lo vuelve a intentar con una clave buena
            using var segundo = _bd.Crear();
            Assert.True((await Servicio(segundo).AceptarAsync(token, ClaveSegura)).Ok);
        }

        [Fact]
        public async Task La_clave_no_puede_contener_el_usuario_ni_el_correo()
        {
            var (_, _, token, _) = await InvitacionValida();

            using var db = _bd.Crear();
            var r = await Servicio(db).AceptarAsync(token, "maria.lopez-2026-Segura!");

            Assert.False(r.Ok);
            Assert.False(Invitado(db).IsActive);
        }

        [Fact]
        public async Task Un_tenant_suspendido_despues_de_invitar_no_deja_aceptar()
        {
            var (_, _, token, _) = await InvitacionValida();
            using (var db = _bd.Crear())
            {
                db.Tenants.Single(t => t.IdTenant == DatosBase.TenantA).Estado = EstadoTenant.Suspendido;
                db.SaveChanges();
            }

            using var verificacion = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoDisponible, (await Servicio(verificacion).ConsultarAsync(token)).Estado);
            Assert.False((await Servicio(verificacion).AceptarAsync(token, ClaveSegura)).Ok);
            Assert.False(Invitado(verificacion).IsActive);
        }

        [Fact]
        public async Task El_tope_de_usuarios_tambien_se_comprueba_al_aceptar()
        {
            var (_, _, token, _) = await InvitacionValida();
            _derechos.Permite = false;   // entre invitar y aceptar el tenant llegó al tope

            using var db = _bd.Crear();
            var r = await Servicio(db).AceptarAsync(token, ClaveSegura);

            Assert.False(r.Ok);
            Assert.Contains("permite hasta 3 usuarios activos", string.Join(" ", r.Errores));
            Assert.False(Invitado(db).IsActive);
            Assert.Null(db.Invitaciones.Single().UsadaEn);
        }

        [Fact]
        public async Task Si_alguien_activo_al_usuario_por_otro_camino_el_enlace_ya_no_sirve()
        {
            var (_, _, token, _) = await InvitacionValida();
            using (var db = _bd.Crear())
            {
                Invitado(db).IsActive = true;   // por ejemplo, un administrador lo editó
                db.SaveChanges();
            }

            using var verificacion = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoDisponible, (await Servicio(verificacion).ConsultarAsync(token)).Estado);
            Assert.False((await Servicio(verificacion).AceptarAsync(token, ClaveSegura)).Ok);
        }

        // ── Reenviar y anular ─────────────────────────────────────────────────

        [Fact]
        public async Task Reenviar_da_un_enlace_nuevo_y_el_anterior_deja_de_servir()
        {
            var (_, _, tokenViejo, idInvitacion) = await InvitacionValida();
            _reloj.Avanzar(TimeSpan.FromDays(3));

            string? tokenNuevo = null;
            ResultadoDeInvitar r;
            using (var db = _bd.Crear()) r = await Servicio(db).ReenviarAsync(AdminDeA, idInvitacion, t => { tokenNuevo = t; return Enlace(t); });

            Assert.True(r.Ok);
            Assert.True(r.CorreoEnviado);
            Assert.Equal(2, _correo.Enviados.Count);
            Assert.NotEqual(tokenViejo, tokenNuevo);
            using var db2 = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.NoEncontrada, (await Servicio(db2).ConsultarAsync(tokenViejo)).Estado);
            Assert.Equal(EstadoDeConsulta.Valida, (await Servicio(db2).ConsultarAsync(tokenNuevo)).Estado);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddDays(7), db2.Invitaciones.Single().VenceEn);    // la vigencia empieza de nuevo
            Assert.Single(db2.Users.Where(u => u.Id != IdAdminSembrado));                                                                       // no se creó otro usuario
        }

        [Fact]
        public async Task Reenviar_sirve_para_una_invitacion_vencida_pero_no_para_una_aceptada()
        {
            var (_, _, token, idInvitacion) = await InvitacionValida();
            _reloj.Avanzar(TimeSpan.FromDays(10));

            string? nuevo = null;
            using (var db = _bd.Crear()) Assert.True((await Servicio(db).ReenviarAsync(AdminDeA, idInvitacion, t => { nuevo = t; return Enlace(t); })).Ok);

            using (var db = _bd.Crear()) Assert.True((await Servicio(db).AceptarAsync(nuevo, ClaveSegura)).Ok);
            using (var db = _bd.Crear())
            {
                var r = await Servicio(db).ReenviarAsync(AdminDeA, idInvitacion, Enlace);
                Assert.False(r.Ok);
                Assert.Contains("ya se aceptó", string.Join(" ", r.Errores));
            }
            Assert.NotNull(token);
        }

        [Fact]
        public async Task Anular_deja_el_enlace_sin_efecto_y_el_usuario_sin_activar()
        {
            var (_, _, token, idInvitacion) = await InvitacionValida();

            bool anulada;
            using (var db = _bd.Crear()) anulada = await Servicio(db).AnularAsync(AdminDeA, idInvitacion);

            Assert.True(anulada);
            using var verificacion = _bd.Crear();
            Assert.Equal(EstadoDeConsulta.Vencida, (await Servicio(verificacion).ConsultarAsync(token)).Estado);
            Assert.False((await Servicio(verificacion).AceptarAsync(token, ClaveSegura)).Ok);
            Assert.False(Invitado(verificacion).IsActive);
            Assert.Single(verificacion.Invitaciones);                                     // queda como rastro
        }

        [Fact]
        public async Task Una_invitacion_aceptada_no_se_anula()
        {
            var (_, _, token, idInvitacion) = await InvitacionValida();
            using (var db = _bd.Crear()) Assert.True((await Servicio(db).AceptarAsync(token, ClaveSegura)).Ok);

            using var verificacion = _bd.Crear();
            Assert.False(await Servicio(verificacion).AnularAsync(AdminDeA, idInvitacion));
        }

        [Fact]
        public async Task Un_administrador_de_otra_empresa_no_ve_ni_reenvia_ni_anula_la_invitacion()
        {
            var (_, _, _, idInvitacion) = await InvitacionValida();
            var deOtraEmpresa = new QuienOperaUsuarios(false, DatosBase.EmpresaB, "jefe.b");

            using var db = _bd.Crear();
            Assert.Empty(await Servicio(db).ListarAsync(deOtraEmpresa));
            Assert.False((await Servicio(db).ReenviarAsync(deOtraEmpresa, idInvitacion, Enlace)).Ok);
            Assert.False(await Servicio(db).AnularAsync(deOtraEmpresa, idInvitacion));
            Assert.Single(await Servicio(db).ListarAsync(AdminDeA));
        }

        // ── Lista ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task La_lista_muestra_persona_empresa_rol_y_estado_sin_el_token()
        {
            var rol = NuevoRol(nombre: "Flota");
            var pendiente = await Invitar(AdminDeA, Solicitud(NuevaPersona(nombre: "Maria", apellido: "Lopez"), "maria@x.hn", rol));
            var aceptada = await Invitar(AdminDeA, Solicitud(NuevaPersona(nombre: "Juan", apellido: "Perez"), "juan@x.hn", rol));
            var vencida = await Invitar(AdminDeA, Solicitud(NuevaPersona(nombre: "Luis", apellido: "Diaz"), "luis@x.hn", rol, rolLegado: "empresa_admin"));
            using (var db = _bd.Crear()) Assert.True((await Servicio(db).AceptarAsync(aceptada.Token, ClaveSegura)).Ok);
            using (var db = _bd.Crear()) Assert.True(await Servicio(db).AnularAsync(AdminDeA, vencida.Resultado.IdInvitacion!.Value));

            IReadOnlyList<InvitacionListada> filas;
            using (var db = _bd.Crear()) filas = await Servicio(db).ListarAsync(AdminDeA);

            Assert.Equal(3, filas.Count);
            var maria = filas.Single(f => f.Correo == "maria@x.hn");
            Assert.Equal(EstadoDeInvitacion.Pendiente, maria.Estado);
            Assert.Equal("Lopez, Maria", maria.Persona);
            Assert.Equal("Empresa de prueba 1", maria.Empresa);
            Assert.Equal("Flota", maria.Rol);
            Assert.Equal("jefe.a", maria.CreadaPor);
            Assert.Equal(EstadoDeInvitacion.Usada, filas.Single(f => f.Correo == "juan@x.hn").Estado);
            var luis = filas.Single(f => f.Correo == "luis@x.hn");
            Assert.Equal(EstadoDeInvitacion.Vencida, luis.Estado);
            Assert.Equal("Administrador de empresa", luis.Rol);
            Assert.DoesNotContain(pendiente.Token!, string.Join("|", filas.Select(f => f.ToString())));
        }

        [Fact]
        public async Task El_administrador_general_solo_lista_las_del_tenant_en_que_trabaja()
        {
            var rolA = NuevoRol();
            var rolB = NuevoRol(DatosBase.EmpresaB);
            Assert.True((await Invitar(AdminDeA, Solicitud(NuevaPersona(), "a@x.hn", rolA))).Resultado.Ok);
            Assert.True((await Invitar(AdminGeneral, Solicitud(NuevaPersona(DatosBase.EmpresaB, "Otro", "Ajeno"), "b@x.hn", rolB, idEmpresa: DatosBase.EmpresaB))).Resultado.Ok);

            using var db = _bd.Crear();
            var delTenantA = await Servicio(db).ListarAsync(AdminGeneral);   // el contexto de la prueba es el tenant A

            Assert.Equal(new[] { "a@x.hn" }, delTenantA.Select(f => f.Correo));
        }
    }
}
