using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using eGestion360Web.Data;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Que la identidad (F1.5, ADR-003 y D50) salga solo de la cookie y no vuelva a colarse la sesión como fuente de «quién eres», y que
    /// el cableado de la autenticación en <c>Program.cs</c> esté completo y en el orden que necesita.
    /// </summary>
    public class IdentidadArquitecturaTests
    {
        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj"))) carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static IEnumerable<string> Archivos(string extension)
            => new[] { "Pages", "Services", "Data", "Models" }
                .SelectMany(c => Directory.EnumerateFiles(Path.Combine(Raiz(), c), extension, SearchOption.AllDirectories))
                .Append(Path.Combine(Raiz(), "Program.cs"));

        private static string Relativa(string ruta) => Path.GetRelativePath(Raiz(), ruta).Replace('\\', '/');

        [Fact]
        public void AuthHelper_no_decide_nada_por_lo_que_diga_la_sesion()
        {
            var texto = File.ReadAllText(Path.Combine(Raiz(), "Services", "AuthHelper.cs"));

            // La sesión puede guardar permisos y el tenant de soporte, pero ni el rol, ni la empresa, ni quién eres.
            foreach (var clave in new[] { "UserId", "Role", "EmpresaId", "EmpresaRolId", "MustChangePassword", "Username", "Email" })
                Assert.DoesNotContain($"GetString(\"{clave}\")", texto);
            Assert.DoesNotContain("SetString(\"UserId\"", texto);
            Assert.DoesNotContain("SetString(\"Role\"", texto);
        }

        [Fact]
        public void Ninguna_pantalla_decide_si_hay_sesion_mirando_la_sesion()
        {
            var culpables = Archivos("*.cs").Concat(Archivos("*.cshtml"))
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"Session\.GetString\(""UserId""\)\s*(!=|==)\s*null"))
                .Select(Relativa).ToList();

            Assert.True(culpables.Count == 0,
                "Estas pantallas preguntan a la sesión si hay un usuario; deben usar AuthHelper.HayCuenta / IsAuthenticated: " + string.Join(", ", culpables));
        }

        [Fact]
        public void Solo_el_espejo_y_la_entrada_en_curso_escriben_la_identidad_en_la_sesion()
        {
            var permitidos = new[] { "Services/Identidad/SesionDesdeIdentidadMiddleware.cs", "Services/Identidad/EntradaEnCurso.cs" };
            var identidad = new[] { "UserId", "Username", "Email", "Role", "EmpresaId", "EmpresaRolId", "MustChangePassword" };

            var culpables = Archivos("*.cs").Concat(Archivos("*.cshtml"))
                .Where(f => !permitidos.Contains(Relativa(f)))
                .Where(f => identidad.Any(k => Regex.IsMatch(File.ReadAllText(f), $@"Session\.SetString\(""{k}""")))
                .Select(Relativa).ToList();

            Assert.True(culpables.Count == 0, "Estas pantallas escriben su propia identidad en la sesión: " + string.Join(", ", culpables));
        }

        [Fact]
        public void Program_cablea_la_autenticacion_por_cookie_con_las_llaves_en_la_base()
        {
            var programa = File.ReadAllText(Path.Combine(Raiz(), "Program.cs"));

            Assert.Contains("AddAuthentication(ReclamosDeIdentidad.Esquema)", programa);
            Assert.Contains(".AddCookie(ReclamosDeIdentidad.Esquema", programa);
            Assert.Contains("o.Cookie.HttpOnly = true", programa);
            Assert.Contains("SameSiteMode.Lax", programa);
            Assert.Contains("o.SlidingExpiration = true", programa);
            Assert.Contains("o.Events.OnValidatePrincipal = EventosDeCookieDeIdentidad.AlValidarAsync", programa);
            Assert.Contains("AddDataProtection().SetApplicationName(\"eGestion360\")", programa);
            Assert.Contains("new RepositorioDeLlaves(", programa);
            foreach (var servicio in new[] { "IValidadorDeIdentidad, ValidadorDeIdentidad", "ICargadorDePermisos, CargadorDePermisos", "IBloqueoDeCuentas, BloqueoDeCuentas",
                         "IServicioMfa, ServicioMfa", "IDirectorioDeCuentas, DirectorioDeCuentas", "IAutenticador, Autenticador",
                         "ISincronizadorDeCuentas, SincronizadorDeCuentas" })
                Assert.Contains(servicio, programa);
        }

        [Fact]
        public void Los_middlewares_van_en_el_orden_que_necesita_cada_uno()
        {
            var programa = File.ReadAllText(Path.Combine(Raiz(), "Program.cs"));
            int Pos(string texto)
            {
                var i = programa.IndexOf(texto, StringComparison.Ordinal);
                Assert.True(i >= 0, "Falta en Program.cs: " + texto);
                return i;
            }

            // Sesión → cookie → tenant (de la cookie) → estado del tenant → espejo en la sesión (necesita el tenant) → autorización.
            var orden = new[]
            {
                Pos("app.UseSession()"), Pos("app.UseAuthentication()"), Pos("app.UseMiddleware<TenantMiddleware>()"),
                Pos("app.UseMiddleware<EstadoDelTenantMiddleware>()"), Pos("app.UseMiddleware<SesionDesdeIdentidadMiddleware>()"),
                Pos("app.UseAuthorization()")
            };
            Assert.Equal(orden.OrderBy(x => x), orden);
        }

        [Fact]
        public void El_login_no_toca_la_tabla_de_usuarios_ni_guarda_claves()
        {
            var login = File.ReadAllText(Path.Combine(Raiz(), "Pages", "Login.cshtml.cs"));

            Assert.DoesNotContain("_context.Users", login);
            Assert.DoesNotContain("Session.SetString", login);
        }

        // ── Invitaciones por correo (ADR-028) ─────────────────────────────────

        [Fact]
        public void Solo_el_servicio_de_invitaciones_crea_invitaciones()
        {
            var culpables = Archivos("*.cs")
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bInvitaciones\.(Add|AddRange|Update|Remove)\("))
                .Select(Relativa).Where(f => f != "Services/Identidad/ServicioDeInvitaciones.cs").ToList();

            Assert.True(culpables.Count == 0, "Estas pantallas escriben invitaciones sin pasar por IServicioDeInvitaciones: " + string.Join(", ", culpables));
        }

        [Fact]
        public void El_token_de_una_invitacion_nunca_se_guarda_ni_se_escribe_en_el_registro()
        {
            var servicio = File.ReadAllText(Path.Combine(Raiz(), "Services", "Identidad", "ServicioDeInvitaciones.cs"));

            // Lo único que se guarda del token es su huella.
            Assert.Contains("TokenHash = Huella(token)", servicio);
            Assert.DoesNotContain("TokenHash = token", servicio);

            // Ninguna línea de registro menciona el token, el enlace ni la clave.
            var lineasDeRegistro = servicio.Split('\n').Where(l => l.Contains("_log.Log")).ToList();
            Assert.NotEmpty(lineasDeRegistro);
            foreach (var linea in lineasDeRegistro)
                Assert.DoesNotMatch(@"(?i)\b(token|enlace|clave|password)\b", linea);

            // Los datos que el correo lleva a la persona (nombres de empresa y de quien invita) van escapados.
            Assert.Contains("WebUtility.HtmlEncode(empresa)", servicio);
            Assert.Contains("WebUtility.HtmlEncode(invitadoPor)", servicio);
        }

        [Fact]
        public void Aceptar_una_invitacion_fija_el_tenant_antes_de_abrir_la_transaccion()
        {
            var servicio = File.ReadAllText(Path.Combine(Raiz(), "Services", "Identidad", "ServicioDeInvitaciones.cs"));
            var metodo = servicio[servicio.IndexOf("public async Task<ResultadoDeAceptar> AceptarAsync", StringComparison.Ordinal)..];

            var fijar = metodo.IndexOf("_contextoFijable?.Fijar(", StringComparison.Ordinal);
            var abrir = metodo.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
            Assert.True(fijar > 0 && abrir > 0, "AceptarAsync debe fijar el tenant y abrir la transacción.");
            Assert.True(fijar < abrir, "El tenant se fija ANTES de abrir la transacción: con la conexión abierta la base no deja cambiarlo.");
        }

        [Fact]
        public void La_pantalla_de_aceptar_no_usa_la_sesion_y_protege_el_token_de_la_url()
        {
            var pagina = File.ReadAllText(Path.Combine(Raiz(), "Pages", "AceptarInvitacion.cshtml.cs"));

            Assert.DoesNotContain("Session", pagina);
            Assert.Contains("Referrer-Policy", pagina);
            Assert.Contains("no-referrer", pagina);
            Assert.Contains("no-store", pagina);
        }
    }

    /// <summary>Las llaves de Data Protection viven en <c>saas.llaves_proteccion_datos</c> (D48).</summary>
    public class RepositorioDeLlavesTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly ServiceProvider _servicios;

        public RepositorioDeLlavesTests()
        {
            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => _bd.Crear());
            _servicios = servicios.BuildServiceProvider();
        }

        public void Dispose()
        {
            _servicios.Dispose();
            _bd.Dispose();
        }

        [Fact]
        public void Lo_que_se_guarda_se_lee_despues_y_en_el_mismo_orden()
        {
            IXmlRepository repositorio = new RepositorioDeLlaves(_servicios.GetRequiredService<IServiceScopeFactory>());
            Assert.Empty(repositorio.GetAllElements());

            repositorio.StoreElement(new XElement("key", new XAttribute("id", "uno")), "llave-uno");
            repositorio.StoreElement(new XElement("key", new XAttribute("id", "dos")), "llave-dos");

            var leidas = repositorio.GetAllElements();
            Assert.Equal(new[] { "uno", "dos" }, leidas.Select(e => (string?)e.Attribute("id")));

            using var db = _bd.Crear();
            Assert.Equal(new[] { "llave-uno", "llave-dos" }, db.LlavesProteccionDatos.OrderBy(l => l.IdLlave).Select(l => l.NombreAmistoso));
        }

        [Fact]
        public void Otro_repositorio_ve_las_mismas_llaves_como_tras_un_reinicio_del_sitio()
        {
            var antes = new RepositorioDeLlaves(_servicios.GetRequiredService<IServiceScopeFactory>());
            antes.StoreElement(new XElement("key", new XAttribute("id", "persistida")), "llave");

            var despuesDelReinicio = new RepositorioDeLlaves(_servicios.GetRequiredService<IServiceScopeFactory>());

            Assert.Equal("persistida", (string?)Assert.Single(despuesDelReinicio.GetAllElements()).Attribute("id"));
        }
    }
}
