using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.6a: el script 041 crea el catálogo de permisos (<c>saas.permisos</c>), los roles plantilla, los permisos de cada rol
    /// (<c>dbo.rol_permisos</c>, con RLS) y migra <c>empresa_rol_permisos</c>. Estas pruebas mantienen ordenados el script, el catálogo en C#
    /// (<see cref="CatalogoDePermisos"/>), el modelo de EF, su verificador de solo lectura (<c>verificar_despues_041.sql</c>), la documentación
    /// y el arnés de aislamiento: una lista que se desvíe de la otra falla aquí. No necesitan base de datos; el comportamiento (aplicar, repetir,
    /// revertir, abortar, la política RLS encendida) se ensayó contra una copia real de producción y está en
    /// <c>1 - Documetacion/Operacion/F1/LEEME_F16.md</c>.
    /// </summary>
    public class ScriptsPermisosTests
    {
        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz() }.Concat(partes).ToArray()), Encoding.UTF8);

        private static string S041() => Leer("2 - Script SQL", "041_permisos_catalogo_y_roles.sql");
        private static string Verificador() => Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_041.sql");
        private static string Indice() => Leer("1 - Documetacion", "INDICE_SCRIPTS_SQL.md");

        private static string SinComentarios(string sql)
        {
            var t = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ");
            return Regex.Replace(t, @"--[^\r\n]*", " ");
        }

        private static string SinRollback(string sql) => sql[..sql.LastIndexOf("/*", StringComparison.Ordinal)];

        // ── Forma de los archivos ─────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_script_es_ASCII_puro_y_trae_PRECHECK_CAMBIO_POSTCHECK_y_ROLLBACK()
        {
            var s = S041();
            Assert.True(s.All(c => c < 128), "El script 041 debe ser ASCII puro (los textos con tilde van con NCHAR).");
            Assert.Contains("PRECHECK 041", s);
            Assert.Contains("CAMBIO 041 aplicado", s);
            Assert.Contains("POSTCHECK", s);
            Assert.Contains("ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)", s);
            Assert.Contains("BEGIN TRANSACTION", SinRollback(s));
            Assert.Contains("SET XACT_ABORT ON", s);
            Assert.Contains("Falta la marca del PRECHECK", s);   // sin PRECHECK en la misma sesion, los demas bloques no hacen nada
        }

        [Fact]
        public void El_cambio_solo_agrega_salvo_la_correccion_de_textos_de_modulos()
        {
            var codigo = SinComentarios(SinRollback(S041()));

            Assert.DoesNotMatch(@"\bDELETE\s+FROM\b", codigo);
            Assert.DoesNotMatch(@"\bTRUNCATE\s+TABLE\b", codigo);
            Assert.DoesNotMatch(@"\bDROP\s+(TABLE|COLUMN|INDEX|CONSTRAINT)\b(?!\s+#)", Regex.Replace(codigo, @"DROP TABLE #\w+", string.Empty));
            Assert.DoesNotMatch(@"\bALTER\s+TABLE\b", codigo);

            // El unico UPDATE toca dbo.modulos (D65); todo lo demas es INSERT.
            var updates = Regex.Matches(codigo, @"\bUPDATE\s+(?<alias>\w+)\s+SET\b").Cast<Match>().ToList();
            Assert.Equal(2, updates.Count);
            Assert.Contains("FROM dbo.modulos m", codigo);
            Assert.Contains("NCHAR(195)", codigo);   // solo las filas con el dano

            var inserts = Regex.Matches(codigo, @"\bINSERT\s+INTO\s+(?<tabla>[\w.#]+)").Cast<Match>().Select(m => m.Groups["tabla"].Value).Distinct().Order().ToList();
            Assert.Equal(new[] { "#catalogo_041", "#columnas_041", "#esperado_041", "#precheck_041", "dbo.rol_permisos", "saas.permisos", "saas.roles_plantilla",
                                 "saas.roles_plantilla_permisos" }.Order().ToList(), inserts);
        }

        [Fact]
        public void El_script_no_toca_empresa_rol_permisos_ni_nada_fuera_de_lo_declarado()
        {
            var codigo = SinComentarios(SinRollback(S041()));
            // Solo se LEE (SELECT ... FROM dbo.empresa_rol_permisos): ni se inserta, ni se actualiza, ni se borra.
            Assert.DoesNotMatch(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|ALTER\s+TABLE|DROP\s+TABLE)\s+dbo\.empresa_rol_permisos\b", codigo);
            Assert.DoesNotMatch(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+dbo\.(Users|empresa_roles|empresa_modulos|accesos_empresa)\b", codigo);
            Assert.Contains("FROM dbo.empresa_rol_permisos erp", codigo);
        }

        // ── El catálogo en SQL y en C# son el mismo ──────────────────────────────────────────────────────────────────────

        private sealed record FilaDelCatalogo(int Orden, string Modulo, string Recurso, string Accion, string Ambito, string? Legada, string Descripcion);

        /// <summary>Un literal de SQL hecho de <c>N'texto'</c> y <c>NCHAR(n)</c> unidos con <c>+</c>, ya como texto.</summary>
        private static string DecodificarLiteral(string expresion)
        {
            var piezas = Regex.Matches(expresion, @"N'(?<texto>(?:[^']|'')*)'|NCHAR\((?<codigo>\d+)\)").Cast<Match>().ToList();
            var resto = Regex.Replace(expresion, @"N'(?:[^']|'')*'|NCHAR\(\d+\)", string.Empty);
            Assert.True(resto.All(c => c == '+' || char.IsWhiteSpace(c)), "La descripcion tiene algo que no es N'...' ni NCHAR(n): " + expresion);

            var sb = new StringBuilder();
            foreach (var p in piezas)
                sb.Append(p.Groups["codigo"].Success ? ((char)int.Parse(p.Groups["codigo"].Value)).ToString() : p.Groups["texto"].Value.Replace("''", "'"));
            return sb.ToString();
        }

        private static List<FilaDelCatalogo> CatalogoDelScript()
        {
            var bloque = Regex.Match(S041(), @"INSERT INTO #catalogo_041[\s\S]*?;\s*\r?\n").Value;
            var filas = Regex.Matches(bloque,
                @"^\s*\((?<orden>\d+), '(?<modulo>[a-z_]+)', '(?<recurso>[a-z_]+)', '(?<accion>[a-z_]+)', '(?<ambito>[a-z]+)', (?<legada>NULL|'[a-z]+'), (?<desc>.+)\)[,;]\s*$",
                RegexOptions.Multiline).Cast<Match>().ToList();

            return filas.Select(m => new FilaDelCatalogo(
                int.Parse(m.Groups["orden"].Value), m.Groups["modulo"].Value, m.Groups["recurso"].Value, m.Groups["accion"].Value, m.Groups["ambito"].Value,
                m.Groups["legada"].Value == "NULL" ? null : m.Groups["legada"].Value.Trim('\''), DecodificarLiteral(m.Groups["desc"].Value))).ToList();
        }

        [Fact]
        public void El_catalogo_del_script_es_el_catalogo_en_C_sharp_fila_por_fila()
        {
            var delScript = CatalogoDelScript();

            Assert.Equal(CatalogoDePermisos.Todos.Count, delScript.Count);
            Assert.Equal(Enumerable.Range(1, delScript.Count), delScript.Select(f => f.Orden));   // el orden es la posicion

            for (var i = 0; i < delScript.Count; i++)
            {
                var c = CatalogoDePermisos.Todos[i];
                var s = delScript[i];
                Assert.True(
                    c.Modulo == s.Modulo && c.Recurso == s.Recurso && c.Accion == s.Accion && c.Ambito == s.Ambito && c.CasillaLegada == s.Legada && c.Descripcion == s.Descripcion,
                    $"Fila {i + 1}: C# = {c} / script = {s}");
            }
        }

        [Fact]
        public void El_catalogo_cumple_las_reglas_de_la_base()
        {
            var todos = CatalogoDePermisos.Todos;

            Assert.Equal(72, todos.Count);
            Assert.Equal(todos.Count, todos.Select(p => p.Codigo).Distinct(StringComparer.Ordinal).Count());
            Assert.All(todos, p =>
            {
                Assert.Matches(@"^[a-z0-9_]+\.[a-z0-9_]+\.[a-z0-9_]+$", p.Codigo);                    // CK_permisos_codigo
                Assert.Contains(p.Ambito, AmbitoDePermiso.Todos);                                      // CK_permisos_ambito
                Assert.True(p.CasillaLegada == null || (p.Ambito == AmbitoDePermiso.Modulo && CasillaLegada.Todas.Contains(p.CasillaLegada)),
                    "CK_permisos_accion_legada: " + p.Codigo);
                Assert.True(!string.IsNullOrWhiteSpace(p.Descripcion) && p.Descripcion.Length <= 200, p.Codigo);
                Assert.True(p.Codigo.Length <= 80 && p.Modulo.Length <= 30 && p.Recurso.Length <= 40 && p.Accion.Length <= 30, p.Codigo);

                // CK_permisos_ambito_modulo
                var esperado = p.Modulo switch { "plataforma" => AmbitoDePermiso.Plataforma, "administracion" => AmbitoDePermiso.Empresa, _ => AmbitoDePermiso.Modulo };
                Assert.Equal(esperado, p.Ambito);
            });

            // Las cifras que repite el verificador salen de aqui.
            Assert.Equal(new[] { 17, 9, 28, 8, 10 }, new[] { "plataforma", "administracion", "flota", "catalogos", "facturacion" }.Select(m => todos.Count(p => p.Modulo == m)));
            Assert.Equal(44, todos.Count(p => p.CasillaLegada != null));
            Assert.Equal(new[] { 17, 9, 46 }, new[] { AmbitoDePermiso.Plataforma, AmbitoDePermiso.Empresa, AmbitoDePermiso.Modulo }.Select(a => todos.Count(p => p.Ambito == a)));
        }

        [Fact]
        public void Los_modulos_del_catalogo_existen_y_cada_casilla_legada_concede_algo()
        {
            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();
            var modulosReales = db.Modulos.Select(m => m.Codigo).ToList();

            Assert.All(CatalogoDePermisos.ModulosConPermisos, m => Assert.Contains(m, modulosReales));
            Assert.Equal(new[] { "flota", "catalogos", "facturacion" }, CatalogoDePermisos.ModulosConPermisos);

            // La migracion de roles necesita que 'ver' conceda algo en cada modulo; 'eliminar' no lo necesita (catalogos y flota si lo tienen).
            foreach (var modulo in CatalogoDePermisos.ModulosConPermisos)
                Assert.NotEmpty(CatalogoDePermisos.DeCasillaLegada(modulo, CasillaLegada.Ver));
        }

        [Fact]
        public void Las_plantillas_siguen_la_regla_y_las_cifras_del_verificador_coinciden()
        {
            // Regla del script: administrador = todos los permisos de ambito modulo; operador = casilla legada ver, crear o editar; consulta = ver.
            var s = SinComentarios(SinRollback(S041()));
            Assert.Contains("rp.codigo = rp.modulo + '_administrador'", s);
            Assert.Contains("p.accion_legada IN ('ver', 'crear', 'editar')", s);
            Assert.Contains("p.accion_legada = 'ver'", s);

            var modulos = CatalogoDePermisos.ModulosConPermisos;
            int Administrador(string m) => CatalogoDePermisos.Todos.Count(p => p.Ambito == AmbitoDePermiso.Modulo && p.Modulo == m);
            int Operador(string m) => CatalogoDePermisos.Todos.Count(p => p.Ambito == AmbitoDePermiso.Modulo && p.Modulo == m && p.CasillaLegada is "ver" or "crear" or "editar");
            int Consulta(string m) => CatalogoDePermisos.Todos.Count(p => p.Ambito == AmbitoDePermiso.Modulo && p.Modulo == m && p.CasillaLegada == "ver");

            Assert.Equal(new[] { 28, 26, 11 }, new[] { Administrador("flota"), Operador("flota"), Consulta("flota") });
            Assert.Equal(new[] { 8, 5, 2 }, new[] { Administrador("catalogos"), Operador("catalogos"), Consulta("catalogos") });
            Assert.Equal(new[] { 10, 8, 4 }, new[] { Administrador("facturacion"), Operador("facturacion"), Consulta("facturacion") });

            var verificador = Verificador();
            var total = modulos.Sum(m => Administrador(m) + Operador(m) + Consulta(m));
            Assert.Equal(102, total);
            Assert.Contains($"N'{total}'", verificador);                                  // permisos de plantilla
            Assert.Contains($"N'{modulos.Count * 3}'", verificador);                      // plantillas
            Assert.Contains($"N'{CatalogoDePermisos.Todos.Count}'", verificador);         // permisos en el catalogo
            Assert.Contains("N'17 / 9 / 28 / 8 / 10'", verificador);
            Assert.Contains("N'17 / 9 / 46'", verificador);
            Assert.Contains($"N'{CatalogoDePermisos.Todos.Count(p => p.CasillaLegada != null)}'", verificador);
        }

        // ── El modelo de EF y el script describen las mismas tablas y columnas ────────────────────────────────────────

        [Fact]
        public void Las_columnas_del_script_son_las_del_modelo_de_EF()
        {
            var s = S041();
            var bloque = Regex.Match(s, @"INSERT INTO #columnas_041[\s\S]*?;\s*\r?\n").Value;
            var delScript = Regex.Matches(bloque, @"\(N'(?<tabla>(?:saas|dbo)\.\w+)', N'(?<columna>\w+)'\)").Cast<Match>()
                .GroupBy(m => m.Groups["tabla"].Value)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Groups["columna"].Value).Order(StringComparer.Ordinal).ToList());

            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();
            var delModelo = new[] { typeof(Permiso), typeof(RolPlantilla), typeof(RolPlantillaPermiso), typeof(RolPermiso) }
                .ToDictionary(
                    t => $"{db.Model.FindEntityType(t)!.GetSchema() ?? "dbo"}.{db.Model.FindEntityType(t)!.GetTableName()}",
                    t =>
                    {
                        var et = db.Model.FindEntityType(t)!;
                        var tabla = StoreObjectIdentifier.Table(et.GetTableName()!, et.GetSchema());
                        return et.GetProperties().Select(p => p.GetColumnName(tabla)!).Order(StringComparer.Ordinal).ToList();
                    });

            Assert.Equal(4, delScript.Count);
            foreach (var (tabla, columnas) in delScript)
            {
                Assert.True(delModelo.ContainsKey(tabla), "El modelo de EF no mapea " + tabla);
                Assert.Equal(columnas, delModelo[tabla]);
            }
            Assert.Equal(delScript.Keys.Order().ToList(), delModelo.Keys.Order().ToList());
        }

        [Fact]
        public void Rol_permisos_es_una_tabla_de_tenant_y_los_permisos_son_del_plano_de_control()
        {
            var clasificacion = ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla);

            Assert.Equal(CategoriaTabla.Tenant, clasificacion["rol_permisos"].Categoria);
            Assert.True(clasificacion["rol_permisos"].EnModeloEf);
            Assert.Equal("041", clasificacion["rol_permisos"].CreadaPorScript);
            foreach (var plano in new[] { "permisos", "roles_plantilla", "roles_plantilla_permisos" })
            {
                Assert.Equal(CategoriaTabla.Plataforma, clasificacion[plano].Categoria);
                Assert.Equal("041", clasificacion[plano].CreadaPorScript);
            }

            // El modelo: RolPermiso lleva id_tenant (ITenantEntity) y las del plano de control no.
            Assert.True(typeof(ITenantEntity).IsAssignableFrom(typeof(RolPermiso)));
            Assert.False(typeof(ITenantEntity).IsAssignableFrom(typeof(Permiso)));
            Assert.False(typeof(ITenantEntity).IsAssignableFrom(typeof(RolPlantilla)));
        }

        [Fact]
        public void Rol_permisos_entra_a_la_politica_con_un_filtro_y_cuatro_bloqueos_de_fn_tenant_actual_y_nada_de_saas_entra()
        {
            var s = S041();
            var predicados = Regex.Matches(s,
                    @"ADD (?<tipo>FILTER|BLOCK) PREDICATE seg\.(?<fn>\w+)\(id_tenant\) ON (?<tabla>[\w.]+)(?: (?<op>AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?")
                .Cast<Match>().ToList();

            Assert.Equal(5, predicados.Count);
            Assert.All(predicados, p => Assert.Equal("fn_tenant_actual", p.Groups["fn"].Value));
            Assert.All(predicados, p => Assert.Equal("dbo.rol_permisos", p.Groups["tabla"].Value));
            Assert.Single(predicados, p => p.Groups["tipo"].Value == "FILTER");
            Assert.Equal(new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE DELETE", "BEFORE UPDATE" },
                         predicados.Where(p => p.Groups["tipo"].Value == "BLOCK").Select(p => p.Groups["op"].Value).Order(StringComparer.Ordinal).ToArray());

            // 199 (036) + 5 (038, accesos_empresa) + 5 (041): el PRECHECK acepta 199, 204 y 209.
            Assert.Contains("199 + @predAcceso + @predRol", s);
            Assert.DoesNotMatch(@"ADD\s+(FILTER|BLOCK)\s+PREDICATE\s+[\w.]+\(\w+\)\s+ON\s+saas\.", s);   // el plano de control no lleva RLS (ADR-013)
        }

        [Fact]
        public void Rol_permisos_tiene_la_clave_compuesta_al_rol_y_la_restriccion_de_ambito()
        {
            var s = SinComentarios(SinRollback(S041()));

            Assert.Contains("FKT_rol_permisos_empresa_roles_id_rol FOREIGN KEY (id_tenant, id_rol) REFERENCES dbo.empresa_roles (id_tenant, id_rol)", s);
            Assert.Contains("FK_rol_permisos_permisos FOREIGN KEY (id_permiso, ambito_permiso) REFERENCES saas.permisos (id_permiso, ambito)", s);
            Assert.Contains("CK_rol_permisos_ambito CHECK (ambito_permiso IN ('empresa', 'modulo'))", s);   // un rol nunca recibe permisos de plataforma
            Assert.Contains("FK_roles_plantilla_permisos_plantilla FOREIGN KEY (id_rol_plantilla, modulo)", s);
            Assert.Contains("FK_roles_plantilla_permisos_permisos FOREIGN KEY (id_permiso, modulo)", s);
        }

        // ── Verificador, diaria, documentación, registro ────────────────────────────────────────────────────────────────

        [Fact]
        public void El_verificador_es_de_solo_lectura_y_la_comprobacion_diaria_lo_corre_cuando_el_041_esta_aplicado()
        {
            var v = SinComentarios(Verificador());
            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC|EXECUTE)\b", v);
            Assert.Contains("RESUMEN", v);

            var diaria = Leer("1 - Documetacion", "Operacion", "F1", "comprobacion_diaria.ps1");
            Assert.Contains("OBJECT_ID(N'dbo.rol_permisos', N'U')", diaria);
            Assert.Contains("verificar_despues_041.sql", diaria);
        }

        [Fact]
        public void Los_verificadores_anteriores_aceptan_las_cifras_del_041()
        {
            var f15 = Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_f15.sql");
            var f17 = Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_f17.sql");

            foreach (var v in new[] { f15, f17 })
            {
                Assert.Contains("AS con041", v);
                Assert.Contains("OBJECT_ID(N'dbo.rol_permisos', N'U')", v);
                Assert.Contains("5 * c.con041", v);   // predicados
            }
            Assert.Contains("57 + c.con041", f15);    // tablas de dbo
            Assert.Contains("42 + c.con041", f15);    // columnas id_tenant
            Assert.Contains("56 + c.con038 + c.con041", f17);
        }

        [Fact]
        public void El_indice_de_scripts_y_la_documentacion_nombran_el_041()
        {
            var indice = Indice();
            Assert.Contains("| 041 | 041_permisos_catalogo_y_roles.sql |", indice);
            Assert.Contains("verificar_despues_041.sql", indice);

            Assert.True(File.Exists(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", "LEEME_F16.md")));
            var adr = Leer("1 - Documetacion", "Arquitectura", "ADR-029-permisos-por-accion.md");
            Assert.Contains("Aceptada", adr);
            Assert.Contains("ADR-029", Leer("1 - Documetacion", "Arquitectura", "README.md"));
        }

        [Fact]
        public void El_evaluador_esta_registrado_pero_ninguna_pagina_ni_filtro_lo_usa_todavia()
        {
            var program = Leer("Program.cs");
            Assert.Contains("IEvaluadorDePermisos, eGestion360Web.Services.Permisos.EvaluadorDePermisos", program);

            // F1.6a no cambia comportamiento: nada de Pages/ ni de los filtros y middlewares llama al evaluador hasta F1.6b.
            var usuarios = Directory.GetFiles(Raiz(), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}eGestion360Web.Tests{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}Permisos{Path.DirectorySeparatorChar}")
                            && Path.GetFileName(f) != "Program.cs")
                .Where(f => File.ReadAllText(f).Contains("IEvaluadorDePermisos", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToList();
            Assert.Empty(usuarios);
        }
    }
}
