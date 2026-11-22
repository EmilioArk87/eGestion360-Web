using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Filtro que restringe una página a usuarios administradores.
    /// Sin sesión redirige al Login; con sesión pero sin rol admin, al Menú principal.
    /// Se aplica desde Program.cs a las páginas de mantenimiento.
    /// </summary>
    public class AdminOnlyPageFilter : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;

            if (!AuthHelper.IsAuthenticated(http))
            {
                context.Result = new RedirectToPageResult("/Login");
                return;
            }

            if (!AuthHelper.IsAdmin(http))
            {
                context.Result = new RedirectToPageResult("/MainMenu");
                return;
            }

            await next();
        }
    }
}
