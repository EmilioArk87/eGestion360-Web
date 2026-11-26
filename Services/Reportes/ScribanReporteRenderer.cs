using System.Globalization;
using System.Text.Json;
using Scriban;
using Scriban.Runtime;

namespace eGestion360Web.Services.Reportes
{
    /// <summary>La plantilla o la configuración de una versión no se pueden usar: no compila, el JSON es inválido o falló al generar.</summary>
    public sealed class PlantillaDeReporteInvalidaException : Exception
    {
        public PlantillaDeReporteInvalidaException(string mensaje, Exception? interna = null) : base(mensaje, interna) { }
    }

    /// <summary>Una versión ya compilada: la plantilla Scriban y la configuración convertida a objetos de Scriban. Es inmutable y se reutiliza.</summary>
    public sealed class PlantillaCompilada
    {
        internal PlantillaCompilada(Template plantilla, JsonElement? configuracion)
        {
            Plantilla = plantilla;
            Configuracion = configuracion;
        }

        internal Template Plantilla { get; }
        internal JsonElement? Configuracion { get; }
    }

    /// <summary>
    /// Compila y genera plantillas Scriban (ADR-031, RP2). La plantilla corre en el sandbox de Scriban: sin <c>include</c> (no hay cargador de
    /// plantillas), con tope de vueltas, de recursión y de tamaño de salida, y con la cultura invariable para que una medida 12.5 salga
    /// <c>12.5mm</c> y no <c>12,5mm</c> en el CSS. Los datos de personas se escapan en la propia plantilla con <c>html.escape</c>.
    /// </summary>
    public static class ScribanReporteRenderer
    {
        /// <summary>Tope de vueltas de un <c>for</c> o <c>while</c>.</summary>
        public const int TopeDeVueltas = 2_000;

        /// <summary>Tope de caracteres de la salida (una hoja de constancia ronda los 8 000).</summary>
        public const int TopeDeSalida = 2_000_000;

        /// <summary>Compila el contenido y lee la configuración; falla con un mensaje legible si algo no sirve.</summary>
        public static PlantillaCompilada Compilar(string contenido, string? configuracionJson)
        {
            ArgumentNullException.ThrowIfNull(contenido);
            var plantilla = Template.Parse(contenido);
            if (plantilla.HasErrors)
                throw new PlantillaDeReporteInvalidaException(
                    "La plantilla no compila: " + string.Join("; ", plantilla.Messages.Select(m => m.ToString())));

            JsonElement? configuracion = null;
            if (!string.IsNullOrWhiteSpace(configuracionJson))
            {
                try
                {
                    using var documento = JsonDocument.Parse(configuracionJson);
                    configuracion = documento.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new PlantillaDeReporteInvalidaException("La configuración JSON de la plantilla no es válida: " + ex.Message, ex);
                }
            }
            return new PlantillaCompilada(plantilla, configuracion);
        }

        /// <summary>
        /// Genera el HTML. <paramref name="modelo"/> son las variables de la fuente de datos (diccionarios, listas y valores simples);
        /// la configuración llega como <c>cfg</c>.
        /// </summary>
        public static string Renderizar(PlantillaCompilada compilada, IReadOnlyDictionary<string, object?> modelo)
        {
            ArgumentNullException.ThrowIfNull(compilada);
            ArgumentNullException.ThrowIfNull(modelo);

            var globales = new ScriptObject();
            foreach (var (clave, valor) in modelo) globales[clave] = AScriban(valor);
            globales["cfg"] = compilada.Configuracion is { } cfg ? DeJson(cfg) : new ScriptObject();

            var contexto = new TemplateContext
            {
                LoopLimit = TopeDeVueltas,
                RecursiveLimit = 50,
                OutputLimit = TopeDeSalida,
                StrictVariables = false,
                EnableRelaxedMemberAccess = true,
                TemplateLoader = null
            };
            contexto.PushCulture(CultureInfo.InvariantCulture);
            contexto.PushGlobal(globales);

            try
            {
                return compilada.Plantilla.Render(contexto);
            }
            catch (Exception ex) when (ex is Scriban.Syntax.ScriptRuntimeException or InvalidOperationException)
            {
                throw new PlantillaDeReporteInvalidaException("La plantilla falló al generarse: " + ex.Message, ex);
            }
        }

        /// <summary>Diccionarios a <see cref="ScriptObject"/> y listas a <see cref="ScriptArray"/>, en profundidad.</summary>
        private static object? AScriban(object? valor) => valor switch
        {
            null => null,
            string or bool or int or long or decimal or double => valor,
            IReadOnlyDictionary<string, object?> d => ObjetoDe(d),
            IDictionary<string, object?> d => ObjetoDe(d),
            System.Collections.IEnumerable lista => ListaDe(lista),
            _ => valor
        };

        private static ScriptObject ObjetoDe(IEnumerable<KeyValuePair<string, object?>> pares)
        {
            var objeto = new ScriptObject();
            foreach (var (clave, valor) in pares) objeto[clave] = AScriban(valor);
            return objeto;
        }

        private static ScriptArray ListaDe(System.Collections.IEnumerable lista)
        {
            var arreglo = new ScriptArray();
            foreach (var elemento in lista) arreglo.Add(AScriban(elemento));
            return arreglo;
        }

        private static object? DeJson(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var objeto = new ScriptObject();
                    foreach (var p in e.EnumerateObject()) objeto[p.Name] = DeJson(p.Value);
                    return objeto;
                case JsonValueKind.Array:
                    var arreglo = new ScriptArray();
                    foreach (var x in e.EnumerateArray()) arreglo.Add(DeJson(x));
                    return arreglo;
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.Number:
                    return e.TryGetInt64(out var entero) ? entero : e.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
    }
}
