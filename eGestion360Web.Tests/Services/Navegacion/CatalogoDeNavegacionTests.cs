using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Navegacion;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Navegacion
{
    /// <summary>
    /// El catálogo único de navegación del menú configurable (etapa 2 del plan secundario): todos los estilos, el hub y el buscador Ctrl+K muestran
    /// solo los módulos activos de la empresa y las pantallas que la persona puede abrir, con la misma pregunta que hace cada pantalla (ADR-029).
    /// </summary>
    public class CatalogoDeNavegacionTests
    {
        // ── Dobles ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class RequisitosFalsos : IRequisitosDePaginas
        {
            public Dictionary<string, RequisitoDePagina> Pantallas { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<RequisitoDePagina?> ObtenerAsync(string rutaAbsoluta, string verbo, string? handler)
                => Task.FromResult(Pantallas.TryGetValue(rutaAbsoluta, out var r) ? r : null);
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

        private static RequisitoDePagina Pide(string codigo) => new(TipoDeRequisito.Permiso, codigo, null, DelHandler: false);

        private static HttpContext Persona(string rol = "empresa_user", bool autenticado = true, params string[] modulosActivos)
        {
            var http = SesionFalsa.Contexto(rol, "1", autenticado: autenticado);
            // Los módulos de la petición, como los deja EstadoDelTenantMiddleware (el plan los concede y la empresa los tiene activos).
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = new DerechosDeLaPeticion(
                new DerechosDelTenant(1, "T", TipoTenant.Cliente, EstadoTenant.Activo, ModoDeSuspension.Lectura, null, "plan",
                    new HashSet<string>(modulosActivos.Select(CodigosDeDerecho.DeModulo)), new Dictionary<string, int?>()),
                new HashSet<string>(modulosActivos));
            return http;
        }

        private static Task<MenuDeNavegacion> Menu(HttpContext http, RequisitosFalsos requisitos, string[] permisos,
            string modo = "Aplicar", IReadOnlyList<ModuloDeNavegacion>? definicion = null)
        {
            var efectivos = new PermisosFalsos(permisos);
            var decisor = new DecisorDeVisibilidad(requisitos, efectivos, new OpcionesFalsas(modo));
            var acceso = new HttpContextAccessor { HttpContext = http };
            var catalogo = definicion == null
                ? new CatalogoDeNavegacion(acceso, decisor, efectivos)
                : new CatalogoDeNavegacion(acceso, decisor, efectivos, definicion);
            return catalogo.ObtenerAsync();
        }

        private static RequisitosFalsos FlotaConDosPantallas() => new()
        {
            Pantallas =
            {
                ["/Flota/Catalogos/Vehiculos/Index"] = Pide("flota.vehiculos.ver"),
                ["/Flota/Catalogos/Rutas/Index"] = Pide("flota.rutas.ver"),
            },
        };

        // ── Módulos ────────────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Un_modulo_activo_se_muestra_a_quien_puede_hacer_algo_en_el_y_se_oculta_a_quien_no()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "flota", "catalogos" }), FlotaConDosPantallas(), new[] { "flota.vehiculos.ver" });

            Assert.Contains(menu.Modulos, m => m.Clave == "flota");
            Assert.DoesNotContain(menu.Modulos, m => m.Clave == "catalogos");   // activo, pero su rol no hace nada en él
        }

        [Fact]
        public async Task Un_modulo_inactivo_no_se_muestra_aunque_la_persona_tenga_permisos_de_el()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "flota" }), new RequisitosFalsos(), new[] { "facturacion.factura.ver", "flota.vehiculos.ver" });

            Assert.DoesNotContain(menu.Modulos, m => m.Clave == "facturacion");
        }

        [Fact]
        public async Task Un_modulo_sin_pantallas_todavia_se_muestra_como_proximamente_si_esta_activo()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "bancos" }), new RequisitosFalsos(), Array.Empty<string>());

            var bancos = Assert.Single(menu.Modulos, m => m.Clave == "bancos");
            Assert.True(bancos.Proximamente);
            Assert.DoesNotContain(menu.Modulos, m => m.Clave == "contabilidad");   // no activo
        }

        [Fact]
        public async Task Solo_quedan_las_pantallas_que_la_persona_puede_abrir()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "flota" }), FlotaConDosPantallas(), new[] { "flota.vehiculos.ver" });

            var flota = Assert.Single(menu.Modulos, m => m.Clave == "flota");
            Assert.Contains(flota.Pantallas, p => p.Ruta == "/Flota/Catalogos/Vehiculos/Index");
            Assert.DoesNotContain(flota.Pantallas, p => p.Ruta == "/Flota/Catalogos/Rutas/Index");
        }

        [Fact]
        public async Task En_modo_Registrar_no_se_oculta_ninguna_pantalla_porque_ninguna_se_niega()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "flota" }), FlotaConDosPantallas(), new[] { "flota.vehiculos.ver" }, modo: "Registrar");

            var flota = Assert.Single(menu.Modulos, m => m.Clave == "flota");
            Assert.Contains(flota.Pantallas, p => p.Ruta == "/Flota/Catalogos/Rutas/Index");
        }

        [Fact]
        public async Task Un_modulo_cuyas_pantallas_listas_quedan_todas_ocultas_no_aparece()
        {
            var definicion = new[]
            {
                new ModuloDeNavegacion("demo", "Demo", "", "fa-x", "#000", GrupoDeNavegacion.Empresa,
                    new[]
                    {
                        new PantallaDeNavegacion("/Demo/Index", "Lista", "", "fa-x"),
                        new PantallaDeNavegacion("/Demo/Futuro", "Futuro", "", "fa-x", Proximamente: true),
                    }),
            };
            var requisitos = new RequisitosFalsos { Pantallas = { ["/Demo/Index"] = Pide("administracion.demo.ver") } };

            var sin = await Menu(Persona(), requisitos, Array.Empty<string>(), definicion: definicion);
            var con = await Menu(Persona(), requisitos, new[] { "administracion.demo.ver" }, definicion: definicion);

            Assert.True(sin.EstaVacio);   // la pantalla «Próximamente» sola no sostiene la tarjeta
            Assert.Equal(2, Assert.Single(con.Modulos).Pantallas.Count);
        }

        [Fact]
        public async Task Los_bloques_de_administracion_salen_solo_a_quien_tiene_permisos_de_ese_ambito()
        {
            var deEmpresa = await Menu(Persona(), new RequisitosFalsos(), new[] { "administracion.roles.ver" });
            var deSistema = await Menu(Persona(), new RequisitosFalsos(), new[] { "plataforma.soporte.ver" });
            var ninguno = await Menu(Persona(modulosActivos: new[] { "flota" }), new RequisitosFalsos(), new[] { "flota.vehiculos.ver" });

            Assert.Contains(deEmpresa.Modulos, m => m.Grupo == GrupoDeNavegacion.Empresa);
            Assert.DoesNotContain(deEmpresa.Modulos, m => m.Grupo == GrupoDeNavegacion.Plataforma);
            Assert.Contains(deSistema.Modulos, m => m.Grupo == GrupoDeNavegacion.Plataforma);
            Assert.DoesNotContain(deSistema.Modulos, m => m.Grupo == GrupoDeNavegacion.Empresa);
            Assert.DoesNotContain(ninguno.Modulos, m => m.Grupo != GrupoDeNavegacion.Operacion);
        }

        [Fact]
        public async Task El_operador_en_la_plataforma_ve_todos_los_modulos_como_en_el_menu_anterior()
        {
            var menu = await Menu(Persona("admin"), new RequisitosFalsos(), new[] { "flota.vehiculos.ver", "plataforma.empresas.ver" });

            Assert.Contains(menu.Modulos, m => m.Clave == "flota");
            Assert.Contains(menu.Modulos, m => m.Clave == "bancos");
            Assert.Contains(menu.Modulos, m => m.Clave == "sistema");
        }

        [Fact]
        public async Task Sin_sesion_el_menu_esta_vacio()
        {
            var menu = await Menu(Persona(autenticado: false, modulosActivos: new[] { "flota" }), new RequisitosFalsos(), new[] { "flota.vehiculos.ver" });

            Assert.True(menu.EstaVacio);
        }

        // ── Buscador y módulo activo ───────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task El_buscador_lleva_solo_pantallas_listas_y_permitidas_y_el_inicio_de_cada_modulo()
        {
            var menu = await Menu(Persona(modulosActivos: new[] { "flota", "catalogos" }), FlotaConDosPantallas(),
                new[] { "flota.vehiculos.ver", "catalogos.clientes.ver" });

            var busqueda = menu.Busqueda();
            Assert.Contains(busqueda, e => e.Ruta == "/Flota/Catalogos/Vehiculos/Index");
            Assert.DoesNotContain(busqueda, e => e.Ruta == "/Flota/Catalogos/Rutas/Index");     // sin permiso
            Assert.DoesNotContain(busqueda, e => e.Ruta == "/Catalogos/Proveedores/Index");     // próximamente
            Assert.Contains(busqueda, e => e.Ruta == "/Flota/Index");                           // el centro del módulo
            Assert.All(busqueda, e => Assert.Equal(e.Texto, e.Texto.ToLowerInvariant()));
        }

        [Theory]
        [InlineData("/Flota/Catalogos/Vehiculos/Edit", "flota")]
        [InlineData("/Flota/Index", "flota")]
        [InlineData("/Facturacion/Details", "facturacion")]
        [InlineData("/Admin/Invitaciones/Create", "empresa")]
        [InlineData("/Admin/EmailConfig", "sistema")]
        [InlineData("/MainMenu", null)]
        [InlineData(null, null)]
        public async Task La_pantalla_actual_marca_su_modulo(string? ruta, string? clave)
        {
            var menu = await Menu(Persona("admin"), new RequisitosFalsos(),
                new[] { "flota.vehiculos.ver", "facturacion.factura.ver", "administracion.invitaciones.ver", "plataforma.empresas.ver" });

            Assert.Equal(clave, menu.ModuloDe(ruta)?.Clave);
        }

        // ── La definición ──────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Cada_pantalla_lista_del_catalogo_existe_y_no_se_repite()
        {
            var existentes = PantallasExistentes();
            var rutas = ModeloNavegacion.Modulos.SelectMany(m => m.PantallasListas.Select(p => p.Ruta)
                    .Concat(m.RutaIndice != null ? new[] { m.RutaIndice } : Array.Empty<string>()))
                .ToList();

            var inexistentes = rutas.Where(r => !existentes.Contains(r)).Distinct().ToList();
            Assert.True(inexistentes.Count == 0, "El catálogo de navegación apunta a pantallas que no existen: " + string.Join(", ", inexistentes));

            var repetidas = ModeloNavegacion.Modulos.SelectMany(m => m.Pantallas).GroupBy(p => p.Ruta, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(repetidas.Count == 0, "Pantallas repetidas en el catálogo: " + string.Join(", ", repetidas));
        }

        [Fact]
        public void Las_claves_de_modulo_son_unicas_y_los_modulos_de_operacion_tienen_su_codigo_de_contrato()
        {
            Assert.Equal(ModeloNavegacion.Modulos.Count, ModeloNavegacion.Modulos.Select(m => m.Clave).Distinct().Count());
            Assert.All(ModeloNavegacion.Modulos.Where(m => m.Grupo == GrupoDeNavegacion.Operacion), m => Assert.False(string.IsNullOrEmpty(m.CodigoModulo)));
            Assert.All(ModeloNavegacion.Modulos.Where(m => m.Grupo != GrupoDeNavegacion.Operacion), m => Assert.False(string.IsNullOrEmpty(m.PrefijoDePermiso)));
        }

        private static HashSet<string> PantallasExistentes()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);

            var paginas = Path.Combine(carpeta!.FullName, "Pages");
            return Directory.EnumerateFiles(paginas, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_') && File.ReadLines(f).Any(l => l.TrimStart().StartsWith("@page", StringComparison.Ordinal)))
                .Select(f => "/" + Path.GetRelativePath(paginas, f).Replace(Path.DirectorySeparatorChar, '/')[..^".cshtml".Length])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
