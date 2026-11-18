using Microsoft.AspNetCore.Mvc.RazorPages;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Los permisos de quien hace la petición, para decidir qué se muestra (menú, tarjetas y botones). Es la misma pregunta que hace
    /// <see cref="PermisosPageFilter"/> antes de cada pantalla (ADR-008: una evaluación por petición para páginas, menú y botones): se
    /// evalúan una sola vez por petición y se comparten por <c>HttpContext.Items</c>, así que preguntar desde diez botones cuesta una lectura.
    /// </summary>
    public interface IPermisosDeLaPeticion
    {
        /// <summary>
        /// Los permisos de la persona. Sin sesión iniciada, o si no se pudieron evaluar, no tiene ninguno: lo que no se puede asegurar no se
        /// muestra (la guarda real sigue siendo la del filtro de la pantalla).
        /// </summary>
        Task<PermisosEfectivos> ObtenerAsync();
    }

    public sealed class PermisosDeLaPeticion : IPermisosDeLaPeticion
    {
        private readonly IHttpContextAccessor _accesor;
        private readonly IEvaluadorDePermisos _evaluador;
        private readonly ILogger<PermisosDeLaPeticion> _log;

        public PermisosDeLaPeticion(IHttpContextAccessor accesor, IEvaluadorDePermisos evaluador, ILogger<PermisosDeLaPeticion> log)
        {
            _accesor = accesor;
            _evaluador = evaluador;
            _log = log;
        }

        public async Task<PermisosEfectivos> ObtenerAsync()
        {
            var http = _accesor.HttpContext;
            if (http == null || !AuthHelper.IsAuthenticated(http)) return PermisosEfectivos.Ninguno;

            if (http.Items.TryGetValue(PermisosPageFilter.ClavePermisosEfectivos, out var guardado) && guardado is PermisosEfectivos ya) return ya;

            try
            {
                var permisos = await _evaluador.EvaluarAsync(ContextoDePermisosHttp.Crear(http), http.RequestAborted);
                http.Items[PermisosPageFilter.ClavePermisosEfectivos] = permisos;
                return permisos;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mostrar menos nunca da más acceso que el que concede el filtro de cada pantalla.
                _log.LogError(ex, "Permisos: no se pudieron evaluar los permisos para armar {Ruta}; se oculta lo que depende de ellos.", http.Request.Path.Value);
                return PermisosEfectivos.Ninguno;
            }
        }
    }

    /// <summary>
    /// <c>this.Puede("flota.personas.crear")</c> para mostrar u ocultar un botón desde el modelo de una pantalla. Lee los permisos que el
    /// filtro de permisos calculó al abrirla (cualquier pantalla con <see cref="RequierePermisoAttribute"/>); si no se calcularon (un centro
    /// de navegación con <see cref="PaginaSinPermisoAttribute"/>), pide los permisos al servicio con <c>await permisos.ObtenerAsync()</c> primero.
    /// Si no hay permisos calculados, oculta. La guarda real es el <c>[RequierePermiso]</c> del handler.
    /// </summary>
    public static class PermisosDeLaPagina
    {
        public static bool Puede(this PageModel pagina, string codigo) =>
            pagina.HttpContext.Items[PermisosPageFilter.ClavePermisosEfectivos] is PermisosEfectivos permisos && permisos.Puede(codigo);
    }
}
