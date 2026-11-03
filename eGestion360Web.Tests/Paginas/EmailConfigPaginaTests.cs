using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Pages.Admin;
using eGestion360Web.Services;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// La pantalla «Configuración de Email Corporativo» (/admin/email-config): guardar, editar y los botones de cada perfil.
    /// El formulario se enlaza con el enlazador real de ASP.NET, igual que cuando el navegador lo envía: así se ve lo que el sitio
    /// rechaza (antes el botón Guardar «no hacía nada» porque el formulario nunca era válido y no se mostraba ningún motivo).
    /// </summary>
    public class EmailConfigPaginaTests : IDisposable
    {
        private const string ClaveDelBuzon = "Clave-Del-Buzon-De-Prueba-1";
        private const string ClaveNueva = "Otra-Clave-De-Prueba-2";

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly CorreoEspia _correo = new();
        private readonly IServiceProvider _mvc = new ServiceCollection().AddLogging().AddMvc().Services.BuildServiceProvider();

        public void Dispose() => _bd.Dispose();

        private static EncryptionService Cifrado() => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "clave-de-cifrado-solo-para-pruebas",
                ["Encryption:IV"] = "iv-solo-pruebas"
            }).Build(),
            NullLogger<EncryptionService>.Instance);

        /// <summary>Lo que el navegador envía al pulsar «Guardar Configuración» (los casilleros con asp-for mandan el oculto «false» detrás).</summary>
        private static Dictionary<string, string[]> Formulario(int id = 0, string nombre = "Notificaciones Somee 2", string clave = ClaveDelBuzon,
            bool ssl = true, bool predeterminada = true, string host = "mail.ejemplo.test", int puerto = 465)
            => new()
            {
                ["ConfigId"] = [id.ToString()],
                ["Configuration.ProfileName"] = [nombre],
                ["Configuration.Provider"] = ["SMTP"],
                ["Configuration.FromEmail"] = ["notificaciones@ejemplo.test"],
                ["Configuration.FromName"] = ["eGestion360 Notificaciones"],
                ["Configuration.SmtpHost"] = [host],
                ["Configuration.SmtpPort"] = [puerto.ToString()],
                ["Configuration.Username"] = ["notificaciones@ejemplo.test"],
                ["PlainPassword"] = [clave],
                ["Configuration.UseSsl"] = ssl ? ["true", "false"] : ["false"],
                ["Configuration.IsDefault"] = predeterminada ? ["true", "false"] : ["false"],
            };

        private EmailConfigModel NuevaPagina(ApplicationDbContext db, ModelStateDictionary? estado = null)
        {
            var http = new DefaultHttpContext { RequestServices = _mvc };
            var servicio = new EmailConfigurationService(db, Cifrado(), NullLogger<EmailConfigurationService>.Instance);
            var gestor = new EmailManagerService(servicio, _correo, NullLogger<EmailManagerService>.Instance);
            var pagina = new EmailConfigModel(gestor, NullLogger<EmailConfigModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor(), estado ?? new ModelStateDictionary())),
                TempData = new TempDataDictionary(http, new TempDataEnMemoria())
            };
            return pagina;
        }

        /// <summary>Enlaza los [BindProperty] de la página con los campos recibidos, como lo hace Razor Pages, y los deja en la página.</summary>
        private async Task<EmailConfigModel> Enlazar(Dictionary<string, string[]> campos, ApplicationDbContext db)
        {
            var estado = new ModelStateDictionary();
            var pagina = NuevaPagina(db, estado);
            var metadatos = _mvc.GetRequiredService<IModelMetadataProvider>();
            var fabrica = _mvc.GetRequiredService<IModelBinderFactory>();
            var enlazador = _mvc.GetRequiredService<ParameterBinder>();
            var valores = new FormValueProvider(BindingSource.Form,
                new FormCollection(campos.ToDictionary(c => c.Key, c => new StringValues(c.Value))), CultureInfo.InvariantCulture);

            foreach (var propiedad in typeof(EmailConfigModel).GetProperties().Where(p => p.GetCustomAttribute<BindPropertyAttribute>() != null))
            {
                var metadato = metadatos.GetMetadataForProperty(typeof(EmailConfigModel), propiedad.Name);
                var info = BindingInfo.GetBindingInfo(propiedad.GetCustomAttributes());
                var enlazadorDeModelo = fabrica.CreateBinder(new ModelBinderFactoryContext { Metadata = metadato, BindingInfo = info, CacheToken = metadato });
                var parametro = new ParameterDescriptor { Name = propiedad.Name, ParameterType = propiedad.PropertyType, BindingInfo = info };
                var resultado = await enlazador.BindModelAsync(pagina.PageContext, enlazadorDeModelo, valores, parametro, metadato, null);
                if (resultado.IsModelSet) propiedad.SetValue(pagina, resultado.Model);
            }
            return pagina;
        }

        private async Task<(EmailConfigModel Pagina, IActionResult Resultado)> Enviar(Dictionary<string, string[]> campos)
        {
            using var db = _bd.Crear();
            var pagina = await Enlazar(campos, db);
            return (pagina, await pagina.OnPostAsync());
        }

        private EmailConfiguration Sembrar(string nombre, bool predeterminada = false, bool activa = true)
        {
            using var db = _bd.Crear();
            var perfil = new EmailConfiguration
            {
                ProfileName = nombre, Provider = "SMTP", FromEmail = "viejo@ejemplo.test", FromName = "Viejo", SmtpHost = "smtp.viejo.test",
                SmtpPort = 587, UseSsl = true, Username = "viejo@ejemplo.test", PasswordHash = Cifrado().Encrypt("Clave-Vieja-De-Prueba-0"),
                IsDefault = predeterminada, IsActive = activa, TestEmailsSent = 3, LastTestedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), CreatedBy = "Admin"
            };
            db.EmailConfigurations.Add(perfil);
            db.SaveChanges();
            return perfil;
        }

        private EmailConfiguration Leer(string nombre)
        {
            using var db = _bd.Crear();
            return db.EmailConfigurations.Single(c => c.ProfileName == nombre);
        }

        private static string Errores(EmailConfigModel pagina)
            => string.Join(" | ", pagina.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

        private static JsonElement Json(IActionResult resultado)
            => JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(resultado).Value);

        // ── Guardar un perfil nuevo ───────────────────────────────────────────

        [Fact]
        public async Task El_formulario_tal_como_lo_envia_el_navegador_se_guarda()
        {
            var (pagina, resultado) = await Enviar(Formulario());

            Assert.True(resultado is RedirectToPageResult, Errores(pagina));
            Assert.True(pagina.ModelState.IsValid, Errores(pagina));
            var guardado = Leer("Notificaciones Somee 2");
            Assert.Equal("mail.ejemplo.test", guardado.SmtpHost);
            Assert.Equal(465, guardado.SmtpPort);
            Assert.True(guardado.IsActive);
            Assert.Contains("guardada exitosamente", pagina.TempData["SuccessMessage"]!.ToString());
        }

        [Fact]
        public async Task La_contrasena_se_guarda_cifrada_y_se_puede_descifrar_para_enviar()
        {
            await Enviar(Formulario());

            var guardado = Leer("Notificaciones Somee 2");
            Assert.NotEqual(ClaveDelBuzon, guardado.PasswordHash);
            Assert.DoesNotContain(ClaveDelBuzon, guardado.PasswordHash);
            Assert.Equal(ClaveDelBuzon, Cifrado().Decrypt(guardado.PasswordHash));
            Assert.True(guardado.IsConfigured);
        }

        [Fact]
        public async Task Marcarla_predeterminada_le_quita_el_titulo_al_perfil_anterior()
        {
            Sembrar("Notificaciones Somee", predeterminada: true);

            await Enviar(Formulario(predeterminada: true));

            Assert.True(Leer("Notificaciones Somee 2").IsDefault);
            Assert.False(Leer("Notificaciones Somee").IsDefault);
        }

        [Fact]
        public async Task Sin_marcar_predeterminada_no_toca_al_perfil_actual()
        {
            Sembrar("Notificaciones Somee", predeterminada: true);

            await Enviar(Formulario(predeterminada: false));

            Assert.False(Leer("Notificaciones Somee 2").IsDefault);
            Assert.True(Leer("Notificaciones Somee").IsDefault);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task El_casillero_de_SSL_se_respeta_marcado_o_no(bool ssl)
        {
            await Enviar(Formulario(ssl: ssl, puerto: 25));

            Assert.Equal(ssl, Leer("Notificaciones Somee 2").UseSsl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Sin_contrasena_no_se_guarda_y_explica_por_que(string clave)
        {
            var (pagina, resultado) = await Enviar(Formulario(clave: clave));

            Assert.IsType<PageResult>(resultado);
            Assert.True(pagina.ReabrirFormulario);
            Assert.Contains("contraseña", Errores(pagina), StringComparison.OrdinalIgnoreCase);
            using var db = _bd.Crear();
            Assert.Empty(db.EmailConfigurations);
        }

        [Fact]
        public async Task Un_nombre_repetido_se_rechaza_con_un_mensaje_claro_aunque_cambien_las_mayusculas()
        {
            Sembrar("Notificaciones Somee");

            var (pagina, resultado) = await Enviar(Formulario(nombre: "  notificaciones SOMEE "));

            Assert.IsType<PageResult>(resultado);
            Assert.True(pagina.ReabrirFormulario);
            Assert.Contains("Ya existe un perfil", Errores(pagina));
            using var db = _bd.Crear();
            Assert.Single(db.EmailConfigurations);
        }

        [Fact]
        public async Task Un_campo_obligatorio_vacio_no_se_guarda_y_el_formulario_vuelve_con_lo_escrito()
        {
            var campos = Formulario();
            campos["Configuration.SmtpHost"] = [""];

            var (pagina, resultado) = await Enviar(campos);

            Assert.IsType<PageResult>(resultado);
            Assert.True(pagina.ReabrirFormulario);
            Assert.NotEmpty(Errores(pagina));
            Assert.Equal("Notificaciones Somee 2", pagina.Configuration.ProfileName);   // lo escrito sigue ahí para volver a pintarlo
            using var db = _bd.Crear();
            Assert.Empty(db.EmailConfigurations);
        }

        // ── Editar un perfil guardado ─────────────────────────────────────────

        [Fact]
        public async Task Editar_cambia_los_datos_y_conserva_estado_pruebas_y_fecha_de_alta()
        {
            var original = Sembrar("Notificaciones Somee", predeterminada: true);

            var (pagina, resultado) = await Enviar(Formulario(id: original.Id, nombre: "Notificaciones Somee", clave: "", host: "mail.nuevo.test", puerto: 587));

            Assert.True(resultado is RedirectToPageResult, Errores(pagina));
            var editado = Leer("Notificaciones Somee");
            Assert.Equal(original.Id, editado.Id);
            Assert.Equal("mail.nuevo.test", editado.SmtpHost);
            Assert.Equal(587, editado.SmtpPort);
            Assert.Equal("notificaciones@ejemplo.test", editado.FromEmail);
            Assert.True(editado.IsActive);
            Assert.Equal(3, editado.TestEmailsSent);
            Assert.Equal(original.LastTestedAt, editado.LastTestedAt);
            Assert.Equal(original.CreatedAt, editado.CreatedAt);
            Assert.Equal("Admin", editado.CreatedBy);
            using var db = _bd.Crear();
            Assert.Single(db.EmailConfigurations);   // editó, no creó otro
        }

        [Fact]
        public async Task Editar_sin_escribir_contrasena_conserva_la_que_ya_estaba()
        {
            var original = Sembrar("Notificaciones Somee");

            await Enviar(Formulario(id: original.Id, nombre: "Notificaciones Somee", clave: "", predeterminada: false));

            var editado = Leer("Notificaciones Somee");
            Assert.Equal(original.PasswordHash, editado.PasswordHash);
            Assert.Equal("Clave-Vieja-De-Prueba-0", Cifrado().Decrypt(editado.PasswordHash));
        }

        [Fact]
        public async Task Editar_escribiendo_contrasena_la_reemplaza_cifrada()
        {
            var original = Sembrar("Notificaciones Somee");

            await Enviar(Formulario(id: original.Id, nombre: "Notificaciones Somee", clave: ClaveNueva, predeterminada: false));

            var editado = Leer("Notificaciones Somee");
            Assert.NotEqual(original.PasswordHash, editado.PasswordHash);
            Assert.Equal(ClaveNueva, Cifrado().Decrypt(editado.PasswordHash));
        }

        [Fact]
        public async Task Editar_puede_renombrar_pero_no_tomar_el_nombre_de_otro_perfil()
        {
            var primero = Sembrar("Perfil uno");
            Sembrar("Perfil dos");

            var (pagina, resultado) = await Enviar(Formulario(id: primero.Id, nombre: "perfil DOS", clave: "", predeterminada: false));

            Assert.IsType<PageResult>(resultado);
            Assert.Contains("Ya existe un perfil", Errores(pagina));
            Assert.Equal("Perfil uno", Leer("Perfil uno").ProfileName);

            var (_, renombrado) = await Enviar(Formulario(id: primero.Id, nombre: "Perfil tres", clave: "", predeterminada: false));
            Assert.IsType<RedirectToPageResult>(renombrado);
            Assert.Equal(primero.Id, Leer("Perfil tres").Id);
        }

        [Fact]
        public async Task Editar_sin_cambiar_el_nombre_no_choca_con_si_mismo()
        {
            var original = Sembrar("Notificaciones Somee");

            var (pagina, resultado) = await Enviar(Formulario(id: original.Id, nombre: "Notificaciones Somee", clave: "", predeterminada: false));

            Assert.True(resultado is RedirectToPageResult, Errores(pagina));
        }

        [Fact]
        public async Task Editar_un_perfil_que_ya_no_existe_avisa_y_no_crea_uno_nuevo()
        {
            var (pagina, resultado) = await Enviar(Formulario(id: 999, nombre: "Fantasma", clave: ClaveNueva));

            Assert.IsType<PageResult>(resultado);
            Assert.Contains("ya no existe", Errores(pagina));
            using var db = _bd.Crear();
            Assert.Empty(db.EmailConfigurations);
        }

        [Fact]
        public async Task Editar_un_perfil_no_deja_al_predeterminado_sin_titular()
        {
            var predeterminado = Sembrar("Notificaciones Somee", predeterminada: true);

            await Enviar(Formulario(id: predeterminado.Id, nombre: "Notificaciones Somee", clave: "", predeterminada: false));

            Assert.True(Leer("Notificaciones Somee").IsDefault);   // el título solo se cede eligiendo otro como predeterminado
        }

        // ── Botones de cada fila ──────────────────────────────────────────────

        [Fact]
        public async Task Probar_envia_con_el_perfil_elegido_y_no_con_el_predeterminado()
        {
            var predeterminado = Sembrar("Notificaciones Somee", predeterminada: true);
            var otro = Sembrar("Perfil nuevo");
            using var db = _bd.Crear();

            var json = Json(await NuevaPagina(db).OnPostTestAsync(otro.Id));

            Assert.True(json.GetProperty("success").GetBoolean());
            var (idPerfil, para) = Assert.Single(_correo.PruebasPorPerfil);
            Assert.Equal(otro.Id, idPerfil);
            Assert.NotEqual(predeterminado.Id, idPerfil);
            Assert.Equal("viejo@ejemplo.test", para);   // la prueba llega al propio buzón del perfil
        }

        [Fact]
        public async Task Probar_que_falla_explica_que_revisar()
        {
            var perfil = Sembrar("Perfil nuevo");
            _correo.Responde = false;
            using var db = _bd.Crear();

            var json = Json(await NuevaPagina(db).OnPostTestAsync(perfil.Id));

            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Contains("contraseña", json.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Probar_un_perfil_inexistente_no_envia_nada()
        {
            using var db = _bd.Crear();

            var json = Json(await NuevaPagina(db).OnPostTestAsync(999));

            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Empty(_correo.PruebasPorPerfil);
        }

        [Fact]
        public async Task La_estrella_pasa_el_predeterminado_a_otro_perfil()
        {
            var actual = Sembrar("Notificaciones Somee", predeterminada: true);
            var otro = Sembrar("Perfil nuevo");
            using var db = _bd.Crear();

            var json = Json(await NuevaPagina(db).OnPostSetDefaultAsync(otro.Id));

            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.True(Leer("Perfil nuevo").IsDefault);
            Assert.False(Leer("Notificaciones Somee").IsDefault);
            Assert.NotEqual(actual.Id, otro.Id);
        }

        [Fact]
        public async Task La_estrella_no_deja_como_predeterminado_a_un_perfil_inactivo()
        {
            Sembrar("Notificaciones Somee", predeterminada: true);
            var inactivo = Sembrar("Perfil apagado", activa: false);
            using var db = _bd.Crear();

            var json = Json(await NuevaPagina(db).OnPostSetDefaultAsync(inactivo.Id));

            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.True(Leer("Notificaciones Somee").IsDefault);
        }

        [Fact]
        public async Task Pausar_y_reactivar_cambia_el_estado()
        {
            var perfil = Sembrar("Perfil nuevo");

            using (var db = _bd.Crear())
                Assert.True(Json(await NuevaPagina(db).OnPostToggleActiveAsync(perfil.Id)).GetProperty("success").GetBoolean());
            Assert.False(Leer("Perfil nuevo").IsActive);

            using (var db = _bd.Crear())
                Assert.True(Json(await NuevaPagina(db).OnPostToggleActiveAsync(perfil.Id)).GetProperty("success").GetBoolean());
            Assert.True(Leer("Perfil nuevo").IsActive);
        }

        [Fact]
        public async Task Pausar_el_predeterminado_pasa_el_titulo_a_otro_perfil_activo()
        {
            var predeterminado = Sembrar("Notificaciones Somee", predeterminada: true);
            Sembrar("Perfil nuevo");
            using var db = _bd.Crear();

            await NuevaPagina(db).OnPostToggleActiveAsync(predeterminado.Id);

            Assert.False(Leer("Notificaciones Somee").IsDefault);
            Assert.True(Leer("Perfil nuevo").IsDefault);
        }

        [Fact]
        public async Task Eliminar_borra_un_perfil_pero_nunca_el_predeterminado()
        {
            var predeterminado = Sembrar("Notificaciones Somee", predeterminada: true);
            var otro = Sembrar("Perfil nuevo");

            using (var db = _bd.Crear())
                Assert.False(Json(await NuevaPagina(db).OnPostDeleteAsync(predeterminado.Id)).GetProperty("success").GetBoolean());
            using (var db = _bd.Crear())
                Assert.True(Json(await NuevaPagina(db).OnPostDeleteAsync(otro.Id)).GetProperty("success").GetBoolean());

            using var verificacion = _bd.Crear();
            Assert.Equal(new[] { "Notificaciones Somee" }, verificacion.EmailConfigurations.Select(c => c.ProfileName).ToArray());
        }

        [Fact]
        public async Task La_lista_trae_todos_los_perfiles_con_el_predeterminado_primero()
        {
            Sembrar("Zeta");
            Sembrar("Notificaciones Somee", predeterminada: true);
            using var db = _bd.Crear();
            var pagina = NuevaPagina(db);

            await pagina.OnGetAsync();

            Assert.Equal(new[] { "Notificaciones Somee", "Zeta" }, pagina.Configurations!.Select(c => c.ProfileName).ToArray());
            Assert.Equal(2, pagina.Stats!["TotalConfigurations"]);
        }
    }
}
