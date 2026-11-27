using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Una pantalla que cambia la clave de un usuario deja antes la petición en el tenant de ese usuario: el guardado escribe su fila
    /// de auditoría en ese tenant y la política RLS la rechaza desde otro. Las pruebas sobre SQLite no ven la política, así que una
    /// pantalla nueva que lo olvidara fallaría solo en SQL Server.
    /// </summary>
    public class ClavesYTenantTests
    {
        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj"))) carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        [Fact]
        public void Cada_pantalla_que_cambia_una_clave_fija_antes_el_tenant_del_usuario()
        {
            // Asignar la clave de un usuario ya existente (usuario.Password = ...); las altas la ponen al construir el usuario.
            var pantallas = Directory.EnumerateFiles(Path.Combine(Raiz(), "Pages"), "*.cs", SearchOption.AllDirectories)
                .Select(ruta => (Ruta: Path.GetRelativePath(Raiz(), ruta).Replace('\\', '/'), Texto: File.ReadAllText(ruta)))
                .Select(p => (p.Ruta, p.Texto, Cambio: Regex.Match(p.Texto, @"\.Password\s*=(?!=)")))
                .Where(p => p.Cambio.Success)
                .ToList();
            Assert.NotEmpty(pantallas);

            var olvidan = pantallas
                .Where(p =>
                {
                    var fijar = p.Texto.IndexOf("FijarTenantDelUsuarioAsync(", StringComparison.Ordinal);
                    return fijar < 0 || fijar > p.Cambio.Index;
                })
                .Select(p => p.Ruta).ToList();

            Assert.True(olvidan.Count == 0,
                "Estas pantallas cambian una clave sin llamar antes a FijarTenantDelUsuarioAsync: " + string.Join(", ", olvidan));
        }
    }
}
