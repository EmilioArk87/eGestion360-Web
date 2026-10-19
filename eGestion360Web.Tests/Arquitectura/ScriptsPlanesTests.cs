using System.Text;
using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.7: el script 040 crea en el esquema <c>saas</c> los planes, sus versiones, los derechos de uso y la asignación de plan de
    /// cada tenant, agrega <c>saas.tenants.modo_suspension</c> y la regla de que el tenant interno no se suspende ni se cancela. Estas
    /// pruebas mantienen ordenados el script, su verificador de solo lectura (<c>verificar_despues_f17.sql</c>), el script de emergencia
    /// (<c>emergencia_derechos_abrir.sql</c>), la documentación y el arnés de aislamiento: una lista que se desvíe de la otra falla aquí.
    /// No necesitan base de datos; el comportamiento (aplicar, repetir, revertir, abortar, la política RLS encendida y apagada) se ensayó
    /// contra una copia real en LocalDB y está en <c>1 - Documetacion/Operacion/F1/LEEME_F17.md</c>.
    /// </summary>
    public class ScriptsPlanesTests
    {
        private static readonly string[] TablasNuevas =
            { "planes", "plan_versiones", "derechos", "plan_version_derechos", "tenant_planes" };

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string LeerScript(string patron)
            => File.ReadAllText(Directory.GetFiles(Path.Combine(Raiz(), "2 - Script SQL"), patron).Single(), Encoding.UTF8);

        private static string S040() => LeerScript("040_*.sql");
        private static string Emergencia() => LeerScript("emergencia_derechos_abrir.sql");

        private static string Verificacion()
            => File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", "verificar_despues_f17.sql"), Encoding.UTF8);

        private static string Leeme()
            => File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", "LEEME_F17.md"), Encoding.UTF8);

        private static string Indice()
            => File.ReadAllText(Path.Combine(Raiz(), "1 - Documetacion", "INDICE_SCRIPTS_SQL.md"), Encoding.UTF8);

        /// <summary>El código ejecutable: sin comentarios <c>--</c> ni <c>/* */</c>.</summary>
        private static string SinComentarios(string sql)
        {
            var t = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ");
            return Regex.Replace(t, @"--[^\r\n]*", " ");
        }

        /// <summary>El script sin la sección ROLLBACK (el último bloque comentado).</summary>
        private static string SinRollback(string sql) => sql[..sql.LastIndexOf("/*", StringComparison.Ordinal)];

        private static string Rollback(string sql) => sql[sql.LastIndexOf("/*", StringComparison.Ordinal)..];

        // ── Forma de los archivos ─────────────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("040_planes_derechos_estado_tenant.sql")]
        [InlineData("emergencia_derechos_abrir.sql")]
        public void Los_scripts_de_planes_son_ASCII(string archivo)
            => Assert.DoesNotMatch(@"[^\x00-\x7F]", File.ReadAllText(Path.Combine(Raiz(), "2 - Script SQL", archivo), Encoding.UTF8));

        [Fact]
        public void El_verificador_de_F1_7_es_ASCII()
            => Assert.DoesNotMatch(@"[^\x00-\x7F]", Verificacion());

        [Fact]
        public void El_script_040_sigue_el_patron_PRECHECK_CAMBIO_POSTCHECK_ROLLBACK()
        {
            var s = S040();

            Assert.Contains("SET XACT_ABORT ON;", s);
            Assert.Contains("SET QUOTED_IDENTIFIER ON;", s);   // saas.tenants y tenant_planes llevan indices filtrados

            // La marca del PRECHECK se crea al final de su lote y el CAMBIO la exige.
            Assert.Contains("CREATE TABLE #precheck_040 (ok BIT NOT NULL);", s);
            Assert.Contains("IF OBJECT_ID(N'tempdb..#precheck_040') IS NULL", s);
            Assert.Contains("Falta la marca del PRECHECK", s);

            var precheck = s.IndexOf("PRECHECK 040 no paso", StringComparison.Ordinal);
            var inicioTransaccion = s.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal);
            var postcheck = s.IndexOf("4. POSTCHECK", StringComparison.Ordinal);
            var commit = s.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal);
            var rollback = s.LastIndexOf("/*", StringComparison.Ordinal);

            Assert.True(precheck > 0 && precheck < inicioTransaccion, "El PRECHECK va antes de la transacción.");
            Assert.True(inicioTransaccion < postcheck && postcheck < commit, "El POSTCHECK va dentro de la transacción, antes del COMMIT.");
            Assert.True(commit < rollback, "La sección ROLLBACK va al final.");
            Assert.True(s.IndexOf("/*", StringComparison.Ordinal) == rollback, "El único bloque /* */ del script es el ROLLBACK.");
            Assert.True(s.TrimEnd().EndsWith("*/", StringComparison.Ordinal), "La sección ROLLBACK cierra el archivo.");

            Assert.Contains("THROW 50040,", s);
            Assert.Contains("BEGIN CATCH", s);
            Assert.Contains("ROLLBACK TRANSACTION;", SinRollback(s));
        }

        [Fact]
        public void El_script_crea_solo_las_cinco_tablas_de_saas_y_el_ROLLBACK_las_quita_hijos_primero()
        {
            var s = S040();
            var creadas = Regex.Matches(SinRollback(s), @"CREATE TABLE (?<esquema>\w+)\.(?<tabla>\w+)")
                .Select(m => (m.Groups["esquema"].Value, m.Groups["tabla"].Value)).ToList();

            Assert.Equal(TablasNuevas.Length, creadas.Count);
            Assert.All(creadas, c => Assert.Equal("saas", c.Item1));   // plano de control, ADR-013
            Assert.Equal(TablasNuevas.Order(), creadas.Select(c => c.Item2).Order());

            var rollback = Rollback(s);
            var posiciones = TablasNuevas.ToDictionary(t => t, t => rollback.IndexOf($"DROP TABLE saas.{t};", StringComparison.Ordinal));
            Assert.All(posiciones, p => Assert.True(p.Value > 0, $"El ROLLBACK no quita saas.{p.Key}."));

            // Una tabla se quita antes que las tablas a las que apunta.
            void Antes(string hija, string padre) => Assert.True(posiciones[hija] < posiciones[padre], $"{hija} debe quitarse antes que {padre}.");
            Antes("tenant_planes", "plan_versiones");
            Antes("plan_version_derechos", "plan_versiones");
            Antes("plan_version_derechos", "derechos");
            Antes("plan_versiones", "planes");

            // ...y lo que se agrega a saas.tenants se quita también.
            foreach (var nombre in new[] { "CK_tenants_interno_activo", "CK_tenants_modo_suspension", "DF_tenants_modo_suspension" })
                Assert.Contains($"DROP CONSTRAINT {nombre};", rollback);
            Assert.Contains("ALTER TABLE saas.tenants DROP COLUMN modo_suspension;", rollback);
        }

        [Fact]
        public void El_ROLLBACK_se_detiene_si_hay_asignaciones_que_no_son_de_este_script()
        {
            var rollback = Rollback(S040());
            Assert.Contains("motivo <> N'script:040'", rollback);
            Assert.True(rollback.IndexOf("THROW 50040", StringComparison.Ordinal) < rollback.IndexOf("DROP TABLE", StringComparison.Ordinal));
        }

        [Fact]
        public void Cada_tabla_nueva_lleva_la_auditoria_de_saas_tenants_y_los_nombres_siguen_la_convencion()
        {
            var cambio = SinRollback(S040());
            var tablas = Regex.Matches(cambio, @"CREATE TABLE saas\.(?<t>\w+) \((?<cuerpo>[\s\S]*?)\r?\n        \);");
            Assert.Equal(TablasNuevas.Length, tablas.Count);

            foreach (Match tabla in tablas)
            {
                var cuerpo = tabla.Groups["cuerpo"].Value;
                var nombre = tabla.Groups["t"].Value;
                foreach (var columna in new[] { "creado_por", "fecha_creacion", "modificado_por", "fecha_modificacion" })
                    Assert.True(cuerpo.Contains(columna, StringComparison.Ordinal), $"saas.{nombre} no lleva {columna}.");
                Assert.Matches(@"^[a-z]+(_[a-z]+)*$", nombre);
            }

            // Toda restriccion lleva nombre propio con su prefijo (ESTANDARES_ERP §5).
            var restricciones = Regex.Matches(cambio, @"CONSTRAINT (?<n>\w+) (?<tipo>PRIMARY KEY|FOREIGN KEY|UNIQUE|CHECK|DEFAULT)")
                .Select(m => (Nombre: m.Groups["n"].Value, Tipo: m.Groups["tipo"].Value)).ToList();
            Assert.True(restricciones.Count >= 25, $"Se esperaban al menos 25 restricciones con nombre y hay {restricciones.Count}.");
            foreach (var (nombre, tipo) in restricciones)
            {
                var prefijo = tipo switch { "PRIMARY KEY" => "PK_", "FOREIGN KEY" => "FK_", "UNIQUE" => "UX_", "CHECK" => "CK_", _ => "DF_" };
                Assert.True(nombre.StartsWith(prefijo, StringComparison.Ordinal), $"{nombre} es {tipo}: debe empezar por {prefijo}.");
            }

            // Ninguna clave de las tablas nuevas sin nombre (las tablas temporales del PRECHECK quedan fuera: empiezan antes de la transaccion).
            var soloTablas = cambio[cambio.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal)..];
            Assert.DoesNotMatch(@"(?<!CONSTRAINT \w+ )(PRIMARY KEY|FOREIGN KEY)", soloTablas);
            foreach (Match indice in Regex.Matches(cambio, @"CREATE (UNIQUE )?INDEX (?<n>\w+)"))
                Assert.Matches(@"^(UX|IX)_", indice.Groups["n"].Value);
        }

        [Fact]
        public void El_plano_de_control_no_depende_del_ERP_ni_lleva_RLS()
        {
            var cambio = SinComentarios(SinRollback(S040()));

            Assert.DoesNotContain("REFERENCES dbo.", cambio);                   // ADR-006: saas no apunta a tablas de un tenant
            Assert.DoesNotContain("SECURITY POLICY", cambio);                   // ADR-013: el plano de control no lleva RLS
            Assert.DoesNotContain("PREDICATE", cambio);
            foreach (var tabla in TablasNuevas)
                Assert.DoesNotContain($"ALTER TABLE dbo.{tabla}", cambio);

            // Solo escribe en saas: el unico DML sobre dbo es la lectura de modulos, empresas y usuarios.
            Assert.DoesNotMatch(@"\b(INSERT INTO|UPDATE|DELETE FROM|ALTER TABLE|DROP TABLE)\s+dbo\.", cambio);
        }

        // ── Reglas de integridad ──────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_tenant_interno_no_se_suspende_y_la_suspension_es_de_solo_lectura_salvo_que_se_elija_el_bloqueo()
        {
            var s = SinRollback(S040());

            Assert.Contains("CK_tenants_interno_activo", s);
            Assert.Contains("CHECK (tipo <> ''interno'' OR estado = ''activo'')", s);
            Assert.Contains("ADD modo_suspension VARCHAR(10) NOT NULL", s);
            Assert.Contains("DF_tenants_modo_suspension DEFAULT ''lectura''", s);
            Assert.Contains("CHECK (modo_suspension IN (''lectura'', ''bloqueo''))", s);

            // Un solo plan vigente por tenant y valor_limite solo para los derechos de tipo limite.
            Assert.Contains("CREATE UNIQUE INDEX UX_tenant_planes_vigente ON saas.tenant_planes (id_tenant) WHERE vigente_hasta IS NULL;", s);
            Assert.Contains("CHECK (valor_limite IS NULL OR (tipo_derecho = 'limite' AND valor_limite >= 0))", s);
            Assert.Contains("FOREIGN KEY (id_derecho, tipo_derecho)", s);
            Assert.Contains("CONSTRAINT UX_derechos_id_tipo UNIQUE (id_derecho, tipo)", s);
            Assert.Contains("CHECK (tipo IN ('modulo', 'caracteristica', 'limite'))", s);
        }

        [Fact]
        public void Con_la_politica_encendida_el_PRECHECK_y_el_POSTCHECK_leen_los_modulos_tenant_por_tenant()
        {
            // Con seg.pol_tenant encendida, dbo.empresa_modulos no devuelve filas a una sesion sin tenant: leerla sin fijar el contexto
            // haria que "nadie pierde acceso" pasara sin mirar nada. Cada lectura va dentro de un ciclo que fija el tenant.
            var s = SinComentarios(S040());

            var lecturas = Regex.Matches(s, @"FROM dbo\.empresa_modulos em").Count;
            var fijados = Regex.Matches(s, @"sys\.sp_set_session_context @key = N'id_tenant', @value = @t;").Count;
            var reinicios = Regex.Matches(s, @"sys\.sp_set_session_context @key = N'id_tenant', @value = NULL;").Count;

            Assert.Equal(2, lecturas);                    // PRECHECK y POSTCHECK
            Assert.Equal(lecturas, fijados);
            Assert.True(reinicios >= 2 * fijados, "Cada lectura debe dejar el contexto en NULL al terminar y en el CATCH.");

            // Ninguna lectura de empresa_modulos queda fuera de un lote donde se fija el contexto.
            foreach (Match lectura in Regex.Matches(s, @"FROM dbo\.empresa_modulos em"))
            {
                var inicioLote = s.LastIndexOf("\nGO", lectura.Index, StringComparison.Ordinal);
                var fijo = s.LastIndexOf("sp_set_session_context", lectura.Index, StringComparison.Ordinal);
                Assert.True(fijo > inicioLote, "Una lectura de dbo.empresa_modulos no tiene el contexto de tenant fijado antes.");
            }
        }

        [Fact]
        public void Nadie_pierde_acceso_el_PRECHECK_exige_cobertura_de_modulos_y_cabida_en_los_limites()
        {
            var s = S040();

            Assert.Contains("perderia el acceso", s);
            Assert.Contains("perderian el acceso", s);       // POSTCHECK
            Assert.Contains("WHERE em.activo = 1", s);
            Assert.Contains("usuarios activos y el plan", s);
            Assert.Contains("empresas y el plan", s);
            Assert.Contains("existen a medias", s);
            Assert.Contains("CK_tenants_interno_activo", s);
            Assert.DoesNotContain("TRUNCATE", SinComentarios(s));
        }

        // ── Contenido sembrado: coherencia entre el script, el verificador y la emergencia ───────────────────────────────────

        private static readonly Regex FilaDePlanDerecho = new(@"\('(?<plan>\w+)',\s*'(?<derecho>[a-z_.]+)',\s*(?<valor>NULL|\d+)\)", RegexOptions.Compiled);

        private static List<(string Plan, string Derecho, string Valor)> DerechosSembrados()
        {
            var s = SinRollback(S040());
            var inicio = s.IndexOf("CREATE TABLE #plan_derecho_040", StringComparison.Ordinal);
            var fin = s.IndexOf("CREATE TABLE #asignacion_040", StringComparison.Ordinal);
            return FilaDePlanDerecho.Matches(s[inicio..fin])
                .Select(m => (m.Groups["plan"].Value, m.Groups["derecho"].Value, m.Groups["valor"].Value)).ToList();
        }

        [Fact]
        public void Solo_se_siembran_los_planes_interno_demostracion_y_flota_con_los_limites_de_usuarios_y_empresas()
        {
            var derechos = DerechosSembrados();

            Assert.Equal(new[] { "demostracion", "flota", "interno" }, derechos.Select(d => d.Plan).Distinct().Order());

            var limites = derechos.Where(d => d.Derecho.StartsWith("limite.", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "limite.empresas", "limite.usuarios" }, limites.Select(l => l.Derecho).Distinct().Order());   // D54
            Assert.All(new[] { "interno", "demostracion", "flota" }, plan =>
                Assert.Equal(2, limites.Count(l => l.Plan == plan)));

            // Valores provisionales documentados en el encabezado.
            string Tope(string plan, string limite) => limites.Single(l => l.Plan == plan && l.Derecho == limite).Valor;
            Assert.Equal("NULL", Tope("interno", "limite.usuarios"));
            Assert.Equal("NULL", Tope("interno", "limite.empresas"));
            Assert.Equal("10", Tope("demostracion", "limite.usuarios"));
            Assert.Equal("2", Tope("demostracion", "limite.empresas"));
            Assert.Equal("50", Tope("flota", "limite.usuarios"));
            Assert.Equal("5", Tope("flota", "limite.empresas"));

            var modulos = derechos.Where(d => d.Derecho.StartsWith("modulo.", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "modulo.catalogos", "modulo.flota" }, modulos.Where(m => m.Plan == "demostracion").Select(m => m.Derecho).Order());
            Assert.Equal(new[] { "modulo.flota" }, modulos.Where(m => m.Plan == "flota").Select(m => m.Derecho));
            Assert.DoesNotContain(modulos, m => m.Plan == "interno");   // los de interno se leen de dbo.modulos, no se escriben a mano

            var s = S040();
            Assert.Contains("SELECT 'interno', 'modulo.' + CAST(m.codigo AS VARCHAR(50)), NULL", s);
            Assert.Contains("FROM dbo.modulos m;", s);
            Assert.Contains("PROVISIONALES", s);
        }

        [Fact]
        public void El_reparto_de_tenants_es_el_de_D52_y_el_script_y_el_verificador_dicen_lo_mismo()
        {
            var reparto = new[] { ("SIP", "interno"), ("DEMO", "demostracion"), ("TRANSGAR", "flota"), ("IISE", "flota") };
            var s = S040();
            var v = Verificacion();

            foreach (var (tenant, plan) in reparto)
            {
                Assert.Contains($"('{tenant}', '{plan}')", s);
                Assert.Contains($"(N'{tenant}', N'{plan}')", v);
            }

            Assert.Contains("N'script:040'", s);   // el motivo de las asignaciones
            Assert.Contains("motivo = N'script:040'", v);
        }

        [Fact]
        public void Las_listas_del_verificador_son_las_mismas_que_crea_el_script()
        {
            var cambio = SinRollback(S040());
            var v = Verificacion();

            // Toda clave, indice y restriccion que crea 040 esta nombrada en el verificador.
            var nombres = Regex.Matches(cambio, @"CONSTRAINT (?<n>(PK|FK|UX|CK)_\w+)").Select(m => m.Groups["n"].Value)
                .Concat(Regex.Matches(cambio, @"CREATE (?:UNIQUE )?INDEX (?<n>\w+)").Select(m => m.Groups["n"].Value))
                .Distinct().ToList();
            var sinVerificar = nombres.Where(n => !n.StartsWith("DF_", StringComparison.Ordinal) && !v.Contains(n, StringComparison.Ordinal)).ToList();
            Assert.True(sinVerificar.Count == 0, "El verificador no comprueba: " + string.Join(", ", sinVerificar));

            // Los 13 indices (claves primarias, unicas e IX_) que espera el verificador son los que crea el script.
            var enElVerificador = Regex.Matches(v, @"^\s*\(N'(?:planes|plan_versiones|derechos|plan_version_derechos|tenant_planes)', N'(?<n>(?:PK|UX|IX)_\w+)'", RegexOptions.Multiline)
                .Select(m => m.Groups["n"].Value).Order().ToList();
            var enElScript = nombres.Where(n => n.StartsWith("PK_", StringComparison.Ordinal) && !n.StartsWith("PK_tenants", StringComparison.Ordinal)
                                               || n.StartsWith("UX_", StringComparison.Ordinal) || n.StartsWith("IX_", StringComparison.Ordinal))
                .Order().ToList();
            Assert.Equal(13, enElVerificador.Count);
            Assert.Equal(enElScript, enElVerificador);

            // Las 5 tablas y las 4 columnas de auditoria.
            foreach (var tabla in TablasNuevas)
                Assert.Contains($"N'{tabla}'", v);
            foreach (var columna in new[] { "creado_por", "fecha_creacion", "modificado_por", "fecha_modificacion" })
                Assert.Contains($"N'{columna}'", v);

            // Los dos CHECK de saas.tenants y la columna nueva.
            Assert.Contains("N'CK_tenants_modo_suspension'", v);
            Assert.Contains("N'CK_tenants_interno_activo'", v);
            Assert.Contains("modo_suspension", v);
            Assert.Contains("DF_tenants_modo_suspension", v);

            // Limites y modulos sembrados.
            Assert.Contains("demostracion 10/2, flota 50/5, interno NULL/NULL", v);
            Assert.Contains("N'catalogos,flota'", v);
            Assert.Contains("N'limite.empresas,limite.usuarios'", v);
            Assert.Contains("CAST('20261015' AS DATE)", v);
            Assert.Contains("CAST('20261015' AS DATE)", cambio);
        }

        [Fact]
        public void El_verificador_solo_lee_y_tiene_las_comprobaciones_que_dice_el_LEEME()
        {
            var v = Verificacion();
            // Sin comentarios ni literales de texto: lo que queda es codigo ejecutable (mismo criterio que VerificacionesDeSalidaF1Tests).
            var codigo = Regex.Replace(SinComentarios(v), @"N?'(?:[^']|'')*'", "''");

            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY)\b", codigo.Replace("\r", " "));
            Assert.DoesNotMatch(@"(?i)\b(sp|xp)_\w", codigo);
            Assert.DoesNotContain("#", codigo);   // sin tablas temporales

            // Una fila por comprobacion en la lista "r": (NNN, texto, esperado, obtenido).
            var filas = Regex.Matches(v, @"^\s*\((?<n>\d{3}),\s", RegexOptions.Multiline).Count;
            Assert.Equal(46, filas);
            Assert.Contains("RESUMEN", v);
            Assert.Matches(@"46 comprobaciones", Leeme());
        }

        // ── La emergencia ─────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void La_emergencia_abre_los_derechos_sin_borrar_nada_y_es_idempotente()
        {
            var e = Emergencia();
            var codigo = SinComentarios(e);

            // Encabezado como el de emergencia_rls_apagar.sql: no espera /alerta-bd y solo se corre si Emilio lo pide.
            Assert.Contains("EMERGENCIA", e);
            Assert.Contains("no espera la aprobacion", e);
            Assert.Contains("solo se corre", e);
            Assert.DoesNotContain("PRECHECK", e.Replace("sin PRECHECK", ""));   // no depende de nada

            Assert.Contains("SET estado = 'activo'", codigo);
            Assert.Contains("codigo = 'interno'", codigo);
            Assert.Contains("SET vigente_hasta = @ahora", codigo);              // cierra la asignacion vigente, no la borra
            Assert.Contains("INSERT INTO saas.tenant_planes", codigo);
            Assert.Contains("Nada que hacer", codigo);

            // "sin borrar nada": ni DELETE, ni DROP, ni TRUNCATE.
            Assert.DoesNotMatch(@"\b(DELETE|DROP|TRUNCATE)\b", codigo);
            Assert.DoesNotContain("modo_suspension", codigo);   // si 040 no esta aplicado la columna no existe y el lote no compilaria
        }

        // ── Documentación, índice y arnés ─────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Las_tablas_nuevas_estan_clasificadas_como_plataforma_sin_RLS()
        {
            var clasificadas = ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla, StringComparer.Ordinal);

            foreach (var tabla in TablasNuevas)
            {
                Assert.True(clasificadas.TryGetValue(tabla, out var c), $"{tabla} no está en ClasificacionDeTablas.");
                Assert.Equal(CategoriaTabla.Plataforma, c!.Categoria);
                Assert.False(ClasificacionDeTablas.LlevaIdTenant(c.Categoria), $"{tabla} es del plano de control: no es de tenant.");
                Assert.False(c.EnBaseReal);   // no existían en eBD_SPD el 2026-10-10
            }
        }

        [Fact]
        public void El_indice_de_scripts_registra_el_040_la_emergencia_y_el_verificador()
        {
            var indice = Indice();

            Assert.Matches(@"\| 040 \| 040_planes_derechos_estado_tenant\.sql \|[^\r\n]*\| Pendiente \|", indice);
            Assert.Contains("emergencia_derechos_abrir.sql", indice);
            Assert.Contains("verificar_despues_f17.sql", indice);
            Assert.DoesNotMatch(@"[^\x00-\x7F]", indice);
        }

        [Fact]
        public void El_LEEME_de_F1_7_documenta_los_archivos_y_el_ejecutor()
        {
            var leeme = Leeme();

            foreach (var archivo in new[] { "040_planes_derechos_estado_tenant.sql", "verificar_despues_f17.sql", "emergencia_derechos_abrir.sql", "ejecutar_verificacion.ps1" })
                Assert.Contains(archivo, leeme);
        }

        // ── Cero secretos ─────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Ningun_archivo_de_F1_7_lleva_secretos_ni_cadenas_de_conexion()
        {
            var textos = new Dictionary<string, string>
            {
                ["040"] = S040(), ["emergencia"] = Emergencia(), ["verificador"] = Verificacion(), ["LEEME_F17"] = Leeme()
            };
            var patrones = new[]
            {
                @"(?i)\b(password|pwd|passwd|secret|api[_ -]?key|bearer|connectionstring)\b\s*[=:]\s*\S",
                @"(?i)\b(server|data source|initial catalog|user id|uid|integrated security)\s*=\s*\S",
                @"(?i)[a-z][a-z0-9+.-]*://[^\s/@]+:[^\s/@]+@",
                @"-----BEGIN [A-Z ]*PRIVATE KEY-----",
                @"\b[A-Za-z0-9+/]{40,}={0,2}\b"
            };

            foreach (var (nombre, texto) in textos)
                foreach (var patron in patrones)
                    Assert.False(Regex.IsMatch(texto, patron), $"{nombre} tiene algo que parece un secreto ({patron}).");
        }
    }
}
