using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Las pantallas de Flota operan sobre los datos de UNA empresa, la de la sesión. Antes, si la sesión no traía
    /// empresa (el administrador general no pertenece a ninguna), cada pantalla caía en la empresa 1 sin avisar y el
    /// administrador acababa viendo y registrando datos de SIP creyendo que no tocaba nada. Ahora esas sesiones se
    /// desvían a una página con el motivo antes de que corra cualquier handler.
    ///
    /// Es un filtro global por prefijo de ruta (y no una guarda por página) por la misma razón que
    /// <see cref="AdminOnlyPageFilter"/>: cubre de una vez todos los handlers actuales y los que se agreguen después
    /// dentro de /Flota/. Las pantallas de Personas quedan fuera porque ya se bloquean solas con su propio mensaje.
    /// </summary>
    public class EmpresaRequeridaPageFilter : IPageFilter
    {
        /// <summary>Prefijos de ruta (ViewEnginePath) que exigen una empresa en la sesión.</summary>
        public static readonly string[] PrefijosConEmpresa =
        {
            "/Flota/",
        };

        /// <summary>Rutas dentro de esos prefijos que no se desvían: la página del aviso y las que ya se bloquean solas.</summary>
        public static readonly string[] Excepciones =
        {
            PaginaSinEmpresa,
            "/Flota/Catalogos/Personas/",
        };

        /// <summary>Página que explica por qué la sesión no puede entrar a Flota.</summary>
        public const string PaginaSinEmpresa = "/Flota/SinEmpresa";

        /// <summary>True si la página (su ViewEnginePath) necesita empresa en la sesión.</summary>
        public static bool Aplica(string? ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return false;
            if (!PrefijosConEmpresa.Any(p => ruta.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;
            return !Excepciones.Any(e => ruta.StartsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            if (!Aplica(context.ActionDescriptor.ViewEnginePath)) return;

            var http = context.HttpContext;

            if (!AuthHelper.IsAuthenticated(http))
                context.Result = new RedirectToPageResult("/Login");
            else if (AuthHelper.GetEmpresaId(http) is not > 0)
                context.Result = new RedirectToPageResult(PaginaSinEmpresa);
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}
