using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.4b: con la política RLS encendida cada conexión tiene que llevar el tenant en su contexto de sesión (ADR-023), y eso
    /// lo hace el interceptor de EF al abrirla. Estas pruebas vigilan que ningún código abra la conexión por otro camino y que
    /// cada proceso de fondo diga para qué tenant trabaja. Son pruebas del código fuente, como las de
    /// <c>IgnoreQueryFilters</c> en <c>AmbitoDePlataformaTests</c>.
    /// </summary>
    public class ConexionesDirectasTests
    {
        /// <summary>Archivos que pueden abrir una conexión propia, con el motivo (revisado el 2026-10-13).</summary>
        private static readonly Dictionary<string, string> ConexionPropiaPermitida = new()
        {
            ["Services/TasasCambio/BloqueoJob.cs"] = "Mantiene un candado de aplicación (sp_getapplock) en su propia conexión; no lee ni escribe tablas.",
        };

        /// <summary>Archivos que pueden usar <c>GetDbConnection()</c>: deben abrir la conexión con <c>Database.OpenConnectionAsync</c>.</summary>
        private static readonly string[] GetDbConnectionPermitido = { "Services/SenCombustibleService.cs" };

        /// <summary>Código con un ámbito de servicios propio que no necesita fijar tenant, con el motivo.</summary>
        private static readonly Dictionary<string, string> AmbitoSinTenantPermitido = new()
        {
            ["Program.cs"] = "Al arrancar solo consulta si hay migraciones pendientes; no lee datos de tenant.",
            ["Services/KpiSyncBackgroundService.cs"] = "Solo llama a SenCombustibleService, que fija el ámbito de plataforma en el suyo.",
            ["Services/TasasCambio/TasasCambioConfiguracionService.cs"] = "Lee y guarda la configuración del job: una tabla de la plataforma, sin política.",
        };

        private static IEnumerable<string> ArchivosDeLaAplicacion()
        {
            var raiz = RaizDelRepositorio();
            return new[] { "Pages", "Services", "Data", "Models" }
                .SelectMany(c => Directory.EnumerateFiles(Path.Combine(raiz, c), "*.cs", SearchOption.AllDirectories))
                .Append(Path.Combine(raiz, "Program.cs"));
        }

        private static string Relativa(string archivo) => Path.GetRelativePath(RaizDelRepositorio(), archivo).Replace('\\', '/');

        [Fact]
        public void Ningun_codigo_abre_una_conexion_a_la_base_por_fuera_de_EF()
        {
            var propias = ArchivosDeLaAplicacion()
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"new\s+(Microsoft\.Data\.SqlClient\.)?SqlConnection\s*\("))
                .Select(Relativa).Where(r => !ConexionPropiaPermitida.ContainsKey(r)).ToList();
            Assert.True(propias.Count == 0,
                "Una conexión abierta a mano no lleva el tenant en su contexto de sesión; usa el ApplicationDbContext: " + string.Join(", ", propias));

            var conGetDbConnection = ArchivosDeLaAplicacion()
                .Where(f => File.ReadAllText(f).Contains("GetDbConnection("))
                .Select(Relativa).ToList();
            Assert.True(conGetDbConnection.All(r => GetDbConnectionPermitido.Contains(r)),
                "GetDbConnection() solo se admite en: " + string.Join(", ", GetDbConnectionPermitido) + ". Hay uso en: " + string.Join(", ", conGetDbConnection));

            foreach (var permitido in GetDbConnectionPermitido)
            {
                var texto = File.ReadAllText(Path.Combine(RaizDelRepositorio(), permitido));
                Assert.Contains("Database.OpenConnectionAsync(", texto);   // la abre EF, para que el interceptor fije el contexto
                Assert.DoesNotMatch(@"\bconn\.OpenAsync\(", texto);
            }
        }

        [Fact]
        public void Cada_proceso_con_ambito_de_servicios_propio_declara_su_tenant()
        {
            var sinDeclarar = ArchivosDeLaAplicacion()
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.CreateScope\(|\.CreateAsyncScope\("))
                .Select(f => (Ruta: Relativa(f), Texto: File.ReadAllText(f)))
                .Where(a => !AmbitoSinTenantPermitido.ContainsKey(a.Ruta)
                            && !a.Texto.Contains("FijarPlataforma(") && !a.Texto.Contains(".Fijar("))
                .Select(a => a.Ruta).ToList();

            Assert.True(sinDeclarar.Count == 0,
                "Un proceso que crea su propio ámbito de servicios debe decir para qué tenant trabaja (TenantContext.Fijar o FijarPlataforma): "
                + string.Join(", ", sinDeclarar));
        }

        [Fact]
        public void Program_registra_el_interceptor_de_contexto_de_sesion_en_el_contexto_de_EF()
        {
            var programa = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "Program.cs"));
            Assert.Contains("AddScoped<ContextoDeSesionInterceptor>()", programa);
            Assert.Matches(@"AddInterceptors\([^;]*GetRequiredService<ContextoDeSesionInterceptor>\(\)", programa);
        }

        private static string RaizDelRepositorio()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }
    }
}
