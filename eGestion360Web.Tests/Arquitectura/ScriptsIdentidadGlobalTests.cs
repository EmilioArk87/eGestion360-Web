using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.5: los scripts 038 (identidad global con membresías: 9 tablas, una de ellas de tenant con RLS) y 039 (copia de las
    /// 8 filas de <c>dbo.Users</c> a cuentas, alias, membresías y accesos por empresa), el script de emergencia del MFA y las
    /// verificaciones <c>verificar_despues_038.sql</c> y <c>verificar_despues_f15.sql</c>. Estas pruebas mantienen ordenados el arnés
    /// de aislamiento, los scripts y las verificaciones: una tabla de tenant nueva que no entre a la política, una lista de columnas
    /// o restricciones que se desvíe del <c>CREATE TABLE</c> o una cifra de las verificaciones que no cuadre con las reglas del 039
    /// falla aquí. También vigilan que ningún archivo lleve secretos. No necesitan base de datos: el comportamiento se ensayó contra
    /// una restauración de la copia real (política apagada y encendida), con repetición, ROLLBACK y abortos del PRECHECK.
    /// </summary>
    public class ScriptsIdentidadGlobalTests
    {
        private const string S036 = "036_rls_politicas_apagadas.sql";
        private const string S038 = "038_identidad_global.sql";
        private const string S039 = "039_migrar_cuentas.sql";
        private const string EmergenciaMfa = "emergencia_mfa_desactivar.sql";
        private const string Verif038 = "verificar_despues_038.sql";
        private const string VerifF15 = "verificar_despues_f15.sql";

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Script(string nombre) => File.ReadAllText(Path.Combine(Raiz(), "2 - Script SQL", nombre), Encoding.UTF8);

        private static string Operacion(string nombre) => File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", nombre), Encoding.UTF8);

        /// <summary>Lo que ejecuta el script: todo menos la sección ROLLBACK, que es el último bloque comentado.</summary>
        private static string Cambio(string texto) => texto[..texto.LastIndexOf("/*", StringComparison.Ordinal)];

        private static string Rollback(string texto) => texto[texto.LastIndexOf("/*", StringComparison.Ordinal)..];

        /// <summary>El código sin comentarios (los bloques /* */ y las líneas --): lo que de verdad ejecuta la base.</summary>
        private static string Codigo(string texto)
            => Regex.Replace(Regex.Replace(texto, @"/\*[\s\S]*?\*/", " "), @"--[^\r\n]*", " ");

        private static string Entre(string texto, string inicio, string fin)
        {
            var i = texto.IndexOf(inicio, StringComparison.Ordinal);
            Assert.True(i >= 0, "No se encontró: " + inicio);
            var j = texto.IndexOf(fin, i, StringComparison.Ordinal);
            Assert.True(j > i, "No se encontró: " + fin);
            return texto[i..j];
        }

        // ── Lo que declara el 038 ────────────────────────────────────────────────────────────────────────────────

        private static readonly Regex CreaTabla = new(
            @"CREATE TABLE (?<esq>saas|dbo)\.(?<tabla>\w+) \((?<cuerpo>[\s\S]*?)\r?\n {8}\);", RegexOptions.Compiled);

        private static readonly Regex FilaColumna = new(
            @"\(N'(?<t>[a-z_]+\.[a-z_]+)', N'(?<c>[a-z_]+)', '(?<tipo>[a-z0-9()]+)', (?<nulable>[01])\)", RegexOptions.Compiled);

        private static readonly Regex FilaObjeto = new(
            @"\(N'(?<t>[a-z_]+\.[a-z_]+)', N'(?<n>[A-Za-z0-9_]+)', '(?<tipo>[A-Z]{1,2})'\)", RegexOptions.Compiled);

        private sealed record Columna(string Tabla, string Nombre, string Tipo, bool Nulable);

        private sealed record Objeto(string Tabla, string Nombre, string Tipo);

        private static List<Columna> ColumnasDeLaLista(string texto)
            => FilaColumna.Matches(texto).Select(m => new Columna(m.Groups["t"].Value, m.Groups["c"].Value, m.Groups["tipo"].Value, m.Groups["nulable"].Value == "1")).ToList();

        private static List<Objeto> ObjetosDeLaLista(string texto)
            => FilaObjeto.Matches(texto).Select(m => new Objeto(m.Groups["t"].Value, m.Groups["n"].Value, m.Groups["tipo"].Value)).ToList();

        private static List<Columna> ListaDeColumnasDel038(string s038)
            => ColumnasDeLaLista(Entre(s038, "INSERT INTO #columnas_038", "CREATE TABLE #objetos_038"));

        private static List<Objeto> ListaDeObjetosDel038(string s038)
            => ObjetosDeLaLista(Entre(s038, "INSERT INTO #objetos_038", "DECLARE @errores"));

        /// <summary>Tablas que crea el CAMBIO del 038, con el cuerpo de su CREATE TABLE.</summary>
        private static List<(string Esquema, string Tabla, string Cuerpo)> TablasQueCrea(string s038)
            => CreaTabla.Matches(Cambio(s038)).Select(m => (m.Groups["esq"].Value, m.Groups["tabla"].Value, m.Groups["cuerpo"].Value)).ToList();

        private static List<Columna> ColumnasDelCreate(string esquema, string tabla, string cuerpo)
        {
            var resultado = new List<Columna>();
            foreach (var linea in cuerpo.Split('\n').Select(l => l.Trim()))
            {
                if (linea.Length == 0 || linea.StartsWith("--") || linea.StartsWith("CONSTRAINT")) continue;
                var m = Regex.Match(linea, @"^\[?(?<c>\w+)\]?\s+(?<t>[A-Za-z0-9]+(?:\(\w+(?:,\d+)?\))?)\s*(?<resto>.*)$");
                Assert.True(m.Success, $"No se entiende la columna de {esquema}.{tabla}: {linea}");
                var tipo = m.Groups["t"].Value.ToLowerInvariant();
                if (tipo == "rowversion") tipo = "timestamp";
                var resto = m.Groups["resto"].Value;
                resultado.Add(new Columna($"{esquema}.{tabla}", m.Groups["c"].Value, tipo, !resto.Contains("NOT NULL") && Regex.IsMatch(resto, @"\bNULL\b")));
            }
            return resultado;
        }

        // ── Arnés: las tablas posteriores a la salida de F1 ─────────────────────────────────────────────────────

        [Fact]
        public void Las_tablas_posteriores_a_la_salida_de_F1_declaran_su_script_y_no_estaban_en_la_base_del_2026_10_10()
        {
            var posteriores = ClasificacionDeTablas.PosterioresASalidaDeF1.ToList();
            Assert.NotEmpty(posteriores);

            foreach (var t in posteriores)
            {
                Assert.False(t.EnBaseReal, $"{t.Tabla} no existía el 2026-10-10 (EnBaseReal es una fecha fija); su script va en CreadaPorScript.");
                var archivos = Directory.GetFiles(Path.Combine(Raiz(), "2 - Script SQL"), t.CreadaPorScript + "_*.sql");
                Assert.True(archivos.Length == 1, $"{t.Tabla}: no hay un único script {t.CreadaPorScript}_*.sql.");
            }

            // Una tabla que no está en la base del 2026-10-10 ni en el modelo de EF solo puede venir de un script posterior.
            var huerfanas = ClasificacionDeTablas.Todas.Where(t => !t.EnBaseReal && !t.EnModeloEf && t.CreadaPorScript == null).Select(t => t.Tabla).ToList();
            Assert.True(huerfanas.Count == 0, "Tablas fuera de la base y de EF sin script que las cree: " + string.Join(", ", huerfanas));

            // El conjunto de la salida de F1 no cambia cuando llegan scripts nuevos: siguen siendo las 41 tablas con id_tenant.
            Assert.Equal(41, ClasificacionDeTablas.DeLaSalidaDeF1.Count(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria)));
            Assert.DoesNotContain(ClasificacionDeTablas.DeLaSalidaDeF1, t => t.CreadaPorScript != null);
        }

        [Fact]
        public void Accesos_empresa_es_tabla_de_tenant_y_las_otras_ocho_del_038_son_del_plano_de_control()
        {
            var del038 = ClasificacionDeTablas.PosterioresASalidaDeF1.Where(t => t.CreadaPorScript == "038").ToList();
            Assert.Equal(9, del038.Count);

            var accesos = Assert.Single(del038, t => t.Categoria == CategoriaTabla.Tenant);
            Assert.Equal("accesos_empresa", accesos.Tabla);
            Assert.All(del038.Where(t => t.Tabla != "accesos_empresa"), t => Assert.Equal(CategoriaTabla.Plataforma, t.Categoria));
        }

        // ── Script 038 ───────────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(S038)]
        [InlineData(S039)]
        [InlineData(EmergenciaMfa)]
        public void Los_scripts_de_F1_5_son_ASCII(string archivo) => Assert.DoesNotMatch(@"[^\x00-\x7F]", Script(archivo));

        [Theory]
        [InlineData(S038, "038")]
        [InlineData(S039, "039")]
        public void Cada_script_tiene_PRECHECK_CAMBIO_POSTCHECK_ROLLBACK_y_es_idempotente(string archivo, string numero)
        {
            var texto = Script(archivo);
            var cambio = Cambio(texto);

            Assert.Contains($"PRECHECK {numero}", cambio);
            Assert.Contains($"#precheck_{numero}", cambio);
            Assert.Contains($"IF OBJECT_ID(N'tempdb..#precheck_{numero}') IS NULL", cambio);   // el CAMBIO exige la marca del PRECHECK
            Assert.Contains("POSTCHECK", cambio);
            Assert.Contains("SET XACT_ABORT ON", cambio);
            Assert.Contains("BEGIN TRANSACTION", cambio);
            Assert.Contains("COMMIT TRANSACTION", cambio);
            Assert.Contains("BEGIN CATCH", cambio);
            Assert.Contains("ROLLBACK TRANSACTION", cambio);
            Assert.Contains($"THROW 50{numero}", cambio);
            Assert.Contains("Nada que hacer", cambio);

            var rollback = Rollback(texto);
            Assert.EndsWith("*/", rollback.TrimEnd());
            Assert.Contains("BEGIN TRANSACTION", rollback);
            Assert.Contains("COMMIT TRANSACTION", rollback);
            Assert.Contains("THROW 50" + numero, rollback);   // la guarda del ROLLBACK
            Assert.Contains("ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)", texto);
        }

        [Fact]
        public void El_038_crea_las_9_tablas_que_clasifica_el_arnes_cada_una_en_su_esquema()
        {
            var s038 = Script(S038);
            var creadas = TablasQueCrea(s038);

            var esperadas = ClasificacionDeTablas.PosterioresASalidaDeF1.Where(t => t.CreadaPorScript == "038")
                .Select(t => $"{(t.Categoria == CategoriaTabla.Tenant ? "dbo" : "saas")}.{t.Tabla}")
                .Order(StringComparer.Ordinal).ToList();

            Assert.Equal(9, creadas.Count);
            Assert.Equal(esperadas, creadas.Select(t => $"{t.Esquema}.{t.Tabla}").Order(StringComparer.Ordinal).ToList());
        }

        [Fact]
        public void El_ROLLBACK_del_038_borra_las_mismas_tablas_en_un_orden_que_respeta_las_claves_foraneas()
        {
            var s038 = Script(S038);
            var creadas = TablasQueCrea(s038).Select(t => $"{t.Esquema}.{t.Tabla}").ToList();
            var rollback = Rollback(s038);

            var borradas = Regex.Matches(rollback, @"DROP TABLE (?<t>(?:saas|dbo)\.\w+);").Select(m => m.Groups["t"].Value).ToList();
            Assert.Equal(creadas.Order(StringComparer.Ordinal).ToList(), borradas.Order(StringComparer.Ordinal).ToList());

            // Los hijos antes que los padres: accesos_empresa -> membresias -> cuentas.
            Assert.True(borradas.IndexOf("dbo.accesos_empresa") < borradas.IndexOf("saas.membresias"));
            Assert.True(borradas.IndexOf("saas.membresias") < borradas.IndexOf("saas.cuentas"));
            Assert.True(borradas.IndexOf("saas.invitaciones") < borradas.IndexOf("saas.cuentas"));
            Assert.True(borradas.IndexOf("saas.cuenta_alias") < borradas.IndexOf("saas.cuentas"));

            // Antes de borrar accesos_empresa se sacan sus 5 predicados de la política; la guarda exige las tablas vacías.
            Assert.Contains("DROP FILTER PREDICATE ON dbo.accesos_empresa", rollback);
            Assert.Equal(4, Regex.Matches(rollback, @"DROP BLOCK PREDICATE ON dbo\.accesos_empresa (AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE)").Count);
            Assert.True(rollback.IndexOf("DROP FILTER PREDICATE", StringComparison.Ordinal) < rollback.IndexOf("DROP TABLE dbo.accesos_empresa", StringComparison.Ordinal));
            Assert.Contains("p.rows > 0", rollback);
        }

        private static readonly Regex PredicadoDeLaPolitica = new(
            @"ADD (?<tipo>FILTER|BLOCK) PREDICATE seg\.(?<fn>\w+)\(id_tenant\) ON dbo\.(?<tabla>\w+)(?: (?<op>AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?",
            RegexOptions.Compiled);

        [Fact]
        public void Accesos_empresa_entra_a_la_politica_con_un_filtro_y_cuatro_bloqueos_de_fn_tenant_actual()
        {
            var s038 = Script(S038);
            var predicados = PredicadoDeLaPolitica.Matches(Cambio(s038))
                .Select(m => (Tipo: m.Groups["tipo"].Value, Funcion: m.Groups["fn"].Value, Tabla: m.Groups["tabla"].Value, Operacion: m.Groups["op"].Value)).ToList();

            Assert.Equal(5, predicados.Count);
            Assert.All(predicados, p => Assert.Equal("accesos_empresa", p.Tabla));
            Assert.All(predicados, p => Assert.Equal("fn_tenant_actual", p.Funcion));      // tabla de tenant: sin excepciones (ADR-015)
            Assert.Single(predicados, p => p.Tipo == "FILTER");
            Assert.Equal(new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE DELETE", "BEFORE UPDATE" },
                         predicados.Where(p => p.Tipo == "BLOCK").Select(p => p.Operacion).Order(StringComparer.Ordinal).ToArray());

            // 199 predicados del 036 + 5 = 204, y las tablas de la política son las que clasifica el arnés (incluida accesos_empresa).
            var s036 = Script(S036);
            var de036 = PredicadoDeLaPolitica.Matches(s036).Select(m => m.Groups["tabla"].Value).ToList();
            Assert.Equal(199, de036.Count);
            Assert.Contains("199 -> 204", s038);   // el encabezado nombra las cifras
            Assert.Contains("<> 204", s038);       // y el POSTCHECK las exige

            var enLaPolitica = de036.Concat(predicados.Select(p => p.Tabla)).Distinct().Order(StringComparer.Ordinal).ToList();
            var clasificadas = ClasificacionDeTablas.Todas
                .Where(t => ClasificacionDeTablas.LlevaIdTenant(t.Categoria) && (t.EnBaseReal || t.CreadaPorScript != null) && t.Tabla != "domain_events")
                .Select(t => t.Tabla).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(clasificadas, enLaPolitica);
            Assert.Equal(41, enLaPolitica.Count);
        }

        [Fact]
        public void El_038_deja_la_politica_como_la_encontro_y_ninguna_tabla_de_saas_entra_en_ella()
        {
            var cambio = Cambio(Script(S038));

            Assert.DoesNotContain("STATE =", Codigo(cambio));                    // ni la enciende ni la apaga
            Assert.Contains("@estabaEncendida", cambio);                          // y el POSTCHECK lo comprueba
            Assert.DoesNotMatch(@"PREDICATE seg\.\w+\(id_tenant\) ON saas\.", cambio);   // el plano de control no lleva RLS (ADR-013, D19)
        }

        [Fact]
        public void Las_listas_de_columnas_y_restricciones_del_038_coinciden_con_su_CREATE_TABLE()
        {
            var s038 = Script(S038);
            var listaColumnas = ListaDeColumnasDel038(s038);
            var listaObjetos = ListaDeObjetosDel038(s038);

            Assert.Equal(83, listaColumnas.Count);
            Assert.Equal(60, listaObjetos.Count);

            var creadas = TablasQueCrea(s038);
            var delCreate = creadas.SelectMany(t => ColumnasDelCreate(t.Esquema, t.Tabla, t.Cuerpo)).ToList();
            var ordenado = (IEnumerable<Columna> l) => l.OrderBy(c => c.Tabla, StringComparer.Ordinal).ThenBy(c => c.Nombre, StringComparer.Ordinal).ToList();
            Assert.Equal(ordenado(delCreate), ordenado(listaColumnas));

            // Restricciones (CONSTRAINT dentro del CREATE TABLE) e indices (CREATE INDEX) por tabla.
            var nombresDelCreate = creadas
                .SelectMany(t => Regex.Matches(t.Cuerpo, @"CONSTRAINT (\w+)").Select(m => (Tabla: $"{t.Esquema}.{t.Tabla}", Nombre: m.Groups[1].Value)))
                .Concat(Regex.Matches(Cambio(s038), @"CREATE (?:UNIQUE )?INDEX (?<n>\w+) ON (?<t>(?:saas|dbo)\.\w+)")
                    .Select(m => (Tabla: m.Groups["t"].Value, Nombre: m.Groups["n"].Value)))
                .OrderBy(x => x.Tabla, StringComparer.Ordinal).ThenBy(x => x.Nombre, StringComparer.Ordinal).ToList();
            var nombresDeLaLista = listaObjetos.Select(o => (o.Tabla, o.Nombre))
                .OrderBy(x => x.Tabla, StringComparer.Ordinal).ThenBy(x => x.Nombre, StringComparer.Ordinal).ToList();
            Assert.Equal(nombresDelCreate, nombresDeLaLista);

            // Los tipos de la lista son coherentes con el prefijo del nombre: UX_ unico, IX_ no, FK_/FKT_ clave foranea, etc.
            foreach (var o in listaObjetos)
            {
                var esperado = o.Nombre switch
                {
                    var n when n.StartsWith("PK_") => "PK",
                    var n when n.StartsWith("CK_") => "C",
                    var n when n.StartsWith("DF_") => "D",
                    var n when n.StartsWith("FK_") || n.StartsWith("FKT_") => "F",
                    "UX_cuentas_uid_publico" => "UQ",
                    var n when n.StartsWith("UX_") => "UI",
                    var n when n.StartsWith("IX_") => "IX",
                    _ => "?"
                };
                Assert.True(esperado == o.Tipo, $"{o.Nombre} esta listada como {o.Tipo} y por su nombre deberia ser {esperado}.");
            }

            // 13 claves foraneas nuevas: el 038 suma 13 a las 152 de la base (165).
            Assert.Equal(13, listaObjetos.Count(o => o.Tipo == "F"));
            Assert.Contains("+ 13", s038);
        }

        [Fact]
        public void Las_claves_foraneas_del_038_hacia_tablas_de_tenant_son_compuestas_con_id_tenant_primero()
        {
            var cambio = Cambio(Script(S038));

            var compuestas = Regex.Matches(cambio,
                    @"CONSTRAINT (?<nombre>\w+) FOREIGN KEY \(id_tenant, (?<cols>\w+)\) REFERENCES (?<padre>\w+\.\w+) \(id_tenant, (?<pcols>\w+)\)")
                .Select(m => (Nombre: m.Groups["nombre"].Value, Padre: m.Groups["padre"].Value)).ToList();

            Assert.All(compuestas, c => Assert.StartsWith("FKT_", c.Nombre));
            Assert.Equal(new[] { "dbo.empresa_roles", "dbo.empresas", "dbo.personas", "saas.membresias" }, compuestas.Select(c => c.Padre).Order(StringComparer.Ordinal).ToArray());

            // Toda clave hacia una tabla de tenant (empresas, personas, roles) o hacia membresias lleva id_tenant: ninguna FK_ simple las apunta.
            var simples = Regex.Matches(cambio, @"CONSTRAINT (?<nombre>FK_\w+) FOREIGN KEY \((?<cols>[\w, ]+)\) REFERENCES (?<padre>\w+\.\w+)")
                .Select(m => (Nombre: m.Groups["nombre"].Value, Padre: m.Groups["padre"].Value)).ToList();
            Assert.All(simples, s => Assert.True(s.Padre is "saas.tenants" or "saas.cuentas",
                $"{s.Nombre} apunta a {s.Padre} con clave simple: hacia una tabla de tenant debe ser compuesta (id_tenant, ...)."));

            // accesos_empresa lleva su propio id_tenant obligatorio, su clave hacia saas.tenants y su indice, como las demas tablas de tenant.
            var accesos = TablasQueCrea(Script(S038)).Single(t => t.Tabla == "accesos_empresa").Cuerpo;
            Assert.Matches(@"id_tenant\s+INT\s+NOT NULL", accesos);
            Assert.Contains("FK_accesos_empresa_tenants", accesos);
            Assert.Contains("CREATE INDEX IX_accesos_empresa_tenant ON dbo.accesos_empresa (id_tenant)", cambio);
        }

        [Fact]
        public void El_038_no_toca_ninguna_tabla_existente_ni_escribe_datos()
        {
            var codigo = Codigo(Cambio(Script(S038)));

            // Las tablas temporales (#columnas_038, #precheck_038) son de la sesion y no cuentan como escribir datos.
            // "AFTER INSERT", "BEFORE UPDATE", etc. son los tipos de predicado de bloqueo de la politica, no sentencias.
            var sinTemporales = Regex.Replace(codigo.Replace("ON DELETE CASCADE", ""), @"INSERT\s+INTO\s+#\w+|(?:AFTER|BEFORE)\s+(?:INSERT|UPDATE|DELETE)", "");
            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\b", sinTemporales);
            Assert.DoesNotMatch(@"ALTER TABLE", codigo);          // solo agrega tablas; la unica sentencia ALTER es la de la politica
            Assert.DoesNotMatch(@"DROP\s", Regex.Replace(codigo, @"DROP\s+TABLE\s+#\w+", ""));   // solo se borran las temporales de la sesion
            Assert.Single(Regex.Matches(codigo, @"ALTER SECURITY POLICY seg\.pol_tenant"));
        }

        // ── Verificaciones de solo lectura de F1.5 ──────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(Verif038)]
        [InlineData(VerifF15)]
        public void Las_listas_de_las_verificaciones_de_F1_5_son_las_del_038(string archivo)
        {
            var s038 = Script(S038);
            var verificador = Operacion(archivo);

            // Las mismas filas, en el mismo orden, que el PRECHECK/POSTCHECK del 038.
            Assert.Equal(ListaDeColumnasDel038(s038), ColumnasDeLaLista(verificador));
            Assert.Equal(ListaDeObjetosDel038(s038), ObjetosDeLaLista(verificador));

            // Columnas por tabla y claves foraneas: las cifras que el verificador espera salen de las listas.
            var columnas = ListaDeColumnasDel038(s038);
            foreach (var m in Regex.Matches(verificador, @"\((?<n>\d), N'(?<esq>saas|dbo)', N'(?<tabla>\w+)', (?<ncols>\d+)\)").Cast<Match>())
            {
                var completa = $"{m.Groups["esq"].Value}.{m.Groups["tabla"].Value}";
                Assert.Equal(columnas.Count(c => c.Tabla == completa), int.Parse(m.Groups["ncols"].Value));
            }
            Assert.Equal(9, Regex.Matches(verificador, @"\(\d, N'(?:saas|dbo)', N'\w+', \d+\)").Count);

            var objetos = ListaDeObjetosDel038(s038);
            Assert.Contains($"N'{objetos.Count} de {objetos.Count}'", verificador);
            Assert.Contains($"N'{objetos.Count(o => o.Tipo == "F")} de {objetos.Count(o => o.Tipo == "F")}'", verificador);
            Assert.Contains("N'204'", verificador);    // 199 + 5 predicados
            Assert.Contains("CAST(165 + 5 * c.con040", verificador);    // 152 + 13 claves foraneas (+5 si ya esta el 040)
            Assert.Contains("N'57'", verificador);     // 56 + accesos_empresa tablas de dbo
        }

        private static int Esperado(string verificador, int orden)
        {
            var m = Regex.Match(verificador, $@"\({orden}, N'(?:[^']|'')*', N'(?<v>\d+)'");
            Assert.True(m.Success, $"El verificador no tiene la fila {orden}.");
            return int.Parse(m.Groups["v"].Value);
        }

        [Fact]
        public void Las_cifras_de_verificar_despues_f15_son_las_de_las_reglas_del_039()
        {
            var s039 = Script(S039);
            var verificador = Operacion(VerifF15);

            string Valores(string tabla) => Entre(s039, $"INSERT INTO #{tabla}_039", ";");
            var cuentas = Regex.Matches(Valores("cuentas"), @"\((?<id>\d+)\s*,\s*'(?<estado>\w+)'\s*,\s*(?<op>[01])\)").Cast<Match>().ToList();
            var alias = Regex.Matches(Valores("alias"), @"\((?<u>\d+)\s*,\s*(?<c>\d+)\s*,\s*'(?<tipo>\w+)'\s*,\s*(?<puede>[01])\)").Cast<Match>().ToList();
            var membresias = Regex.Matches(Valores("membresias"), @"\((?<c>\d+)\s*,\s*'(?<t>\w+)'\s*,\s*(?<p>NULL|\d+)\s*,\s*'(?<estado>\w+)'\)").Cast<Match>().ToList();
            var accesos = Regex.Matches(Valores("accesos"), @"\((?<o>\d+)\s*,\s*(?<c>\d+)\s*,\s*'(?<t>\w+)'\s*,\s*(?<activo>[01])\)").Cast<Match>().ToList();

            Assert.Equal(6, cuentas.Count);
            Assert.Equal(4, alias.Count);
            Assert.Equal(5, membresias.Count);
            Assert.Equal(5, accesos.Count);

            // Las cifras del verificador salen de las reglas, no de otra parte.
            Assert.Equal(cuentas.Count, Esperado(verificador, 610));
            Assert.Equal(cuentas.Count(c => c.Groups["estado"].Value == "activa"), Esperado(verificador, 620));
            Assert.Equal(cuentas.Count(c => c.Groups["estado"].Value == "inactiva"), Esperado(verificador, 630));
            Assert.Equal(cuentas.Count(c => c.Groups["op"].Value == "1"), Esperado(verificador, 640));
            Assert.Equal(alias.Count, Esperado(verificador, 670));
            Assert.Equal(alias.Count(a => a.Groups["tipo"].Value == "usuario" && a.Groups["puede"].Value == "1"), Esperado(verificador, 680));
            Assert.Equal(alias.Count(a => a.Groups["tipo"].Value == "correo" && a.Groups["puede"].Value == "0"), Esperado(verificador, 690));
            Assert.Equal(membresias.Count, Esperado(verificador, 700));
            Assert.Equal(membresias.Count(m => m.Groups["estado"].Value == "activa"), Esperado(verificador, 710));
            Assert.Equal(membresias.Count(m => m.Groups["estado"].Value == "suspendida"), Esperado(verificador, 720));
            Assert.Equal(membresias.Count, Esperado(verificador, 730));
            Assert.Equal(accesos.Count, Esperado(verificador, 750));

            // El POSTCHECK del 039 lleva las mismas cifras.
            Assert.Matches(@"@malas <> 6 THROW", s039);
            Assert.Matches(@"@malas <> 4 THROW", s039);
            Assert.Matches(@"@malas <> 5 THROW", s039);
            Assert.Contains("@accesosTotales <> 5", s039);
        }

        // ── Script 039 ───────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_039_espera_exactamente_los_8_usuarios_de_produccion_con_sus_Id()
        {
            var s039 = Script(S039);
            var filas = Regex.Matches(Entre(s039, "INSERT INTO #users_039", ";"),
                    @"\((?<id>\d+),\s+N'(?<nombre>[^']+)',\s+(?<empresa>NULL|\d+),\s+(?<rol>NULL|\d+),\s+(?<persona>NULL|\d+),\s+N'(?<texto>[^']+)',\s+(?<activo>NULL|[01])\)")
                .Select(m => (Id: int.Parse(m.Groups["id"].Value), Nombre: m.Groups["nombre"].Value, Empresa: m.Groups["empresa"].Value,
                              Rol: m.Groups["rol"].Value, Persona: m.Groups["persona"].Value, Texto: m.Groups["texto"].Value)).ToList();

            Assert.Equal(new[] { 1, 2, 4, 5, 6, 7, 8, 10 }, filas.Select(f => f.Id).ToArray());
            Assert.Equal(new[] { "admin", "cliente_demo", "egaray", "nnolasco", "Moran23113", "transgar.admin", "transgar.flota", "egaray.transgar" },
                         filas.Select(f => f.Nombre).ToArray());

            // Las caracteristicas medidas en produccion (2026-10-15).
            Assert.Equal(("NULL", "NULL", "102", "admin"), (filas[0].Empresa, filas[0].Rol, filas[0].Persona, filas[0].Texto));     // admin
            Assert.Equal(("2", "1", "103", "empresa_user"), (filas[1].Empresa, filas[1].Rol, filas[1].Persona, filas[1].Texto));    // cliente_demo
            Assert.Equal(("1", "2", "102", "empresa_user"), (filas[2].Empresa, filas[2].Rol, filas[2].Persona, filas[2].Texto));    // egaray
            Assert.Equal(("4", "5", "104", "empresa_user"), (filas[7].Empresa, filas[7].Rol, filas[7].Persona, filas[7].Texto));    // egaray.transgar
            Assert.All(new[] { 3, 4 }, i => Assert.Equal(("NULL", "NULL", "NULL"), (filas[i].Empresa, filas[i].Rol, filas[i].Persona)));   // nnolasco, Moran23113

            // D45: admin, egaray y egaray.transgar son la misma persona -> una sola cuenta de operador; D24: las demas, inactivas.
            Assert.Contains("(1, 'activa', 1)", s039);
            Assert.Matches(@"\(2, 'activa', 0\)", s039);
            foreach (var id in new[] { 5, 6, 7, 8 }) Assert.Contains($"({id}, 'inactiva', 0)", s039);
            Assert.DoesNotMatch(@"\((4|10), '(activa|inactiva)', [01]\)", Entre(s039, "INSERT INTO #cuentas_039", ";"));
        }

        [Fact]
        public void El_039_busca_los_tenants_por_codigo_y_no_escribe_en_dbo_Users()
        {
            var s039 = Script(S039);
            var codigo = Codigo(Cambio(s039));

            // Tenants por codigo (SIP, DEMO, TRANSGAR), nunca por id fijo.
            foreach (var cod in new[] { "'SIP'", "'DEMO'", "'TRANSGAR'" }) Assert.Contains(cod, codigo);
            Assert.DoesNotMatch(@"id_tenant\s*=\s*\d", codigo);
            Assert.DoesNotMatch(@"id_tenant\s+IN\s*\(\s*\d", codigo);

            // dbo.Users se lee, nunca se modifica ni se borra.
            Assert.DoesNotMatch(@"(INSERT\s+INTO|UPDATE|DELETE(?:\s+FROM)?|MERGE(?:\s+INTO)?|TRUNCATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE)\s+dbo\.Users", codigo);
            Assert.DoesNotMatch(@"(?i)\bDROP\s+(?!TABLE\s+#)", codigo);   // solo se limpian tablas temporales (#...)
            Assert.Contains("CHECKSUM_AGG", codigo);            // el POSTCHECK compara una suma de control de Users antes y despues
        }

        [Fact]
        public void El_039_fija_el_contexto_de_cada_tenant_sin_solo_lectura_y_lo_restaura_aunque_falle()
        {
            var s039 = Script(S039);
            var codigo = Codigo(s039);

            Assert.Contains("sp_set_session_context", codigo);
            Assert.DoesNotMatch(@"@read_only\s*=\s*1", codigo);   // con solo lectura la sesion no podria cambiar de tenant
            Assert.Contains("@ctxOriginal", codigo);

            var cambio = Codigo(Entre(s039, "-- CAMBIO (una transaccion)", "-- Resultado:"));
            var catchDelCambio = cambio[cambio.IndexOf("BEGIN CATCH", StringComparison.Ordinal)..];   // el CATCH externo del CAMBIO
            Assert.Contains("sp_set_session_context", catchDelCambio);   // el contexto no es transaccional: se devuelve tambien al fallar
        }

        [Fact]
        public void El_039_no_imprime_ni_registra_claves_ni_correos()
        {
            var s039 = Script(S039);
            var codigo = Codigo(s039);

            // Ni los PRINT ni los mensajes de error del PRECHECK usan la clave ni el correo; el resultado final tampoco los selecciona.
            foreach (var linea in codigo.Split('\n').Where(l => l.Contains("PRINT") || l.Contains("@errores +=") || l.Contains("@mensaje") || l.Contains("@msg")))
                Assert.DoesNotMatch(@"(?i)Password|hash_clave|\bEmail\b|\bcorreo\s*\)|\.alias\b", linea);

            var resultado = Codigo(Entre(s039, "-- Resultado:", "-- ROLLBACK"));
            Assert.DoesNotMatch(@"(?i)Password|hash_clave|\bEmail\b|\bc\.correo\b", resultado);
        }

        [Fact]
        public void El_ROLLBACK_del_039_borra_solo_lo_que_creo_el_script_en_el_orden_de_las_claves_y_con_guarda()
        {
            var rollback = Rollback(Script(S039));

            Assert.Equal(4, Regex.Matches(rollback, @"DELETE FROM (?:dbo\.accesos_empresa|saas\.membresias|saas\.cuenta_alias|saas\.cuentas) WHERE creado_por = N'script:039'").Count);
            Assert.True(rollback.IndexOf("DELETE FROM dbo.accesos_empresa", StringComparison.Ordinal) < rollback.IndexOf("DELETE FROM saas.membresias", StringComparison.Ordinal));
            Assert.True(rollback.IndexOf("DELETE FROM saas.membresias", StringComparison.Ordinal) < rollback.IndexOf("DELETE FROM saas.cuenta_alias", StringComparison.Ordinal));
            Assert.True(rollback.IndexOf("DELETE FROM saas.cuenta_alias", StringComparison.Ordinal) < rollback.IndexOf("DELETE FROM saas.cuentas", StringComparison.Ordinal));
            Assert.DoesNotContain("dbo.Users", rollback);                     // dbo.Users no se toca
            Assert.Contains("sp_set_session_context", rollback);              // los accesos son de tenant: se borran con el contexto de cada uno
            foreach (var tabla in new[] { "cuenta_mfa", "cuenta_intentos", "cuenta_mfa_recuperacion", "invitaciones" })
                Assert.Contains($"saas.{tabla}", rollback);                    // la guarda mira lo que la aplicacion pudo escribir despues
        }

        // ── Emergencia del MFA ───────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void La_emergencia_del_MFA_obliga_a_editar_el_correo_y_solo_borra_el_MFA_de_una_cuenta()
        {
            var texto = Script(EmergenciaMfa);
            var codigo = Codigo(texto);

            // Guarda: con el valor de ejemplo no hace nada.
            var declaracion = codigo.IndexOf("DECLARE @correo", StringComparison.Ordinal);
            var guarda = codigo.IndexOf("IF @correo = N'EDITAR-ESTE-CORREO'", StringComparison.Ordinal);
            Assert.True(declaracion >= 0 && guarda > declaracion, "La guarda tiene que ir justo despues de declarar @correo.");
            Assert.Matches(@"DECLARE @correo NVARCHAR\(256\) = N'EDITAR-ESTE-CORREO';", codigo);
            Assert.Matches(@"IF @correo = N'EDITAR-ESTE-CORREO'\s+THROW 50090", codigo);
            Assert.True(guarda < codigo.IndexOf("DELETE", StringComparison.Ordinal), "La guarda va antes de cualquier DELETE.");

            // Solo MFA de UNA cuenta: nunca borra la cuenta ni usa DROP/TRUNCATE; cada DELETE tiene su WHERE por id_cuenta.
            Assert.DoesNotMatch(@"\b(DROP|TRUNCATE|MERGE|INSERT)\b", codigo);
            Assert.DoesNotMatch(@"DELETE FROM saas\.cuentas\b", codigo);
            var deletes = Regex.Matches(codigo, @"DELETE FROM (?<t>[\w.]+)(?<resto>[^;]*);").Cast<Match>().ToList();
            Assert.Equal(new[] { "saas.cuenta_mfa", "saas.cuenta_mfa_recuperacion" }, deletes.Select(d => d.Groups["t"].Value).Order(StringComparer.Ordinal).ToArray());
            Assert.All(deletes, d => Assert.Contains("WHERE id_cuenta = @id_cuenta", d.Groups["resto"].Value));
            Assert.Matches(@"UPDATE saas\.cuentas SET modificado_por = N'emergencia:mfa'[^;]*WHERE id_cuenta = @id_cuenta;", codigo);

            // Idempotente y sin dependencias: avisa y sale si no hay cuenta, tablas o MFA; como emergencia_rls_apagar.sql, sin PRECHECK.
            Assert.Contains("Nada que hacer", texto);
            Assert.DoesNotContain("PRECHECK", texto.Replace("sin PRECHECK", ""));
            Assert.Contains("BEGIN TRANSACTION", codigo);
            Assert.Contains("COMMIT TRANSACTION", codigo);
        }

        // ── Sin secretos ni datos personales en los archivos nuevos ─────────────────────────────────────────────

        [Fact]
        public void Los_archivos_de_F1_5_no_llevan_hashes_correos_ni_cadenas_de_conexion()
        {
            var archivos = new Dictionary<string, string>
            {
                [S038] = Script(S038),
                [S039] = Script(S039),
                [EmergenciaMfa] = Script(EmergenciaMfa),
                [Verif038] = Operacion(Verif038),
                [VerifF15] = Operacion(VerifF15),
                ["LEEME_F15.md"] = Operacion("LEEME_F15.md")
            };

            foreach (var (nombre, texto) in archivos)
            {
                Assert.DoesNotMatch(@"\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{40,}", texto);               // forma de un hash BCrypt
                Assert.DoesNotMatch(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+\.[A-Za-z]{2,}", texto);     // un correo (las variables @x no llevan dominio)
                Assert.DoesNotMatch(@"(?i)(password|pwd)\s*=\s*[^\s;]", texto);                     // Password=... de una cadena de conexion
                Assert.DoesNotMatch(@"(?i)(server|data source|user id|connectionstring)\s*=", texto);
                Assert.True(!Regex.IsMatch(texto, @"(?i)\b(api[_-]?key|secret[_-]?key|bearer)\b"), nombre + " parece llevar una clave.");
            }
        }

        // ── Documentacion ────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_indice_de_scripts_y_el_LEEME_de_F1_5_nombran_los_archivos_nuevos()
        {
            var indice = File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "INDICE_SCRIPTS_SQL.md"), Encoding.UTF8);
            Assert.Matches(@"\| 038 \| 038_identidad_global\.sql \|[^\r\n]*\| Aplicado 2026-10-30 \|", indice);
            Assert.Matches(@"\| 039 \| 039_migrar_cuentas\.sql \|[^\r\n]*\| Aplicado 2026-10-30 \|", indice);
            Assert.Contains("emergencia_mfa_desactivar.sql", indice);
            Assert.Contains(Verif038, indice);
            Assert.Contains(VerifF15, indice);

            var leeme = Operacion("LEEME_F15.md");
            foreach (var archivo in new[] { S038, S039, EmergenciaMfa, Verif038, VerifF15 })
                Assert.Contains(archivo, leeme);
        }
    }
}
