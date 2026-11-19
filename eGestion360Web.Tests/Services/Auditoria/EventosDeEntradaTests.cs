using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Services.Auditoria
{
    /// <summary>
    /// Qué eventos de seguridad deja cada servicio de entrada (F1.8a, ADR-030): el inicio de sesión que no entra, el bloqueo, el cambio
    /// de sitio, la salida y el segundo factor. Se usa un espía en lugar del servicio real; lo que el servicio real hace con el evento lo
    /// cubre <see cref="AuditoriaDeSeguridadTests"/>.
    /// </summary>
    public class EventosDeEntradaTests : IDisposable
    {
        private const string ClaveBuena = "Clave-Segura-1";
        private const string ClaveMala = "Clave-Equivocada-9";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly AuditoriaEspia _espia = new();
        private readonly EphemeralDataProtectionProvider _proteccion = new();
        private readonly ApplicationDbContext _db;

        public EventosDeEntradaTests() => _db = _bd.Crear();

        public void Dispose()
        {
            _db.Dispose();
            _bd.Dispose();
        }

        private Autenticador Armar()
        {
            var opciones = IdentidadDePrueba.Opciones();
            return new Autenticador(_db, new DirectorioDeCuentas(_db, IdentidadDePrueba.Tenant()), new BloqueoDeCuentas(_db, opciones, _reloj, _espia),
                new ServicioMfa(_db, _proteccion, opciones, _reloj, _espia), new PasswordService(), opciones,
                NullLogger<Autenticador>.Instance, _reloj, _espia);
        }

        private BloqueoDeCuentas ArmarBloqueo() => new(_db, IdentidadDePrueba.Opciones(), _reloj, _espia);

        private ServicioMfa ArmarMfa() => new(_db, _proteccion, IdentidadDePrueba.Opciones(), _reloj, _espia);

        private string CodigoDe(string secreto, DateTime? en = null)
            => new Totp(Base32Encoding.ToBytes(secreto)).ComputeTotp(en ?? _reloj.GetUtcNow().UtcDateTime);

        private static JsonElement DetalleDe(EventoDeSeguridad e) => JsonSerializer.SerializeToElement(e.Detalle);

        // ── Inicio de sesión que no entra ─────────────────────────────────────

        [Fact]
        public async Task Una_cuenta_que_no_existe_deja_un_evento_agregable_con_el_nombre_enmascarado()
        {
            var r = await Armar().VerificarCredencialesAsync("maria.lopez@empresa.hn", ClaveMala);

            Assert.False(r.Correctas);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.SesionEntrada, e.Tipo);
            Assert.Equal(ResultadosDeEvento.Fallo, e.Resultado);
            Assert.True(e.Agregable);
            Assert.Equal("cuenta_inexistente", e.ClaveDeAgregado);
            Assert.Null(e.IdCuenta);
            Assert.Equal("ma***@e***.hn", e.Usuario);
            Assert.DoesNotContain("maria.lopez", _espia.Detalles());   // el correo completo de un desconocido no se guarda
            Assert.Equal("cuenta_inexistente", DetalleDe(e).GetProperty("motivo").GetString());
        }

        [Fact]
        public async Task Una_clave_incorrecta_de_una_cuenta_que_existe_se_registra_cada_vez_con_su_cuenta()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana");

            await Armar().VerificarCredencialesAsync("ana", ClaveMala);
            await Armar().VerificarCredencialesAsync("ana@prueba.local", ClaveMala);

            Assert.Equal(2, _espia.Eventos.Count);
            Assert.All(_espia.Eventos, e =>
            {
                Assert.Equal(TiposDeEvento.SesionEntrada, e.Tipo);
                Assert.Equal(ResultadosDeEvento.Fallo, e.Resultado);
                Assert.False(e.Agregable);
                Assert.Equal(cuenta.IdCuenta, e.IdCuenta);
                Assert.Equal("ana", e.Usuario);
                Assert.Equal("clave_incorrecta", DetalleDe(e).GetProperty("motivo").GetString());
            });
        }

        [Fact]
        public async Task Una_clave_incorrecta_nunca_aparece_en_ningun_evento()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana");

            await Armar().VerificarCredencialesAsync("ana", ClaveMala);
            await Armar().VerificarCredencialesAsync("desconocido", ClaveMala);
            await Armar().VerificarCredencialesAsync("ana", ClaveBuena);

            Assert.DoesNotContain(ClaveMala, _espia.Detalles());
            Assert.DoesNotContain(ClaveBuena, _espia.Detalles());
        }

        [Fact]
        public async Task El_quinto_fallo_deja_el_fallo_y_el_bloqueo_y_el_siguiente_intento_un_evento_agregable()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana");

            for (var i = 0; i < 5; i++) await Armar().VerificarCredencialesAsync("ana", ClaveMala);

            Assert.Equal(5, _espia.Eventos.Count(e => e.Tipo == TiposDeEvento.SesionEntrada));
            var bloqueo = Assert.Single(_espia.De(TiposDeEvento.SesionBloqueo));
            Assert.Equal(cuenta.IdCuenta, bloqueo.IdCuenta);
            Assert.Equal(ResultadosDeEvento.Ok, bloqueo.Resultado);

            _espia.Eventos.Clear();
            var r = await Armar().VerificarCredencialesAsync("ana", ClaveBuena);   // aun con la clave buena, la cuenta sigue bloqueada

            Assert.Equal(FalloDeCredenciales.Bloqueada, r.Fallo);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(ResultadosDeEvento.Fallo, e.Resultado);
            Assert.True(e.Agregable);
            Assert.Equal("bloqueada", e.ClaveDeAgregado);
            Assert.Equal(cuenta.IdCuenta, e.IdCuenta);
        }

        [Fact]
        public async Task Una_cuenta_inactiva_con_la_clave_buena_deja_el_fallo_con_su_estado()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana", estado: EstadoCuenta.Inactiva);

            var r = await Armar().VerificarCredencialesAsync("ana", ClaveBuena);

            Assert.False(r.Correctas);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(ResultadosDeEvento.Fallo, e.Resultado);
            Assert.Equal(cuenta.IdCuenta, e.IdCuenta);
            Assert.Equal("cuenta_inactiva", DetalleDe(e).GetProperty("motivo").GetString());
        }

        [Fact]
        public async Task Verificar_las_credenciales_buenas_no_deja_evento_porque_la_entrada_se_registra_al_entrar()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana");

            var r = await Armar().VerificarCredencialesAsync("ana", ClaveBuena);

            Assert.True(r.Correctas);
            Assert.Empty(_espia.Eventos);
        }

        // ── Bloqueo ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Cuatro_fallos_no_dejan_evento_de_bloqueo_y_el_quinto_deja_uno()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var bloqueo = ArmarBloqueo();

            for (var i = 0; i < 4; i++) await bloqueo.RegistrarFalloAsync(cuenta.IdCuenta);
            Assert.Empty(_espia.Eventos);

            await bloqueo.RegistrarFalloAsync(cuenta.IdCuenta);

            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.SesionBloqueo, e.Tipo);
            Assert.Equal(cuenta.IdCuenta, e.IdCuenta);
            Assert.Contains("hasta", DetalleDe(e).EnumerateObject().Select(p => p.Name));
        }

        // ── Entrar, cambiar de sitio y salir ──────────────────────────────────

        private static HttpContext HttpParaEntrar(DatosDeIdentidad? conCookie = null)
        {
            var servicios = new ServiceCollection().AddSingleton<IAuthenticationService, AutenticacionSinEfectos>().BuildServiceProvider();
            var http = conCookie != null ? SesionFalsa.ConIdentidad(conCookie) : new DefaultHttpContext { Session = new SesionFalsa() };
            http.RequestServices = servicios;
            return http;
        }

        private sealed class AutenticacionSinEfectos : IAuthenticationService
        {
            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
            public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        }

        [Fact]
        public async Task Una_entrada_nueva_deja_la_entrada_en_la_plataforma_y_el_contexto_elegido_con_el_tenant()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", usuario: "ana");
            var datos = DatosDeIdentidadDePrueba.Empresa("empresa_admin", DatosBase.EmpresaA, DatosBase.TenantA) with { IdCuenta = cuenta.IdCuenta, MfaVerificada = true };

            await Armar().EntrarAsync(HttpParaEntrar(), cuenta, datos);

            Assert.Equal(new[] { TiposDeEvento.SesionEntrada, TiposDeEvento.ContextoElegido }, _espia.Eventos.Select(e => e.Tipo));
            var entrada = _espia.Eventos[0];
            Assert.Equal(cuenta.IdCuenta, entrada.IdCuenta);
            Assert.Equal(datos.NombreUsuario, entrada.Usuario);
            Assert.Equal(DatosBase.TenantA, entrada.IdTenant);
            Assert.Equal(DatosBase.EmpresaA, entrada.IdEmpresa);
            Assert.Equal("tenant", DetalleDe(entrada).GetProperty("sitio").GetString());
            Assert.True(DetalleDe(entrada).GetProperty("mfa").GetBoolean());

            var contexto = _espia.Eventos[1];
            Assert.Equal(DatosBase.TenantA, contexto.IdTenant);
            Assert.False(DetalleDe(contexto).GetProperty("cambio").GetBoolean());
        }

        [Fact]
        public async Task El_operador_que_entra_sin_empresa_deja_el_sitio_de_plataforma_sin_tenant()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "admin@prueba.local", usuario: "admin", operador: true);
            var datos = DatosDeIdentidadDePrueba.Plataforma() with { IdCuenta = cuenta.IdCuenta };

            await Armar().EntrarAsync(HttpParaEntrar(), cuenta, datos);

            Assert.All(_espia.Eventos, e =>
            {
                Assert.Null(e.IdTenant);
                Assert.Null(e.IdEmpresa);
                Assert.True(e.EsOperador);
                Assert.Equal("plataforma", DetalleDe(e).GetProperty("sitio").GetString());
            });
        }

        [Fact]
        public async Task Cambiar_de_sitio_con_la_sesion_abierta_deja_solo_el_contexto_elegido_marcado_como_cambio()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", usuario: "ana");
            var actual = DatosDeIdentidadDePrueba.Empresa("empresa_admin", DatosBase.EmpresaA, DatosBase.TenantA) with { IdCuenta = cuenta.IdCuenta };
            var nuevo = DatosDeIdentidadDePrueba.Empresa("empresa_admin", DatosBase.EmpresaB, DatosBase.TenantB) with { IdCuenta = cuenta.IdCuenta };

            await Armar().EntrarAsync(HttpParaEntrar(actual), cuenta, nuevo);

            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.ContextoElegido, e.Tipo);
            Assert.Equal(DatosBase.TenantB, e.IdTenant);
            Assert.True(DetalleDe(e).GetProperty("cambio").GetBoolean());
        }

        [Fact]
        public async Task Salir_con_la_sesion_abierta_deja_la_salida_y_sin_sesion_no_deja_nada()
        {
            await Armar().SalirAsync(HttpParaEntrar(DatosDeIdentidadDePrueba.Empresa()));
            Assert.Equal(TiposDeEvento.SesionSalida, Assert.Single(_espia.Eventos).Tipo);

            _espia.Eventos.Clear();
            await Armar().SalirAsync(HttpParaEntrar());
            Assert.Empty(_espia.Eventos);
        }

        // ── Segundo factor ────────────────────────────────────────────────────

        [Fact]
        public async Task El_alta_del_segundo_factor_deja_el_fallo_con_un_codigo_malo_y_el_exito_sin_los_codigos()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var mfa = ArmarMfa();
            var alta = await mfa.IniciarEnrolamientoAsync(cuenta.IdCuenta, cuenta.Correo);
            Assert.Empty(_espia.Eventos);   // iniciar el alta todavía no cambia nada

            Assert.Null(await mfa.ConfirmarEnrolamientoAsync(cuenta.IdCuenta, "000000"));
            var fallo = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaAlta, fallo.Tipo);
            Assert.Equal(ResultadosDeEvento.Fallo, fallo.Resultado);

            _espia.Eventos.Clear();
            var codigos = (await mfa.ConfirmarEnrolamientoAsync(cuenta.IdCuenta, CodigoDe(alta.Secreto)))!;

            var ok = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaAlta, ok.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, ok.Resultado);
            Assert.Equal(10, DetalleDe(ok).GetProperty("codigos_de_recuperacion").GetInt32());   // se anota cuántos, nunca cuáles
            Assert.DoesNotContain(alta.Secreto, _espia.Detalles());
            Assert.All(codigos, c => Assert.DoesNotContain(c, _espia.Detalles()));
        }

        [Fact]
        public async Task Verificar_un_codigo_deja_ok_o_fallo_y_un_codigo_de_recuperacion_deja_cuantos_quedan()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var mfa = ArmarMfa();
            var alta = await mfa.IniciarEnrolamientoAsync(cuenta.IdCuenta, cuenta.Correo);
            var codigos = (await mfa.ConfirmarEnrolamientoAsync(cuenta.IdCuenta, CodigoDe(alta.Secreto)))!;
            _espia.Eventos.Clear();

            Assert.False(await mfa.VerificarAsync(cuenta.IdCuenta, "ZZZZZ-ZZZZZ"));
            var fallo = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaVerificada, fallo.Tipo);
            Assert.Equal(ResultadosDeEvento.Fallo, fallo.Resultado);

            _espia.Eventos.Clear();
            _reloj.Avanzar(TimeSpan.FromSeconds(30));
            Assert.True(await mfa.VerificarAsync(cuenta.IdCuenta, CodigoDe(alta.Secreto)));
            var ok = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaVerificada, ok.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, ok.Resultado);

            _espia.Eventos.Clear();
            Assert.True(await mfa.VerificarAsync(cuenta.IdCuenta, codigos[0]));
            var recuperacion = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaRecuperacion, recuperacion.Tipo);
            Assert.Equal(9, DetalleDe(recuperacion).GetProperty("restantes").GetInt32());
            Assert.All(codigos, c => Assert.DoesNotContain(c, _espia.Detalles()));
        }

        [Fact]
        public async Task Desactivar_el_segundo_factor_deja_quien_lo_hizo()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var mfa = ArmarMfa();
            var alta = await mfa.IniciarEnrolamientoAsync(cuenta.IdCuenta, cuenta.Correo);
            await mfa.ConfirmarEnrolamientoAsync(cuenta.IdCuenta, CodigoDe(alta.Secreto));
            _espia.Eventos.Clear();

            await mfa.DesactivarAsync(cuenta.IdCuenta, "admin");

            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.MfaDesactivada, e.Tipo);
            Assert.Equal(cuenta.IdCuenta, e.IdCuenta);
            Assert.Equal("admin", DetalleDe(e).GetProperty("por").GetString());
        }

        // ── Sin auditoría ─────────────────────────────────────────────────────

        [Fact]
        public async Task Sin_servicio_de_auditoria_todo_sigue_funcionando()
        {
            var opciones = IdentidadDePrueba.Opciones();
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", ClaveBuena, usuario: "ana");
            var autenticador = new Autenticador(_db, new DirectorioDeCuentas(_db, IdentidadDePrueba.Tenant()), new BloqueoDeCuentas(_db, opciones, _reloj),
                new ServicioMfa(_db, _proteccion, opciones, _reloj), new PasswordService(), opciones, NullLogger<Autenticador>.Instance, _reloj);

            Assert.True((await autenticador.VerificarCredencialesAsync("ana", ClaveBuena)).Correctas);
            Assert.False((await autenticador.VerificarCredencialesAsync("ana", ClaveMala)).Correctas);
            await autenticador.EntrarAsync(HttpParaEntrar(), cuenta, DatosDeIdentidadDePrueba.Empresa() with { IdCuenta = cuenta.IdCuenta });
            await autenticador.SalirAsync(HttpParaEntrar(DatosDeIdentidadDePrueba.Empresa()));
        }
    }
}
