using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Reportes;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Services.Reportes;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Reportes
{
    /// <summary>
    /// Motor de reportes versionados (ADR-031, Fase 1): la plantilla sembrada por el script 047 compila en Scriban y redacta lo mismo que la
    /// hoja en Razor, y el motor elige la versión del tenant sobre la global (datos inventados).
    /// </summary>
    public class MotorReportesTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.Zero));

        public void Dispose() => _bd.Dispose();

        // ── La semilla del script 047 ───────────────────────────────────────────────────────────────────────────────────────

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static string Script047() =>
            File.ReadAllText(Path.Combine(Raiz(), "2 - Script SQL", "047_motor_reportes_versionados.sql"), Encoding.UTF8);

        /// <summary>El literal N'...' que el script asigna a la variable, con las comillas dobladas de SQL deshechas.</summary>
        internal static string LiteralDelScript(string variable)
        {
            var m = Regex.Match(Script047(), @"SET " + Regex.Escape(variable) + @" = CAST\(N'' AS NVARCHAR\(MAX\)\) \+ N'(?<texto>(?:[^']|'')*)';");
            Assert.True(m.Success, "No se encontró la asignación de " + variable + " en el script 047.");
            return m.Groups["texto"].Value.Replace("''", "'");
        }

        internal static PlantillaCompilada SemillaCompilada() =>
            ScribanReporteRenderer.Compilar(LiteralDelScript("@plantilla"), LiteralDelScript("@configuracion"));

        private static readonly (string Nombre, string Codigo)[] Pruebas = { ("Prueba Psicológica de Manejo", "PPM") };

        private static string SinEtiquetas(string html) => Regex.Replace(html, "<[^>]+>", "");

        private static string Cuerpo(string html) =>
            Regex.Match(html, @"<div class=""cuerpo""[^>]*>(?<c>.*?)</div>", RegexOptions.Singleline).Groups["c"].Value;

        [Theory]
        [InlineData("M", "0801-1990-12345", "apto")]
        [InlineData("F", "0801-1990-12345", "no_apto")]
        [InlineData(null, null, null)]
        public void La_semilla_redacta_el_mismo_parrafo_que_la_hoja_en_codigo(string? sexo, string? dni, string? resultado)
        {
            var fecha = new DateOnly(2026, 3, 7);
            // html.escape escribe los acentos como entidades (&#241;): el navegador los muestra igual; se comparan ya decodificados.
            var html = System.Net.WebUtility.HtmlDecode(ScribanReporteRenderer.Renderizar(SemillaCompilada(),
                ConstanciaPsicologiaDataSource.Modelo(fecha, "Carlos Mejía Ávila", sexo, dni, resultado, Pruebas, modoGuia: false)));

            var enCodigo = RedaccionDeConstancias.LicenciaConducir(fecha, "Carlos Mejía Ávila", sexo, dni, resultado, Pruebas);
            Assert.Equal(enCodigo.Cuerpo, SinEtiquetas(Cuerpo(html)));
            Assert.Contains("<span class=\"junto\">Carlos Mejía Ávila</span>", html);
            Assert.Contains("<span class=\"junto\">" + enCodigo.Partes.Dni + "</span>", html);
            Assert.Contains(">7</div>", html);
            Assert.Contains(">marzo</div>", html);
            Assert.Contains(">26</div>", html);
            Assert.Contains("<div class=\"pruebas\">" + enCodigo.Pruebas + "</div>", html);
            Assert.DoesNotContain("COLEGIO", html);   // sin guía
        }

        [Fact]
        public void La_semilla_lleva_las_medidas_de_la_hoja_en_milimetros_y_la_guia_solo_en_la_hoja_de_prueba()
        {
            var html = ScribanReporteRenderer.Renderizar(SemillaCompilada(),
                ConstanciaPsicologiaDataSource.Modelo(new DateOnly(2026, 11, 22), "Ana López", "F", null, "apto", Pruebas, modoGuia: true));

            foreach (var medida in new[] { "--hoja-ancho: 215mm;", "--hoja-alto: 275mm;", "--fecha-base: 35mm;", "--dia-desde: 14mm;", "--mes-hasta: 68mm;",
                                           "--cuerpo-arriba: 74mm;", "--cuerpo-renglon: 14mm;", "--pruebas-arriba: 204mm;", "--pruebas-sangria: 98mm;",
                                           "@page { size: letter; margin: 0; }", "font-size: 15pt;", "font-size: 13pt;", "font-size: 12pt;" })
                Assert.Contains(medida, html);

            Assert.Contains("COLEGIO DE PSIC&Oacute;LOGOS DE HONDURAS", html);
            Assert.Equal(4 + 2, Regex.Matches(html, "class=\"renglon\"").Count);   // 4 del párrafo y 2 de las pruebas
            Assert.Contains("data-minimo-pt=\"10\"", html);
        }

        [Fact]
        public void Los_datos_de_la_persona_se_escapan()
        {
            var html = ScribanReporteRenderer.Renderizar(SemillaCompilada(),
                ConstanciaPsicologiaDataSource.Modelo(new DateOnly(2026, 11, 22), "<script>alert(1)</script>", "M", "0801\"x", "apto", Pruebas, false));

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("0801\"x", html);
        }

        [Fact]
        public void Una_plantilla_que_no_compila_o_un_json_invalido_se_rechazan_con_un_mensaje()
        {
            Assert.Throws<PlantillaDeReporteInvalidaException>(() => ScribanReporteRenderer.Compilar("{{ if }}", null));
            Assert.Throws<PlantillaDeReporteInvalidaException>(() => ScribanReporteRenderer.Compilar("hola", "{ no es json"));
        }

        [Fact]
        public void Un_bucle_sin_fin_se_corta()
        {
            var compilada = ScribanReporteRenderer.Compilar("{{ for i in 1..100000 }}x{{ end }}", null);
            Assert.Throws<PlantillaDeReporteInvalidaException>(() => ScribanReporteRenderer.Renderizar(compilada, new Dictionary<string, object?>()));
        }

        [Fact]
        public void Las_medidas_con_decimales_salen_con_punto()
        {
            var compilada = ScribanReporteRenderer.Compilar("{{ cfg.medida }}mm", "{ \"medida\": 12.5 }");
            Assert.Equal("12.5mm", ScribanReporteRenderer.Renderizar(compilada, new Dictionary<string, object?>()));
        }

        // ── Resolución tenant → global ─────────────────────────────────────────────────────────────────────────────────────

        private const string Codigo = PlantillasConstancia.CodigoReporteLicenciaConducir;

        /// <summary>Un reporte con una versión publicada y activa; global si <paramref name="idTenant"/> es nulo.</summary>
        private async Task<(int Reporte, int Version)> NuevoReporte(int? idTenant, string plantilla, bool activo = true, string codigo = Codigo)
        {
            using var db = _bd.Crear();
            var reporte = new Reporte
            {
                IdTenant = idTenant, Codigo = codigo, Nombre = "Constancia", Modulo = "psicologia", FuenteDatos = ConstanciaPsicologiaDataSource.Fuente,
                TipoSalida = TiposSalidaReporte.ImpresionHtml, Activo = activo, CreadoPor = "prueba", FechaCreacion = _reloj.GetUtcNow().UtcDateTime
            };
            db.Reportes.Add(reporte);
            await db.SaveChangesAsync();
            var version = new ReporteVersion
            {
                IdReporte = reporte.IdReporte, IdTenant = idTenant, NumeroVersion = 1, Estado = EstadosVersionReporte.Publicada,
                ContenidoPlantilla = plantilla, CreadoPor = "prueba", FechaCreacion = reporte.FechaCreacion, FechaPublicacion = reporte.FechaCreacion
            };
            db.ReporteVersiones.Add(version);
            await db.SaveChangesAsync();
            reporte.IdVersionActiva = version.IdVersion;
            await db.SaveChangesAsync();
            return (reporte.IdReporte, version.IdVersion);
        }

        private MotorReportesService Motor(ApplicationDbContext db, int? idTenant) =>
            new(db, new TenantContextFijo { IdTenant = idTenant, IdCorrelacion = "corr-1" }, new MemoryCache(new MemoryCacheOptions()), _reloj,
                NullLogger<MotorReportesService>.Instance);

        [Fact]
        public async Task Sin_reporte_no_hay_version_y_la_pantalla_sigue_con_su_formato()
        {
            using var db = _bd.CrearComo(DatosBase.TenantA);
            Assert.Null(await Motor(db, DatosBase.TenantA).ResolverAsync(Codigo));
        }

        [Fact]
        public async Task El_tenant_usa_su_version_y_los_demas_la_global()
        {
            await NuevoReporte(null, "global");
            var (propio, versionPropia) = await NuevoReporte(DatosBase.TenantA, "de A");

            using (var db = _bd.CrearComo(DatosBase.TenantA))
            {
                var v = await Motor(db, DatosBase.TenantA).ResolverAsync(Codigo);
                Assert.NotNull(v);
                Assert.True(v!.EsPersonalizada);
                Assert.Equal((propio, versionPropia), (v.IdReporte, v.IdVersion));
                Assert.Equal("de A", Motor(db, DatosBase.TenantA).Renderizar(v, new Dictionary<string, object?>()));
            }
            using (var db = _bd.CrearComo(DatosBase.TenantB))
            {
                var v = await Motor(db, DatosBase.TenantB).ResolverAsync(Codigo);
                Assert.False(v!.EsPersonalizada);
                Assert.Equal("global", Motor(db, DatosBase.TenantB).Renderizar(v, new Dictionary<string, object?>()));
            }
            // En ámbito de plataforma (sin filtro de EF) tampoco se usa la versión de otro tenant.
            using (var db = _bd.CrearComo(null, plataforma: true))
            {
                var v = await Motor(db, DatosBase.TenantB).ResolverAsync(Codigo);
                Assert.Equal("global", Motor(db, DatosBase.TenantB).Renderizar(v!, new Dictionary<string, object?>()));
            }
        }

        [Fact]
        public async Task Un_reporte_inactivo_del_tenant_cae_a_la_global()
        {
            await NuevoReporte(null, "global");
            await NuevoReporte(DatosBase.TenantA, "de A", activo: false);

            using var db = _bd.CrearComo(DatosBase.TenantA);
            var v = await Motor(db, DatosBase.TenantA).ResolverAsync(Codigo);
            Assert.False(v!.EsPersonalizada);
        }

        [Fact]
        public async Task Una_version_que_no_compila_no_se_usa()
        {
            await NuevoReporte(null, "{{ if }}");
            using var db = _bd.CrearComo(DatosBase.TenantA);
            Assert.Null(await Motor(db, DatosBase.TenantA).ResolverAsync(Codigo));
        }

        [Fact]
        public async Task La_ejecucion_lleva_la_version_exacta_y_el_tenant_de_su_empresa()
        {
            var (reporte, version) = await NuevoReporte(null, "global");

            using (var db = _bd.CrearComo(DatosBase.TenantA))
            {
                var motor = Motor(db, DatosBase.TenantA);
                var v = (await motor.ResolverAsync(Codigo))!;
                db.ReporteEjecuciones.Add(motor.NuevaEjecucion(v, DatosBase.EmpresaA, ConstanciaPsicologiaDataSource.EntidadOrigen, 42, idCuenta: 7, "psi.ana"));
                await db.SaveChangesAsync();
            }

            using var leer = _bd.Crear();
            var e = await leer.ReporteEjecuciones.SingleAsync();
            Assert.Equal((DatosBase.TenantA, DatosBase.EmpresaA, reporte, version, 1), (e.IdTenant, e.IdEmpresa, e.IdReporte, e.IdVersion, e.NumeroVersion));
            Assert.Equal((Codigo, "psi.constancias", 42L, 7, "psi.ana", "corr-1"), (e.CodigoReporte, e.EntidadOrigen, e.IdRegistroOrigen, e.IdCuenta, e.Usuario, e.IdCorrelacion));
            Assert.Equal(TiposSalidaReporte.ImpresionHtml, e.TipoSalida);
        }
    }
}
