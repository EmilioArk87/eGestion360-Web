using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using DatosDeIdentidad = eGestion360Web.Services.Identidad.DatosDeIdentidad;

namespace eGestion360Web.Tests.Services.Permisos
{
    /// <summary>
    /// El filtro de permisos de las pantallas (paso F1.6b, ADR-029): en modo Registrar anota lo que negaría y deja pasar a todos; en modo
    /// Aplicar responde 403. Cubre qué se pide (clase, handler, sin declarar), quién pregunta (la identidad de la petición) y una matriz de
    /// perfiles por las 110 páginas reales con el evaluador de verdad.
    /// </summary>
    public class PermisosPageFilterTests : IDisposable
    {
        // ── Páginas de mentira para probar el filtro ──────────────────────────────────────────────────────────────────────────

        [RequierePermiso("flota.vehiculos.ver")]
        private sealed class PaginaDeVehiculos : PageModel
        {
            public void OnGet() { }

            [RequierePermiso("flota.vehiculos.crear")]
            public void OnPostCrear() { }

            public void OnPostSinPermisoPropio() { }
        }

        [PaginaSinPermiso("Prueba: pantalla pública")]
        private sealed class PaginaLibre : PageModel
        {
            [RequierePermiso("flota.vehiculos.eliminar")]
            public void OnPostBorrar() { }

            public void OnGet() { }
        }

        private sealed class PaginaSinDeclarar : PageModel
        {
            public void OnGet() { }
        }

        [RequierePermiso("no.existe.nada")]
        private sealed class PaginaConCodigoInventado : PageModel
        {
            public void OnGet() { }
        }

        // ── Piezas falsas ─────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class EvaluadorFalso : IEvaluadorDePermisos
        {
            private readonly IReadOnlySet<string> _codigos;
            public EvaluadorFalso(params string[] codigos) => _codigos = new HashSet<string>(codigos, StringComparer.Ordinal);
            public bool Falla { get; init; }
            public int Llamadas { get; private set; }
            public ContextoDePermisos? UltimoContexto { get; private set; }

            public Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default)
            {
                Llamadas++;
                UltimoContexto = contexto;
                if (Falla) throw new InvalidOperationException("la base no responde");
                return Task.FromResult(new PermisosEfectivos(_codigos));
            }

            public void Invalidar(int idTenant) { }
            public void InvalidarCatalogo() { }
        }

