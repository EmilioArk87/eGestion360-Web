using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Qué exige abrir una pantalla o ejecutar uno de sus handlers, sin ejecutarla: lo que sirve para decidir si se muestra el enlace, el botón
    /// o la tarjeta que lleva a ella (paso F1.6e-2, ADR-029). Lee los mismos atributos que <see cref="PermisosPageFilter"/>, así que menú y
    /// pantallas nunca discrepan: lo que el menú esconde es lo que la pantalla negaría.
    /// </summary>
    public interface IRequisitosDePaginas
    {
        /// <summary>
        /// Lo que exige el handler de la pantalla <paramref name="rutaAbsoluta"/> (por ejemplo <c>/Flota/Catalogos/Vehiculos/Create</c>) para el verbo
        /// HTTP dado (<c>Get</c> o <c>Post</c>). Nulo si no existe esa pantalla.
        /// </summary>
        Task<RequisitoDePagina?> ObtenerAsync(string rutaAbsoluta, string verbo, string? handler);
    }

    /// <summary>Reglas puras para llegar de un <c>asp-page</c> al permiso: resolver la ruta y encontrar el método del handler.</summary>
    public static class DestinoDePagina
    {
        /// <summary>
        /// Convierte el valor de <c>asp-page</c> en la ruta absoluta de la pantalla, como lo hace Razor Pages: una ruta que empieza con «/» ya
        /// es absoluta y una relativa (<c>Edit</c>, <c>./Edit</c>, <c>../Index</c>) se cuenta desde la carpeta de la pantalla actual.
        /// </summary>
        public static string Resolver(string destino, string paginaActual)
        {
            var valor = destino.Trim();
            string combinado;
            if (valor.StartsWith('/'))
            {
                combinado = valor;
            }
            else
            {
                var i = paginaActual.LastIndexOf('/');
                var carpeta = i <= 0 ? string.Empty : paginaActual[..i];
                combinado = carpeta + "/" + valor;
            }

            var partes = new List<string>();
            foreach (var segmento in combinado.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segmento == ".") continue;
                if (segmento == "..")
                {
                    if (partes.Count > 0) partes.RemoveAt(partes.Count - 1);
                    continue;
                }
                partes.Add(segmento);
            }
            return "/" + string.Join('/', partes);
        }

        /// <summary>
        /// El método que atiende al verbo y al handler (<c>OnPostDelete</c>, <c>OnPostDeleteAsync</c>, <c>OnGetAsync</c>…), con la misma regla de nombres
        /// de Razor Pages, o nulo si la pantalla no lo declara (entonces vale lo que exige la clase).
        /// </summary>
        public static MethodInfo? BuscarHandler(Type pagina, string verbo, string? handler)
        {
            var nombre = "On" + verbo + (handler ?? string.Empty);
            return pagina.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => string.Equals(m.Name, nombre, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(m.Name, nombre + "Async", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Resuelve la clase de cada pantalla una vez y la recuerda: las pantallas no cambian mientras el sitio corre.</summary>
    public sealed class RequisitosDePaginas : IRequisitosDePaginas
    {
        private readonly Lazy<Dictionary<string, PageActionDescriptor>> _paginas;
        private readonly PageLoader _cargador;
        private readonly ConcurrentDictionary<string, Type?> _tipos = new(StringComparer.OrdinalIgnoreCase);

        public RequisitosDePaginas(Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider descriptores, PageLoader cargador)
        {
            _cargador = cargador;
            _paginas = new Lazy<Dictionary<string, PageActionDescriptor>>(() =>
                descriptores.ActionDescriptors.Items.OfType<PageActionDescriptor>()
                    .Where(d => d.ViewEnginePath != null)
                    .GroupBy(d => d.ViewEnginePath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));
        }

        public async Task<RequisitoDePagina?> ObtenerAsync(string rutaAbsoluta, string verbo, string? handler)
        {
            var tipo = await TipoAsync(rutaAbsoluta);
            if (tipo == null) return null;
            return RequisitoDePagina.De(tipo, DestinoDePagina.BuscarHandler(tipo, verbo, handler));
        }

        private async Task<Type?> TipoAsync(string ruta)
        {
            if (_tipos.TryGetValue(ruta, out var conocido)) return conocido;

            // «/Flota» y «/Flota/Index» son la misma pantalla.
            if (!_paginas.Value.TryGetValue(ruta, out var descriptor)) _paginas.Value.TryGetValue(ruta.TrimEnd('/') + "/Index", out descriptor);

            Type? tipo = null;
            if (descriptor != null)
            {
                var compilado = await _cargador.LoadAsync(descriptor, EndpointMetadataCollection.Empty);
                tipo = (compilado.ModelTypeInfo ?? compilado.PageTypeInfo)?.AsType();
            }
            _tipos[ruta] = tipo;
            return tipo;
        }
    }
}
