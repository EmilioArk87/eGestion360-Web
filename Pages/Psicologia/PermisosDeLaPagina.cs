using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Psicologia
{
    /// <summary>
    /// Para mostrar u ocultar botones según los permisos de la sesión. Los calcula el filtro de permisos al abrir la pantalla (cualquier
    /// pantalla con <c>[RequierePermiso]</c>); si no se pudieron calcular, se oculta. La guarda real es el <c>[RequierePermiso]</c> del handler.
    /// </summary>
    internal static class PermisosDeLaPagina
    {
        public static bool Puede(this PageModel pagina, string codigo) =>
            pagina.HttpContext.Items[PermisosPageFilter.ClavePermisosEfectivos] is PermisosEfectivos permisos && permisos.Puede(codigo);
    }
}
