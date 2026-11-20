using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Pregunta, antes de cada handler de una página, si quien la pide tiene el permiso que la página declaró con
    /// <see cref="RequierePermisoAttribute"/> (paso F1.6b, ADR-029, D59). Es un filtro global, como los de tenant y de módulo, para que
    /// cubra de una vez todas las pantallas, también las que se agreguen después; una prueba de arquitectura vigila que ninguna quede
    /// sin declarar.
    ///
    /// Según <see cref="OpcionesDePermisos.ModoPara"/> (el modo global y las carpetas de <c>AplicarEn</c>):
    /// <list type="bullet">
    /// <item><description><b>Registrar</b> (el de partida): no niega nada. Si la persona no tendría el permiso, lo anota en el log con la
    /// cuenta, el rol, el tenant, la pantalla y el permiso, y la petición sigue. Sirve para ver el efecto real antes de bloquear.</description></item>
    /// <item><description><b>Aplicar</b>: responde 403. Una pantalla sin declarar, o con un código que no está en el catálogo, también se
    /// niega (cierra por omisión).</description></item>
    /// </list>
    ///
    /// Va al final de los filtros globales: si uno anterior ya desvió la petición (sin empresa, módulo sin derecho), este no llega a
    /// correr, y el registro solo habla de lo que de verdad habría ejecutado la pantalla. Una pantalla que exige permiso exige una
    /// sesión: sin ella manda al inicio de sesión (a una llamada de datos le responde 403), también en modo Registrar, porque eso es
    /// autenticar y no autorizar. Desde F1.6e-3 lo hace este filtro y no cada pantalla; así las cuatro de diagnóstico, que
    /// <c>AdminOnlyPageFilter</c> cerraba, no quedan abiertas al retirarlo. Un error al evaluar nunca tumba la petición en modo Registrar.
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
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public PermisosPageFilter(IEvaluadorDePermisos evaluador, IOptionsMonitor<OpcionesDePermisos> opciones, ILogger<PermisosPageFilter> log,
            IAuditoriaDeSeguridad? auditoria = null)
        {
            _evaluador = evaluador;
            _opciones = opciones;
            _log = log;
            _auditoria = auditoria;
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
            if (!AuthHelper.IsAuthenticated(http)) return SinSesion(http);

            var ruta = context.ActionDescriptor.ViewEnginePath;
            var modo = _opciones.CurrentValue.ModoPara(ruta);
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

            // F1.8b: el acceso denegado deja su rastro. De mejor esfuerzo (nunca cambia la respuesta) y agregado: quien insiste sobre el mismo
            // permiso desde la misma dirección deja una sola fila por hora.
            if (_auditoria != null)
                await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.AccesoDenegado)
                {
                    Resultado = ResultadosDeEvento.Denegado, Agregable = true, ClaveDeAgregado = requisito.Codigo,
                    IdTenant = contexto.IdTenant, ObjetoTipo = "permiso", ObjetoId = requisito.Codigo,
                    Detalle = new { ruta, metodo = http.Request.Method, permiso = requisito.Codigo },
                }, http.RequestAborted);

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

        private static bool PideDatos(HttpContext http)
            => http.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(http.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        /// <summary>Sin sesión (o con un cambio de clave pendiente) no hay a quién evaluar: al inicio de sesión, o 403 a una llamada de datos.</summary>
        private static IActionResult SinSesion(HttpContext http)
            => PideDatos(http) ? new StatusCodeResult(StatusCodes.Status403Forbidden) : new RedirectToPageResult("/Login");

        private static IActionResult Negar(HttpContext http, string? codigo)
        {
            // Una llamada de datos (fetch) no sigue una página: recibe el 403 directo.
            if (PideDatos(http)) return new StatusCodeResult(StatusCodes.Status403Forbidden);

            var definicion = codigo != null ? CatalogoDePermisos.Buscar(codigo) : null;
            var lineas = definicion != null
                ? new[]
                {
                    new LineaDePagina("Tu rol no incluye: ", definicion.Descripcion, "."),
                    new LineaDePagina("Pide a quien administra tu empresa que lo agregue a tu rol y vuelve a intentarlo."),
                }
                : new[]
                {
                    new LineaDePagina("Tu rol no puede abrir esta pantalla."),
                    new LineaDePagina("Pide a quien administra tu empresa que revise tu rol y vuelve a intentarlo."),
                };

            return PaginaDeAcceso.Respuesta(StatusCodes.Status403Forbidden,
                new DatosDePaginaDeAcceso("No tienes permiso para esto", lineas, definicion?.Codigo, http.TraceIdentifier));
        }
    }
}
