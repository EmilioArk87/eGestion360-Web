using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Las pantallas de clave dejan su evento de seguridad (F1.8a, ADR-030): cambiar la clave (bien o con la actual equivocada) y
    /// restablecerla con un código. Nunca llevan la clave, ni la actual ni la nueva, ni el código.
    /// </summary>
    public class EventosDeClavesTests : IDisposable
    {
        private const string ClaveActual = "Clave-Vigente-2026!";
        private const string ClaveNueva = "Clave-Nueva-Segura-2027!";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly AuditoriaEspia _espia = new();

        public void Dispose() => _bd.Dispose();

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class SincronizadorSinEfectos : ISincronizadorDeCuentas
        {
            public Task SincronizarAsync(int idUser, CancellationToken ct = default) => Task.CompletedTask;
            public Task SincronizarClaveAsync(int idUser, CancellationToken ct = default) => Task.CompletedTask;
            public Task FijarTenantDelUsuarioAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        }

        private sealed class AutenticadorSinEfectos : IAutenticador
        {
            public Task<ResultadoDeCredenciales> VerificarCredencialesAsync(string identificador, string clave, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ExigenciaDeMfa> ExigenciaDeMfaAsync(Cuenta cuenta, ContextosDeLaCuenta contextos, CancellationToken ct = default) => throw new NotSupportedException();
            public DatosDeIdentidad ConstruirIdentidad(Cuenta cuenta, ContextoDeAcceso contexto, string nombreEscrito, bool mfaVerificada) => throw new NotSupportedException();
            public Task EntrarAsync(HttpContext http, Cuenta cuenta, DatosDeIdentidad datos, CancellationToken ct = default) => throw new NotSupportedException();
            public Task SalirAsync(HttpContext http) => Task.CompletedTask;
        }

        /// <summary>Un código de restablecimiento que siempre vale para el usuario indicado.</summary>
        private sealed class CodigoValido : IPasswordResetService
        {
            private readonly User _usuario;
            public CodigoValido(User usuario) => _usuario = usuario;
            public Task<string> GenerateResetCodeAsync(string email, string ipAddress) => throw new NotSupportedException();
            public Task<bool> ValidateResetCodeAsync(string email, string code) => Task.FromResult(true);
            public Task<User?> GetUserByResetCodeAsync(string email, string code) => Task.FromResult<User?>(_usuario);
            public Task<bool> MarkCodeAsUsedAsync(string email, string code) => Task.FromResult(true);
            public Task CleanupExpiredCodesAsync() => Task.CompletedTask;
            public string GenerateNumericCode() => "123456";
        }

        private User NuevoUsuario(bool debeCambiar = false)
        {
            using var db = _bd.Crear();
            var usuario = new User
            {
                Username = "ana.perez", Email = "ana.perez@prueba.local", Password = BCrypt.Net.BCrypt.HashPassword(ClaveActual, 4), Role = "empresa_user",
                IsActive = true, RequirePasswordChange = debeCambiar, EmpresaId = DatosBase.EmpresaA,
            };
            db.Users.Add(usuario);
            db.SaveChanges();
            return usuario;
        }

        private ChangePasswordModel CambiarClave(ApplicationDbContext db, User usuario, string actual, string nueva)
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa() with { IdUserLegado = usuario.Id });
            var pagina = new ChangePasswordModel(db, new SincronizadorSinEfectos(), new AutenticadorSinEfectos(), _espia)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
                CurrentPassword = actual, NewPassword = nueva, ConfirmPassword = nueva,
            };
            return pagina;
        }

        [Fact]
        public async Task Cambiar_la_clave_deja_el_evento_ok_con_si_era_obligatorio_y_sin_ninguna_clave()
        {
            var usuario = NuevoUsuario(debeCambiar: true);
            using var db = _bd.Crear();

            var r = await CambiarClave(db, usuario, ClaveActual, ClaveNueva).OnPostAsync();

            Assert.IsType<RedirectToPageResult>(r);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.ClaveCambiada, e.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, e.Resultado);
            Assert.True(System.Text.Json.JsonSerializer.SerializeToElement(e.Detalle).GetProperty("obligatorio").GetBoolean());
            Assert.DoesNotContain(ClaveActual, _espia.Detalles());
            Assert.DoesNotContain(ClaveNueva, _espia.Detalles());
        }

        [Fact]
        public async Task Con_la_clave_actual_equivocada_deja_el_evento_de_fallo_y_la_clave_no_cambia()
        {
            var usuario = NuevoUsuario();
            using var db = _bd.Crear();

            var r = await CambiarClave(db, usuario, "Clave-Equivocada-9!", ClaveNueva).OnPostAsync();

            Assert.IsType<PageResult>(r);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.ClaveCambiada, e.Tipo);
            Assert.Equal(ResultadosDeEvento.Fallo, e.Resultado);
            Assert.Equal("clave_actual_incorrecta", System.Text.Json.JsonSerializer.SerializeToElement(e.Detalle).GetProperty("motivo").GetString());
            Assert.DoesNotContain("Clave-Equivocada-9!", _espia.Detalles());
            using var lectura = _bd.Crear();
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveActual, lectura.Users.Single(u => u.Id == usuario.Id).Password));
        }

        [Fact]
        public async Task Una_nueva_clave_igual_a_la_actual_o_debil_no_cambia_nada_y_no_deja_evento_de_cambio()
        {
            var usuario = NuevoUsuario();
            using var db = _bd.Crear();

            Assert.IsType<PageResult>(await CambiarClave(db, usuario, ClaveActual, ClaveActual).OnPostAsync());
            Assert.IsType<PageResult>(await CambiarClave(db, usuario, ClaveActual, "ana.perez-2026-Segura!").OnPostAsync());   // contiene el usuario

            Assert.Empty(_espia.Eventos);
        }

        [Fact]
        public async Task Restablecer_la_clave_con_un_codigo_deja_el_evento_con_el_usuario_y_sin_el_codigo_ni_la_clave()
        {
            var usuario = NuevoUsuario();
            using var db = _bd.Crear();
            var http = new DefaultHttpContext();
            var usuarioDelCodigo = db.Users.Single(u => u.Id == usuario.Id);
            var pagina = new ResetPasswordModel(new CodigoValido(usuarioDelCodigo), new ClavesRapidas(), new CorreoEspia(), db,
                NullLogger<ResetPasswordModel>.Instance, new SincronizadorSinEfectos(), _espia)
            {
                PageContext = new PageContext { HttpContext = http },
                Email = "ana.perez@prueba.local", VerificationCode = "483920", NewPassword = ClaveNueva, ConfirmPassword = ClaveNueva,
            };

            var r = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(r);
            Assert.True(pagina.PasswordReset);
            var e = Assert.Single(_espia.Eventos);
            Assert.Equal(TiposDeEvento.ClaveRestablecida, e.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, e.Resultado);
            Assert.Equal(usuario.Id, e.IdUserLegado);   // sin sesión, el servicio busca la cuenta de este usuario
            Assert.Equal("ana.perez", e.Usuario);
            Assert.DoesNotContain("483920", _espia.Detalles());
            Assert.DoesNotContain(ClaveNueva, _espia.Detalles());
        }

        [Fact]
        public async Task Un_codigo_que_no_vale_no_deja_evento_de_restablecimiento()
        {
            var usuario = NuevoUsuario();
            using var db = _bd.Crear();
            var pagina = new ResetPasswordModel(new CodigoInvalido(), new ClavesRapidas(), new CorreoEspia(), db,
                NullLogger<ResetPasswordModel>.Instance, new SincronizadorSinEfectos(), _espia)
            {
                PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
                Email = "ana.perez@prueba.local", VerificationCode = "000000", NewPassword = ClaveNueva, ConfirmPassword = ClaveNueva,
            };

            await pagina.OnPostAsync();

            Assert.False(pagina.PasswordReset);
            Assert.Empty(_espia.Eventos);
            Assert.NotNull(usuario);
        }

        private sealed class CodigoInvalido : IPasswordResetService
        {
            public Task<string> GenerateResetCodeAsync(string email, string ipAddress) => throw new NotSupportedException();
            public Task<bool> ValidateResetCodeAsync(string email, string code) => Task.FromResult(false);
            public Task<User?> GetUserByResetCodeAsync(string email, string code) => Task.FromResult<User?>(null);
            public Task<bool> MarkCodeAsUsedAsync(string email, string code) => Task.FromResult(false);
            public Task CleanupExpiredCodesAsync() => Task.CompletedTask;
            public string GenerateNumericCode() => "000000";
        }
    }
}
