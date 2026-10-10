using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Pages;
using eGestion360Web.Services;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Inicio de sesión (paso F0.7): solo se aceptan contraseñas guardadas con BCrypt. Una guardada en texto plano se
    /// rechaza aunque coincida, no se convierte, y el mensaje es el mismo que con una clave equivocada.
    /// </summary>
    public class LoginTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        /// <summary>Un usuario con su persona vinculada (script 021), salvo que se pida sin persona.</summary>
        private int CrearUsuario(string nombre, string claveGuardada, bool conPersona = true)
        {
            using var db = _bd.Crear();
            int? idPersona = conPersona
                ? PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Emilio", "Garay", codigoInterno: "U-" + nombre).IdPersona
                : null;
            var usuario = new User { Email = $"{nombre}@prueba.local", Role = "admin", IsActive = true, PersonaId = idPersona };
            (usuario.Username, usuario.Password) = (nombre, claveGuardada);
            db.Users.Add(usuario);
            db.SaveChanges();
            return usuario.Id;
        }

        private static (LoginModel Pagina, HttpContext Http) Pagina(ApplicationDbContext db, string usuario, string clave)
        {
            var http = new DefaultHttpContext { Session = new SesionFalsa() };
            var pagina = new LoginModel(db, new PasswordService(), NullLogger<LoginModel>.Instance)
            {
                PageContext = new PageContext { HttpContext = http }
            };
            (pagina.Username, pagina.Password) = (usuario, clave);
            return (pagina, http);
        }

        private static string Mensaje(LoginModel pagina)
            => Assert.Single(Assert.Single(pagina.ModelState.Values).Errors).ErrorMessage;

        [Fact]
        public async Task Una_clave_guardada_con_BCrypt_inicia_sesion()
        {
            CrearUsuario("con_bcrypt", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-1", 4));
            using var db = _bd.Crear();
            var (pagina, http) = Pagina(db, "con_bcrypt", "Clave-Segura-1");

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("/MainMenu", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            Assert.False(string.IsNullOrEmpty(http.Session.GetString("UserId")));
        }

        [Fact]
        public async Task Una_clave_guardada_en_texto_plano_se_rechaza_aunque_coincida_y_no_se_convierte()
        {
            var id = CrearUsuario("texto_plano", "Clave-Plana-1");

            using (var db = _bd.Crear())
            {
                var (pagina, http) = Pagina(db, "texto_plano", "Clave-Plana-1");

                var resultado = await pagina.OnPostAsync();

                Assert.IsType<PageResult>(resultado);
                Assert.False(pagina.ModelState.IsValid);
                Assert.Null(http.Session.GetString("UserId"));
            }

            using var verificacion = _bd.Crear();
            Assert.Equal("Clave-Plana-1", verificacion.Users.Single(u => u.Id == id).Password);
        }

        // ── Persona del usuario (script 021) ────────────────────────────────

        [Fact]
        public async Task Al_entrar_la_sesion_guarda_el_nombre_corto_de_su_persona()
        {
            CrearUsuario("con_persona", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-3", 4));
            using var db = _bd.Crear();
            var (pagina, http) = Pagina(db, "con_persona", "Clave-Segura-3");

            await pagina.OnPostAsync();

            Assert.Equal("Emilio Garay", http.Session.GetString(AuthHelper.ClaveNombrePersona));
            Assert.Equal("con_persona", http.Session.GetString("Username"));   // la auditoría sigue usando el usuario
        }

        [Fact]
        public async Task Un_usuario_sin_persona_no_inicia_sesion_aunque_la_clave_sea_correcta()
        {
            CrearUsuario("sin_persona", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-4", 4), conPersona: false);
            using var db = _bd.Crear();
            var (pagina, http) = Pagina(db, "sin_persona", "Clave-Segura-4");

            var resultado = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(resultado);
            Assert.Equal(LoginModel.MensajeSinPersona, Mensaje(pagina));
            Assert.Null(http.Session.GetString("UserId"));
        }

        [Fact]
        public async Task Un_usuario_cuya_persona_fue_eliminada_o_fusionada_no_inicia_sesion()
        {
            var idEliminada = CrearUsuario("persona_eliminada", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-5", 4));
            var idFusionada = CrearUsuario("persona_fusionada", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-6", 4));
            using (var db = _bd.Crear())
            {
                var idPersonaEliminada = db.Users.Single(u => u.Id == idEliminada).PersonaId;
                var idPersonaFusionada = db.Users.Single(u => u.Id == idFusionada).PersonaId;
                db.Personas.Single(p => p.IdPersona == idPersonaEliminada).Eliminado = true;
                var otra = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Otra", "Persona", codigoInterno: "U-otra");
                db.Personas.Single(p => p.IdPersona == idPersonaFusionada).IdPersonaPrincipal = otra.IdPersona;
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            foreach (var (usuario, clave) in new[] { ("persona_eliminada", "Clave-Segura-5"), ("persona_fusionada", "Clave-Segura-6") })
            {
                var (pagina, http) = Pagina(lectura, usuario, clave);
                await pagina.OnPostAsync();
                Assert.Equal(LoginModel.MensajeSinPersona, Mensaje(pagina));
                Assert.Null(http.Session.GetString("UserId"));
            }
        }

        [Fact]
        public async Task Sin_persona_y_con_clave_equivocada_el_mensaje_es_el_de_la_clave()
        {
            CrearUsuario("sin_persona_2", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-7", 4), conPersona: false);
            CrearUsuario("con_bcrypt_3", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-8", 4));

            using var db = _bd.Crear();
            var (sinPersona, _) = Pagina(db, "sin_persona_2", "otra-clave");
            var (equivocada, _) = Pagina(db, "con_bcrypt_3", "otra-clave");
            await sinPersona.OnPostAsync();
            await equivocada.OnPostAsync();

            // Quien no sabe la clave no se entera de si el usuario tiene persona o no.
            Assert.Equal(Mensaje(equivocada), Mensaje(sinPersona));
        }

        [Fact]
        public async Task Una_clave_en_texto_plano_da_el_mismo_mensaje_que_una_clave_equivocada()
        {
            CrearUsuario("texto_plano_2", "Clave-Plana-2");
            CrearUsuario("con_bcrypt_2", BCrypt.Net.BCrypt.HashPassword("Clave-Segura-2", 4));

            using var db = _bd.Crear();
            var (textoPlano, _) = Pagina(db, "texto_plano_2", "Clave-Plana-2");
            var (equivocada, _) = Pagina(db, "con_bcrypt_2", "otra-clave");
            await textoPlano.OnPostAsync();
            await equivocada.OnPostAsync();

            Assert.Equal(Mensaje(equivocada), Mensaje(textoPlano));
        }
    }
}
