using System.Text.RegularExpressions;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Un parcial de formulario (<c>_XxxForm</c>) que recibe <c>model="Model.Prop"</c> NO hereda el prefijo del campo: sus
    /// <c>asp-for</c> generan nombres sin prefijo (<c>Placa</c>) mientras los campos ocultos de la página llevan el prefijo
    /// (<c>Vehiculo.IdVehiculo</c>). Con la propiedad enlazada por prefijo, el modelo llega vacío, la validación falla en
    /// silencio y «Editar» no guarda (error de «Editar vehículo», 2026-10-15). Clientes lo resuelve pasando
    /// <c>view-data</c> con <c>HtmlFieldPrefix</c>; estas pruebas exigen lo mismo a todo parcial de formulario.
    /// </summary>
    public class ParcialesDeFormularioTests
    {
        // <partial name="_AlgoForm" model="Model.Prop" ... /> (la etiqueta puede ocupar varias líneas)
        private static readonly Regex Parcial = new(
            @"<partial\s+name=""(?<nombre>_\w*Form)""(?<resto>[^>]*?)/>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex ModeloDePagina = new(@"model=""Model\.(?<prop>\w+)""", RegexOptions.CultureInvariant);

        [Fact]
        public void Todo_parcial_de_formulario_recibe_el_prefijo_de_su_propiedad()
        {
            var raiz = RaizDelRepositorio();
            var sinPrefijo = new List<string>();
            var encontrados = 0;

            foreach (var archivo in Directory.EnumerateFiles(Path.Combine(raiz, "Pages"), "*.cshtml", SearchOption.AllDirectories))
            {
                var texto = File.ReadAllText(archivo);
                foreach (Match m in Parcial.Matches(texto))
                {
                    var modelo = ModeloDePagina.Match(m.Groups["resto"].Value);
                    if (!modelo.Success) continue;

                    encontrados++;
                    var prop = modelo.Groups["prop"].Value;
                    var resto = m.Groups["resto"].Value;
                    var fijaPrefijo = resto.Contains("HtmlFieldPrefix")
                        && (resto.Contains($"nameof(Model.{prop})") || resto.Contains($"\"{prop}\""));
                    if (!fijaPrefijo)
                        sinPrefijo.Add($"{Path.GetRelativePath(raiz, archivo)}: {m.Groups["nombre"].Value} con model=\"Model.{prop}\"");
                }
            }

            Assert.True(encontrados >= 4, "Se esperaban al menos los parciales de Vehículos y Clientes; hay " + encontrados);
            Assert.True(sinPrefijo.Count == 0,
                "Estos parciales de formulario no fijan HtmlFieldPrefix y sus campos no se enlazarán a la propiedad de la página:\n  "
                + string.Join("\n  ", sinPrefijo));
        }

        [Theory]
        [InlineData("Pages/Flota/Catalogos/Vehiculos/Create.cshtml")]
        [InlineData("Pages/Flota/Catalogos/Vehiculos/Edit.cshtml")]
        public void Las_pantallas_de_vehiculo_enlazan_el_formulario_con_el_prefijo_Vehiculo(string ruta)
        {
            var texto = File.ReadAllText(Path.Combine(RaizDelRepositorio(), ruta));

            Assert.Matches(@"<partial\s+name=""_VehiculoForm""[^>]*HtmlFieldPrefix\s*=\s*nameof\(Model\.Vehiculo\)", texto);
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
