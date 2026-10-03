using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Pages.Flota.Catalogos.Personas;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Personas
{
    public class EditModel : PersonaAdminPaginaBase
    {
        private readonly IPersonaService _personas;
        private readonly IPersonaAdminConsultaService _consulta;
        private readonly IPersonaConsultaService _catalogos;

        public EditModel(
            IPersonaService personas, IPersonaAdminConsultaService consulta, IPersonaConsultaService catalogos,
            IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _personas = personas;
            _consulta = consulta;
            _catalogos = catalogos;
        }

        [BindProperty] public PersonaDatosInput Datos { get; set; } = new();
        [BindProperty] public int? IdDepartamentoNacimiento { get; set; }
        [BindProperty] public int? IdDepartamentoResidencia { get; set; }

        /// <summary>Token de concurrencia que leyó la pantalla (base64). Si otro usuario guarda antes, no se pisa su cambio.</summary>
        [BindProperty] public string? TokenConcurrencia { get; set; }

        public int IdPersona { get; private set; }
        public PersonaAdminDetalle? Detalle { get; private set; }
        public PersonaEdicion? Edicion { get; private set; }
        public PersonaCatalogos Catalogos { get; private set; } = PersonaCatalogos.Vacios;

        public PersonaFormularioVista Vista => ConstruirVista(
            Datos, Catalogos, IdDepartamentoNacimiento, IdDepartamentoResidencia,
            nombresPorSeparar: Edicion is { NombresSeparados: false } ? Edicion.NombresRegistrados : null);

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar();
            if (bloqueo != null) return bloqueo;

            IdPersona = id;
            if (!await CargarAsync(id, ct)) return NotFound();

            Datos = Edicion!.Datos;
            IdDepartamentoNacimiento = Edicion.IdDepartamentoNacimiento;
            IdDepartamentoResidencia = Edicion.IdDepartamentoResidencia;
            TokenConcurrencia = Convert.ToBase64String(Edicion.TokenConcurrencia);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar();
            if (bloqueo != null) return bloqueo;

            IdPersona = id;

            // La persona sale de la ruta, nunca del formulario; el administrador no trabaja con ninguna empresa y no edita datos de empleo.
            Datos.IdPersona = id;
            Datos.IdEmpresa = 0;
            Datos.Empleado = null;

            if (!await CargarAsync(id, ct)) return NotFound();

            if (!ModelState.IsValid)
                return Page();

            var resultado = await _personas.ActualizarComoAdministradorAsync(new ActualizarPersonaInput
            {
                Datos = Datos,
                Usuario = Usuario,
                TokenConcurrencia = DecodificarToken(TokenConcurrencia)
            }, ct);

            switch (resultado.Estado)
            {
                case EstadoActualizarPersona.NoEncontrada:
                    return NotFound();

                case EstadoActualizarPersona.Actualizada:
                    TempData["Mensaje"] = $"{NombresPersona.ComponerNombres(Datos.PrimerNombre, null)} {NombresPersona.Limpiar(Datos.PrimerApellido)} actualizado correctamente.";
                    GuardarAvisos(resultado.Advertencias);
                    return RedirectToPage("Index");

                default:
                    AgregarErrores(resultado.Errores);
                    return Page();
            }
        }

        private async Task<bool> CargarAsync(int id, CancellationToken ct)
        {
            Detalle = await _consulta.ObtenerAsync(id, ct);
            Edicion = await _consulta.ObtenerParaEdicionAsync(id, ct);
            if (Detalle == null || Edicion == null) return false;

            // Sin empresa: la lista de cargos queda vacía, que es lo que se quiere (no hay sección laboral).
            Catalogos = await _catalogos.CatalogosAsync(0, ct);
            return true;
        }

        private static byte[]? DecodificarToken(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            try { return Convert.FromBase64String(texto); }
            catch (FormatException) { return null; }
        }
    }
}
