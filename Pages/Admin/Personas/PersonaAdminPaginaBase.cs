using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using eGestion360Web.Pages.Flota.Catalogos.Personas;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Personas
{
    /// <summary>
    /// Lo que comparten las pantallas de «Personas del sistema»: solo entra el administrador general (rol admin), que no
    /// trabaja con ninguna empresa y ve a todas las personas de todas las empresas. Cualquier otro usuario, incluido el
    /// administrador de una empresa, vuelve al menú.
    /// </summary>
    public abstract class PersonaAdminPaginaBase : PageModel
    {
        private readonly PersonaValidacionOptions _opciones;
        private readonly TimeProvider _tiempo;

        protected PersonaAdminPaginaBase(IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
        {
            _opciones = opciones.Value;
            _tiempo = tiempo;
        }

        protected string Usuario => HttpContext.Session.GetString("Username") ?? "system";

        /// <summary>Nulo si el usuario es el administrador general; si no, la redirección que la página debe devolver.</summary>
        protected IActionResult? Entrar()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToPage("/MainMenu");

            return null;
        }

        /// <summary>El formulario compartido en su versión del administrador: solo datos personales, con los umbrales y la fecha de hoy en Honduras (UTC-6).</summary>
        protected PersonaFormularioVista ConstruirVista(
            PersonaDatosInput datos, PersonaCatalogos catalogos,
            int? idDepartamentoNacimiento, int? idDepartamentoResidencia, string? nombresPorSeparar = null) => new()
        {
            Datos = datos,
            Catalogos = catalogos,
            IdDepartamentoNacimiento = idDepartamentoNacimiento,
            IdDepartamentoResidencia = idDepartamentoResidencia,
            EsEdicion = true,
            SoloDatosPersonales = true,
            NombresPorSeparar = nombresPorSeparar,
            CodigoConductor = _opciones.CodigoCargoConductor,
            EdadMinima = _opciones.EdadMinima,
            EdadMinimaConductor = _opciones.EdadMinimaConductor,
            Hoy = DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6))
        };

        /// <summary>Pasa los errores del servicio a ModelState con los nombres de campo del formulario (Datos.*).</summary>
        protected void AgregarErrores(IEnumerable<ErrorValidacion> errores)
        {
            foreach (var error in errores)
            {
                var clave = string.IsNullOrEmpty(error.Campo) ? string.Empty : "Datos." + error.Campo;
                ModelState.AddModelError(clave, error.Mensaje);
            }
        }

        protected void GuardarAvisos(IReadOnlyList<string> avisos)
        {
            if (avisos.Count > 0)
                TempData["Avisos"] = string.Join(" · ", avisos);
        }
    }
}
