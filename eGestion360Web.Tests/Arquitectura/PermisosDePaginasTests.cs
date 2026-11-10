using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Que ninguna pantalla quede sin decidir su permiso (paso F1.6b, ADR-029, D59). Cada página declara <c>[RequierePermiso("…")]</c> con un
    /// código del catálogo, o <c>[PaginaSinPermiso("motivo")]</c> y entonces está en la lista fija de excepciones de esta prueba. Una pantalla
    /// nueva rompe la prueba hasta que alguien decide su permiso; agregar una excepción obliga a tocar también la lista de aquí.
    /// </summary>
    public class PermisosDePaginasTests
    {
        private const string EspacioDePaginas = "eGestion360Web.Pages";

        /// <summary>Las pantallas que no piden permiso, revisadas una por una. Agregar una aquí es una decisión de revisión, no un trámite.</summary>
        private static readonly string[] PaginasSinPermiso =
        {
            // Antes de tener sesión o públicas.
            "/About", "/AceptarInvitacion", "/Contact", "/Error", "/ForgotPassword", "/Index", "/Login", "/Products", "/Register", "/ResetPassword", "/SinAcceso",
            // De la propia cuenta.
            "/ChangePassword", "/Logout", "/Mfa", "/MfaAlta", "/SeleccionarContexto",
            // Centros de navegación: cada opción que ofrecen pide su propio permiso.
            "/MainMenu", "/Catalogos/Index", "/Flota/Index", "/Flota/Catalogos/Index", "/Flota/Gastos/Index", "/Flota/Operacion/Index", "/Flota/MantenimientoDatos/Index",
            // Explica por qué Flota no abre sin empresa.
            "/Flota/SinEmpresa",
        };

        private static string RaizDelRepositorio()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        /// <summary>Todas las clases de página concretas: <c>/Flota/Catalogos/Vehiculos/Edit</c> sale de <c>Pages.Flota.Catalogos.Vehiculos.EditModel</c>.</summary>
        private static IReadOnlyList<(string Ruta, Type Tipo)> Paginas()
            => typeof(PermisosPageFilter).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(PageModel).IsAssignableFrom(t)
                            && t.Namespace != null && (t.Namespace == EspacioDePaginas || t.Namespace.StartsWith(EspacioDePaginas + ".", StringComparison.Ordinal)))
                .Select(t => (Ruta: RutaDe(t), Tipo: t))
                .OrderBy(p => p.Ruta, StringComparer.Ordinal)
                .ToList();

        private static string RutaDe(Type tipo)
        {
            var carpeta = tipo.Namespace!.Length > EspacioDePaginas.Length
                ? tipo.Namespace[EspacioDePaginas.Length..].Replace('.', '/')
                : string.Empty;
            Assert.EndsWith("Model", tipo.Name);
            return carpeta + "/" + tipo.Name[..^"Model".Length];
        }

        private static IEnumerable<MethodInfo> Handlers(Type tipo)
            => tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<RequierePermisoAttribute>(inherit: false) != null);

        [Fact]
        public void Se_encuentran_las_110_paginas_del_sistema()
        {
            var paginas = Paginas();

            // Si la reflexión dejara de ver las páginas, las demás pruebas pasarían en vacío: este piso lo impide.
            Assert.True(paginas.Count >= 110, $"Se esperaban al menos 110 páginas y se encontraron {paginas.Count}.");
        }

        [Fact]
        public void Cada_pagina_tiene_su_archivo_y_su_ruta_es_la_de_su_espacio_de_nombres()
        {
            var raiz = RaizDelRepositorio();
            var sinArchivo = Paginas()
                .Where(p => !File.Exists(Path.Combine(raiz, "Pages", p.Ruta.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) + ".cshtml.cs")))
                .Select(p => p.Ruta)
                .ToList();

            Assert.True(sinArchivo.Count == 0, "Páginas cuyo espacio de nombres no coincide con su carpeta: " + string.Join(", ", sinArchivo));
        }

        [Fact]
        public void Toda_pantalla_tiene_su_clase_de_pagina_para_poder_declarar_su_permiso()
        {
            // Las vistas parciales y los diseños empiezan con «_» y no son pantallas; cualquier otro .cshtml es una pantalla y necesita su .cshtml.cs.
            var pantallas = Path.Combine(RaizDelRepositorio(), "Pages");
            var sinClase = Directory.EnumerateFiles(pantallas, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_') && !File.Exists(f + ".cs"))
                .Select(f => Path.GetRelativePath(pantallas, f))
                .ToList();

            Assert.True(sinClase.Count == 0, "Pantallas sin clase de página (no se les puede declarar permiso): " + string.Join(", ", sinClase));
        }

        [Fact]
        public void Toda_pagina_declara_su_permiso_o_su_motivo_para_no_pedirlo_pero_no_ambos()
        {
            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                var pide = tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
                var sin = tipo.GetCustomAttribute<PaginaSinPermisoAttribute>(inherit: false);
                if (pide == null && sin == null) problemas.Add($"{ruta}: no declara [RequierePermiso] ni [PaginaSinPermiso]");
                if (pide != null && sin != null) problemas.Add($"{ruta}: declara las dos cosas");
                if (sin != null && string.IsNullOrWhiteSpace(sin.Motivo)) problemas.Add($"{ruta}: [PaginaSinPermiso] sin motivo");
            }

            Assert.True(problemas.Count == 0, "Decide el permiso de estas pantallas (ADR-029, D59): " + string.Join("; ", problemas));
        }

        [Fact]
        public void Todo_codigo_declarado_existe_en_el_catalogo_y_no_se_repiten_atributos_heredados()
        {
            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                if (tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false) is { } deLaClase && !CatalogoDePermisos.Existe(deLaClase.Codigo))
                    problemas.Add($"{ruta}: «{deLaClase.Codigo}» no está en el catálogo");

                foreach (var metodo in Handlers(tipo))
                {
                    var codigo = metodo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false)!.Codigo;
                    if (!CatalogoDePermisos.Existe(codigo)) problemas.Add($"{ruta}.{metodo.Name}: «{codigo}» no está en el catálogo");
                    if (!Regex.IsMatch(metodo.Name, "^On(Get|Post|Put|Delete|Patch)")) problemas.Add($"{ruta}.{metodo.Name}: el atributo está en un método que no es un handler");
                }
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        [Fact]
        public void Las_paginas_sin_permiso_son_exactamente_las_revisadas()
        {
            var declaradas = Paginas()
                .Where(p => p.Tipo.GetCustomAttribute<PaginaSinPermisoAttribute>(inherit: false) != null)
                .Select(p => p.Ruta)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();

            var revisadas = PaginasSinPermiso.OrderBy(r => r, StringComparer.Ordinal).ToList();

            Assert.Equal(revisadas, declaradas);
        }

        [Fact]
        public void Una_pagina_con_handlers_de_otro_permiso_los_declara_dentro_del_mismo_modulo_que_su_clase()
        {
            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                var deLaClase = tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
                if (deLaClase == null) continue;
                var moduloDeLaClase = CatalogoDePermisos.Buscar(deLaClase.Codigo)?.Modulo;

                foreach (var metodo in Handlers(tipo))
                {
                    var moduloDelHandler = CatalogoDePermisos.Buscar(metodo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false)!.Codigo)?.Modulo;
                    if (moduloDelHandler != moduloDeLaClase)
                        problemas.Add($"{ruta}.{metodo.Name}: pide un permiso de «{moduloDelHandler}» y la página es de «{moduloDeLaClase}»");
                }
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        [Fact]
        public void Una_pantalla_de_una_carpeta_de_modulo_pide_permisos_de_ese_modulo()
        {
            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                var modulo = DerechoDeModuloPageFilter.ModuloDe(ruta);
                var pide = tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
                if (modulo == null || pide == null) continue;

                var definicion = CatalogoDePermisos.Buscar(pide.Codigo);
                if (definicion == null || definicion.Modulo != modulo || definicion.Ambito != AmbitoDePermiso.Modulo)
                    problemas.Add($"{ruta}: está en la carpeta del módulo «{modulo}» y pide «{pide.Codigo}»");
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        [Fact]
        public void Una_pantalla_fuera_de_las_carpetas_de_modulo_no_pide_permisos_de_modulo_salvo_las_tasas_de_cambio()
        {
            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                if (DerechoDeModuloPageFilter.ModuloDe(ruta) != null) continue;
                var pide = tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
                if (pide == null) continue;

                var definicion = CatalogoDePermisos.Buscar(pide.Codigo);
                if (definicion != null && definicion.Ambito == AmbitoDePermiso.Modulo)
                    problemas.Add($"{ruta}: pide el permiso de módulo «{pide.Codigo}» fuera de una carpeta de módulo");
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        [Fact]
        public void La_accion_del_permiso_coincide_con_el_nombre_de_la_pagina()
        {
            // Convención de D59 vigilada, no deducida: el nombre de la pantalla dice qué acción pide.
            var esperada = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Index"] = "ver", ["Details"] = "ver", ["Create"] = "crear", ["Edit"] = "editar",
                ["Delete"] = "eliminar", ["Anular"] = "anular", ["Aplicar"] = "aplicar",
            };

            var problemas = new List<string>();
            foreach (var (ruta, tipo) in Paginas())
            {
                var nombre = ruta[(ruta.LastIndexOf('/') + 1)..];
                var pide = tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
                if (pide == null || !esperada.TryGetValue(nombre, out var accion)) continue;

                var real = CatalogoDePermisos.Buscar(pide.Codigo)?.Accion;
                if (real != accion) problemas.Add($"{ruta}: se llama «{nombre}» y pide «{pide.Codigo}» (se esperaba la acción «{accion}»)");
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        [Fact]
        public void Todo_permiso_de_pantalla_del_catalogo_lo_pide_alguna_pagina_o_esta_en_la_lista_de_los_que_aun_no_tienen_pantalla()
        {
            var pedidos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, tipo) in Paginas())
            {
                if (tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false) is { } p) pedidos.Add(p.Codigo);
                foreach (var m in Handlers(tipo)) pedidos.Add(m.GetCustomAttribute<RequierePermisoAttribute>(inherit: false)!.Codigo);
            }

            // Permisos del catálogo que ninguna pantalla pide todavía: hoy no hay pantalla que los use (la tabla de roles los reparte igual).
            var sinPantalla = new[]
            {
                "administracion.usuarios.ver",
            };

            var huerfanos = CatalogoDePermisos.Todos.Select(p => p.Codigo).Where(c => !pedidos.Contains(c) && !sinPantalla.Contains(c)).ToList();

            Assert.True(huerfanos.Count == 0, "Permisos del catálogo que ninguna página pide: " + string.Join(", ", huerfanos));
        }

        [Fact]
        public void Program_registra_el_filtro_de_permisos_al_final_de_los_globales_y_las_opciones()
        {
            var program = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "Program.cs"));

            var modulo = program.IndexOf("options.Filters.Add<DerechoDeModuloPageFilter>()", StringComparison.Ordinal);
            var permisos = program.IndexOf("options.Filters.Add<eGestion360Web.Services.Permisos.PermisosPageFilter>()", StringComparison.Ordinal);
            Assert.True(modulo >= 0 && permisos > modulo, "El filtro de permisos debe registrarse después del de módulos: así solo habla de lo que de verdad ejecutaría la pantalla.");

            var filtros = Regex.Matches(program, @"options\.Filters\.Add<").Count;
            var ultimo = program.LastIndexOf("options.Filters.Add<", StringComparison.Ordinal);
            Assert.Equal(permisos, ultimo);
            Assert.True(filtros >= 6);

            Assert.Contains("OpcionesDePermisos>(builder.Configuration.GetSection(", program);
        }

        [Fact]
        public void El_modo_de_partida_en_la_configuracion_es_Registrar()
        {
            var json = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "appsettings.json"));

            Assert.Matches(@"""Permisos""\s*:\s*\{\s*""Modo""\s*:\s*""Registrar""", json);
        }
    }
}
