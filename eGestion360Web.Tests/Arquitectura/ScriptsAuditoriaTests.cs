using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.8a: el script 045 crea <c>dbo.auditoria_seguridad</c> (tabla de tenant, con RLS) y <c>saas.auditoria_plataforma</c> (plano de
    /// control), las dos inmutables, y agrega dos columnas nulas a <c>dbo.bitacora_cambios</c>. Estas pruebas mantienen ordenados el script, el
    /// modelo de EF, el catálogo de eventos, el verificador de solo lectura (<c>verificar_despues_045.sql</c>), la documentación y el arnés de
    /// aislamiento. No necesitan base de datos; el comportamiento (aplicar, repetir, revertir, abortar, la RLS, la inmutabilidad) se ensayó contra
    /// una copia real y está en <c>1 - Documetacion/Operacion/F1/LEEME_F18.md</c>.
    /// </summary>
    public class ScriptsAuditoriaTests
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

        private static string S045() => Leer("2 - Script SQL", "045_auditoria_seguridad.sql");
        private static string Verificador() => Leer("1 - Documetacion", "Operacion", "F1", "verificar_despues_045.sql");

        private static string SinComentarios(string sql)
        {
            var t = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ");
            return Regex.Replace(t, @"--[^\r\n]*", " ");
        }

        private static string SinRollback(string sql) => sql[..sql.LastIndexOf("/*", StringComparison.Ordinal)];

        // ── Forma del archivo ─────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void El_script_es_ASCII_puro_y_trae_PRECHECK_CAMBIO_POSTCHECK_y_ROLLBACK()
        {
            var s = S045();
            Assert.True(s.All(c => c < 128), "El script 045 debe ser ASCII puro.");
            Assert.Contains("PRECHECK 045", s);
            Assert.Contains("CAMBIO 045 aplicado", s);
            Assert.Contains("POSTCHECK", s);
            Assert.Contains("ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)", s);
            Assert.Contains("BEGIN TRANSACTION", SinRollback(s));
            Assert.Contains("SET XACT_ABORT ON", s);
            Assert.Contains("Falta la marca del PRECHECK", s);   // sin PRECHECK en la misma sesion, el bloque de cambio no hace nada
            Assert.Single(Regex.Matches(s, @"/\*"));              // el unico comentario de bloque es el del ROLLBACK
        }

        [Fact]
        public void El_cambio_solo_agrega_y_lo_unico_que_altera_es_la_bitacora_y_la_politica()
        {
            var codigo = SinComentarios(SinRollback(S045()));

            Assert.DoesNotMatch(@"\bDELETE\s+FROM\b", codigo);
            Assert.DoesNotMatch(@"\bTRUNCATE\s+TABLE\b", codigo);
            Assert.DoesNotMatch(@"\bUPDATE\s+\w[\w.]*\s+SET\b", codigo);
            Assert.DoesNotMatch(@"\bDROP\b", Regex.Replace(codigo, @"DROP TABLE #\w+", string.Empty));   // solo se sueltan las temporales

            // INSERT solo a las temporales del PRECHECK: ninguna fila de ninguna tabla real se escribe al aplicar.
            var inserts = Regex.Matches(codigo, @"\bINSERT\s+INTO\s+(?<tabla>[\w.#]+)").Cast<Match>().Select(m => m.Groups["tabla"].Value).Distinct().Order().ToList();
            Assert.Equal(new[] { "#columnas_045", "#precheck_045" }, inserts);

            // ALTER TABLE: solo dbo.bitacora_cambios ADD <columna> NULL, y las dos columnas nuevas.
            var alters = Regex.Matches(codigo, @"ALTER\s+TABLE\s+(?<tabla>[\w.]+)\s+ADD\s+(?<columna>\w+)\s+(?<tipo>[A-Z]+(?:\(\d+\))?)\s+(?<nulo>NULL)").Cast<Match>().ToList();
            Assert.Equal(2, alters.Count);
            Assert.Equal(Regex.Matches(codigo, @"\bALTER\s+TABLE\b").Count, alters.Count);
            Assert.All(alters, a => Assert.Equal("dbo.bitacora_cambios", a.Groups["tabla"].Value));
            Assert.Equal(new[] { "id_correlacion", "id_cuenta" }, alters.Select(a => a.Groups["columna"].Value).Order(StringComparer.Ordinal).ToArray());

            // ALTER SECURITY POLICY: solo ADD (nunca DROP ni ALTER de un predicado ya puesto).
            Assert.Equal(1, Regex.Matches(codigo, @"\bALTER\s+SECURITY\s+POLICY\b").Count);
            Assert.DoesNotMatch(@"\bDROP\s+(FILTER|BLOCK)\b", codigo);
        }

        [Fact]
        public void Las_dos_tablas_son_inmutables_no_llevan_clave_foranea_a_cuentas_ni_empresas_y_el_tenant_si_se_valida()
        {
            var codigo = SinComentarios(SinRollback(S045()));

            Assert.Contains("CREATE TRIGGER dbo.TR_auditoria_seguridad_inmutable", codigo);
            Assert.Contains("CREATE TRIGGER saas.TR_auditoria_plataforma_inmutable", codigo);
            Assert.Equal(2, Regex.Matches(codigo, @"INSTEAD OF UPDATE, DELETE").Count);

            var claves = Regex.Matches(codigo, @"FOREIGN KEY \((?<col>\w+)\) REFERENCES (?<ref>[\w.]+) \((?<refcol>\w+)\)").Cast<Match>().ToList();
            Assert.Equal(2, claves.Count);
            Assert.All(claves, k =>
            {
                Assert.Equal("id_tenant", k.Groups["col"].Value);
                Assert.Equal("saas.tenants", k.Groups["ref"].Value);   // la auditoria nunca depende de cuentas ni de empresas
            });
        }

        [Fact]
        public void Solo_la_tabla_de_tenant_entra_a_la_politica_con_un_filtro_y_cuatro_bloqueos_de_fn_tenant_actual()
        {
            var s = SinRollback(S045());
            var predicados = Regex.Matches(s,
                    @"ADD (?<tipo>FILTER|BLOCK) PREDICATE seg\.(?<fn>\w+)\(id_tenant\) ON (?<tabla>[\w.]+)(?: (?<op>AFTER INSERT|AFTER UPDATE|BEFORE UPDATE|BEFORE DELETE))?")
                .Cast<Match>().ToList();

            Assert.Equal(5, predicados.Count);
            Assert.All(predicados, p => Assert.Equal("fn_tenant_actual", p.Groups["fn"].Value));
            Assert.All(predicados, p => Assert.Equal("dbo.auditoria_seguridad", p.Groups["tabla"].Value));
            Assert.Single(predicados, p => p.Groups["tipo"].Value == "FILTER");
            Assert.Equal(new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE DELETE", "BEFORE UPDATE" },
                         predicados.Where(p => p.Groups["tipo"].Value == "BLOCK").Select(p => p.Groups["op"].Value).Order(StringComparer.Ordinal).ToArray());
            Assert.DoesNotMatch(@"ADD\s+(FILTER|BLOCK)\s+PREDICATE\s+[\w.]+\(\w+\)\s+ON\s+saas\.", s);   // el plano de control no lleva RLS (ADR-013)

            // El PRECHECK acepta 0 o 5 predicados en la tabla nueva, sin contar la politica entera (que puede o no tener 038 a 044).
            Assert.Contains("@predSeg NOT IN (0, 5)", s);
        }

        [Fact]
        public void El_tipo_del_evento_que_acepta_la_tabla_es_el_de_la_forma_grupo_punto_accion_del_catalogo()
        {
            var s = SinRollback(S045());

            Assert.Equal(2, Regex.Matches(s, @"LIKE '%\.%' AND tipo COLLATE Latin1_General_100_BIN2 NOT LIKE '%\[\^a-z0-9_\.\]%'").Count);
            Assert.All(TiposDeEvento.Todos, t => Assert.Matches("^[a-z0-9_]+\\.[a-z0-9_]+$", t));
            Assert.Equal(2, Regex.Matches(s, @"resultado IN \('ok', 'fallo', 'denegado'\)").Count);
            Assert.Equal(new[] { "ok", "fallo", "denegado" }, new[] { ResultadosDeEvento.Ok, ResultadosDeEvento.Fallo, ResultadosDeEvento.Denegado });
        }

        // ── El modelo de EF y el script describen las mismas tablas y columnas ────────────────────────────────────────

        private sealed record ColumnaDelScript(string Nombre, string Tipo, int? Largo, bool Nulable);

        /// <summary>Las columnas que declara el <c>CREATE TABLE</c> de una tabla, con tipo, largo y nulabilidad.</summary>
        private static List<ColumnaDelScript> ColumnasDe(string tabla)
        {
            var bloque = Regex.Match(S045(), @"CREATE TABLE " + Regex.Escape(tabla) + @" \((?<cuerpo>[\s\S]*?)\r?\n\s+CONSTRAINT PK_").Groups["cuerpo"].Value;
            Assert.False(string.IsNullOrWhiteSpace(bloque), "No se encontró el CREATE TABLE de " + tabla);

            return Regex.Matches(bloque, @"^\s+(?<col>[a-z_]+)\s+(?<tipo>BIGINT|DATETIME2|NVARCHAR|VARCHAR|INT|BIT)(?:\((?<largo>\d+)(?:,\d+)?\))?(?<resto>.*)$", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(m => new ColumnaDelScript(m.Groups["col"].Value, m.Groups["tipo"].Value,
                    m.Groups["largo"].Success ? int.Parse(m.Groups["largo"].Value) : null,
                    Regex.IsMatch(m.Groups["resto"].Value, @"(?<!NOT )\bNULL\b")))
                .ToList();
        }

        [Theory]
        [InlineData(typeof(AuditoriaSeguridad), "dbo.auditoria_seguridad")]
        [InlineData(typeof(AuditoriaPlataforma), "saas.auditoria_plataforma")]
        public void Las_columnas_del_script_son_las_del_modelo_de_EF_con_el_mismo_tipo_largo_y_nulabilidad(Type entidad, string tabla)
        {
            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();
            var et = db.Model.FindEntityType(entidad)!;
            var id = StoreObjectIdentifier.Table(et.GetTableName()!, et.GetSchema());
            Assert.Equal(tabla, $"{et.GetSchema() ?? "dbo"}.{et.GetTableName()}");

            var delScript = ColumnasDe(tabla);
            Assert.Equal(15, delScript.Count);
            Assert.Equal(delScript.Select(c => c.Nombre).Order(StringComparer.Ordinal).ToList(),
                         et.GetProperties().Select(p => p.GetColumnName(id)!).Order(StringComparer.Ordinal).ToList());

            foreach (var columna in delScript)
            {
                var propiedad = et.GetProperties().Single(p => p.GetColumnName(id) == columna.Nombre);
                Assert.True(columna.Nulable == propiedad.IsColumnNullable(), $"{tabla}.{columna.Nombre}: nulabilidad distinta entre el script y el modelo.");

                if (columna.Tipo is "VARCHAR" or "NVARCHAR")
                {
                    Assert.Equal(columna.Largo, propiedad.GetMaxLength());
                    var tipoDeColumna = propiedad.GetColumnType();
                    if (columna.Tipo == "VARCHAR") Assert.Equal($"varchar({columna.Largo})", tipoDeColumna, ignoreCase: true);
                    else Assert.DoesNotContain("varchar", tipoDeColumna, StringComparison.OrdinalIgnoreCase);   // unicode: sin tipo explícito
                }
            }
        }

        [Fact]
        public void La_bitacora_agrega_las_dos_columnas_nulas_que_tiene_el_modelo()
        {
            var alter = SinComentarios(SinRollback(S045()));
            Assert.Contains("ALTER TABLE dbo.bitacora_cambios ADD id_cuenta INT NULL", alter);
            Assert.Contains("ALTER TABLE dbo.bitacora_cambios ADD id_correlacion VARCHAR(64) NULL", alter);

            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();
            var et = db.Model.FindEntityType(typeof(BitacoraCambio))!;
            var id = StoreObjectIdentifier.Table(et.GetTableName()!, et.GetSchema());
            var cuenta = et.GetProperties().Single(p => p.GetColumnName(id) == "id_cuenta");
            var correlacion = et.GetProperties().Single(p => p.GetColumnName(id) == "id_correlacion");

            Assert.True(cuenta.IsColumnNullable());
            Assert.True(correlacion.IsColumnNullable());
            Assert.Equal(64, correlacion.GetMaxLength());
            Assert.Equal("varchar(64)", correlacion.GetColumnType(), ignoreCase: true);
        }

        [Fact]
        public void Auditoria_seguridad_es_una_tabla_de_tenant_y_auditoria_plataforma_es_del_plano_de_control()
        {
            var clasificacion = ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla);

            Assert.Equal(CategoriaTabla.Tenant, clasificacion["auditoria_seguridad"].Categoria);
            Assert.True(clasificacion["auditoria_seguridad"].EnModeloEf);
            Assert.Equal("045", clasificacion["auditoria_seguridad"].CreadaPorScript);
            Assert.Equal(CategoriaTabla.Plataforma, clasificacion["auditoria_plataforma"].Categoria);
            Assert.True(clasificacion["auditoria_plataforma"].EnModeloEf);
            Assert.Equal("045", clasificacion["auditoria_plataforma"].CreadaPorScript);

            // El modelo: la de tenant lleva id_tenant obligatorio (ITenantEntity); la de plataforma lo lleva nulable y no es de tenant.
            Assert.True(typeof(ITenantEntity).IsAssignableFrom(typeof(AuditoriaSeguridad)));
            Assert.False(typeof(ITenantEntity).IsAssignableFrom(typeof(AuditoriaPlataforma)));
            Assert.Equal(typeof(int?), typeof(AuditoriaPlataforma).GetProperty(nameof(AuditoriaPlataforma.IdTenant))!.PropertyType);
        }

        // ── El catálogo de eventos y su destino ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Los_eventos_de_la_cuenta_van_a_la_plataforma_y_el_contexto_elegido_sigue_al_tenant()
        {
            var deLaCuenta = new[]
            {
                TiposDeEvento.SesionEntrada, TiposDeEvento.SesionSalida, TiposDeEvento.SesionBloqueo, TiposDeEvento.MfaVerificada, TiposDeEvento.MfaAlta,
                TiposDeEvento.MfaRecuperacion, TiposDeEvento.MfaDesactivada, TiposDeEvento.ClaveCambiada, TiposDeEvento.ClaveRestablecida, TiposDeEvento.InvitacionAceptada,
            };
            Assert.All(deLaCuenta, t => Assert.Equal(DestinoDeEvento.Plataforma, TiposDeEvento.DestinoDe(t)));
            Assert.Equal(DestinoDeEvento.SegunTenant, TiposDeEvento.DestinoDe(TiposDeEvento.ContextoElegido));
            Assert.Equal(deLaCuenta.Append(TiposDeEvento.ContextoElegido).Order(StringComparer.Ordinal).ToList(), TiposDeEvento.Todos.Order(StringComparer.Ordinal).ToList());
        }

        [Fact]
        public void Todo_tipo_del_catalogo_esta_documentado_en_el_ADR_030()
        {
            var adr = Leer("1 - Documetacion", "Arquitectura", "ADR-030-auditoria-de-seguridad.md");

            Assert.All(TiposDeEvento.Todos, tipo => Assert.Contains($"`{tipo}`", adr));
        }

        // ── Verificador, diaria, documentación, registro ────────────────────────────────────────────────────────────────

        [Fact]
        public void El_verificador_es_de_solo_lectura_y_la_comprobacion_diaria_lo_corre_cuando_el_045_esta_aplicado()
        {
            // Sin comentarios ni literales de texto («INSTEAD OF UPDATE, DELETE» es una etiqueta): lo que queda es código ejecutable.
            var conTextos = SinComentarios(Verificador());
            var v = Regex.Replace(conTextos, @"N?'(?:[^']|'')*'", "''");
            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC|EXECUTE)\b", v);
            Assert.Contains("RESUMEN", conTextos);

            var diaria = Leer("1 - Documetacion", "Operacion", "F1", "comprobacion_diaria.ps1");
            Assert.Contains("OBJECT_ID(N'dbo.auditoria_seguridad', N'U')", diaria);
            Assert.Contains("verificar_despues_045.sql", diaria);
        }

        [Fact]
        public void Los_verificadores_anteriores_aceptan_las_cifras_del_045()
        {
            foreach (var archivo in new[] { "verificar_despues_f15.sql", "verificar_despues_f17.sql" })
            {
                var v = Leer("1 - Documetacion", "Operacion", "F1", archivo);
                Assert.Contains("AS con045", v);
                Assert.Contains("OBJECT_ID(N'dbo.auditoria_seguridad', N'U')", v);
                Assert.Contains("5 * c.con045", v);   // los 5 predicados de la tabla de tenant
            }
        }

        [Fact]
        public void El_rollback_se_detiene_si_las_tablas_de_auditoria_o_las_columnas_de_la_bitacora_tienen_datos()
        {
            var s = S045();
            var rollback = s[s.LastIndexOf("/*", StringComparison.Ordinal)..];

            Assert.Contains("ROLLBACK 045 detenido: las tablas de auditoria tienen filas", rollback);
            Assert.Contains("ROLLBACK 045 detenido: la bitacora ya tiene datos", rollback);
            Assert.Contains("DROP COLUMN id_cuenta", rollback);
            Assert.Contains("DROP COLUMN id_correlacion", rollback);
            Assert.Contains("DROP FILTER PREDICATE ON dbo.auditoria_seguridad", rollback);
        }

        [Fact]
        public void El_indice_de_scripts_y_la_documentacion_nombran_el_045()
        {
            var indice = Leer("1 - Documetacion", "INDICE_SCRIPTS_SQL.md");
            Assert.Contains("| 045 | 045_auditoria_seguridad.sql |", indice);
            Assert.Contains("verificar_despues_045.sql", indice);

            Assert.True(File.Exists(Path.Combine(Raiz(), "1 - Documetacion", "Operacion", "F1", "LEEME_F18.md")));
            var adr = Leer("1 - Documetacion", "Arquitectura", "ADR-030-auditoria-de-seguridad.md");
            Assert.Contains("ADR-030", Leer("1 - Documetacion", "Arquitectura", "README.md"));
            Assert.Contains("D70", adr);
            Assert.Contains("D76", adr);
        }

        [Fact]
        public void El_servicio_esta_registrado_como_singleton_y_las_pantallas_no_lo_dejan_obligatorio()
        {
            var program = Leer("Program.cs");
            Assert.Contains("AddSingleton<IAuditoriaDeSeguridad, AuditoriaDeSeguridad>()", program);

            // Quien registra eventos lo recibe como dependencia opcional: sin servicio (en pruebas de otras piezas) todo sigue funcionando.
            foreach (var archivo in new[] { "Services/Identidad/Autenticador.cs", "Services/Identidad/BloqueoDeCuentas.cs", "Services/Identidad/ServicioMfa.cs",
                                            "Services/Identidad/ServicioDeInvitaciones.cs", "Pages/ChangePassword.cshtml.cs", "Pages/ResetPassword.cshtml.cs" })
                Assert.Matches(@"IAuditoriaDeSeguridad\?\s+auditoria\s*=\s*null", Leer(archivo.Split('/')));
        }
    }
}
