using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Exige sesión de administrador del sistema en las páginas de mantenimiento y
    /// diagnóstico. Estas páginas no tenían ningún control de acceso y quedaban
    /// expuestas de forma anónima contra la BD real: volcado de la tabla de usuarios,
    /// historial de códigos de recuperación, reseteo de la contraseña del admin y
    /// configuración SMTP.
    ///
    /// Se aplica como filtro global (y no como guardas por handler, que es el patrón
    /// del resto del sistema) porque así cubre de una sola vez todos los handlers
    /// actuales y futuros de esas páginas, sin depender de recordar la guarda.
    /// </summary>
    public class AdminOnlyPageFilter : IPageFilter
    {
        /// <summary>Rutas de Razor Pages (ViewEnginePath) que exigen rol admin.</summary>
        public static readonly string[] PaginasProtegidas =
        {
            "/Admin/EmailConfig",
            "/ConfigurarHostinger",
            "/DebugUsers",
            "/EncryptPasswords",
            "/ResetAdmin",
            "/ResetCodesHistory",
            "/ValidarEmails",
        };

        private static readonly HashSet<string> Protegidas =
            new(PaginasProtegidas, StringComparer.OrdinalIgnoreCase);

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            if (!Protegidas.Contains(context.ActionDescriptor.ViewEnginePath)) return;

            var http = context.HttpContext;

            if (!AuthHelper.IsAuthenticated(http))
                context.Result = new RedirectToPageResult("/Login");
            else if (!AuthHelper.IsAdmin(http))
                context.Result = new RedirectToPageResult("/MainMenu");
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}
