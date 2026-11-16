using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Pacientes
{
    /// <summary>
    /// Alta de un paciente a la vez, además de la importación por lotes. Usa el mismo servicio que la importación, así que valen las
    /// mismas reglas: DNI obligatorio, la persona se reutiliza si ya está en la cuenta (con la verificación D8, sin revelar nada) y, si se
    /// elige una jornada, se le abre su atención en borrador con la edad declarada. La empresa la pone el servidor, nunca el formulario.
    /// </summary>
    [RequierePermiso("psicologia.pacientes.crear")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPacienteService _pacientes;

        public CreateModel(ApplicationDbContext db, IPacienteService pacientes)
        {
            _db = db;
            _pacientes = pacientes;
        }

        public sealed class FormularioPaciente
        {
            [Required(ErrorMessage = "Escribe el DNI del paciente.")]
            [StringLength(20, ErrorMessage = "El DNI no puede pasar de 20 caracteres.")]
            public string? Dni { get; set; }

            [Required(ErrorMessage = "Escribe el primer nombre.")]
            [StringLength(50, ErrorMessage = "El primer nombre no puede pasar de 50 caracteres.")]
            public string? PrimerNombre { get; set; }

            [StringLength(50, ErrorMessage = "El segundo nombre no puede pasar de 50 caracteres.")]
            public string? SegundoNombre { get; set; }

            [Required(ErrorMessage = "Escribe el primer apellido.")]
            [StringLength(50, ErrorMessage = "El primer apellido no puede pasar de 50 caracteres.")]
            public string? PrimerApellido { get; set; }

            [StringLength(50, ErrorMessage = "El segundo apellido no puede pasar de 50 caracteres.")]
            public string? SegundoApellido { get; set; }

            [Required(ErrorMessage = "Elige el sexo.")]
            public string? Sexo { get; set; }

            [StringLength(30, ErrorMessage = "El teléfono no puede pasar de 30 caracteres.")]
            public string? Telefono { get; set; }

            [EmailAddress(ErrorMessage = "El correo no es válido.")]
            [StringLength(150, ErrorMessage = "El correo no puede pasar de 150 caracteres.")]
            public string? Correo { get; set; }

            /// <summary>Jornada de la que viene; vacío = registro individual, sin atención.</summary>
            public int? IdJornada { get; set; }

            /// <summary>Edad que declara; se anota en la atención de la jornada (decisión D3).</summary>
            [Range(0, 120, ErrorMessage = "La edad debe estar entre 0 y 120 años.")]
            public int? Edad { get; set; }
        }

        [BindProperty]
        public FormularioPaciente Input { get; set; } = new();

        public sealed record OpcionJornada(int IdJornada, string Nombre, DateOnly Fecha);

        /// <summary>Jornadas abiertas de la empresa de la sesión.</summary>
        public List<OpcionJornada> Jornadas { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int? idJornada)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            await CargarJornadasAsync();
            if (idJornada is { } id && Jornadas.Any(j => j.IdJornada == id))
                Input.IdJornada = id;
            return Page();
        }

        /// <param name="accion">"otro" vuelve al formulario vacío (con la misma jornada) para registrar al siguiente.</param>
        public async Task<IActionResult> OnPostAsync(string? accion)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (idEmpresa <= 0)
                ModelState.AddModelError(string.Empty, "Entra a una empresa para registrar pacientes.");

            if (!ModelState.IsValid)
            {
                await CargarJornadasAsync();
                return Page();
            }

            var r = await _pacientes.RegistrarAsync(new RegistrarPacienteInput
            {
                IdEmpresa = idEmpresa,
                IdJornada = Input.IdJornada,
                EdadDeclarada = Input.IdJornada == null ? null : (byte?)Input.Edad,   // sin jornada no hay atención donde anotarla
                Usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system",
                Persona = new PersonaDatosInput
                {
                    PrimerNombre = Input.PrimerNombre,
                    SegundoNombre = Input.SegundoNombre,
                    PrimerApellido = Input.PrimerApellido,
                    SegundoApellido = Input.SegundoApellido,
                    Documento = Input.Dni,
                    Sexo = Input.Sexo,
                    Telefono = Input.Telefono,
                    Email = Input.Correo
                }
            });

            if (!r.Ok)
            {
                foreach (var e in r.Errores)
                    ModelState.AddModelError(CampoDelFormulario(e.Campo), e.Mensaje);
                await CargarJornadasAsync();
                return Page();
            }

            var paciente = r.Paciente!;
            TempData["PacienteMensaje"] = Mensaje(r, Input.IdJornada != null);
            if (r.Advertencias.Count > 0)
                TempData["PacienteAdvertencias"] = string.Join(" ", r.Advertencias);

            if (accion == "otro")
            {
                TempData["PacienteUltimoId"] = paciente.IdPaciente.ToString();
                return RedirectToPage(new { idJornada = Input.IdJornada });
            }
            return RedirectToPage("/Psicologia/Pacientes/Detalle", new { id = paciente.IdPaciente });
        }

        public static string Mensaje(ResultadoPaciente r, bool conJornada)
        {
            var expediente = r.Paciente!.NumeroExpediente;
            var texto = r.Estado switch
            {
                EstadoPaciente.Creado => $"Paciente registrado con el expediente {expediente}.",
                EstadoPaciente.Vinculado => $"La persona ya estaba registrada en la cuenta: se agregó como paciente con el expediente {expediente}.",
                _ => $"Ya era paciente con el expediente {expediente}: no se abrió otro expediente."
            };
            if (r.AtencionNueva) texto += " Se abrió su atención en la jornada.";
            else if (conJornada) texto += " Ya tenía su atención en esa jornada.";
            return texto;
        }

        /// <summary>Lleva el campo de un error del servicio ("Persona.Documento") al del formulario ("Input.Dni").</summary>
        public static string CampoDelFormulario(string campo) => campo switch
        {
            "Persona.Documento" or "Persona.TipoDocumento" or "Persona.PaisEmisor" => "Input.Dni",
            "Persona.Email" => "Input.Correo",
            "Persona.PrimerNombre" or "Persona.SegundoNombre" or "Persona.PrimerApellido" or "Persona.SegundoApellido"
                or "Persona.Sexo" or "Persona.Telefono" => "Input." + campo["Persona.".Length..],
            "IdJornada" => "Input.IdJornada",
            "EdadDeclarada" => "Input.Edad",
            _ => string.Empty
        };

        private async Task CargarJornadasAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Jornadas = await _db.JornadasPsicologia.AsNoTracking()
                .Where(j => j.IdEmpresa == idEmpresa && j.Estado == EstadosJornada.Abierta)
                .OrderByDescending(j => j.FechaInicio).ThenByDescending(j => j.IdJornada)
                .Select(j => new OpcionJornada(j.IdJornada, j.Nombre, j.FechaInicio))
                .ToListAsync();
        }
    }
}
