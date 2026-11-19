using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Pacientes
{
    /// <summary>
    /// Editar los datos de un paciente ya registrado (pedido de Emilio, 2026-11-17): los mismos del alta, sin la jornada ni la edad, que
    /// son de cada atención. Guarda con <see cref="IEdicionPacienteService"/>. La empresa y el paciente los pone el servidor; el token de
    /// concurrencia viaja oculto para no pisar un cambio que otro hizo mientras tanto.
    /// </summary>
    [RequierePermiso("psicologia.pacientes.editar")]
    public class EditarModel : PageModel
    {
        private readonly IEdicionPacienteService _edicion;

        public EditarModel(IEdicionPacienteService edicion) => _edicion = edicion;

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

            /// <summary>Token de concurrencia de la persona, en base64.</summary>
            public string? Token { get; set; }
        }

        [BindProperty]
        public FormularioPaciente Input { get; set; } = new();

        public int IdPaciente { get; private set; }
        public string Expediente { get; private set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var p = await _edicion.ObtenerAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id);
            if (p == null) return NotFound();

            IdPaciente = p.IdPaciente;
            Expediente = p.NumeroExpediente;
            var d = p.Datos;
            Input = new FormularioPaciente
            {
                Dni = d.Dni, PrimerNombre = d.PrimerNombre, SegundoNombre = d.SegundoNombre, PrimerApellido = d.PrimerApellido,
                SegundoApellido = d.SegundoApellido, Sexo = d.Sexo, Telefono = d.Telefono, Correo = d.Correo,
                Token = d.TokenConcurrencia is { Length: > 0 } t ? Convert.ToBase64String(t) : null
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var p = await _edicion.ObtenerAsync(idEmpresa, id);
            if (p == null) return NotFound();
            IdPaciente = p.IdPaciente;
            Expediente = p.NumeroExpediente;

            if (!ModelState.IsValid) return Page();

            byte[]? token = null;
            if (!string.IsNullOrEmpty(Input.Token))
            {
                try { token = Convert.FromBase64String(Input.Token); }
                catch (FormatException) { return BadRequest(); }
            }

            var r = await _edicion.GuardarAsync(new EditarPacienteInput
            {
                IdEmpresa = idEmpresa,
                IdPaciente = id,
                Dni = Input.Dni,
                PrimerNombre = Input.PrimerNombre,
                SegundoNombre = Input.SegundoNombre,
                PrimerApellido = Input.PrimerApellido,
                SegundoApellido = Input.SegundoApellido,
                Sexo = Input.Sexo,
                Telefono = Input.Telefono,
                Correo = Input.Correo,
                TokenConcurrencia = token,
                Usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system"
            });

            if (!r.Ok)
            {
                foreach (var e in r.Errores)
                    ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : "Input." + e.Campo, e.Mensaje);
                return Page();
            }

            TempData["PacienteMensaje"] = "Datos del paciente actualizados. El cambio queda en la bitácora.";
            if (r.Advertencias.Count > 0)
                TempData["PacienteAdvertencias"] = string.Join(" ", r.Advertencias);
            return RedirectToPage("/Psicologia/Pacientes/Detalle", new { id });
        }
    }
}
