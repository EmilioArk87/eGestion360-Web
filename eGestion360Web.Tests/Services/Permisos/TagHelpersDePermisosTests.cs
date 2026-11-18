using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Tests.Services.Permisos
{
    /// <summary>
    /// Menú, tarjetas y botones preguntan lo que pregunta cada pantalla (paso F1.6e-2, ADR-029): se muestra solo lo que la persona puede abrir. Pruebas de
    /// la resolución de rutas y handlers y de los tag helpers que ocultan enlaces, formularios, botones y tarjetas.
    /// </summary>
    public class TagHelpersDePermisosTests
    {
        // ── Resolver la ruta y el handler ──────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("/Flota/Catalogos/Vehiculos/Create", "/Cualquiera/Index", "/Flota/Catalogos/Vehiculos/Create")]
        [InlineData("Edit", "/Flota/Catalogos/Vehiculos/Index", "/Flota/Catalogos/Vehiculos/Edit")]
        [InlineData("./Create", "/Flota/Catalogos/Vehiculos/Index", "/Flota/Catalogos/Vehiculos/Create")]
        [InlineData("../Index", "/Flota/Catalogos/Vehiculos/Index", "/Flota/Catalogos/Index")]
        [InlineData("../../Gastos/Index", "/Flota/Catalogos/Vehiculos/Index", "/Flota/Gastos/Index")]
        [InlineData("Index", "/Facturacion/Index", "/Facturacion/Index")]
        [InlineData("/Admin//Soporte/./Index", "/Index", "/Admin/Soporte/Index")]
        [InlineData("Details", "/Index", "/Details")]
        public void Una_ruta_se_resuelve_como_lo_hace_Razor_Pages(string destino, string actual, string esperada)
        {
            Assert.Equal(esperada, DestinoDePagina.Resolver(destino, actual));
        }

        private sealed class PaginaDeEjemplo
        {
            public void OnGetAsync() { }
            public void OnPostAsync() { }
            public void OnPostDeleteAsync() { }
            public void OnPostAnular() { }
            public void OnGetExportar() { }
        }

        [Theory]
        [InlineData("Get", null, "OnGetAsync")]
        [InlineData("Post", null, "OnPostAsync")]
        [InlineData("Post", "Delete", "OnPostDeleteAsync")]
        [InlineData("Post", "delete", "OnPostDeleteAsync")]
        [InlineData("Post", "Anular", "OnPostAnular")]
        [InlineData("Get", "Exportar", "OnGetExportar")]
        [InlineData("Get", "Delete", null)]
        [InlineData("Post", "NoExiste", null)]
        public void El_handler_se_busca_por_verbo_y_nombre_con_o_sin_Async(string verbo, string? handler, string? esperado)
        {
            Assert.Equal(esperado, DestinoDePagina.BuscarHandler(typeof(PaginaDeEjemplo), verbo, handler)?.Name);
        }

        // ── Los tag helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class RequisitosFalsos : IRequisitosDePaginas
        {
            public List<(string Ruta, string Verbo, string? Handler)> Preguntas { get; } = new();
            public Dictionary<string, RequisitoDePagina> Pantallas { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<RequisitoDePagina?> ObtenerAsync(string rutaAbsoluta, string verbo, string? handler)
            {
                Preguntas.Add((rutaAbsoluta, verbo, handler));
                return Task.FromResult(Pantallas.TryGetValue(rutaAbsoluta, out var r) ? r : null);
            }
        }

        private sealed class PermisosFalsos : IPermisosDeLaPeticion
        {
            private readonly PermisosEfectivos _permisos;
            public PermisosFalsos(params string[] codigos) => _permisos = new PermisosEfectivos(new HashSet<string>(codigos, StringComparer.Ordinal));
            public Task<PermisosEfectivos> ObtenerAsync() => Task.FromResult(_permisos);
        }

        private sealed class OpcionesFalsas : IOptionsMonitor<OpcionesDePermisos>
        {
            public OpcionesFalsas(string? modo) => CurrentValue = new OpcionesDePermisos { Modo = modo };
            public OpcionesDePermisos CurrentValue { get; }
            public OpcionesDePermisos Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<OpcionesDePermisos, string?> listener) => null;
        }

        private sealed class VistaFalsa : IView
        {
            public string Path => "vista";
            public Task RenderAsync(ViewContext context) => Task.CompletedTask;
        }

        private sealed class TempDataFalso : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static ViewContext Vista(string paginaActual)
        {
            var http = new DefaultHttpContext();
            var accion = new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor { ViewEnginePath = paginaActual });
            return new ViewContext(accion, new VistaFalsa(), new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new ModelStateDictionary()),
                new TempDataDictionary(http, new TempDataFalso()), TextWriter.Null, new HtmlHelperOptions());
        }

        private static RequisitoDePagina Pide(string codigo) => new(TipoDeRequisito.Permiso, codigo, null, DelHandler: false);
        private static RequisitoDePagina SinPermiso() => new(TipoDeRequisito.SinPermiso, null, "centro de navegación", DelHandler: false);

        private static DecisorDeVisibilidad Decisor(RequisitosFalsos requisitos, PermisosFalsos permisos, string? modo = "Aplicar")
            => new(requisitos, permisos, new OpcionesFalsas(modo));

        private static (TagHelperContext Contexto, TagHelperOutput Salida) Elemento(string etiqueta, params (string Nombre, string Valor)[] atributos)
        {
            var lista = new TagHelperAttributeList(atributos.Select(a => new TagHelperAttribute(a.Nombre, a.Valor)));
            var contexto = new TagHelperContext(etiqueta, lista, new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));
            var salida = new TagHelperOutput(etiqueta, new TagHelperAttributeList(lista), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
            return (contexto, salida);
        }

        private static async Task<bool> SeOculta(OcultarSinPermisoTagHelper ayudante, string etiqueta, params (string Nombre, string Valor)[] atributos)
        {
            var (contexto, salida) = Elemento(etiqueta, atributos);
            await ayudante.ProcessAsync(contexto, salida);
            return salida.TagName == null;
        }

        private static OcultarSinPermisoTagHelper EnlaceA(DecisorDeVisibilidad decisor, string actual, string? pagina, string? handler = null)
            => new(decisor) { ViewContext = Vista(actual), Pagina = pagina, Handler = handler };

        [Fact]
        public async Task Un_enlace_a_una_pantalla_que_la_persona_no_puede_abrir_se_oculta()
        {
            var requisitos = new RequisitosFalsos { Pantallas = { ["/Flota/Catalogos/Vehiculos/Create"] = Pide("flota.vehiculos.crear") } };
            var decisor = Decisor(requisitos, new PermisosFalsos("flota.vehiculos.ver"));

            Assert.True(await SeOculta(EnlaceA(decisor, "/Flota/Catalogos/Vehiculos/Index", "Create"), "a"));
        }

        [Fact]
        public async Task Un_enlace_a_una_pantalla_que_la_persona_si_puede_abrir_se_muestra()
        {
            var requisitos = new RequisitosFalsos { Pantallas = { ["/Flota/Catalogos/Vehiculos/Create"] = Pide("flota.vehiculos.crear") } };
            var decisor = Decisor(requisitos, new PermisosFalsos("flota.vehiculos.crear"));

            Assert.False(await SeOculta(EnlaceA(decisor, "/Flota/Catalogos/Vehiculos/Index", "Create"), "a"));
        }

        [Fact]
        public async Task La_ruta_relativa_se_pregunta_desde_la_carpeta_de_la_pantalla_actual()
        {
            var requisitos = new RequisitosFalsos();
            var decisor = Decisor(requisitos, new PermisosFalsos());

            await SeOculta(EnlaceA(decisor, "/Flota/Catalogos/Vehiculos/Index", "./Edit"), "a");

            Assert.Equal(("/Flota/Catalogos/Vehiculos/Edit", "Get", null), requisitos.Preguntas.Single());
        }

        [Fact]
        public async Task Una_pantalla_sin_permiso_declarado_desconocida_o_de_navegacion_se_muestra()
        {
            var requisitos = new RequisitosFalsos
            {
                Pantallas =
                {
                    ["/Flota/Index"] = SinPermiso(),
                    ["/Sin/Declarar"] = new RequisitoDePagina(TipoDeRequisito.SinDeclarar, null, null, false),
                },
            };
            var decisor = Decisor(requisitos, new PermisosFalsos());

            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/Flota/Index"), "a"));          // centro de navegación
            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/Sin/Declarar"), "a"));         // sin declarar: no hay nada que asegurar
            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/No/Existe"), "a"));            // pantalla desconocida
        }

        [Fact]
        public async Task En_modo_Registrar_nada_se_oculta_porque_nada_se_niega()
        {
            var requisitos = new RequisitosFalsos { Pantallas = { ["/Flota/Catalogos/Vehiculos/Delete"] = Pide("flota.vehiculos.eliminar") } };
            var decisor = Decisor(requisitos, new PermisosFalsos(), modo: "Registrar");

            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/Flota/Catalogos/Vehiculos/Delete"), "a"));
        }

        [Fact]
        public async Task Un_formulario_con_handler_pregunta_por_el_handler_con_su_verbo_y_usa_la_pantalla_actual_si_no_dice_otra()
        {
            var requisitos = new RequisitosFalsos
            {
                Pantallas = { ["/Flota/Catalogos/Vehiculos/Index"] = new RequisitoDePagina(TipoDeRequisito.Permiso, "flota.vehiculos.eliminar", null, DelHandler: true) },
            };
            var decisor = Decisor(requisitos, new PermisosFalsos());
            var ayudante = EnlaceA(decisor, "/Flota/Catalogos/Vehiculos/Index", pagina: null, handler: "Delete");

            Assert.True(await SeOculta(ayudante, "form", ("method", "post")));
            Assert.Equal(("/Flota/Catalogos/Vehiculos/Index", "Post", "Delete"), requisitos.Preguntas.Single());
        }

        [Theory]
        [InlineData("a", null, "Get")]
        [InlineData("form", "post", "Post")]
        [InlineData("form", "POST", "Post")]
        [InlineData("form", "get", "Get")]
        [InlineData("form", null, "Get")]
        [InlineData("button", null, "Post")]
        public async Task El_verbo_sale_de_la_etiqueta_y_del_method_del_formulario(string etiqueta, string? method, string verbo)
        {
            var requisitos = new RequisitosFalsos();
            var ayudante = EnlaceA(Decisor(requisitos, new PermisosFalsos()), "/Index", "/Alguna/Pantalla", handler: "X");

            await SeOculta(ayudante, etiqueta, method == null ? Array.Empty<(string, string)>() : new[] { ("method", method) });

            Assert.Equal(verbo, requisitos.Preguntas.Single().Verbo);
        }

        [Fact]
        public async Task Una_tarjeta_de_un_subcentro_se_muestra_si_se_puede_abrir_alguna_de_sus_pantallas()
        {
            var requisitos = new RequisitosFalsos
            {
                Pantallas =
                {
                    ["/Flota/Gastos/Repuestos/Index"] = Pide("flota.repuestos.ver"),
                    ["/Flota/Gastos/Seguros/Index"] = Pide("flota.seguros.ver"),
                },
            };
            const string Destinos = "/Flota/Gastos/Repuestos/Index;/Flota/Gastos/Seguros/Index";

            async Task<bool> Oculta(params string[] permisos)
            {
                var (contexto, salida) = Elemento("div", ("eg-si-puede-abrir", Destinos));
                var ayudante = new SiPuedeAbrirTagHelper(Decisor(requisitos, new PermisosFalsos(permisos))) { ViewContext = Vista("/Flota/Gastos/Index"), Destino = Destinos };
                await ayudante.ProcessAsync(contexto, salida);
                return salida.TagName == null;
            }

            Assert.False(await Oculta("flota.seguros.ver"));            // alcanza con una
            Assert.False(await Oculta("flota.repuestos.ver", "flota.seguros.ver"));
            Assert.True(await Oculta("flota.vehiculos.ver"));           // ninguna: la tarjeta no deja un centro vacío
        }

        [Fact]
        public async Task El_operador_con_todo_el_catalogo_ve_todo()
        {
            var requisitos = new RequisitosFalsos
            {
                Pantallas =
                {
                    ["/Empresas/Index"] = Pide("plataforma.empresas.ver"),
                    ["/Flota/Catalogos/Vehiculos/Delete"] = Pide("flota.vehiculos.eliminar"),
                },
            };
            var decisor = Decisor(requisitos, new PermisosFalsos(CatalogoDePermisos.Todos.Select(p => p.Codigo).ToArray()));

            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/Empresas/Index"), "a"));
            Assert.False(await SeOculta(EnlaceA(decisor, "/Index", "/Flota/Catalogos/Vehiculos/Delete"), "a"));
        }

        // ── Los permisos de la petición ────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Sin_sesion_la_persona_no_tiene_ningun_permiso()
        {
            var http = new DefaultHttpContext();
            var servicio = new PermisosDeLaPeticion(new HttpContextAccessor { HttpContext = http }, new EvaluadorQueNoSeLlama(),
                new Tests.Infra.RegistroEnMemoria<PermisosDeLaPeticion>());

            var permisos = await servicio.ObtenerAsync();

            Assert.Empty(permisos.Codigos);
        }

        [Fact]
        public async Task Los_permisos_se_evaluan_una_sola_vez_por_peticion_y_se_comparten_con_el_filtro()
        {
            var http = Tests.Infra.SesionFalsa.Contexto("empresa_user", "1");
            var evaluador = new EvaluadorQueCuenta(new[] { "flota.vehiculos.ver" });
            var servicio = new PermisosDeLaPeticion(new HttpContextAccessor { HttpContext = http }, evaluador, new Tests.Infra.RegistroEnMemoria<PermisosDeLaPeticion>());

            var primera = await servicio.ObtenerAsync();
            var segunda = await servicio.ObtenerAsync();

            Assert.Same(primera, segunda);
            Assert.Equal(1, evaluador.Llamadas);
            Assert.Same(primera, http.Items[PermisosPageFilter.ClavePermisosEfectivos]);   // el filtro lee la misma clave
        }

        [Fact]
        public async Task Un_error_al_evaluar_no_tumba_la_vista_y_la_persona_ve_menos()
        {
            var http = Tests.Infra.SesionFalsa.Contexto("empresa_user", "1");
            var servicio = new PermisosDeLaPeticion(new HttpContextAccessor { HttpContext = http }, new EvaluadorQueFalla(), new Tests.Infra.RegistroEnMemoria<PermisosDeLaPeticion>());

            var permisos = await servicio.ObtenerAsync();

            Assert.Empty(permisos.Codigos);
            Assert.False(http.Items.ContainsKey(PermisosPageFilter.ClavePermisosEfectivos));   // no se recuerda el fallo
        }

        private sealed class EvaluadorQueNoSeLlama : IEvaluadorDePermisos
        {
            public Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default) => throw new InvalidOperationException("No debía evaluarse.");
            public void Invalidar(int idTenant) { }
            public void InvalidarCatalogo() { }
        }

        private sealed class EvaluadorQueCuenta : IEvaluadorDePermisos
        {
            private readonly string[] _codigos;
            public EvaluadorQueCuenta(string[] codigos) => _codigos = codigos;
            public int Llamadas { get; private set; }
            public Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default)
            {
                Llamadas++;
                return Task.FromResult(new PermisosEfectivos(new HashSet<string>(_codigos, StringComparer.Ordinal)));
            }
            public void Invalidar(int idTenant) { }
            public void InvalidarCatalogo() { }
        }

        private sealed class EvaluadorQueFalla : IEvaluadorDePermisos
        {
            public Task<PermisosEfectivos> EvaluarAsync(ContextoDePermisos contexto, CancellationToken ct = default) => throw new InvalidOperationException("Falla de prueba.");
            public void Invalidar(int idTenant) { }
            public void InvalidarCatalogo() { }
        }
    }
}
