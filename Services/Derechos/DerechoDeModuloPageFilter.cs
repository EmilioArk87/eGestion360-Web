using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services.Derechos
{
    /// <summary>
    /// Cada carpeta de módulo exige el derecho de uso de ese módulo (paso F1.7): sin él, la pantalla responde 403 y el menú ya no
    /// la ofrece. Es un filtro global por prefijo de ruta, como <see cref="AdminOnlyPageFilter"/>: cubre de una vez todas las
    /// pantallas de la carpeta, también las que se agreguen después, y evita una guarda manual en cada página. «Tener el módulo» es
    /// lo que decide <see cref="DerechosDeLaPeticion.TieneModulo"/>: el plan del tenant lo concede y la empresa lo tiene activo y
    /// vigente. El administrador general (operador) entra a todo.
    ///
    /// La lista de carpetas la vigila <c>DerechosDeUsoTests</c>: una carpeta de módulo nueva sin su derecho hace fallar la prueba.
    /// </summary>
    public class DerechoDeModuloPageFilter : IPageFilter
    {
        /// <summary>Carpeta de pantallas → código del módulo (el derecho es <c>modulo.&lt;código&gt;</c>).</summary>
        public static readonly (string Prefijo, string Modulo)[] ModulosPorRuta =
        {
            ("/Flota/", "flota"),
            ("/Facturacion/", "facturacion"),
            ("/Catalogos/", "catalogos"),
            ("/Contabilidad/", "contabilidad"),
        };

        /// <summary>Páginas dentro de esas carpetas que no exigen el módulo: la que explica por qué Flota no abre sin empresa.</summary>
        public static readonly string[] Excepciones = { "/Flota/SinEmpresa" };

        /// <summary>El módulo que exige una pantalla (su <c>ViewEnginePath</c>); <c>null</c> si no es de ningún módulo.</summary>
        public static string? ModuloDe(string? ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return null;
            if (Excepciones.Any(e => ruta.Equals(e, StringComparison.OrdinalIgnoreCase))) return null;

            foreach (var (prefijo, modulo) in ModulosPorRuta)
                if (ruta.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) return modulo;
            return null;
        }

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            var modulo = ModuloDe(context.ActionDescriptor.ViewEnginePath);
            if (modulo == null) return;

            var http = context.HttpContext;
            // Sin sesión, cada pantalla manda al inicio de sesión; el operador entra a todo.
            if (!AuthHelper.IsAuthenticated(http) || AuthHelper.IsAdmin(http)) return;
            if (AuthHelper.HasModulo(http, modulo)) return;

            context.Result = PaginaDeAcceso.Respuesta(StatusCodes.Status403Forbidden, new DatosDePaginaDeAcceso(
                "No tienes acceso a este módulo",
                new[]
                {
                    new LineaDePagina("Tu cuenta no tiene el módulo «", modulo, "» en su plan o no está activado para tu empresa."),
                    new LineaDePagina("Pide a quien administra tu cuenta que lo active."),
                },
                Referencia: http.TraceIdentifier));
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}
