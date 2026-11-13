using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Explica por qué el tenant no puede guardar cambios o entrar (suspendido en solo lectura, suspendido en bloqueo o cancelado).
    /// A ella manda <see cref="EstadoDelTenantMiddleware"/> y responde 403 mientras el motivo siga vigente. Lee el estado de lo que el
    /// middleware dejó en la petición: nada del estado viene de la URL ni de un formulario. La respuesta es la misma página de acceso que
    /// ven quienes no tienen un permiso (<see cref="PaginaDeAcceso"/>, la familia visual del <c>app_offline.htm</c>); por eso no usa el
    /// diseño del sitio, que necesita una sesión que aquí puede estar bloqueada.
    /// </summary>
    [PaginaSinPermiso("Explica por qué no se puede entrar")]
    public class SinAccesoModel : PageModel
    {
        public bool SoloLectura { get; private set; }

        public bool Bloqueado { get; private set; }

        public string NombreDelTenant { get; private set; } = "Tu cuenta";

        public string EstadoEnPalabras { get; private set; } = string.Empty;

        public string? Motivo { get; private set; }

        public IActionResult OnGet()
        {
            if (HttpContext.Items.TryGetValue(EstadoDelTenantMiddleware.ClaveDerechos, out var valor) && valor is DerechosDeLaPeticion derechos)
            {
                var tenant = derechos.Tenant;
                NombreDelTenant = tenant.Codigo;
                Motivo = tenant.MotivoEstado;
                SoloLectura = tenant.EnSoloLectura;
                Bloqueado = tenant.Bloqueado;
                EstadoEnPalabras = tenant.Estado == EstadoTenant.Cancelado ? "cancelado" : "suspendido";
            }

            var motivo = string.IsNullOrWhiteSpace(Motivo) ? null : new LineaDePagina("Motivo: ", Motivo);

            // Mientras haya un motivo, la respuesta es 403; sin él (alguien llegó a la página con todo en orden) es una página normal.
            if (SoloLectura)
            {
                return PaginaDeAcceso.Respuesta(StatusCodes.Status403Forbidden, new DatosDePaginaDeAcceso(
                    "Tu cuenta está en solo lectura",
                    Lineas(new LineaDePagina($"{NombreDelTenant} está suspendido. Puedes consultar tus datos, pero no se guardan cambios mientras dure la suspensión."),
                        new LineaDePagina("El cambio que intentaste no se guardó."), motivo,
                        new LineaDePagina("Si crees que es un error, comunícate con quien administra tu cuenta.")),
                    Referencia: HttpContext.TraceIdentifier));
            }

            if (Bloqueado)
            {
                return PaginaDeAcceso.Respuesta(StatusCodes.Status403Forbidden, new DatosDePaginaDeAcceso(
                    "Tu cuenta no puede usar el sistema por ahora",
                    Lineas(new LineaDePagina($"{NombreDelTenant} está {EstadoEnPalabras} y no tiene acceso a sus pantallas."), motivo,
                        new LineaDePagina("Comunícate con quien administra tu cuenta para reactivarla.")),
                    Referencia: HttpContext.TraceIdentifier,
                    TextoDelBotonPrincipal: "Cerrar sesión", EnlaceDelBotonPrincipal: "/Logout", OfrecerVolverAtras: false));
            }

            return PaginaDeAcceso.Respuesta(StatusCodes.Status200OK, new DatosDePaginaDeAcceso(
                "No hay nada que explicar",
                Lineas(new LineaDePagina("Tu cuenta funciona con normalidad.")),
                Referencia: HttpContext.TraceIdentifier));
        }

        private static IReadOnlyList<LineaDePagina> Lineas(params LineaDePagina?[] lineas) => lineas.OfType<LineaDePagina>().ToList();
    }
}
