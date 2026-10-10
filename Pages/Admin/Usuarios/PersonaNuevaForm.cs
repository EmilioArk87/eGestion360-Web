using eGestion360Web.Services;
using eGestion360Web.Services.Personas;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    /// <summary>
    /// Los datos mínimos para crear la persona de un usuario desde la pantalla de usuarios. Las reglas (documento válido y
    /// único, edad, nombres) las aplica <see cref="IUsuarioPersonaService"/>, no este formulario.
    /// </summary>
    public sealed class PersonaNuevaForm
    {
        public string TipoDocumento { get; set; } = "DNI";
        public string? Documento { get; set; }
        public string? PrimerNombre { get; set; }
        public string? SegundoNombre { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }
        public DateOnly? FechaNacimiento { get; set; }
        public bool ConfirmarQueEsOtraPersona { get; set; }

        public PersonaDatosInput ADatos() => new()
        {
            TipoDocumento = TipoDocumento,
            Documento = Documento,
            PrimerNombre = PrimerNombre ?? string.Empty,
            SegundoNombre = SegundoNombre,
            PrimerApellido = PrimerApellido ?? string.Empty,
            SegundoApellido = SegundoApellido,
            FechaNacimiento = FechaNacimiento
        };
    }

    /// <summary>Lo que comparten Crear y Editar usuario para trabajar con la persona.</summary>
    internal static class PersonaDeUsuarioPagina
    {
        /// <summary>Quién opera, según la sesión: el administrador general o el administrador de una empresa.</summary>
        public static QuienOperaUsuarios Quien(HttpContext http) => new(
            AuthHelper.IsAdmin(http),
            AuthHelper.GetEmpresaId(http),
            http.Session.GetString("Username") ?? "system");

        /// <summary>Pasa los errores del servicio al formulario de la persona («PersonaNueva.Campo»).</summary>
        public static void AgregarErrores(ModelStateDictionary modelState, IEnumerable<ErrorValidacion> errores)
        {
            foreach (var e in errores)
            {
                modelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : "PersonaNueva." + e.Campo, e.Mensaje);
            }
        }
    }
}
