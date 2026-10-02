using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class EditModel : PersonaPaginaBase
    {
        private readonly IPersonaService _personas;
        private readonly IPersonaConsultaService _consulta;

        public EditModel(
            IPersonaService personas, IPersonaConsultaService consulta,
            IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _personas = personas;
            _consulta = consulta;
        }

        [BindProperty] public PersonaDatosInput Datos { get; set; } = new();
        [BindProperty] public int? IdDepartamentoNacimiento { get; set; }
        [BindProperty] public int? IdDepartamentoResidencia { get; set; }

        /// <summary>Token de concurrencia que leyó la pantalla (base64). Si otro usuario guarda antes, no se pisa su cambio.</summary>
        [BindProperty] public string? TokenConcurrencia { get; set; }

        /// <summary>El usuario confirma la fusión que se le ofreció.</summary>
        [BindProperty] public bool ConfirmarFusion { get; set; }

        public int IdPersona { get; private set; }
        public PersonaEdicion? Edicion { get; private set; }
        public PersonaCatalogos Catalogos { get; private set; } = PersonaCatalogos.Vacios;

        /// <summary>El documento escrito es de otra ficha y los datos coinciden: se ofrece fusionar.</summary>
        public bool OfreceFusion { get; private set; }

        public PersonaFormularioVista Vista => ConstruirVista(
            Datos, Catalogos, IdDepartamentoNacimiento, IdDepartamentoResidencia, esEdicion: true,
            nombresPorSeparar: Edicion is { NombresSeparados: false } ? Edicion.NombresRegistrados : null);

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Editar);
            if (bloqueo != null) return bloqueo;

            IdPersona = id;
            Edicion = await _consulta.ObtenerParaEdicionAsync(IdEmpresa, id, ct);
            if (Edicion == null) return NotFound();

            Datos = Edicion.Datos;
            IdDepartamentoNacimiento = Edicion.IdDepartamentoNacimiento;
            IdDepartamentoResidencia = Edicion.IdDepartamentoResidencia;
            TokenConcurrencia = Convert.ToBase64String(Edicion.TokenConcurrencia);
            Catalogos = await _consulta.CatalogosAsync(IdEmpresa, ct);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Editar);
            if (bloqueo != null) return bloqueo;

            IdPersona = id;

            // La persona y la empresa salen de la ruta y de la sesión, nunca del formulario.
            Datos.IdEmpresa = IdEmpresa;
            Datos.IdPersona = id;
            Datos.Empleado ??= new EmpleadoDatosInput();

            // Solo se edita a quien es de la empresa de la sesión: no se distingue "no existe" de "es de otra".
            Edicion = await _consulta.ObtenerParaEdicionAsync(IdEmpresa, id, ct);
            if (Edicion == null) return NotFound();

            Catalogos = await _consulta.CatalogosAsync(IdEmpresa, ct);

            if (!ModelState.IsValid)
                return Page();

            var resultado = await _personas.ActualizarAsync(new ActualizarPersonaInput
            {
                Datos = Datos,
                Usuario = Usuario,
                TokenConcurrencia = DecodificarToken(TokenConcurrencia),
                ConfirmarFusion = ConfirmarFusion
            }, ct);

            switch (resultado.Estado)
            {
                case EstadoActualizarPersona.NoEncontrada:
                    return NotFound();

                case EstadoActualizarPersona.Actualizada:
                    TempData["Mensaje"] = $"{NombresPersona.ComponerNombres(Datos.PrimerNombre, null)} {NombresPersona.Limpiar(Datos.PrimerApellido)} actualizado correctamente.";
                    GuardarAvisos(resultado.Advertencias);
                    return RedirectToPage("Index");

                case EstadoActualizarPersona.Fusionada:
                    TempData["Mensaje"] = "La ficha se fusionó con la persona que ya estaba registrada con ese documento.";
                    GuardarAvisos(resultado.Advertencias);
                    return RedirectToPage("Index");

                case EstadoActualizarPersona.RequiereFusion:
                    OfreceFusion = true;
                    ConfirmarFusion = false;
                    return Page();

                default:
                    AgregarErrores(resultado.Errores);
                    return Page();
            }
        }

        private static byte[]? DecodificarToken(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            try { return Convert.FromBase64String(texto); }
            catch (FormatException) { return null; }
        }
    }
}