        private sealed class OpcionesFalsas : IOptionsMonitor<OpcionesDePermisos>
        {
            public OpcionesFalsas(string? modo) => CurrentValue = new OpcionesDePermisos { Modo = modo };
            public OpcionesFalsas(OpcionesDePermisos opciones) => CurrentValue = opciones;
            public OpcionesDePermisos CurrentValue { get; }
            public OpcionesDePermisos Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<OpcionesDePermisos, string?> listener) => null;
        }

        private readonly BaseDeDatosDePrueba _base = new();
        private readonly MemoryCache _memoria = new(new MemoryCacheOptions());

        public void Dispose()
        {
            _base.Dispose();
            _memoria.Dispose();
        }

        private static (PageContext Pagina, PageHandlerExecutingContext Contexto) Armar(Type pagina, string? handler, HttpContext http, string metodo = "GET", string ruta = "/Flota/Catalogos/Vehiculos/Index")
        {
            http.Request.Method = metodo;
            var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = ruta, RelativePath = ruta + ".cshtml" };
            var pageContext = new PageContext(new ActionContext(http, new RouteData(), descriptor));
            var metodoInfo = handler == null ? null : pagina.GetMethod(handler, BindingFlags.Public | BindingFlags.Instance);
            var descriptorDelHandler = new HandlerMethodDescriptor { MethodInfo = metodoInfo! };
            var instancia = RuntimeHelpers.GetUninitializedObject(pagina);
            var contexto = new PageHandlerExecutingContext(pageContext, Array.Empty<IFilterMetadata>(), metodoInfo == null ? new HandlerMethodDescriptor() : descriptorDelHandler,
                new Dictionary<string, object?>(), instancia);
            return (pageContext, contexto);
        }

        private sealed record Resultado(bool Siguio, IActionResult? Respuesta, RegistroEnMemoria<PermisosPageFilter> Log, EvaluadorFalso Evaluador);

        private static async Task<Resultado> Correr(Type pagina, string? handler, HttpContext http, EvaluadorFalso evaluador, string? modo = "Registrar",
            string metodo = "GET", string ruta = "/Flota/Catalogos/Vehiculos/Index")
        {
            var log = new RegistroEnMemoria<PermisosPageFilter>();
            var filtro = new PermisosPageFilter(evaluador, new OpcionesFalsas(modo), log);
            var (pageContext, contexto) = Armar(pagina, handler, http, metodo, ruta);

            var siguio = false;
            await filtro.OnPageHandlerExecutionAsync(contexto, () =>
            {
                siguio = true;
                return Task.FromResult(new PageHandlerExecutedContext(pageContext, Array.Empty<IFilterMetadata>(), contexto.HandlerMethod!, contexto.HandlerInstance));
            });
            return new Resultado(siguio, contexto.Result, log, evaluador);
        }

        private static HttpContext Usuario(string rol = "empresa_user", int? idRol = null, int idTenant = 2, string[]? modulos = null, bool operadorEnEmpresa = false)
        {
            var http = SesionFalsa.ConIdentidad(new DatosDeIdentidad(
                IdCuenta: 7, IdUserLegado: 7, NombreUsuario: "ana.prueba", Correo: "ana@example.test", NombrePersona: "Ana", Rol: rol,
                EsOperador: rol == "admin" || operadorEnEmpresa, DebeCambiarClave: false, IdMembresia: 3, IdTenant: idTenant, CodigoTenant: "T", IdEmpresa: 1,
                IdRolEmpresa: idRol, MfaVerificada: false, Sello: "s"));
            http.Items[TenantMiddleware.ClaveIdTenant] = idTenant;
            http.Items[TenantMiddleware.ClaveIdEmpresa] = 1;
            var habilitados = modulos ?? new[] { "flota" };
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = new DerechosDeLaPeticion(
                new DerechosDelTenant(idTenant, "T", TipoTenant.Cliente, EstadoTenant.Activo, ModoDeSuspension.Lectura, null, "plan",
                    new HashSet<string>(habilitados.Select(CodigosDeDerecho.DeModulo)), new Dictionary<string, int?>()),
                new HashSet<string>(habilitados));
            return http;
        }

        private static HttpContext SinSesion() => SesionFalsa.Contexto(rol: null, autenticado: false);

        // ── Qué se pide ───────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_handler_con_atributo_pide_el_suyo_y_los_demas_el_de_la_clase()
        {
            var tipo = typeof(PaginaDeVehiculos);

            var delHandler = RequisitoDePagina.De(tipo, tipo.GetMethod(nameof(PaginaDeVehiculos.OnPostCrear)));
            Assert.Equal("flota.vehiculos.crear", delHandler.Codigo);
            Assert.True(delHandler.DelHandler);

            var delaClase = RequisitoDePagina.De(tipo, tipo.GetMethod(nameof(PaginaDeVehiculos.OnPostSinPermisoPropio)));
            Assert.Equal("flota.vehiculos.ver", delaClase.Codigo);
            Assert.False(delaClase.DelHandler);

            Assert.Equal("flota.vehiculos.ver", RequisitoDePagina.De(tipo, null).Codigo);
        }

        [Fact]
        public void Una_clase_sin_atributos_no_declaro_nada_y_una_libre_lo_dice_con_su_motivo()
        {
            Assert.Equal(TipoDeRequisito.SinDeclarar, RequisitoDePagina.De(typeof(PaginaSinDeclarar), null).Tipo);

            var libre = RequisitoDePagina.De(typeof(PaginaLibre), typeof(PaginaLibre).GetMethod(nameof(PaginaLibre.OnGet)));
            Assert.Equal(TipoDeRequisito.SinPermiso, libre.Tipo);
            Assert.Equal("Prueba: pantalla pública", libre.Motivo);
        }

        [Fact]
        public void Un_handler_con_permiso_propio_lo_pide_aunque_la_clase_diga_que_no_pide_ninguno()
        {
            var requisito = RequisitoDePagina.De(typeof(PaginaLibre), typeof(PaginaLibre).GetMethod(nameof(PaginaLibre.OnPostBorrar)));

            Assert.Equal(TipoDeRequisito.Permiso, requisito.Tipo);
            Assert.Equal("flota.vehiculos.eliminar", requisito.Codigo);
        }

        [Theory]
        [InlineData("Aplicar", ModoDePermisos.Aplicar)]
        [InlineData("aplicar", ModoDePermisos.Aplicar)]
        [InlineData("  APLICAR ", ModoDePermisos.Aplicar)]
        [InlineData("Registrar", ModoDePermisos.Registrar)]
        [InlineData("registrar", ModoDePermisos.Registrar)]
        [InlineData("Apagado", ModoDePermisos.Registrar)]
        [InlineData("", ModoDePermisos.Registrar)]
        [InlineData(null, ModoDePermisos.Registrar)]
        public void Solo_la_palabra_Aplicar_activa_el_bloqueo_todo_lo_demas_registra(string? texto, ModoDePermisos esperado)
            => Assert.Equal(esperado, new OpcionesDePermisos { Modo = texto }.ModoActual);

        // ── Modo Registrar: anota y deja pasar ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task En_registrar_quien_tiene_el_permiso_pasa_sin_que_se_anote_nada()
        {
            var r = await Correr(typeof(PaginaDeVehiculos), "OnGet", Usuario(idRol: 5), new EvaluadorFalso("flota.vehiculos.ver"));

            Assert.True(r.Siguio);
            Assert.Null(r.Respuesta);
            Assert.Empty(r.Log.Mensajes);
        }

        [Fact]
        public async Task En_registrar_quien_no_tiene_el_permiso_pasa_igual_pero_queda_anotado_con_todos_los_datos()
        {
            var r = await Correr(typeof(PaginaDeVehiculos), "OnPostCrear", Usuario(idRol: 5), new EvaluadorFalso("flota.vehiculos.ver"),
                metodo: "POST", ruta: "/Flota/Catalogos/Vehiculos/Create");

            Assert.True(r.Siguio);
            Assert.Null(r.Respuesta);
            var anotado = Assert.Single(r.Log.Mensajes);
            Assert.Equal(LogLevel.Warning, anotado.Nivel);
            Assert.Contains("[registro]", anotado.Mensaje);
            Assert.Contains("POST /Flota/Catalogos/Vehiculos/Create", anotado.Mensaje);
            Assert.Contains("flota.vehiculos.crear", anotado.Mensaje);
            Assert.Contains("cuenta 7", anotado.Mensaje);
            Assert.Contains("empresa_user/5", anotado.Mensaje);
            Assert.Contains("tenant 2", anotado.Mensaje);
        }

        [Fact]
        public async Task El_modo_por_omision_o_desconocido_registra_y_no_bloquea()
        {
            foreach (var modo in new string?[] { null, "", "Cualquiera" })
            {
                var r = await Correr(typeof(PaginaDeVehiculos), "OnPostCrear", Usuario(idRol: 5), new EvaluadorFalso(), modo, "POST");

                Assert.True(r.Siguio);
                Assert.Null(r.Respuesta);
                Assert.Single(r.Log.Mensajes);
            }
        }

        // ── Modo Aplicar: niega ───────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task En_aplicar_quien_tiene_el_permiso_pasa()
        {
            var r = await Correr(typeof(PaginaDeVehiculos), "OnGet", Usuario(idRol: 5), new EvaluadorFalso("flota.vehiculos.ver"), "Aplicar");

            Assert.True(r.Siguio);
            Assert.Null(r.Respuesta);
            Assert.Empty(r.Log.Mensajes);
        }

        [Fact]
        public async Task En_aplicar_quien_no_lo_tiene_recibe_403_con_lo_que_le_falta_y_el_handler_no_corre()
        {
            var r = await Correr(typeof(PaginaDeVehiculos), "OnPostCrear", Usuario(idRol: 5), new EvaluadorFalso("flota.vehiculos.ver"), "Aplicar", "POST");

            Assert.False(r.Siguio);
            var respuesta = Assert.IsType<ContentResult>(r.Respuesta);
            Assert.Equal(StatusCodes.Status403Forbidden, respuesta.StatusCode);
            Assert.Contains("Crear vehículos", respuesta.Content);
            Assert.Contains("/MainMenu", respuesta.Content);
            var anotado = Assert.Single(r.Log.Mensajes);
            Assert.DoesNotContain("[registro]", anotado.Mensaje);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("text/html, application/json;q=0.9")]
        public async Task En_aplicar_una_llamada_de_datos_sin_permiso_recibe_403_directo_sin_pagina(string aceptar)
        {
            var http = Usuario(idRol: 5);
            http.Request.Headers.Accept = aceptar;

            var r = await Correr(typeof(PaginaDeVehiculos), "OnPostCrear", http, new EvaluadorFalso(), "Aplicar", "POST");

            Assert.False(r.Siguio);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(r.Respuesta).StatusCode);
        }

        // ── Lo que el filtro no toca ──────────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Registrar")]
        [InlineData("Aplicar")]
        public async Task Una_pantalla_sin_permiso_pasa_sin_evaluar_nada(string modo)
        {
            var evaluador = new EvaluadorFalso();

            var r = await Correr(typeof(PaginaLibre), "OnGet", Usuario(idRol: 5), evaluador, modo);

            Assert.True(r.Siguio);
            Assert.Equal(0, evaluador.Llamadas);
            Assert.Empty(r.Log.Mensajes);
        }

        [Theory]
        [InlineData("Registrar")]
        [InlineData("Aplicar")]
        public async Task Sin_sesion_el_filtro_no_actua_y_cada_pantalla_manda_sola_al_inicio_de_sesion(string modo)
        {
            var evaluador = new EvaluadorFalso();

            var r = await Correr(typeof(PaginaDeVehiculos), "OnGet", SinSesion(), evaluador, modo);

            Assert.True(r.Siguio);
            Assert.Null(r.Respuesta);
            Assert.Equal(0, evaluador.Llamadas);
        }

        [Fact]
        public async Task Con_cambio_de_clave_pendiente_tampoco_actua()
        {
            var http = SesionFalsa.Contexto("empresa_user", "1", debeCambiarClave: true);
            var evaluador = new EvaluadorFalso();

            var r = await Correr(typeof(PaginaDeVehiculos), "OnGet", http, evaluador, "Aplicar");

            Assert.True(r.Siguio);
            Assert.Equal(0, evaluador.Llamadas);
        }

        [Fact]
        public async Task Los_permisos_se_evaluan_una_sola_vez_por_peticion()
        {
            var evaluador = new EvaluadorFalso("flota.vehiculos.ver");
            var http = Usuario(idRol: 5);

            await Correr(typeof(PaginaDeVehiculos), "OnGet", http, evaluador);
            await Correr(typeof(PaginaDeVehiculos), "OnGet", http, evaluador);

            Assert.Equal(1, evaluador.Llamadas);
            Assert.IsType<PermisosEfectivos>(http.Items[PermisosPageFilter.ClavePermisosEfectivos]);
        }

        // ── Descuidos de programación ─────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Una_pantalla_sin_declarar_pasa_en_registrar_y_se_avisa_una_sola_vez_pero_se_niega_en_aplicar()
        {
            var primera = await Correr(typeof(PaginaSinDeclarar), "OnGet", Usuario(idRol: 5), new EvaluadorFalso(), "Registrar", ruta: "/Prueba/SinDeclararA");
            var segunda = await Correr(typeof(PaginaSinDeclarar), "OnGet", Usuario(idRol: 5), new EvaluadorFalso(), "Registrar", ruta: "/Prueba/SinDeclararA");
            var aplicar = await Correr(typeof(PaginaSinDeclarar), "OnGet", Usuario(idRol: 5), new EvaluadorFalso(), "Aplicar", ruta: "/Prueba/SinDeclararB");

            Assert.True(primera.Siguio);
            Assert.Equal(LogLevel.Error, Assert.Single(primera.Log.Mensajes).Nivel);
            Assert.True(segunda.Siguio);
            Assert.Empty(segunda.Log.Mensajes);
            Assert.False(aplicar.Siguio);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ContentResult>(aplicar.Respuesta).StatusCode);
        }

        [Fact]
        public async Task Un_codigo_que_no_esta_en_el_catalogo_se_trata_como_pantalla_sin_declarar()
        {
            var registrar = await Correr(typeof(PaginaConCodigoInventado), "OnGet", Usuario(idRol: 5), new EvaluadorFalso("no.existe.nada"), "Registrar", ruta: "/Prueba/Inventado1");
            var aplicar = await Correr(typeof(PaginaConCodigoInventado), "OnGet", Usuario(idRol: 5), new EvaluadorFalso("no.existe.nada"), "Aplicar", ruta: "/Prueba/Inventado2");

            Assert.True(registrar.Siguio);
            Assert.False(aplicar.Siguio);
        }

        [Fact]
        public async Task Si_el_evaluador_falla_registrar_no_tumba_la_peticion_y_aplicar_cierra_por_omision()
        {
            var registrar = await Correr(typeof(PaginaDeVehiculos), "OnGet", Usuario(idRol: 5), new EvaluadorFalso { Falla = true }, "Registrar");
            var aplicar = await Correr(typeof(PaginaDeVehiculos), "OnGet", Usuario(idRol: 5), new EvaluadorFalso { Falla = true }, "Aplicar");

            Assert.True(registrar.Siguio);
            Assert.Equal(LogLevel.Error, Assert.Single(registrar.Log.Mensajes).Nivel);
            Assert.False(aplicar.Siguio);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ContentResult>(aplicar.Respuesta).StatusCode);
        }

        // ── Quién pregunta: el contexto sale de la petición ───────────────────────────────────────────────────────────────────

        [Fact]
        public void El_operador_dentro_de_una_empresa_se_arma_como_operador_aunque_entre_con_el_rol_de_ese_acceso()
        {
            // D66: dentro de SIP el operador entra como empresa_user con el rol «Flota», que no tiene permisos; sigue siendo personal de la plataforma.
            var http = Usuario("empresa_user", idRol: 9, idTenant: 2, operadorEnEmpresa: true);

            var contexto = ContextoDePermisosHttp.Crear(http);

            Assert.False(AuthHelper.IsAdmin(http));
            Assert.True(contexto.EsOperador);
            Assert.Equal(AuthHelper.EmpresaUserRole, contexto.RolLegado);
            Assert.Equal(9, contexto.IdRol);
        }

        [Fact]
        public void Un_usuario_de_empresa_que_no_es_operador_no_se_arma_como_operador()
        {
            var contexto = ContextoDePermisosHttp.Crear(Usuario("empresa_user", idRol: 9, idTenant: 2));

            Assert.False(contexto.EsOperador);
        }

        [Fact]
        public void El_operador_dentro_de_una_empresa_con_un_rol_sin_permisos_pasa_por_todas_las_pantallas_en_aplicar()
        {
            var (_, vacio) = SembrarRoles();

            var diferencias = Diferencias(
                () => Usuario("empresa_user", idRol: vacio, idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos", "facturacion" }, operadorEnEmpresa: true),
                (_, _) => true).GetAwaiter().GetResult();

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Con_la_configuracion_real_el_operador_dentro_de_una_empresa_pasa_por_la_administracion_y_la_plataforma()
        {
            var (_, vacio) = SembrarRoles();
            HttpContext Operador() => Usuario("empresa_user", idRol: vacio, idTenant: IdTenantDelPerfil, operadorEnEmpresa: true);

            Pasa(await CorrerConConfiguracionReal("/Empresas/Create", "OnPostAsync", Operador(), "POST"), "Empresas/Create");
            Pasa(await CorrerConConfiguracionReal("/Admin/Soporte/Index", "OnPostEntrarAsync", Operador(), "POST"), "Soporte entrar");
            Pasa(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnGetAsync", Operador()), "Usuarios/Create");
        }

        [Fact]
        public void El_operador_se_arma_como_operador_con_o_sin_tenant()
        {
            var http = SesionFalsa.Contexto("admin", "1");

            var contexto = ContextoDePermisosHttp.Crear(http);

            Assert.True(contexto.EsOperador);
            Assert.Null(contexto.IdTenant);
        }

        [Fact]
        public void El_administrador_de_empresa_lleva_su_rol_de_texto_su_tenant_y_los_modulos_que_puede_usar()
        {
            var http = Usuario("empresa_admin", idRol: null, idTenant: 2, modulos: new[] { "flota", "catalogos" });

            var contexto = ContextoDePermisosHttp.Crear(http);

            Assert.False(contexto.EsOperador);
            Assert.Equal(AuthHelper.EmpresaAdminRole, contexto.RolLegado);
            Assert.Equal(2, contexto.IdTenant);
            Assert.Null(contexto.IdRol);
            Assert.True(contexto.ModulosHabilitados.SetEquals(new[] { "flota", "catalogos" }));
        }

        [Fact]
        public void Un_usuario_con_rol_lleva_el_id_del_rol_y_solo_los_modulos_que_el_plan_concede_y_la_empresa_tiene_activos()
        {
            var http = Usuario("empresa_user", idRol: 9, idTenant: 2, modulos: new[] { "flota" });

            var contexto = ContextoDePermisosHttp.Crear(http);

            Assert.Equal(AuthHelper.EmpresaUserRole, contexto.RolLegado);
            Assert.Equal(9, contexto.IdRol);
            Assert.True(contexto.ModulosHabilitados.SetEquals(new[] { "flota" }));
        }

        [Fact]
        public async Task El_filtro_le_pasa_al_evaluador_el_contexto_de_la_peticion()
        {
            var evaluador = new EvaluadorFalso("flota.vehiculos.ver");

            await Correr(typeof(PaginaDeVehiculos), "OnGet", Usuario("empresa_user", idRol: 9, idTenant: 2), evaluador);

            Assert.NotNull(evaluador.UltimoContexto);
            Assert.Equal(9, evaluador.UltimoContexto!.IdRol);
            Assert.Equal(2, evaluador.UltimoContexto.IdTenant);
        }

        // ── Con el evaluador de verdad: la matriz de perfiles por las 110 páginas ─────────────────────────────────────────────

        private const int IdTenantDelPerfil = DatosBase.TenantA;

        /// <summary>El catálogo completo y dos roles de la empresa A: «Consulta» (solo las casillas ver) y «Vacío» (sin permisos).</summary>
        private (int Consulta, int Vacio) SembrarRoles()
        {
            using var db = _base.Crear();
            var ahora = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
            var orden = 0;
            foreach (var d in CatalogoDePermisos.Todos)
                db.Permisos.Add(new Permiso
                {
                    Codigo = d.Codigo, Modulo = d.Modulo, Recurso = d.Recurso, Accion = d.Accion, Ambito = d.Ambito, AccionLegada = d.CasillaLegada,
                    Descripcion = d.Descripcion, Orden = ++orden, CreadoPor = "pruebas", FechaCreacion = ahora
                });
            var consulta = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, IdTenant = IdTenantDelPerfil, Nombre = "Consulta", Activo = true };
            var vacio = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, IdTenant = IdTenantDelPerfil, Nombre = "Vacío", Activo = true };
            db.EmpresaRoles.AddRange(consulta, vacio);
            db.SaveChanges();

            foreach (var d in CatalogoDePermisos.Todos.Where(p => p.Ambito == AmbitoDePermiso.Modulo && p.CasillaLegada == CasillaLegada.Ver))
            {
                var permiso = db.Permisos.Single(p => p.Codigo == d.Codigo);
                db.RolPermisos.Add(new RolPermiso
                {
                    IdTenant = IdTenantDelPerfil, IdRol = consulta.IdRol, IdPermiso = permiso.IdPermiso, AmbitoPermiso = permiso.Ambito,
                    CreadoPor = "pruebas", FechaCreacion = ahora
                });
            }
            db.SaveChanges();
            return (consulta.IdRol, vacio.IdRol);
        }

        private static IReadOnlyList<(string Ruta, Type Tipo)> PaginasReales()
            => typeof(PermisosPageFilter).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(PageModel).IsAssignableFrom(t) && t.Namespace != null && t.Namespace.StartsWith("eGestion360Web.Pages", StringComparison.Ordinal))
                .Select(t => (Ruta: (t.Namespace!.Length > "eGestion360Web.Pages".Length ? t.Namespace["eGestion360Web.Pages".Length..].Replace('.', '/') : "") + "/" + t.Name[..^"Model".Length], Tipo: t))
                .ToList();

        private async Task<IReadOnlyList<string>> Diferencias(Func<HttpContext> perfil, Func<string, DefinicionDePermiso?, bool> deberiaPasar)
        {
            var diferencias = new List<string>();
            foreach (var (ruta, tipo) in PaginasReales())
            {
                var requisito = RequisitoDePagina.De(tipo, null);
                var definicion = requisito.Codigo == null ? null : CatalogoDePermisos.Buscar(requisito.Codigo);

                await using var db = _base.CrearComo(IdTenantDelPerfil);
                var filtro = new PermisosPageFilter(new EvaluadorDePermisos(db, new MemoryCache(new MemoryCacheOptions())), new OpcionesFalsas("Aplicar"), new RegistroEnMemoria<PermisosPageFilter>());
                var http = perfil();
                var (pageContext, contexto) = Armar(tipo, null, http, ruta: ruta);

                var siguio = false;
                await filtro.OnPageHandlerExecutionAsync(contexto, () =>
                {
                    siguio = true;
                    return Task.FromResult(new PageHandlerExecutedContext(pageContext, Array.Empty<IFilterMetadata>(), contexto.HandlerMethod!, contexto.HandlerInstance));
                });

                var esperado = requisito.Tipo == TipoDeRequisito.SinPermiso || deberiaPasar(ruta, definicion);
                if (siguio != esperado) diferencias.Add($"{ruta} ({requisito.Codigo ?? "sin permiso"}): pasó={siguio}, se esperaba {esperado}");
            }
            return diferencias;
        }

        [Fact]
        public async Task Matriz_el_operador_pasa_por_todas_las_paginas()
        {
            SembrarRoles();
            var diferencias = await Diferencias(() => Usuario("admin", idTenant: IdTenantDelPerfil, modulos: new[] { "flota" }), (_, _) => true);

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Matriz_el_administrador_de_empresa_pasa_por_la_administracion_y_los_modulos_que_puede_usar_pero_no_por_la_plataforma()
        {
            SembrarRoles();
            var diferencias = await Diferencias(
                () => Usuario("empresa_admin", idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos" }),
                (_, d) => d != null && (d.Ambito == AmbitoDePermiso.Empresa || (d.Ambito == AmbitoDePermiso.Modulo && d.Modulo is "flota" or "catalogos")));

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Matriz_el_rol_de_consulta_solo_pasa_por_las_pantallas_de_ver_de_los_modulos_habilitados()
        {
            var (consulta, _) = SembrarRoles();
            var diferencias = await Diferencias(
                () => Usuario("empresa_user", idRol: consulta, idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos", "facturacion" }),
                (_, d) => d is { Ambito: AmbitoDePermiso.Modulo, CasillaLegada: CasillaLegada.Ver });

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Matriz_un_rol_sin_permisos_no_pasa_por_ninguna_pantalla_que_pida_uno()
        {
            var (_, vacio) = SembrarRoles();
            var diferencias = await Diferencias(
                () => Usuario("empresa_user", idRol: vacio, idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos", "facturacion" }),
                (_, _) => false);

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Matriz_el_rol_de_consulta_pierde_las_pantallas_de_un_modulo_que_el_plan_no_concede()
        {
            var (consulta, _) = SembrarRoles();
            var diferencias = await Diferencias(
                () => Usuario("empresa_user", idRol: consulta, idTenant: IdTenantDelPerfil, modulos: new[] { "catalogos" }),
                (_, d) => d is { Ambito: AmbitoDePermiso.Modulo, CasillaLegada: CasillaLegada.Ver, Modulo: "catalogos" });

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        // ── Con la configuración real (F1.6c): administración y plataforma ya aplican ──────────────────────────────────────────

        private static OpcionesDePermisos OpcionesReales()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj"))) carpeta = carpeta.Parent;
            Assert.NotNull(carpeta);

            var configuracion = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddJsonFile(Path.Combine(carpeta!.FullName, "appsettings.json")).Build();
            return configuracion.GetSection(OpcionesDePermisos.Seccion).Get<OpcionesDePermisos>() ?? new OpcionesDePermisos();
        }

        private static readonly string[] CarpetasQueAplican = { "/Admin", "/Empresas", "/UserManagement", "/DebugUsers", "/ResetCodesHistory", "/ValidarEmails" };

        private static Type PaginaDe(string ruta) => PaginasReales().Single(p => p.Ruta == ruta).Tipo;

        private async Task<(bool Siguio, IActionResult? Respuesta)> CorrerConConfiguracionReal(string ruta, string? handler, HttpContext http, string metodo = "GET")
        {
            await using var db = _base.CrearComo(IdTenantDelPerfil);
            var filtro = new PermisosPageFilter(new EvaluadorDePermisos(db, new MemoryCache(new MemoryCacheOptions())), new OpcionesFalsas(OpcionesReales()),
                new RegistroEnMemoria<PermisosPageFilter>());
            var (pageContext, contexto) = Armar(PaginaDe(ruta), handler, http, metodo, ruta);

            var siguio = false;
            await filtro.OnPageHandlerExecutionAsync(contexto, () =>
            {
                siguio = true;
                return Task.FromResult(new PageHandlerExecutedContext(pageContext, Array.Empty<IFilterMetadata>(), contexto.HandlerMethod!, contexto.HandlerInstance));
            });
            return (siguio, contexto.Result);
        }

        private static void Pasa((bool Siguio, IActionResult? Respuesta) r, string que)
        {
            Assert.True(r.Siguio, $"{que}: debía pasar y se negó");
            Assert.Null(r.Respuesta);
        }

        private static void SeNiega((bool Siguio, IActionResult? Respuesta) r, string que)
        {
            Assert.False(r.Siguio, $"{que}: debía negarse y pasó");
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ContentResult>(r.Respuesta).StatusCode);
        }

        [Fact]
        public void La_configuracion_real_aplica_en_administracion_y_plataforma_y_registra_en_el_resto()
        {
            var opciones = OpcionesReales();

            Assert.Equal(ModoDePermisos.Registrar, opciones.ModoActual);
            Assert.Equal(CarpetasQueAplican, opciones.AplicarEn);
            Assert.Equal(CarpetasQueAplican, OpcionesDePermisos.CarpetasDeAdministracion);
            foreach (var (ruta, _) in PaginasReales())
            {
                var esperado = CarpetasQueAplican.Any(p => ruta == p || ruta.StartsWith(p + "/", StringComparison.Ordinal))
                    ? ModoDePermisos.Aplicar
                    : ModoDePermisos.Registrar;
                Assert.True(opciones.ModoPara(ruta) == esperado, $"{ruta}: se esperaba {esperado}");
            }
        }

        [Fact]
        public void Toda_pantalla_de_plataforma_o_de_administracion_aplica_en_la_configuracion_real()
        {
            var opciones = OpcionesReales();
            var registran = new List<string>();
            foreach (var (ruta, tipo) in PaginasReales())
            {
                var permisos = new List<string>();
                if (tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false) is { } deLaClase) permisos.Add(deLaClase.Codigo);
                permisos.AddRange(tipo.GetMethods().Select(m => m.GetCustomAttribute<RequierePermisoAttribute>(inherit: false)?.Codigo).OfType<string>());

                var esDeAdministracion = permisos.Any(c => c.StartsWith("plataforma.", StringComparison.Ordinal) || c.StartsWith("administracion.", StringComparison.Ordinal));
                if (esDeAdministracion && opciones.ModoPara(ruta) != ModoDePermisos.Aplicar) registran.Add(ruta);
            }

            Assert.True(registran.Count == 0, "Pantallas de plataforma o administración que todavía solo registran: " + string.Join(", ", registran));
        }

        [Theory]
        [InlineData("/Admin", "/Admin/Usuarios/Create", true)]
        [InlineData("/Admin", "/Admin", true)]
        [InlineData("/Admin/", "/Admin/Usuarios/Create", true)]
        [InlineData("/admin", "/Admin/Usuarios/Create", true)]
        [InlineData("/Admin", "/AdminOtraCosa", false)]
        [InlineData("/Empresas", "/Empresas/Index", true)]
        [InlineData("/Empresas", "/EmpresasNuevas/Index", false)]
        [InlineData("/Admin", "/Flota/Index", false)]
        [InlineData("", "/Flota/Index", false)]
        [InlineData("   ", "/Flota/Index", false)]
        public void Las_carpetas_de_AplicarEn_cubren_la_ruta_exacta_y_lo_que_cuelga_de_ella(string prefijo, string ruta, bool aplica)
        {
            var opciones = new OpcionesDePermisos { Modo = "Registrar", AplicarEn = new[] { prefijo } };

            Assert.Equal(aplica ? ModoDePermisos.Aplicar : ModoDePermisos.Registrar, opciones.ModoPara(ruta));
        }

        [Theory]
        [InlineData("Aplicar", "/Flota/Index")]
        [InlineData("Aplicar", "/Cualquiera")]
        public void El_modo_global_Aplicar_vale_para_todas_las_rutas_aunque_no_haya_carpetas(string modo, string ruta)
            => Assert.Equal(ModoDePermisos.Aplicar, new OpcionesDePermisos { Modo = modo }.ModoPara(ruta));

        [Fact]
        public void Sin_configurar_aplican_las_carpetas_de_administracion_y_solo_una_lista_vacia_las_apaga()
        {
            // Quitar la clave de la configuración no deja abiertas las pantallas de administración, que ya no comprueban el rol en su código.
            Assert.Equal(ModoDePermisos.Aplicar, new OpcionesDePermisos().ModoPara("/Admin/Usuarios/Create"));
            Assert.Equal(ModoDePermisos.Aplicar, new OpcionesDePermisos().ModoPara("/Empresas/Index"));
            Assert.Equal(ModoDePermisos.Registrar, new OpcionesDePermisos().ModoPara("/Flota/Index"));
            Assert.Equal(ModoDePermisos.Registrar, new OpcionesDePermisos { AplicarEn = Array.Empty<string>() }.ModoPara("/Admin/Usuarios/Create"));
            Assert.Equal(ModoDePermisos.Registrar, new OpcionesDePermisos { AplicarEn = new[] { "/Admin" } }.ModoPara(null));
        }

        private async Task<IReadOnlyList<string>> DiferenciasReales(Func<HttpContext> perfil, Func<DefinicionDePermiso, bool> deberiaPasar)
        {
            var opciones = OpcionesReales();
            var diferencias = new List<string>();
            foreach (var (ruta, tipo) in PaginasReales())
            {
                var requisito = RequisitoDePagina.De(tipo, null);
                if (requisito.Tipo != TipoDeRequisito.Permiso) continue;
                var definicion = CatalogoDePermisos.Buscar(requisito.Codigo!)!;

                var r = await CorrerConConfiguracionReal(ruta, null, perfil());
                // Donde la configuración solo registra, pasa todo el mundo; donde aplica, pasa quien tiene el permiso.
                var esperado = opciones.ModoPara(ruta) == ModoDePermisos.Registrar || deberiaPasar(definicion);
                if (r.Siguio != esperado) diferencias.Add($"{ruta} ({requisito.Codigo}): pasó={r.Siguio}, se esperaba {esperado}");
            }
            return diferencias;
        }

        [Fact]
        public async Task Con_la_configuracion_real_el_operador_pasa_por_todo()
        {
            SembrarRoles();

            var diferencias = await DiferenciasReales(() => Usuario("admin", idTenant: IdTenantDelPerfil), _ => true);

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Con_la_configuracion_real_el_administrador_de_empresa_pasa_por_la_administracion_pero_no_por_la_plataforma()
        {
            SembrarRoles();

            var diferencias = await DiferenciasReales(
                () => Usuario("empresa_admin", idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos", "facturacion" }),
                d => d.Ambito != AmbitoDePermiso.Plataforma);

            Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
        }

        [Fact]
        public async Task Con_la_configuracion_real_un_usuario_con_rol_no_pasa_por_ninguna_pantalla_de_administracion_ni_de_plataforma()
        {
            var (consulta, vacio) = SembrarRoles();

            foreach (var rol in new[] { consulta, vacio })
            {
                var diferencias = await DiferenciasReales(
                    () => Usuario("empresa_user", idRol: rol, idTenant: IdTenantDelPerfil, modulos: new[] { "flota", "catalogos", "facturacion" }),
                    d => d.Ambito == AmbitoDePermiso.Modulo && d.CasillaLegada == CasillaLegada.Ver);

                Assert.True(diferencias.Count == 0, string.Join("; ", diferencias));
            }
        }

        [Fact]
        public async Task Un_usuario_con_rol_ya_no_llega_al_codigo_de_las_pantallas_de_administracion_y_plataforma()
        {
            var (_, vacio) = SembrarRoles();
            HttpContext Rol() => Usuario("empresa_user", idRol: vacio, idTenant: IdTenantDelPerfil);

            SeNiega(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnGetAsync", Rol()), "Admin/Usuarios/Create GET");
            SeNiega(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnPostAsync", Rol(), "POST"), "Admin/Usuarios/Create POST");
            SeNiega(await CorrerConConfiguracionReal("/Admin/RolesPermisos/Index", "OnPostEditarAsync", Rol(), "POST"), "Admin/RolesPermisos editar");
            SeNiega(await CorrerConConfiguracionReal("/Admin/Invitaciones/Create", "OnGetBuscarPersonasAsync", Rol()), "Invitaciones buscar personas");
            SeNiega(await CorrerConConfiguracionReal("/Empresas/Index", "OnGetAsync", Rol()), "Empresas/Index");
            SeNiega(await CorrerConConfiguracionReal("/UserManagement", "OnPostAsync", Rol(), "POST"), "UserManagement restablecer clave");
            SeNiega(await CorrerConConfiguracionReal("/DebugUsers", "OnGetAsync", Rol()), "DebugUsers");

            // Fuera de esas carpetas el filtro todavía solo registra: la pantalla de flota sigue abriendo para este rol sin permisos.
            Pasa(await CorrerConConfiguracionReal("/Flota/Catalogos/Vehiculos/Create", "OnGetAsync", Rol()), "Flota/Vehiculos/Create (solo se registra)");
        }

        [Fact]
        public async Task El_administrador_de_empresa_administra_su_empresa_pero_no_toca_la_plataforma()
        {
            SembrarRoles();
            HttpContext Admin() => Usuario("empresa_admin", idTenant: IdTenantDelPerfil, modulos: new[] { "flota" });

            Pasa(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnGetAsync", Admin()), "Usuarios/Create GET");
            Pasa(await CorrerConConfiguracionReal("/Admin/Usuarios/Edit", "OnPostVincularAsync", Admin(), "POST"), "Usuarios/Edit vincular");
            Pasa(await CorrerConConfiguracionReal("/Admin/RolesPermisos/Index", "OnPostEditarAsync", Admin(), "POST"), "Roles editar");
            Pasa(await CorrerConConfiguracionReal("/Admin/Invitaciones/Index", "OnPostAnularAsync", Admin(), "POST"), "Invitaciones anular");

            SeNiega(await CorrerConConfiguracionReal("/Empresas/Create", "OnPostAsync", Admin(), "POST"), "Empresas/Create");
            SeNiega(await CorrerConConfiguracionReal("/Admin/Modulos/Index", "OnPostAsync", Admin(), "POST"), "Admin/Modulos");
            SeNiega(await CorrerConConfiguracionReal("/Admin/Soporte/Index", "OnPostEntrarAsync", Admin(), "POST"), "Soporte entrar");
            SeNiega(await CorrerConConfiguracionReal("/Admin/ResetPassword", "OnPostAsync", Admin(), "POST"), "Admin/ResetPassword");
            SeNiega(await CorrerConConfiguracionReal("/ValidarEmails", "OnPostPruebaCompleteAsync", Admin(), "POST"), "ValidarEmails");
        }

        [Fact]
        public async Task El_operador_entra_a_la_plataforma_y_a_la_administracion()
        {
            SembrarRoles();
            HttpContext Operador() => Usuario("admin", idTenant: IdTenantDelPerfil);

            Pasa(await CorrerConConfiguracionReal("/Empresas/Create", "OnPostAsync", Operador(), "POST"), "Empresas/Create");
            Pasa(await CorrerConConfiguracionReal("/Admin/Soporte/Index", "OnPostEntrarAsync", Operador(), "POST"), "Soporte entrar");
            Pasa(await CorrerConConfiguracionReal("/Admin/Outbox/Index", "OnPostResetDeadAsync", Operador(), "POST"), "Outbox gestionar");
            Pasa(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnGetAsync", Operador()), "Usuarios/Create");
        }

        [Fact]
        public async Task Sin_sesion_el_filtro_sigue_sin_actuar_tambien_en_las_carpetas_que_aplican()
        {
            SembrarRoles();

            Pasa(await CorrerConConfiguracionReal("/Admin/Usuarios/Create", "OnGetAsync", SinSesion()), "Usuarios/Create sin sesión");
            Pasa(await CorrerConConfiguracionReal("/Empresas/Index", "OnGetAsync", SinSesion()), "Empresas/Index sin sesión");
        }
    }
}
