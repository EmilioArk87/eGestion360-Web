using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Pages.Catalogos.TiposCambio;
using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;
using Pantalla = eGestion360Web.Pages.Catalogos.TiposCambio.IndexModel;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// Handlers de la pantalla /Catalogos/TiposCambio con la sesión falsa, la base SQLite en memoria y los servicios
    /// reales de tasas. Lo importante: los permisos se revisan en cada handler (no basta con ocultar botones) y una
    /// empresa nunca toca las tasas de otra.
    /// </summary>
    public class PantallaTiposCambioTests : IDisposable
    {
        private const int A = DatosBase.EmpresaA;
        private const int B = DatosBase.EmpresaB;
        private const string UsuarioDePrueba = "usuario.prueba";

        private static readonly DateOnly Viernes = EscenarioTasas.Viernes;
        private static readonly DateOnly Jueves = EscenarioTasas.Jueves;

        private readonly EscenarioTasas _e = new();
        private readonly ColaEjecucionTasasCambio _cola = new();
        private readonly LogQueGuarda<Pantalla> _log = new();
        private readonly List<IDisposable> _desechables = new();

        public void Dispose()
        {
            foreach (var d in _desechables) d.Dispose();
            _e.Dispose();
        }

        // ── Sesiones ──────────────────────────────────────────────────────────

        private static HttpContext Sesion(string? rol, int? empresa = null, string[]? permisos = null, bool conModulo = true,
            bool autenticado = true)
        {
            var http = SesionFalsa.Contexto(rol, empresa?.ToString(), autenticado);
            if (conModulo) http.Session.SetString("Modulos", JsonSerializer.Serialize(new[] { "catalogos" }));
            http.Session.SetString("Permisos", JsonSerializer.Serialize(
                new Dictionary<string, string[]> { ["catalogos"] = permisos ?? new[] { "ver" } }));
            http.Session.SetString("Username", UsuarioDePrueba);
            return http;
        }

        private static HttpContext AdminSistema(int? empresa = null) => Sesion("admin", empresa);
        private static HttpContext AdminEmpresa(int empresa) => Sesion("empresa_admin", empresa);
        private static HttpContext UsuarioQueCrea(int empresa) => Sesion("empresa_user", empresa, new[] { "ver", "crear" });
        private static HttpContext UsuarioSoloLectura(int empresa) => Sesion("empresa_user", empresa, new[] { "ver" });

        // ── Página ────────────────────────────────────────────────────────────

        private Pantalla Pagina(HttpContext http, ITasaCambioConsultaService? consultas = null)
        {
            var db = _e.Bd.Crear();
            _desechables.Add(db);
            var modelo = new Pantalla(consultas ?? _e.Consultas(db), _e.Servicio(), _cola, Options.Create(_e.Opciones), _e.Reloj, _log)
            {
                PageContext = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor())),
            };
            modelo.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return modelo;
        }

        private static RegistroTasaInput RegistroValido() =>
            new() { Moneda = "USD", Tipo = Cat.TipoTasa.Venta, Fecha = Viernes, Tasa = "27.1000" };

        private static async Task<IActionResult> Llamar(Pantalla p, string handler, int idTasa) => handler switch
        {
            "Get" => await p.OnGetAsync(),
            "Registrar" => await p.OnPostRegistrarAsync(RegistroValido()),
            "Aprobar" => await p.OnPostAprobarAsync(idTasa),
            "Rechazar" => await p.OnPostRechazarAsync(idTasa, "No coincide con el BCH."),
            "Ejecutar" => p.OnPostEjecutar(),
            _ => throw new ArgumentOutOfRangeException(nameof(handler))
        };

        public static TheoryData<string> Handlers => new() { "Get", "Registrar", "Aprobar", "Rechazar", "Ejecutar" };

        // ── Datos ─────────────────────────────────────────────────────────────

        private TasaCambio Leer(int idTasa)
        {
            using var db = _e.Bd.Crear();
            return db.TasasCambio.AsNoTracking().Single(t => t.IdTasaCambio == idTasa);
        }

        private List<TasaCambio> Manuales()
        {
            using var db = _e.Bd.Crear();
            return db.TasasCambio.AsNoTracking().Where(t => t.Fuente == Cat.Fuente.Manual && t.CreadoPor == UsuarioDePrueba).ToList();
        }

        private int PendienteOficial() =>
            _e.Sembrar("USD", Cat.TipoTasa.Venta, Viernes, 29.5000m, estado: Cat.EstadoTasa.EnRevision).IdTasaCambio;

        private int PendientePropia(int empresa, string tipo = Cat.TipoTasa.Venta) =>
            _e.Sembrar("USD", tipo, Viernes, 29.5000m, empresa, Cat.EstadoTasa.EnRevision).IdTasaCambio;

        private void SembrarDerivada(string tipo, decimal valor)
        {
            var id = _e.Sembrar("EUR", tipo, Viernes, valor, fuente: Cat.Fuente.Derivada).IdTasaCambio;
            using var db = _e.Bd.Crear();
            db.TasasCambio.Single(t => t.IdTasaCambio == id).EsDerivada = true;
            db.SaveChanges();
        }

        private long SembrarEjecucion()
        {
            using var db = _e.Bd.Crear();
            var ejecucion = new TasaCambioEjecucion
            {
                Disparador = Cat.Disparador.Externo,
                FechaObjetivo = Viernes,
                Intento = 2,
                Estado = Cat.EstadoEjecucion.Parcial,
                Fuente = Cat.Fuente.BchXlsx,
                Endpoint = EscenarioTasas.UrlExcel,
                HttpStatus = 200,
                Leidos = 4,
                Insertados = 2,
                Duplicados = 1,
                Invalidos = 1,
                Mensaje = "Falta el euro: el BCE no respondió.",
                DetalleError = "BCE: HTTP 503 (Service Unavailable).",
                Servidor = "SERVIDOR-PRUEBA",
                EjecutadoPor = "job",
                InicioUtc = new DateTime(2026, 10, 2, 23, 0, 0, DateTimeKind.Utc),
                FinUtc = new DateTime(2026, 10, 2, 23, 0, 12, DateTimeKind.Utc),
                Detalles =
                {
                    new TasaCambioEjecucionDetalle { MonedaOrigen = "USD", MonedaDestino = "HNL", TipoTasa = Cat.TipoTasa.Compra,
                        FechaVigencia = Viernes, ValorLeido = 26.8925m, Resultado = Cat.ResultadoDetalle.Insertada },
                    new TasaCambioEjecucionDetalle { MonedaOrigen = "EUR", MonedaDestino = "HNL", TipoTasa = Cat.TipoTasa.Venta,
                        FechaVigencia = Viernes, Resultado = Cat.ResultadoDetalle.Error, Motivo = "BCE: HTTP 503." }
                }
            };
            db.TasasCambioEjecuciones.Add(ejecucion);
            db.SaveChanges();
            return ejecucion.IdEjecucion;
        }

        /// <summary>Oficiales del viernes (USD y EUR derivado), propias de A y de B, una oficial en revisión y una ejecución.</summary>
        private void SembrarEscenarioCompleto()
        {
            _e.Sembrar("USD", Cat.TipoTasa.Compra, Viernes, 26.8925m);
            _e.Sembrar("USD", Cat.TipoTasa.Venta, Viernes, 27.0270m);
            SembrarDerivada(Cat.TipoTasa.Compra, 29.2459m);
            SembrarDerivada(Cat.TipoTasa.Venta, 29.3946m);
            _e.Sembrar("USD", Cat.TipoTasa.Venta, Viernes, 27.1000m, A);
            _e.Sembrar("USD", Cat.TipoTasa.Compra, Viernes, 26.9500m, B);
            _e.Sembrar("EUR", Cat.TipoTasa.Venta, Jueves, 35.0000m, estado: Cat.EstadoTasa.EnRevision);
            SembrarEjecucion();
        }

        private static void Redirige(IActionResult resultado, string pagina) =>
            Assert.Equal(pagina, Assert.IsType<RedirectToPageResult>(resultado).PageName);

        private static string? Error(Pantalla p) => p.TempData[Pantalla.ClaveError] as string;
        private static string? Mensaje(Pantalla p) => p.TempData[Pantalla.ClaveMensaje] as string;

        // ══════════════════════════════════════════════════════════════════
        //  ACCESO
        // ══════════════════════════════════════════════════════════════════

        [Theory]
        [MemberData(nameof(Handlers))]
        public async Task Sin_sesion_todo_va_al_login_y_no_cambia_nada(string handler)
        {
            var pendiente = PendienteOficial();
            var pagina = Pagina(Sesion(rol: null, empresa: null, conModulo: false, autenticado: false));

            Redirige(await Llamar(pagina, handler, pendiente), "/Login");

            Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(pendiente).Estado);
            Assert.Empty(Manuales());
            Assert.Equal(0, _cola.Pendientes);
        }

        [Theory]
        [MemberData(nameof(Handlers))]
        public async Task Sin_el_modulo_de_catalogos_va_al_menu_y_no_cambia_nada(string handler)
        {
            var pendiente = PendientePropia(A);
            var pagina = Pagina(Sesion("empresa_admin", A, new[] { "ver", "crear" }, conModulo: false));

            Redirige(await Llamar(pagina, handler, pendiente), "/MainMenu");

            Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(pendiente).Estado);
            Assert.Empty(Manuales());
            Assert.Equal(0, _cola.Pendientes);
        }

        [Fact]
        public async Task Quien_debe_cambiar_la_clave_va_al_login()
        {
            var http = AdminSistema();
            http.Session.SetString("MustChangePassword", "1");

            Redirige(await Pagina(http).OnGetAsync(), "/Login");
            Redirige(Pagina(http).OnPostEjecutar(), "/Login");
            Assert.Equal(0, _cola.Pendientes);
        }

        // ══════════════════════════════════════════════════════════════════
        //  APROBAR / RECHAZAR Y EJECUTAR: PERMISOS
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Un_usuario_sin_administracion_no_ejecuta_ni_aprueba_ni_rechaza()
        {
            var oficial = PendienteOficial();
            var propia = PendientePropia(A, Cat.TipoTasa.Compra);
            var http = UsuarioQueCrea(A);

            var ejecutar = Pagina(http);
            Redirige(ejecutar.OnPostEjecutar(), "Index");
            Assert.Contains("administrador del sistema", Error(ejecutar));
            Assert.Equal(0, _cola.Pendientes);

            foreach (var id in new[] { oficial, propia })
            {
                var aprobar = Pagina(http);
                Redirige(await aprobar.OnPostAprobarAsync(id), "Index");
                Assert.Contains("Solo un administrador", Error(aprobar));

                var rechazar = Pagina(http);
                Redirige(await rechazar.OnPostRechazarAsync(id, "Valor raro."), "Index");
                Assert.Contains("Solo un administrador", Error(rechazar));

                Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(id).Estado);
            }

            // En la vista tampoco se ofrecen los botones.
            var get = Pagina(http);
            await get.OnGetAsync();
            Assert.False(get.PuedeRevisar(null));
            Assert.False(get.PuedeRevisar(A));
            Assert.False(get.PuedeEjecutar);
        }

        [Fact]
        public async Task El_admin_de_empresa_no_ejecuta_ni_aprueba_ni_rechaza_la_tasa_oficial()
        {
            var oficial = PendienteOficial();
            var http = AdminEmpresa(A);

            var ejecutar = Pagina(http);
            Redirige(ejecutar.OnPostEjecutar(), "Index");
            Assert.NotNull(Error(ejecutar));
            Assert.Equal(0, _cola.Pendientes);

            var aprobar = Pagina(http);
            Redirige(await aprobar.OnPostAprobarAsync(oficial), "Index");
            Assert.Contains("tasa oficial", Error(aprobar));

            var rechazar = Pagina(http);
            Redirige(await rechazar.OnPostRechazarAsync(oficial, "No me gusta."), "Index");
            Assert.Contains("tasa oficial", Error(rechazar));

            Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(oficial).Estado);
            Assert.Contains(_log.Entradas, l => l.Nivel == LogLevel.Warning && l.Mensaje.Contains("sin permiso"));
        }

        [Fact]
        public async Task El_admin_de_empresa_no_toca_la_tasa_de_otra_empresa_aunque_envie_su_id()
        {
            var deB = PendientePropia(B);
            var http = AdminEmpresa(A);

            var aprobar = Pagina(http);
            Redirige(await aprobar.OnPostAprobarAsync(deB), "Index");
            Assert.Contains("no pertenece a su empresa", Error(aprobar));

            var rechazar = Pagina(http);
            Redirige(await rechazar.OnPostRechazarAsync(deB, "Intento desde otra empresa."), "Index");
            Assert.Contains("no pertenece a su empresa", Error(rechazar));

            var tasa = Leer(deB);
            Assert.Equal(Cat.EstadoTasa.EnRevision, tasa.Estado);
            Assert.Null(tasa.ModificadoPor);

            // Tampoco el administrador del sistema sin empresa en la sesión: las propias son de cada empresa.
            var admin = Pagina(AdminSistema());
            Redirige(await admin.OnPostAprobarAsync(deB), "Index");
            Assert.NotNull(Error(admin));
            Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(deB).Estado);
        }

        [Fact]
        public async Task El_admin_de_empresa_aprueba_y_rechaza_las_propias_de_su_empresa()
        {
            var aAprobar = PendientePropia(A, Cat.TipoTasa.Venta);
            var aRechazar = PendientePropia(A, Cat.TipoTasa.Compra);
            var http = AdminEmpresa(A);

            var aprobar = Pagina(http);
            Redirige(await aprobar.OnPostAprobarAsync(aAprobar), "Index");
            Assert.Null(Error(aprobar));
            Assert.Contains("Tasa aprobada", Mensaje(aprobar));
            Assert.Equal(Cat.EstadoTasa.Vigente, Leer(aAprobar).Estado);
            Assert.Equal(UsuarioDePrueba, Leer(aAprobar).ModificadoPor);

            var rechazar = Pagina(http);
            Redirige(await rechazar.OnPostRechazarAsync(aRechazar, "  Error de captura.  "), "Index");
            Assert.Contains("Tasa rechazada", Mensaje(rechazar));
            Assert.Equal(Cat.EstadoTasa.Rechazada, Leer(aRechazar).Estado);

            var get = Pagina(http);
            await get.OnGetAsync();
            Assert.True(get.PuedeRevisar(A));
            Assert.False(get.PuedeRevisar(B));
            Assert.False(get.PuedeRevisar(null));
        }

        [Fact]
        public async Task El_admin_del_sistema_aprueba_y_rechaza_la_oficial_y_el_rechazo_exige_motivo()
        {
            var aAprobar = PendienteOficial();
            var aRechazar = _e.Sembrar("EUR", Cat.TipoTasa.Compra, Viernes, 40.0000m, estado: Cat.EstadoTasa.EnRevision).IdTasaCambio;
            var http = AdminSistema();

            var aprobar = Pagina(http);
            Redirige(await aprobar.OnPostAprobarAsync(aAprobar), "Index");
            Assert.Contains("Tasa aprobada", Mensaje(aprobar));
            Assert.Equal(Cat.EstadoTasa.Vigente, Leer(aAprobar).Estado);

            foreach (var motivo in new[] { null, "", "   " })
            {
                var sinMotivo = Pagina(http);
                Redirige(await sinMotivo.OnPostRechazarAsync(aRechazar, motivo), "Index");
                Assert.Equal("Indique el motivo del rechazo.", Error(sinMotivo));
                Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(aRechazar).Estado);
            }

            var muyLargo = Pagina(http);
            await muyLargo.OnPostRechazarAsync(aRechazar, new string('x', Pantalla.LargoMaximoMotivo + 1));
            Assert.Contains("no puede pasar de", Error(muyLargo));
            Assert.Equal(Cat.EstadoTasa.EnRevision, Leer(aRechazar).Estado);

            var rechazar = Pagina(http);
            Redirige(await rechazar.OnPostRechazarAsync(aRechazar, "El BCE publicó otro valor."), "Index");
            Assert.Contains("Tasa rechazada", Mensaje(rechazar));
            Assert.Equal(Cat.EstadoTasa.Rechazada, Leer(aRechazar).Estado);

            // Una que ya no está en revisión: lo dice y no cambia nada.
            var otraVez = Pagina(http);
            await otraVez.OnPostAprobarAsync(aRechazar);
            Assert.Contains("no está pendiente de revisión", Error(otraVez));
            Assert.Equal(Cat.EstadoTasa.Rechazada, Leer(aRechazar).Estado);
        }

        [Fact]
        public void Ejecutar_ahora_encola_una_ejecucion_forzada_y_manual_del_admin_del_sistema()
        {
            var pagina = Pagina(AdminSistema());

            Redirige(pagina.OnPostEjecutar(), "Index");

            Assert.Contains("Ejecución solicitada", Mensaje(pagina));
            Assert.Equal(1, _cola.Pendientes);
            Assert.True(_cola.Lector.TryRead(out var solicitud));
            Assert.Equal(Cat.Disparador.Manual, solicitud!.Disparador);
            Assert.True(solicitud.Forzar);
            Assert.Equal(UsuarioDePrueba, solicitud.EjecutadoPor);
        }

        [Fact]
        public async Task Con_el_job_deshabilitado_no_se_ofrece_el_boton_ni_se_encola()
        {
            _e.Opciones.Habilitado = false;

            var get = Pagina(AdminSistema());
            await get.OnGetAsync();
            Assert.False(get.JobHabilitado);
            Assert.False(get.PuedeEjecutar);

            var post = Pagina(AdminSistema());
            Redirige(post.OnPostEjecutar(), "Index");
            Assert.Contains("deshabilitado", Error(post));
            Assert.Equal(0, _cola.Pendientes);
        }

        [Fact]
        public void Con_la_cola_llena_avisa_en_lugar_de_fallar()
        {
            for (var i = 0; i < ColaEjecucionTasasCambio.Capacidad; i++)
                Assert.True(_cola.Solicitar(Cat.Disparador.Manual, "otro"));

            var pagina = Pagina(AdminSistema());
            Redirige(pagina.OnPostEjecutar(), "Index");

            Assert.Contains("esperando turno", Error(pagina));
        }

        // ══════════════════════════════════════════════════════════════════
        //  REGISTRAR TASA PROPIA
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Registrar_usa_la_empresa_de_la_sesion_aunque_el_formulario_traiga_otra()
        {
            var http = UsuarioQueCrea(A);
            // Un formulario manipulado que intenta registrar para la empresa B por todos los nombres posibles.
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["IdEmpresa"] = B.ToString(),
                ["idEmpresa"] = B.ToString(),
                ["Registro.IdEmpresa"] = B.ToString(),
                ["EmpresaId"] = B.ToString(),
                ["Registro.Moneda"] = "USD",
                ["Registro.Tipo"] = "VENTA",
                ["Registro.Fecha"] = "2026-10-02",
                ["Registro.Tasa"] = "27.1000",
            });
            http.Request.QueryString = new QueryString($"?handler=Registrar&idEmpresa={B}");
            var pagina = Pagina(http);

            Redirige(await pagina.OnPostRegistrarAsync(RegistroValido()), "Index");

            Assert.Contains("Tasa registrada", Mensaje(pagina));
            var tasa = Assert.Single(Manuales());
            Assert.Equal(A, tasa.IdEmpresa);
            Assert.Equal("USD", tasa.MonedaOrigen);
            Assert.Equal(Cat.TipoTasa.Venta, tasa.TipoTasa);
            Assert.Equal(Viernes, tasa.FechaVigencia);
            Assert.Equal(27.1000m, tasa.Tasa);
            Assert.Equal(Cat.EstadoTasa.Vigente, tasa.Estado);
        }

        [Fact]
        public void El_formulario_y_los_handlers_no_tienen_ningun_campo_de_empresa()
        {
            Assert.DoesNotContain(typeof(RegistroTasaInput).GetProperties(),
                p => p.Name.Contains("Empresa", StringComparison.OrdinalIgnoreCase));

            var parametros = typeof(Pantalla).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("OnGet", StringComparison.Ordinal) || m.Name.StartsWith("OnPost", StringComparison.Ordinal))
                .SelectMany(m => m.GetParameters())
                .ToList();
            Assert.NotEmpty(parametros);
            Assert.DoesNotContain(parametros, p => p.Name!.Contains("empresa", StringComparison.OrdinalIgnoreCase));

            Assert.Null(typeof(Pantalla).GetCustomAttribute<BindPropertiesAttribute>());
            Assert.DoesNotContain(typeof(Pantalla).GetProperties().Where(p => p.GetCustomAttribute<BindPropertyAttribute>() != null),
                p => p.Name.Contains("Empresa", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Registrar_otra_vez_la_misma_fecha_reemplaza_la_anterior()
        {
            var http = AdminEmpresa(A);

            await Pagina(http).OnPostRegistrarAsync(RegistroValido());
            var segunda = Pagina(http);
            Redirige(await segunda.OnPostRegistrarAsync(new RegistroTasaInput
                { Moneda = "usd", Tipo = "venta", Fecha = Viernes, Tasa = "27,1500" }), "Index");

            Assert.Contains("reemplaza", Mensaje(segunda));
            var tasas = Manuales().OrderBy(t => t.Version).ToList();
            Assert.Equal(2, tasas.Count);
            Assert.Equal(Cat.EstadoTasa.Reemplazada, tasas[0].Estado);
            Assert.Equal(Cat.EstadoTasa.Vigente, tasas[1].Estado);
            Assert.Equal(27.1500m, tasas[1].Tasa);
            Assert.All(tasas, t => Assert.Equal(A, t.IdEmpresa));
        }

        [Theory]
        [InlineData(null, "VENTA", "2026-10-02", "27.1", "Registro.Moneda", "Seleccione la moneda")]
        [InlineData("JPY", "VENTA", "2026-10-02", "27.1", "Registro.Moneda", "Seleccione la moneda")]
        [InlineData("USD", null, "2026-10-02", "27.1", "Registro.Tipo", "Seleccione el tipo")]
        [InlineData("USD", "REFERENCIA", "2026-10-02", "27.1", "Registro.Tipo", "Seleccione el tipo")]
        [InlineData("USD", "VENTA", null, "27.1", "Registro.Fecha", "Indique la fecha")]
        [InlineData("USD", "VENTA", "2026-10-02", null, "Registro.Tasa", "Indique la tasa")]
        [InlineData("USD", "VENTA", "2026-10-02", "abc", "Registro.Tasa", "debe ser un número")]
        [InlineData("USD", "VENTA", "2026-10-02", "-27.1", "Registro.Tasa", "debe ser un número")]
        [InlineData("USD", "VENTA", "2026-10-02", "0", "Registro.Tasa", "mayor que cero")]
        [InlineData("USD", "VENTA", "2026-10-02", "27.10251", "Registro.Tasa", "hasta 4 decimales")]
        public async Task Registrar_valida_en_el_servidor_con_mensajes_en_espanol(string? moneda, string? tipo, string? fecha,
            string? tasa, string campo, string mensaje)
        {
            _e.Sembrar("USD", Cat.TipoTasa.Venta, Viernes, 27.0270m);
            var pagina = Pagina(UsuarioQueCrea(A));

            var resultado = await pagina.OnPostRegistrarAsync(new RegistroTasaInput
            {
                Moneda = moneda, Tipo = tipo, Fecha = fecha == null ? null : DateOnly.ParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture), Tasa = tasa
            });

            Assert.IsType<PageResult>(resultado);
            Assert.True(pagina.ModelState.TryGetValue(campo, out var entrada));
            Assert.Contains(entrada!.Errors, e => e.ErrorMessage.Contains(mensaje));
            Assert.Empty(Manuales());
            Assert.Equal(4, pagina.Vigentes.Count);   // la pantalla se vuelve a mostrar con sus datos
            Assert.Equal(tasa, pagina.Registro.Tasa); // y con lo que escribió el usuario
        }

        [Fact]
        public async Task Registrar_un_dato_que_el_servicio_rechaza_vuelve_al_formulario_con_el_motivo()
        {
            var pagina = Pagina(UsuarioQueCrea(A));

            var resultado = await pagina.OnPostRegistrarAsync(new RegistroTasaInput
                { Moneda = "USD", Tipo = "COMPRA", Fecha = Viernes, Tasa = "80" });

            Assert.IsType<PageResult>(resultado);
            Assert.Contains(pagina.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("Fuera del rango"));
            Assert.Empty(Manuales());

            var futura = Pagina(UsuarioQueCrea(A));
            Assert.IsType<PageResult>(await futura.OnPostRegistrarAsync(new RegistroTasaInput
                { Moneda = "USD", Tipo = "COMPRA", Fecha = Viernes.AddDays(5), Tasa = "26.9" }));
            Assert.Contains(futura.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("Fecha futura"));
            Assert.Empty(Manuales());
        }

        [Fact]
        public async Task Sin_permiso_de_crear_o_sin_empresa_en_la_sesion_no_se_registra()
        {
            var soloLectura = Pagina(UsuarioSoloLectura(A));
            Redirige(await soloLectura.OnPostRegistrarAsync(RegistroValido()), "Index");
            Assert.Contains("No tiene permiso", Error(soloLectura));

            var adminSinEmpresa = Pagina(AdminSistema());
            Redirige(await adminSinEmpresa.OnPostRegistrarAsync(RegistroValido()), "Index");
            Assert.Contains("no tiene empresa", Error(adminSinEmpresa));

            Assert.Empty(Manuales());

            var get = Pagina(UsuarioSoloLectura(A));
            await get.OnGetAsync();
            Assert.False(get.PuedeRegistrar);
        }

        [Theory]
        [InlineData("26.8925", 26.8925)]
        [InlineData(" 27,027 ", 27.027)]
        [InlineData("30", 30)]
        public void La_tasa_se_lee_sin_depender_de_la_cultura_del_servidor(string texto, decimal esperada)
        {
            Assert.Null(Pantalla.LeerTasa(texto, out var tasa));
            Assert.Equal(esperada, tasa);
        }

        // ══════════════════════════════════════════════════════════════════
        //  GET: DATOS, AISLAMIENTO Y BITÁCORA
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task El_GET_carga_vigentes_pendientes_historico_y_bitacora()
        {
            SembrarEscenarioCompleto();
            var pagina = Pagina(UsuarioQueCrea(A));

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.Null(pagina.AvisoBaseDatos);
            Assert.Equal(Viernes, pagina.Hoy);
            Assert.Equal(Viernes, pagina.Registro.Fecha);

            Assert.Equal(4, pagina.Vigentes.Count);
            var usdVenta = pagina.Vigentes.Single(v => v.MonedaOrigen == "USD" && v.TipoTasa == Cat.TipoTasa.Venta).Vigente!;
            Assert.True(usdVenta.EsPropiaEmpresa);                // la de la empresa prevalece
            Assert.Equal(27.1000m, usdVenta.Tasa);
            var usdCompra = pagina.Vigentes.Single(v => v.MonedaOrigen == "USD" && v.TipoTasa == Cat.TipoTasa.Compra).Vigente!;
            Assert.False(usdCompra.EsPropiaEmpresa);              // la de B no aparece
            Assert.Equal(26.8925m, usdCompra.Tasa);
            Assert.All(pagina.Vigentes.Where(v => v.MonedaOrigen == "EUR"), v => Assert.True(v.Vigente!.EsDerivada));

            var pendiente = Assert.Single(pagina.Pendientes);
            Assert.Null(pendiente.IdEmpresa);
            Assert.Equal("EUR", pendiente.MonedaOrigen);

            Assert.Equal(6, pagina.Historico.Total);              // 4 oficiales + 1 en revisión + 1 propia de A
            Assert.DoesNotContain(pagina.Historico.Filas, f => f.IdEmpresa == B);

            var ejecucion = Assert.Single(pagina.Ejecuciones);
            Assert.Equal(Cat.EstadoEjecucion.Parcial, ejecucion.Estado);
            Assert.Equal(2, ejecucion.Detalles.Count);
        }

        [Fact]
        public async Task Sin_tasas_todavia_la_pantalla_carga_vacia()
        {
            var pagina = Pagina(UsuarioSoloLectura(A));

            Assert.IsType<PageResult>(await pagina.OnGetAsync());

            Assert.Null(pagina.AvisoBaseDatos);
            Assert.Equal(4, pagina.Vigentes.Count);
            Assert.All(pagina.Vigentes, v => Assert.Null(v.Vigente));
            Assert.Empty(pagina.Pendientes);
            Assert.Equal(0, pagina.Historico.Total);
            Assert.Empty(pagina.Ejecuciones);
        }

        [Fact]
        public async Task Una_empresa_no_ve_las_tasas_propias_ni_las_pendientes_de_otra()
        {
            SembrarEscenarioCompleto();
            PendientePropia(B, Cat.TipoTasa.Compra);

            var deB = Pagina(AdminEmpresa(B));
            await deB.OnGetAsync();

            Assert.Equal(26.9500m, deB.Vigentes.Single(v => v.MonedaOrigen == "USD" && v.TipoTasa == Cat.TipoTasa.Compra).Vigente!.Tasa);
            Assert.Equal(27.0270m, deB.Vigentes.Single(v => v.MonedaOrigen == "USD" && v.TipoTasa == Cat.TipoTasa.Venta).Vigente!.Tasa);
            Assert.DoesNotContain(deB.Historico.Filas, f => f.IdEmpresa == A);
            Assert.DoesNotContain(deB.Pendientes, p => p.IdEmpresa == A);

            var deA = Pagina(AdminEmpresa(A));
            await deA.OnGetAsync();
            Assert.DoesNotContain(deA.Pendientes, p => p.IdEmpresa == B);
            Assert.DoesNotContain(deA.Historico.Filas, f => f.IdEmpresa == B);

            // El administrador del sistema sin empresa ve solo las oficiales.
            var admin = Pagina(AdminSistema());
            await admin.OnGetAsync();
            Assert.All(admin.Historico.Filas, f => Assert.Null(f.IdEmpresa));
            Assert.All(admin.Pendientes, p => Assert.Null(p.IdEmpresa));
        }

        [Fact]
        public async Task La_bitacora_muestra_endpoint_y_detalle_de_error_solo_al_admin_del_sistema()
        {
            SembrarEjecucion();

            foreach (var http in new[] { UsuarioSoloLectura(A), AdminEmpresa(A) })
            {
                var pagina = Pagina(http);
                await pagina.OnGetAsync();
                var e = Assert.Single(pagina.Ejecuciones);
                Assert.Null(e.Endpoint);
                Assert.Null(e.DetalleError);
                Assert.Equal("Falta el euro: el BCE no respondió.", e.Mensaje);
            }

            var admin = Pagina(AdminSistema());
            await admin.OnGetAsync();
            var completa = Assert.Single(admin.Ejecuciones);
            Assert.Equal(EscenarioTasas.UrlExcel, completa.Endpoint);
            Assert.Contains("HTTP 503", completa.DetalleError);
        }

        [Fact]
        public async Task Los_filtros_del_historico_se_aplican_y_los_valores_desconocidos_se_ignoran()
        {
            SembrarEscenarioCompleto();
            _e.Sembrar("USD", Cat.TipoTasa.Venta, Jueves, 27.0132m);

            var soloEuro = Pagina(UsuarioSoloLectura(A));
            soloEuro.Moneda = "eur";
            soloEuro.Estado = "vigente";
            await soloEuro.OnGetAsync();
            Assert.Equal("EUR", soloEuro.Moneda);
            Assert.Equal(2, soloEuro.Historico.Total);
            Assert.All(soloEuro.Historico.Filas, f => Assert.Equal("EUR", f.MonedaOrigen));

            var desconocidos = Pagina(UsuarioSoloLectura(A));
            desconocidos.Moneda = "XYZ";
            desconocidos.Tipo = "'; DROP TABLE x; --";
            desconocidos.Estado = "OTRO";
            desconocidos.Pagina = -3;
            await desconocidos.OnGetAsync();
            Assert.Null(desconocidos.Moneda);
            Assert.Null(desconocidos.Tipo);
            Assert.Null(desconocidos.Estado);
            Assert.Equal(1, desconocidos.Pagina);
            Assert.Equal(7, desconocidos.Historico.Total);

            var rango = Pagina(UsuarioSoloLectura(A));
            rango.Desde = Viernes;   // al revés: se intercambian
            rango.Hasta = Jueves;
            rango.Tipo = "venta";
            rango.Moneda = "USD";
            await rango.OnGetAsync();
            Assert.Equal(Jueves, rango.Desde);
            Assert.Equal(Viernes, rango.Hasta);
            Assert.Equal(3, rango.Historico.Total);   // USD venta: la oficial del jueves, la oficial y la propia de A del viernes
        }

        [Fact]
        public void Horas_en_hora_de_Honduras_y_tasas_con_4_decimales()
        {
            var pagina = Pagina(UsuarioSoloLectura(A));

            Assert.Equal("02/10/2026 17:00", pagina.HoraHonduras(new DateTime(2026, 10, 2, 23, 0, 0, DateTimeKind.Utc)));
            Assert.Equal("01/10/2026 18:30", pagina.HoraHonduras(new DateTime(2026, 10, 2, 0, 30, 0, DateTimeKind.Unspecified)));
            Assert.Equal("—", pagina.HoraHonduras(null));
            Assert.Equal("17:30", pagina.FinHonduras(new DateTime(2026, 10, 2, 23, 0, 0), new DateTime(2026, 10, 2, 23, 30, 0)));
            Assert.Equal("03/10/2026 00:10", pagina.FinHonduras(new DateTime(2026, 10, 3, 5, 50, 0), new DateTime(2026, 10, 3, 6, 10, 0)));
            Assert.Equal("—", pagina.FinHonduras(new DateTime(2026, 10, 2, 23, 0, 0), null));

            Assert.Equal("L 26.8925", FormatoTasas.Tasa(26.8925m));
            Assert.Equal("L 27.0000", FormatoTasas.Tasa(27m));
            Assert.Equal("L 1,234.5000", FormatoTasas.Tasa(1234.5m));
            Assert.Equal("02/10/2026", FormatoTasas.Fecha(Viernes));
            Assert.Equal("+9.70 %", FormatoTasas.Variacion(29.5m, 26.8925m));
            Assert.Equal("-1.00 %", FormatoTasas.Variacion(99m, 100m));
            Assert.Equal(string.Empty, FormatoTasas.Variacion(27m, null));
        }

        // ══════════════════════════════════════════════════════════════════
        //  BASE DE DATOS SIN EL SCRIPT 020
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Una base vacía, sin las tablas de tasas (como eBD_SPD antes de aplicar el script 020).</summary>
        private ITasaCambioConsultaService ConsultasSinTablas()
        {
            var conexion = new SqliteConnection("DataSource=:memory:");
            conexion.Open();
            _desechables.Add(conexion);
            var db = new ContextoDePrueba(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexion).Options);
            _desechables.Add(db);
            return new TasaCambioConsultaService(db, Options.Create(_e.Opciones), _e.Reloj);
        }

        [Fact]
        public async Task Sin_las_tablas_del_script_020_el_GET_muestra_el_aviso_en_lugar_de_fallar()
        {
            var pagina = Pagina(AdminSistema(), ConsultasSinTablas());

            var resultado = await pagina.OnGetAsync();

            Assert.IsType<PageResult>(resultado);
            Assert.True(pagina.ModuloNoInstalado);
            Assert.Equal(Pantalla.AvisoNoInstalado, pagina.AvisoBaseDatos);
            Assert.Contains("falta aplicar el script 020", pagina.AvisoBaseDatos);
            Assert.Empty(pagina.Vigentes);
            Assert.Empty(pagina.Pendientes);
            Assert.Empty(pagina.Ejecuciones);
            Assert.Equal(0, pagina.Historico.Total);
            Assert.Contains(_log.Entradas, l => l.Nivel == LogLevel.Error && l.Error != null && l.Mensaje.Contains("script 020"));
        }

        [Fact]
        public async Task Sin_las_tablas_del_script_020_aprobar_avisa_en_lugar_de_fallar()
        {
            var pagina = Pagina(AdminSistema(), ConsultasSinTablas());

            Redirige(await pagina.OnPostAprobarAsync(1), "Index");

            Assert.Equal(Pantalla.AvisoNoInstalado, Error(pagina));
            Assert.Contains(_log.Entradas, l => l.Nivel == LogLevel.Error);
        }

        [Fact]
        public void Distingue_tablas_faltantes_de_otros_errores_de_base()
        {
            using var conexion = new SqliteConnection("DataSource=:memory:");
            conexion.Open();
            using var comando = conexion.CreateCommand();

            comando.CommandText = "SELECT * FROM tasas_cambio";
            var faltaTabla = Assert.Throws<SqliteException>(() => comando.ExecuteReader());
            Assert.True(Pantalla.EsErrorDeBaseDeDatos(faltaTabla));
            Assert.True(Pantalla.FaltanTablas(faltaTabla));
            Assert.True(Pantalla.FaltanTablas(new DbUpdateException("envuelta", faltaTabla)));

            comando.CommandText = "SELEC mal escrito";
            var sintaxis = Assert.Throws<SqliteException>(() => comando.ExecuteReader());
            Assert.True(Pantalla.EsErrorDeBaseDeDatos(sintaxis));
            Assert.False(Pantalla.FaltanTablas(sintaxis));

            Assert.False(Pantalla.EsErrorDeBaseDeDatos(new InvalidOperationException("otra cosa")));
        }

        // ── Dobles ────────────────────────────────────────────────────────────

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class LogQueGuarda<T> : ILogger<T>
        {
            public List<(LogLevel Nivel, string Mensaje, Exception? Error)> Entradas { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                Entradas.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
