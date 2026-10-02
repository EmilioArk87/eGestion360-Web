using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Catalogos.Personas
{
    public class CreateModel : PersonaPaginaBase
    {
        private readonly IPersonaService _personas;
        private readonly IPersonaConsultaService _consulta;

        public CreateModel(
            IPersonaService personas, IPersonaConsultaService consulta,
            IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
            : base(opciones, tiempo)
        {
            _personas = personas;
            _consulta = consulta;
        }

        [BindProperty] public PersonaDatosInput Datos { get; set; } = new();

        // Ayudas del formulario (no se guardan): el departamento solo sirve para elegir el municipio.
        [BindProperty] public int? IdDepartamentoNacimiento { get; set; }
        [BindProperty] public int? IdDepartamentoResidencia { get; set; }

        /// <summary>El usuario vio las personas parecidas y confirma que es otra.</summary>
        [BindProperty] public bool ConfirmarQueEsOtraPersona { get; set; }

        public PersonaCatalogos Catalogos { get; private set; } = PersonaCatalogos.Vacios;
        public IReadOnlyList<PersonaParecida> Parecidas { get; private set; } = Array.Empty<PersonaParecida>();

        public PersonaFormularioVista Vista =>
            ConstruirVista(Datos, Catalogos, IdDepartamentoNacimiento, IdDepartamentoResidencia, esEdicion: false);

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Crear);
            if (bloqueo != null) return bloqueo;

            Datos = new PersonaDatosInput
            {
                TipoDocumento = "DNI",
                PaisEmisor = Opciones.PaisPorDefecto,
                PaisNacionalidad = Opciones.PaisPorDefecto,
                Empleado = new EmpleadoDatosInput { MonedaTarifa = Opciones.MonedaPorDefecto }
            };

            Catalogos = await _consulta.CatalogosAsync(IdEmpresa, ct);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            var bloqueo = Entrar(PermisoPersona.Crear);
            if (bloqueo != null) return bloqueo;

            // La empresa y la identidad nunca vienen del navegador.
            Datos.IdEmpresa = IdEmpresa;
            Datos.IdPersona = null;
            Datos.Empleado ??= new EmpleadoDatosInput();

            Catalogos = await _consulta.CatalogosAsync(IdEmpresa, ct);

            // Errores de conversión (una fecha o un número mal escritos): se devuelve el formulario sin llamar al servicio.
            if (!ModelState.IsValid)
                return Page();

            var resultado = await _personas.CrearAsync(new CrearPersonaInput
            {
                Datos = Datos,
                Usuario = Usuario,
                ConfirmarQueEsOtraPersona = ConfirmarQueEsOtraPersona
            }, ct);

            switch (resultado.Estado)
            {
                case EstadoCrearPersona.Creada:
                case EstadoCrearPersona.Vinculada:
                    var nombre = NombresPersona.ComponerNombres(Datos.PrimerNombre, null) + " " + NombresPersona.Limpiar(Datos.PrimerApellido);
                    TempData["Mensaje"] = $"{nombre.Trim()} registrado correctamente.";
                    GuardarAvisos(resultado.Advertencias);
                    return RedirectToPage("Index");

                case EstadoCrearPersona.RequiereConfirmacion:
                    Parecidas = resultado.Parecidas;
                    ConfirmarQueEsOtraPersona = false;
                    return Page();

                default:
                    AgregarErrores(resultado.Errores);
                    return Page();
            }
        }
    }
}
