using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Derechos;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Módulo de Psicología: el script 042 crea el esquema psi (6 tablas de tenant con RLS), el 043 el módulo, su derecho, sus 12
    /// permisos y sus plantillas, y el 044 la cuenta CAPAE con su plan. Estas pruebas mantienen alineados los scripts, el modelo de EF,
    /// las constantes de C#, el arnés de aislamiento, el índice y los verificadores de F1. No necesitan base de datos: el comportamiento
    /// (aplicar, repetir, revertir, aislamiento con la política encendida) se ensayó sobre una copia con el estado de producción y está
    /// anotado en <c>1 - Documetacion/INDICE_SCRIPTS_SQL.md</c>.
    /// </summary>
    public class ScriptsPsicologiaTests
    {
        private static readonly string[] TablasPsi =
        {
            "jornadas", "pacientes", "atenciones", "pruebas", "atencion_pruebas", "tipos_constancia", "constancias", "constancia_pruebas",
            "profesionales", "impresiones", "importaciones", "importacion_filas"
        };

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz() }.Concat(partes).ToArray()), Encoding.UTF8);

        private static string Script(string numero) => numero switch
        {
            "042" => Leer("2 - Script SQL", "042_psicologia_esquema.sql"),
            "043" => Leer("2 - Script SQL", "043_psicologia_modulo_permisos.sql"),
            "044" => Leer("2 - Script SQL", "044_psicologia_alta_cuenta.sql"),
            _ => throw new ArgumentOutOfRangeException(nameof(numero))
        };

        private static string SinComentarios(string sql) => Regex.Replace(Regex.Replace(sql, @"/\*[\s\S]*?\*/", " "), @"--[^\r\n]*", " ");

        private static string SinRollback(string sql) => sql[..sql.LastIndexOf("/*", StringComparison.Ordinal)];

        private static string Cambio(string numero) => SinComentarios(SinRollback(Script(numero)));

        // ── Forma de los archivos ─────────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("042")]
        [InlineData("043")]
        [InlineData("044")]
        public void Cada_script_es_ASCII_puro_y_trae_PRECHECK_CAMBIO_POSTCHECK_y_ROLLBACK(string numero)
        {
            var s = Script(numero);
            Assert.True(s.All(c => c < 128), $"El script {numero} debe ser ASCII puro (los textos con tilde van con NCHAR).");
            Assert.Contains($"PRECHECK {numero} correcto", s);
            Assert.Contains($"CAMBIO {numero} aplicado", s);
            Assert.Contains($"Nada que hacer: el script {numero} ya estaba aplicado", s);   // idempotente
            Assert.Contains("POSTCHECK", s);
            Assert.Contains("ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)", s);
            Assert.Contains("SET XACT_ABORT ON", s);
            Assert.Contains("BEGIN TRANSACTION", SinRollback(s));
            Assert.Contains($"#precheck_{numero}", s);
            Assert.Contains("Falta la marca del PRECHECK", s);
            Assert.Contains("/alerta-bd", s);
        }

        // ── 042: el esquema y el modelo de EF ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Las_columnas_del_042_son_las_del_modelo_de_EF()
        {
            var bloque = Regex.Match(Script("042"), @"INSERT INTO #columnas_042[\s\S]*?;\s*\r?\n").Value;
            var delScript = Regex.Matches(bloque, @"\(N'psi\.(?<tabla>\w+)', N'(?<columna>\w+)'\)").Cast<Match>()
                .GroupBy(m => m.Groups["tabla"].Value)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Groups["columna"].Value).Order(StringComparer.Ordinal).ToList());

            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=.;Database=modelo").Options;
            using var db = new ApplicationDbContext(opciones);
            var delModelo = db.Model.GetEntityTypes().Where(e => e.GetSchema() == "psi")
                .ToDictionary(e => e.GetTableName()!, e =>
                {
                    var tabla = StoreObjectIdentifier.Table(e.GetTableName()!, "psi");
                    return e.GetProperties().Select(p => p.GetColumnName(tabla)!).Order(StringComparer.Ordinal).ToList();
                });

            Assert.Equal(TablasPsi.Order(StringComparer.Ordinal), delScript.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(TablasPsi.Order(StringComparer.Ordinal), delModelo.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(179, delScript.Values.Sum(c => c.Count));
            foreach (var tabla in TablasPsi)
                Assert.Equal(delScript[tabla], delModelo[tabla]);

            // Lo que se crea es exactamente lo que se declara: cada CREATE TABLE psi.x esta en la lista de columnas.
            var creadas = Regex.Matches(Cambio("042"), @"CREATE TABLE psi\.(?<t>\w+)").Cast<Match>().Select(m => m.Groups["t"].Value).Order(StringComparer.Ordinal);
            Assert.Equal(TablasPsi.Order(StringComparer.Ordinal), creadas);
        }

        [Fact]
        public void Las_seis_tablas_son_de_tenant_en_el_arnes_y_en_el_modelo()
        {
            var clasificacion = ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla);
            foreach (var tabla in TablasPsi)
            {
                Assert.Equal(CategoriaTabla.Tenant, clasificacion[tabla].Categoria);
                Assert.True(clasificacion[tabla].EnModeloEf);
                Assert.False(clasificacion[tabla].EnBaseReal);
                Assert.Equal("042", clasificacion[tabla].CreadaPorScript);
            }

            foreach (var tipo in new[] { typeof(Jornada), typeof(Paciente), typeof(Atencion), typeof(Prueba), typeof(AtencionPrueba), typeof(TipoConstancia),
                                       typeof(Constancia), typeof(ConstanciaPrueba), typeof(Profesional), typeof(Impresion), typeof(Importacion), typeof(ImportacionFila) })
                Assert.True(typeof(Models.Plataforma.ITenantEntity).IsAssignableFrom(tipo), tipo.Name + " debe ser ITenantEntity.");

            // id_tenant obligatorio en las 6 tablas (el POSTCHECK del script tambien lo exige).
            Assert.Equal(12, Regex.Matches(Cambio("042"), @"id_tenant\s+INT\s+NOT NULL").Count);
            Assert.DoesNotMatch(@"id_tenant\s+INT\s+NULL", Cambio("042"));
        }

        [Fact]
        public void Las_seis_tablas_entran_a_la_politica_con_un_filtro_y_cuatro_bloqueos_de_fn_tenant_actual()
        {
            var predicados = Regex.Matches(Cambio("042"),
                    @"ADD (?<tipo>FILTER|BLOCK) PREDICATE seg\.(?<fn>\w+)\(id_tenant\) ON (?<tabla>[\w.]+)(?: (?<op>AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?")
                .Cast<Match>().ToList();

            Assert.Equal(60, predicados.Count);
            Assert.All(predicados, p => Assert.Equal("fn_tenant_actual", p.Groups["fn"].Value));
            foreach (var tabla in TablasPsi)
            {
                var deLaTabla = predicados.Where(p => p.Groups["tabla"].Value == "psi." + tabla).ToList();
                Assert.Single(deLaTabla, p => p.Groups["tipo"].Value == "FILTER");
                Assert.Equal(new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE DELETE", "BEFORE UPDATE" },
                    deLaTabla.Where(p => p.Groups["tipo"].Value == "BLOCK").Select(p => p.Groups["op"].Value).Order(StringComparer.Ordinal).ToArray());
            }

            // El PRECHECK acepta la politica de produccion con o sin 038 y 041, y nada del plano de control lleva RLS (ADR-013).
            Assert.Contains("NOT IN (199, 204, 209)", Script("042"));
            Assert.DoesNotMatch(@"ADD\s+(FILTER|BLOCK)\s+PREDICATE\s+[\w.]+\(\w+\)\s+ON\s+saas\.", Cambio("042"));
        }

        [Fact]
        public void Toda_clave_foranea_del_042_es_compuesta_por_tenant_salvo_la_que_apunta_a_tenants()
        {
            var claves = Regex.Matches(Cambio("042"), @"CONSTRAINT (?<nombre>\w+)\s+FOREIGN KEY \((?<columnas>[^)]*)\)\s+REFERENCES (?<destino>[\w.]+)")
                .Cast<Match>().ToList();

            Assert.Equal(37, claves.Count);
            foreach (var k in claves)
            {
                var nombre = k.Groups["nombre"].Value;
                var columnas = k.Groups["columnas"].Value.Replace(" ", string.Empty);
                if (k.Groups["destino"].Value == "saas.tenants")
                {
                    Assert.StartsWith("FK_", nombre);
                    Assert.Equal("id_tenant", columnas);
                }
                else
                {
                    Assert.StartsWith("FKT_", nombre);
                    Assert.StartsWith("id_tenant,", columnas);
                }
            }

            // Dentro de psi, tambien por empresa: una atencion, impresion o fila no puede apuntar a otra empresa del mismo tenant.
            Assert.All(claves.Where(k => k.Groups["destino"].Value.StartsWith("psi.", StringComparison.Ordinal)),
                k => Assert.StartsWith("id_tenant,id_empresa,", k.Groups["columnas"].Value.Replace(" ", string.Empty)));
        }

        private static IReadOnlyList<string> ValoresDelCheck(string sql, string restriccion, string columna)
        {
            var m = Regex.Match(sql, $@"CONSTRAINT {restriccion}\s+CHECK \({columna} IN \((?<valores>[^)]*)\)\)");
            Assert.True(m.Success, $"No se encontro {restriccion}.");
            return Regex.Matches(m.Groups["valores"].Value, @"'(?<v>[a-z_]+)'").Cast<Match>().Select(x => x.Groups["v"].Value).ToList();
        }

        [Fact]
        public void Los_valores_de_los_CHECK_son_los_de_las_constantes_de_CSharp()
        {
            var s = Cambio("042");
            Assert.Equal(TiposVinculo.Todos, ValoresDelCheck(s, "CK_persona_empresa_tipo", "tipo_vinculo"));
            Assert.Equal(EstadosJornada.Todos, ValoresDelCheck(s, "CK_jornadas_estado", "estado"));
            Assert.Equal(EstadosAtencion.Todos, ValoresDelCheck(s, "CK_atenciones_estado", "estado"));
            Assert.Equal(EstadosImportacion.Todos, ValoresDelCheck(s, "CK_importaciones_estado", "estado"));
            Assert.Equal(ResultadosFilaImportacion.Todos, ValoresDelCheck(s, "CK_importacion_filas_resultado", "resultado"));
            Assert.Equal(TamanosPapel.Todos, ValoresDelCheck(s, "CK_impresiones_papel", "tamano_papel"));
            Assert.Equal(EstadosConstancia.Todos, ValoresDelCheck(s, "CK_constancias_estado", "estado"));
            Assert.Equal(TiposProfesional.Todos, ValoresDelCheck(s, "CK_profesionales_tipo", "tipo"));
            Assert.Equal(ResultadosConstancia.Todos, ValoresDelCheck(s, "CK_constancias_resultado", "resultado"));
            Assert.Equal(ResultadosConstancia.Todos, ValoresDelCheck(s, "CK_atenciones_conclusion", "conclusion"));

            // La reversa devuelve el CHECK a los 6 tipos de antes (sin paciente).
            var rollback = Script("042")[Script("042").LastIndexOf("/*", StringComparison.Ordinal)..];
            Assert.Equal(TiposVinculo.Todos.Where(t => t != TiposVinculo.Paciente), ValoresDelCheck(rollback, "CK_persona_empresa_tipo", "tipo_vinculo"));
        }

        [Fact]
        public void El_042_solo_cambia_el_CHECK_de_vinculos_fuera_de_psi_y_no_toca_filas()
        {
            var s = Cambio("042");
            var alteradas = Regex.Matches(s, @"ALTER TABLE (?<t>[\w.]+)").Cast<Match>().Select(m => m.Groups["t"].Value).Distinct().ToList();
            Assert.Equal(new[] { "dbo.persona_empresa" }, alteradas);
            Assert.DoesNotMatch(@"\b(UPDATE|DELETE\s+FROM|MERGE)\s+(dbo|psi|saas)\.", s);
            Assert.DoesNotMatch(@"\bINSERT\s+INTO\s+(dbo|psi|saas)\.", s);
        }

        // ── 043: catalogos ────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_043_no_toca_roles_ni_empresas_y_solo_agrega_el_derecho_al_plan_interno()
        {
            var s = Cambio("043");
            Assert.DoesNotMatch(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+dbo\.(rol_permisos|empresa_rol_permisos|empresa_modulos|empresa_roles)\b", s);
            Assert.DoesNotMatch(@"\bUPDATE\s+\w+", s);
            Assert.DoesNotMatch(@"\bDELETE\s+FROM\b", s);

            var inserts = Regex.Matches(s, @"\bINSERT\s+INTO\s+(?<tabla>[\w.#]+)").Cast<Match>().Select(m => m.Groups["tabla"].Value).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(new[] { "#catalogo_043", "#precheck_043", "dbo.modulos", "saas.derechos", "saas.permisos", "saas.plan_version_derechos",
                                 "saas.roles_plantilla", "saas.roles_plantilla_permisos" }.Order(StringComparer.Ordinal), inserts);
            Assert.Contains("p.codigo = 'interno'", s);   // el derecho solo va al plan interno (regla del 040)
        }

        [Fact]
        public void El_modulo_del_043_esta_en_el_modelo_y_su_carpeta_en_el_filtro_de_derechos()
        {
            Assert.Contains("'psicologia'", Cambio("043"));
            Assert.Contains(("/Psicologia/", "psicologia"), DerechoDeModuloPageFilter.ModulosPorRuta);

            using var bd = new Infra.BaseDeDatosDePrueba();
            using var db = bd.Crear();
            Assert.Contains(db.Modulos, m => m.Codigo == "psicologia" && m.Activo);
        }

        // ── 044: la cuenta nueva ──────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_044_solo_escribe_en_el_plano_de_control()
        {
            var s = Cambio("044");
            var inserts = Regex.Matches(s, @"\bINSERT\s+INTO\s+(?<tabla>[\w.#]+)").Cast<Match>().Select(m => m.Groups["tabla"].Value).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(new[] { "#derechos_044", "#precheck_044", "saas.plan_version_derechos", "saas.plan_versiones", "saas.planes", "saas.tenant_planes",
                                 "saas.tenants" }.Order(StringComparer.Ordinal), inserts);
            Assert.DoesNotMatch(@"\b(UPDATE|DELETE\s+FROM|MERGE)\b", s);
            Assert.DoesNotMatch(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(dbo|psi)\.", s);
            Assert.Contains("'CAPAE'", s);
            Assert.Contains("('limite.empresas',   1)", Script("044"));
            Assert.Contains("('limite.usuarios',   10)", Script("044"));
        }

        // ── Indice y verificadores ────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_indice_registra_los_tres_scripts_y_los_verificadores_aceptan_el_042_y_el_043()
        {
            var indice = Leer("1 - Documetacion", "INDICE_SCRIPTS_SQL.md");
            Assert.Contains("| 042 | 042_psicologia_esquema.sql |", indice);
            Assert.Contains("| 043 | 043_psicologia_modulo_permisos.sql |", indice);
            Assert.Contains("| 044 | 044_psicologia_alta_cuenta.sql |", indice);

            var f15 = Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_f15.sql");
            var f17 = Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_f17.sql");
            var v041 = Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_041.sql");
            foreach (var v in new[] { f15, f17 })
            {
                Assert.Contains("AS con042", v);
                Assert.Contains("60 * c.con042", v);   // predicados
                Assert.Contains("37 * c.con042", v);   // claves foraneas
                Assert.Contains("12 * c.con042", v);   // claves hacia saas.tenants
            }
            Assert.Contains("DECLARE @con043 INT", v041);
        }
    }
}
