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

        private int CrearUsuario(string nombre, string claveGuardada)
        {
            using var db = _bd.Crear();
            var usuario = new User { Email = $"{nombre}@prueba.local", Role = "admin", IsActive = true };
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
