using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Decide si algo que lleva a otra pantalla se muestra: solo si quien mira puede abrirla. Pregunta lo mismo que el filtro de permisos
    /// (<see cref="IRequisitosDePaginas"/> y <see cref="IPermisosDeLaPeticion"/>), así que el menú no ofrece lo que la pantalla negaría y no
    /// esconde lo que dejaría pasar. Solo oculta: la guarda real sigue siendo la del filtro de cada pantalla.
    /// </summary>
    public sealed class DecisorDeVisibilidad
    {
        private readonly IRequisitosDePaginas _requisitos;
        private readonly IPermisosDeLaPeticion _permisos;
        private readonly IOptionsMonitor<OpcionesDePermisos> _opciones;

        public DecisorDeVisibilidad(IRequisitosDePaginas requisitos, IPermisosDeLaPeticion permisos, IOptionsMonitor<OpcionesDePermisos> opciones)
        {
            _requisitos = requisitos;
            _permisos = permisos;
            _opciones = opciones;
        }

        /// <summary>True si hay que ocultar el elemento que lleva a <paramref name="destino"/> (o al handler indicado) con el verbo dado.</summary>
        public async Task<bool> DebeOcultarAsync(ViewContext vista, string? destino, string? handler, string verbo)
        {
            var actual = (vista.ActionDescriptor as PageActionDescriptor)?.ViewEnginePath ?? vista.RouteData.Values["page"]?.ToString();
            if (string.IsNullOrEmpty(actual) && string.IsNullOrWhiteSpace(destino)) return false;

            var ruta = string.IsNullOrWhiteSpace(destino) ? actual! : DestinoDePagina.Resolver(destino, actual ?? "/");
            return await DebeOcultarRutaAsync(ruta, handler, verbo);
        }

        /// <summary>
        /// True si quien mira puede abrir (GET) la pantalla de ruta absoluta <paramref name="rutaAbsoluta"/>. Es la pregunta del menú
        /// configurable (<see cref="Navegacion.CatalogoDeNavegacion"/>), que arma sus enlaces fuera de una vista.
        /// </summary>
        public async Task<bool> PuedeAbrirAsync(string rutaAbsoluta)
            => !await DebeOcultarRutaAsync(DestinoDePagina.Resolver(rutaAbsoluta, "/"), null, "Get");

        private async Task<bool> DebeOcultarRutaAsync(string ruta, string? handler, string verbo)
        {
            var requisito = await _requisitos.ObtenerAsync(ruta, verbo, handler);

            // Sin pantalla conocida, sin permiso declarado o de las que no piden uno: se muestra (no hay nada que asegurar).
            if (requisito is not { Tipo: TipoDeRequisito.Permiso, Codigo: { } codigo }) return false;

            // En modo «Registrar» nada se niega, así que tampoco se oculta nada.
            if (_opciones.CurrentValue.ModoPara(ruta) != ModoDePermisos.Aplicar) return false;

            var permisos = await _permisos.ObtenerAsync();
            return !permisos.Puede(codigo);
        }
    }

    /// <summary>
    /// Oculta los enlaces (<c>&lt;a asp-page&gt;</c>), formularios y botones con <c>asp-page-handler</c> que llevan a una pantalla o a una acción
    /// que quien mira no puede usar. Un <c>&lt;a&gt;</c> es un GET; un formulario usa su <c>method</c> (GET si no lo dice); un botón, POST.
    /// </summary>
    [HtmlTargetElement("a", Attributes = "asp-page")]
    [HtmlTargetElement("form", Attributes = "asp-page")]
    [HtmlTargetElement("form", Attributes = "asp-page-handler")]
    [HtmlTargetElement("button", Attributes = "asp-page-handler")]
    [HtmlTargetElement("input", Attributes = "asp-page-handler")]
    public sealed class OcultarSinPermisoTagHelper : TagHelper
    {
        private readonly DecisorDeVisibilidad _decisor;

        public OcultarSinPermisoTagHelper(DecisorDeVisibilidad decisor) => _decisor = decisor;

        // Antes que los tag helpers de enlaces y formularios: si el elemento se oculta, no se arma su URL.
        public override int Order => -1000;

        [HtmlAttributeName("asp-page")]
        public string? Pagina { get; set; }

        [HtmlAttributeName("asp-page-handler")]
        public string? Handler { get; set; }

        [ViewContext, HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = null!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var verbo = output.TagName.ToLowerInvariant() switch
            {
                "a" => "Get",
                "form" => string.Equals(context.AllAttributes["method"]?.Value?.ToString(), "post", StringComparison.OrdinalIgnoreCase) ? "Post" : "Get",
                _ => "Post",
            };

            if (await _decisor.DebeOcultarAsync(ViewContext, Pagina, Handler, verbo)) output.SuppressOutput();
        }
    }

    /// <summary>
    /// Oculta cualquier elemento (una tarjeta, un elemento de lista) que contenga el acceso a una pantalla si quien mira no puede abrirla:
    /// <c>&lt;div class="col" eg-si-puede-abrir="/Flota/Catalogos/Vehiculos/Index"&gt;</c>. Sirve para que un centro de navegación no deje tarjetas
    /// vacías. Con varias pantallas separadas por «;» (la tarjeta de un subcentro) se muestra si se puede abrir alguna.
    /// </summary>
    [HtmlTargetElement(Attributes = "eg-si-puede-abrir")]
    public sealed class SiPuedeAbrirTagHelper : TagHelper
    {
        private readonly DecisorDeVisibilidad _decisor;

        public SiPuedeAbrirTagHelper(DecisorDeVisibilidad decisor) => _decisor = decisor;

        public override int Order => -1000;

        [HtmlAttributeName("eg-si-puede-abrir")]
        public string Destino { get; set; } = string.Empty;

        [ViewContext, HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = null!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            // Varias pantallas separadas por «;» (la tarjeta de un subcentro): se muestra si se puede abrir alguna.
            var destinos = Destino.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var destino in destinos)
                if (!await _decisor.DebeOcultarAsync(ViewContext, destino, null, "Get")) return;

            if (destinos.Length > 0) output.SuppressOutput();
        }
    }
}
