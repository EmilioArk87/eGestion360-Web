using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public enum PermisoPersona
    {
        Ver,
        Crear,
        Editar
    }

    /// <summary>
    /// Lo que comparten las pantallas de Personas: entrar solo con sesión, con la empresa de la sesión y con el
    /// permiso del módulo de flota. La empresa SIEMPRE sale de la sesión, nunca del navegador, y no se asume
    /// ninguna por defecto: sin empresa, la pantalla se bloquea con un mensaje.
    /// </summary>
    public abstract class PersonaPaginaBase : PageModel
    {
        private const string CodigoModulo = "flota";

        private readonly PersonaValidacionOptions _opciones;
        private readonly TimeProvider _tiempo;

        protected PersonaPaginaBase(IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
        {
            _opciones = opciones.Value;
            _tiempo = tiempo;
        }

        protected PersonaValidacionOptions Opciones => _opciones;

        /// <summary>Si no es nulo, la vista muestra este mensaje en lugar del contenido.</summary>
        public string? MensajeBloqueo { get; private set; }

        /// <summary>Empresa de la sesión; solo es válida después de <see cref="Entrar"/>.</summary>
        protected int IdEmpresa { get; private set; }

        protected string Usuario => HttpContext.Session.GetString("Username") ?? "system";

        /// <summary>
        /// Comprueba la sesión, la empresa y el permiso. Devuelve nulo si se puede continuar; si no, el resultado que
        /// la página debe devolver (la redirección al inicio de sesión o la misma página con su mensaje de bloqueo).
        /// </summary>
        protected IActionResult? Entrar(PermisoPersona permiso)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (IdEmpresa <= 0)
            {
                MensajeBloqueo = "Esta pantalla trabaja con la empresa de tu sesión y tu usuario no tiene una. " +
                                 "Entra con un usuario de la empresa para ver o registrar personal.";
                return Page();
            }

            var permitido = permiso switch
            {
                PermisoPersona.Ver => AuthHelper.PuedeVer(HttpContext, CodigoModulo),
                PermisoPersona.Crear => AuthHelper.PuedeCrear(HttpContext, CodigoModulo),
                PermisoPersona.Editar => AuthHelper.PuedeEditar(HttpContext, CodigoModulo),
                _ => false
            };
            if (!permitido)
            {
                MensajeBloqueo = permiso switch
                {
                    PermisoPersona.Ver => "No tienes permiso para ver el personal.",
                    PermisoPersona.Crear => "No tienes permiso para registrar personal.",
                    _ => "No tienes permiso para modificar el personal."
                };
                return Page();
            }

            return null;
        }

        /// <summary>Pasa los errores del servicio a ModelState con los nombres de campo del formulario (Datos.*).</summary>
        protected void AgregarErrores(IEnumerable<ErrorValidacion> errores)
        {
            foreach (var error in errores)
            {
                var clave = string.IsNullOrEmpty(error.Campo) ? string.Empty : "Datos." + error.Campo;
                ModelState.AddModelError(clave, error.Mensaje);
            }
        }

        /// <summary>Arma lo que necesita el formulario compartido, con los umbrales y la fecha de hoy en Honduras (UTC-6).</summary>
        protected PersonaFormularioVista ConstruirVista(
            PersonaDatosInput datos, PersonaCatalogos catalogos,
            int? idDepartamentoNacimiento, int? idDepartamentoResidencia, bool esEdicion, string? nombresPorSeparar = null) => new()
        {
            Datos = datos,
            Catalogos = catalogos,
            IdDepartamentoNacimiento = idDepartamentoNacimiento,
            IdDepartamentoResidencia = idDepartamentoResidencia,
            EsEdicion = esEdicion,
            NombresPorSeparar = nombresPorSeparar,
            CodigoConductor = _opciones.CodigoCargoConductor,
            EdadMinima = _opciones.EdadMinima,
            EdadMinimaConductor = _opciones.EdadMinimaConductor,
            Hoy = DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6))
        };

        /// <summary>Deja las advertencias del servicio para mostrarlas tras redirigir.</summary>
        protected void GuardarAvisos(IReadOnlyList<string> avisos)
        {
            if (avisos.Count > 0)
                TempData["Avisos"] = string.Join(" · ", avisos);
        }
    }

    /// <summary>Lo que necesita el formulario compartido por Nuevo y Editar.</summary>
    public sealed class PersonaFormularioVista
    {
        public PersonaDatosInput Datos { get; init; } = new();
        public PersonaCatalogos Catalogos { get; init; } = PersonaCatalogos.Vacios;
        public int? IdDepartamentoNacimiento { get; init; }
        public int? IdDepartamentoResidencia { get; init; }
        public bool EsEdicion { get; init; }

        /// <summary>El texto antiguo «nombres apellidos», solo cuando todavía no está separado.</summary>
        public string? NombresPorSeparar { get; init; }

        /// <summary>Código del cargo que exige licencia (sección Conductor).</summary>
        public string CodigoConductor { get; init; } = "CONDUCTOR";

        public int EdadMinima { get; init; } = 16;
        public int EdadMinimaConductor { get; init; } = 18;

        /// <summary>Fecha de hoy en Honduras, para que el navegador compare igual que el servidor.</summary>
        public DateOnly Hoy { get; init; }
    }
}
