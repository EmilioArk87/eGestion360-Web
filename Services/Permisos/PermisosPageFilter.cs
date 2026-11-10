using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Pregunta, antes de cada handler de una página, si quien la pide tiene el permiso que la página declaró con
    /// <see cref="RequierePermisoAttribute"/> (paso F1.6b, ADR-029, D59). Es un filtro global, como los de tenant y de módulo, para que
    /// cubra de una vez todas las pantallas, también las que se agreguen después; una prueba de arquitectura vigila que ninguna quede
    /// sin declarar.
    ///
    /// Según <see cref="OpcionesDePermisos.ModoActual"/>:
    /// <list type="bullet">
    /// <item><description><b>Registrar</b> (el de partida): no niega nada. Si la persona no tendría el permiso, lo anota en el log con la
    /// cuenta, el rol, el tenant, la pantalla y el permiso, y la petición sigue. Sirve para ver el efecto real antes de bloquear.</description></item>
    /// <item><description><b>Aplicar</b>: responde 403. Una pantalla sin declarar, o con un código que no está en el catálogo, también se
    /// niega (cierra por omisión).</description></item>
    /// </list>
    ///
    /// Va al final de los filtros globales: si uno anterior ya desvió la petición (sin sesión, sin empresa, módulo sin derecho), este no
    /// llega a correr, y el registro solo habla de lo que de verdad habría ejecutado la pantalla. No actúa sin sesión iniciada: cada
    /// pantalla manda sola al inicio de sesión. Un error al evaluar nunca tumba la petición en modo Registrar.
    /// </summary>
    public sealed class PermisosPageFilter : IAsyncPageFilter
    {
        /// <summary>Clave de <c>HttpContext.Items</c> con los <see cref="PermisosEfectivos"/> de la petición (ausente si no hubo que evaluarlos).</summary>
        public const string ClavePermisosEfectivos = "eg360.permisos";

        // Las pantallas sin declarar se avisan una vez por ruta y no en cada petición: es un descuido de programación, no un evento.
        private static readonly ConcurrentDictionary<string, bool> RutasAvisadas = new(StringComparer.OrdinalIgnoreCase);

        private readonly IEvaluadorDePermisos _evaluador;
        private readonly IOptionsMonitor<OpcionesDePermisos> _opciones;
        private readonly ILogger<PermisosPageFilter> _log;

        public PermisosPageFilter(IEvaluadorDePermisos evaluador, IOptionsMonitor<OpcionesDePermisos> opciones, ILogger<PermisosPageFilter> log)
        {
            _evaluador = evaluador;
            _opciones = opciones;
            _log = log;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var negacion = await RevisarAsync(context);
            if (negacion != null)
            {
                context.Result = negacion;
                return;
            }

            await next();
        }

        /// <summary>Devuelve la respuesta que niega la petición, o <c>null</c> si sigue su curso.</summary>
        private async Task<IActionResult?> RevisarAsync(PageHandlerExecutingContext context)
        {
            var http = context.HttpContext;
            var requisito = RequisitoDePagina.De(TipoDeLaPagina(context), context.HandlerMethod?.MethodInfo);

            if (requisito.Tipo == TipoDeRequisito.SinPermiso) return null;
            if (!AuthHelper.IsAuthenticated(http)) return null;

            var modo = _opciones.CurrentValue.ModoActual;
            var ruta = context.ActionDescriptor.ViewEnginePath;
            var handler = context.HandlerMethod?.MethodInfo?.Name ?? "(sin handler)";

            if (requisito.Tipo == TipoDeRequisito.SinDeclarar || !CatalogoDePermisos.Existe(requisito.Codigo!))
            {
                if (RutasAvisadas.TryAdd(ruta, true))
                    _log.LogError("Permisos: la pantalla {Ruta} no declaró un permiso válido (código: {Codigo}). Agrega [RequierePermiso] o [PaginaSinPermiso] a su clase.",
                        ruta, requisito.Codigo ?? "ninguno");
                return modo == ModoDePermisos.Aplicar ? Negar(http, null) : null;
            }

            PermisosEfectivos permisos;
            try
            {
                permisos = await EvaluarUnaVezAsync(http);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin poder evaluar: registrar nunca tumba la petición; aplicar cierra por omisión.
                _log.LogError(ex, "Permisos: no se pudieron evaluar los permisos para {Metodo} {Ruta} ({Handler}).", http.Request.Method, ruta, handler);
                return modo == ModoDePermisos.Aplicar ? Negar(http, requisito.Codigo) : null;
            }

            if (permisos.Puede(requisito.Codigo!)) return null;

            var contexto = ContextoDePermisosHttp.Crear(http);
            if (modo == ModoDePermisos.Registrar)
            {
                _log.LogWarning("Permisos [registro]: se negaría {Metodo} {Ruta} ({Handler}) por falta de {Permiso}; cuenta {IdCuenta}, rol {RolLegado}/{IdRol}, tenant {IdTenant}.",
                    http.Request.Method, ruta, handler, requisito.Codigo, AuthHelper.GetCuentaId(http), contexto.RolLegado, contexto.IdRol, contexto.IdTenant);
                return null;
            }

            _log.LogWarning("Permisos: se niega {Metodo} {Ruta} ({Handler}) por falta de {Permiso}; cuenta {IdCuenta}, rol {RolLegado}/{IdRol}, tenant {IdTenant}.",
                http.Request.Method, ruta, handler, requisito.Codigo, AuthHelper.GetCuentaId(http), contexto.RolLegado, contexto.IdRol, contexto.IdTenant);
            return Negar(http, requisito.Codigo);
        }

        /// <summary>La clase que declara el permiso: el modelo de la página (o la página misma si no tiene modelo).</summary>
        private static Type TipoDeLaPagina(PageHandlerExecutingContext context)
        {
            if (context.HandlerInstance != null) return context.HandlerInstance.GetType();
            if (context.ActionDescriptor is CompiledPageActionDescriptor descriptor) return (descriptor.ModelTypeInfo ?? descriptor.PageTypeInfo).AsType();
            return typeof(object);
        }

        /// <summary>Los permisos de la petición, evaluados una sola vez aunque varios filtros o vistas los pidan.</summary>
        private async Task<PermisosEfectivos> EvaluarUnaVezAsync(HttpContext http)
        {
            if (http.Items.TryGetValue(ClavePermisosEfectivos, out var guardado) && guardado is PermisosEfectivos ya) return ya;

            var permisos = await _evaluador.EvaluarAsync(ContextoDePermisosHttp.Crear(http), http.RequestAborted);
            http.Items[ClavePermisosEfectivos] = permisos;
            return permisos;
        }

        // La página declara UTF-8: solo hay que escapar lo que el HTML interpreta, no los acentos.
        private static string EscaparHtml(string texto)
            => texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static IActionResult Negar(HttpContext http, string? codigo)
        {
            // Una llamada de datos (fetch) no sigue una página: recibe el 403 directo.
            var pideDatos = http.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(http.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            if (pideDatos) return new StatusCodeResult(StatusCodes.Status403Forbidden);

            var queFalta = codigo != null && CatalogoDePermisos.Buscar(codigo) is { } p
                ? EscaparHtml(p.Descripcion)
                : "acceder a esta pantalla";
            return new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/html; charset=utf-8",
                Content = "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Sin permiso</title></head><body style=\"font-family:sans-serif;margin:2rem\">"
                          + $"<h1>Sin permiso</h1><p>Tu rol no incluye el permiso para: {queFalta}.</p>"
                          + "<p>Pide a quien administra tu empresa que lo agregue a tu rol.</p>"
                          + "<p><a href=\"/MainMenu\">Volver al menú principal</a></p></body></html>"
            };
        }
    }
}
