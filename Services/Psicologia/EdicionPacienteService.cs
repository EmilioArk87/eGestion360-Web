using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Editar los datos de un paciente ya registrado (pedido de Emilio, 2026-11-17): DNI, nombres, sexo, teléfono y correo, los mismos del
    /// alta. Son datos de la persona maestra (ADR-004), así que se guardan con <see cref="IPersonaService.ActualizarAsync"/>: valida el DNI
    /// (no puede ser de otra persona de la empresa), controla la concurrencia y deja cada cambio en la bitácora. Ese servicio borra lo que
    /// llega vacío, por eso el resto de la ficha (nacimiento, dirección, licencia...) se manda tal como está guardado. Si la persona también
    /// es empleado o cliente en otra empresa de la cuenta, el cambio se ve allí.
    /// </summary>
    public interface IEdicionPacienteService
    {
        /// <summary>Los datos del paciente para el formulario, o nulo si no es de la empresa.</summary>
        Task<PacienteParaEditar?> ObtenerAsync(int idEmpresa, int idPaciente, CancellationToken ct = default);

        Task<ResultadoEditarPaciente> GuardarAsync(EditarPacienteInput input, CancellationToken ct = default);
    }

    public sealed record PacienteParaEditar(int IdPaciente, string NumeroExpediente, EditarPacienteInput Datos);

    public sealed class EditarPacienteInput
    {
        public int IdEmpresa { get; set; }
        public int IdPaciente { get; set; }
        public string? Dni { get; set; }
        public string? PrimerNombre { get; set; }
        public string? SegundoNombre { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }
        public string? Sexo { get; set; }
        public string? Telefono { get; set; }
        public string? Correo { get; set; }

        /// <summary>El token de la persona que leyó el formulario: si otro la cambió mientras tanto, no se guarda.</summary>
        public byte[]? TokenConcurrencia { get; set; }

        public string Usuario { get; set; } = "system";
    }

    /// <param name="Errores">Con el campo del formulario: Dni, PrimerNombre, ..., Correo, o vacío si es general.</param>
    public sealed record ResultadoEditarPaciente(bool Ok, IReadOnlyList<ErrorValidacion> Errores, IReadOnlyList<string> Advertencias);

    public sealed class EdicionPacienteService : IEdicionPacienteService
    {
        public const string MensajeNoEncontrado = "El paciente no existe en esta empresa.";

        /// <summary>El DNI ya es de otra persona de la cuenta y los nombres coinciden: desde aquí no se unen fichas.</summary>
        public const string MensajeOtraPersona =
            "Ese DNI ya está registrado para otra persona de la cuenta. Si es la misma persona, el administrador puede unir las dos fichas.";

        private readonly ApplicationDbContext _db;
        private readonly IPersonaService _personas;

        public EdicionPacienteService(ApplicationDbContext db, IPersonaService personas)
        {
            _db = db;
            _personas = personas;
        }

        public async Task<PacienteParaEditar?> ObtenerAsync(int idEmpresa, int idPaciente, CancellationToken ct = default)
        {
            var paciente = await _db.Pacientes.AsNoTracking()
                .Where(p => p.IdPaciente == idPaciente && p.IdEmpresa == idEmpresa && !p.Eliminado)
                .Select(p => new { p.IdPaciente, p.NumeroExpediente, p.Vinculo.IdPersona })
                .FirstOrDefaultAsync(ct);
            if (paciente == null) return null;

            var persona = await _db.Personas.AsNoTracking().Include(p => p.Documentos)
                .FirstAsync(p => p.IdPersona == paciente.IdPersona, ct);
            // El DNI (el principal primero); nunca un pasaporte u otro documento.
            var dni = persona.Documentos.Where(d => !d.Eliminado && d.TipoDocumento == DocumentosIdentidad.Dni)
                .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento).FirstOrDefault();

            return new PacienteParaEditar(paciente.IdPaciente, paciente.NumeroExpediente, new EditarPacienteInput
            {
                IdEmpresa = idEmpresa,
                IdPaciente = paciente.IdPaciente,
                Dni = dni == null ? null : DocumentosIdentidad.FormatearDni(DocumentosIdentidad.NormalizarNumero(dni.Numero)),
                PrimerNombre = persona.PrimerNombre,
                SegundoNombre = persona.SegundoNombre,
                PrimerApellido = persona.PrimerApellido,
                SegundoApellido = persona.SegundoApellido,
                Sexo = persona.Sexo,
                Telefono = persona.Telefono,
                Correo = persona.Email,
                TokenConcurrencia = persona.TokenConcurrencia
            });
        }

        public async Task<ResultadoEditarPaciente> GuardarAsync(EditarPacienteInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.IdEmpresa <= 0) return Error(string.Empty, "Entra a una empresa para editar pacientes.");

            var paciente = await _db.Pacientes.AsNoTracking()
                .Where(p => p.IdPaciente == input.IdPaciente && p.IdEmpresa == input.IdEmpresa && !p.Eliminado)
                .Select(p => new { p.Vinculo.IdPersona })
                .FirstOrDefaultAsync(ct);
            if (paciente == null) return Error(string.Empty, MensajeNoEncontrado);
            if (string.IsNullOrWhiteSpace(input.Dni)) return Error("Dni", "Escribe el DNI del paciente.");

            var p = await _db.Personas.AsNoTracking().FirstAsync(x => x.IdPersona == paciente.IdPersona, ct);
            var datos = new PersonaDatosInput
            {
                IdEmpresa = input.IdEmpresa,
                IdPersona = paciente.IdPersona,
                Empleado = null,   // un paciente no es empleado: su ficha de empleado, si la tiene en esta empresa, no se toca

                // Lo que se edita aquí.
                PrimerNombre = input.PrimerNombre,
                SegundoNombre = input.SegundoNombre,
                PrimerApellido = input.PrimerApellido,
                SegundoApellido = input.SegundoApellido,
                TipoDocumento = DocumentosIdentidad.Dni,
                PaisEmisor = DocumentosIdentidad.PaisHonduras,
                Documento = input.Dni,
                Sexo = input.Sexo,
                Telefono = input.Telefono,
                Email = input.Correo,

                // El resto de la ficha, tal como está: el servicio de personas borra lo que llega vacío.
                FechaNacimiento = p.FechaNacimiento,
                EstadoCivil = p.EstadoCivil,
                TipoSangre = p.TipoSangre,
                PaisNacionalidad = p.PaisNacionalidad,
                IdMunicipioNacimiento = p.IdMunicipioNacimiento,
                IdMunicipioResidencia = p.IdMunicipioResidencia,
                DireccionResidencia = p.DireccionResidencia,
                TelefonoSecundario = p.TelefonoSecundario,
                ContactoEmergenciaNombre = p.ContactoEmergenciaNombre,
                ContactoEmergenciaTelefono = p.ContactoEmergenciaTelefono,
                ContactoEmergenciaParentesco = p.ContactoEmergenciaParentesco,
                LicenciaTipo = p.LicenciaTipo,
                LicenciaNumero = p.LicenciaNumero,
                LicenciaVencimiento = p.LicenciaVencimiento
            };

            var r = await _personas.ActualizarAsync(new ActualizarPersonaInput
            {
                Datos = datos,
                Usuario = input.Usuario,
                TokenConcurrencia = input.TokenConcurrencia,
                ConfirmarFusion = false
            }, ct);

            return r.Estado switch
            {
                EstadoActualizarPersona.Actualizada => new ResultadoEditarPaciente(true, Array.Empty<ErrorValidacion>(), r.Advertencias),
                EstadoActualizarPersona.NoEncontrada => Error(string.Empty, MensajeNoEncontrado),
                EstadoActualizarPersona.RequiereFusion => Error("Dni", MensajeOtraPersona, r.Advertencias),
                _ => new ResultadoEditarPaciente(false,
                    r.Errores.Select(e => e with { Campo = CampoDelFormulario(e.Campo) }).ToList(), r.Advertencias)
            };
        }

        /// <summary>Lleva el campo de un error de la persona ("Documento", "Email") al del formulario ("Dni", "Correo").</summary>
        public static string CampoDelFormulario(string campo) => campo switch
        {
            nameof(PersonaDatosInput.Documento) or nameof(PersonaDatosInput.TipoDocumento) or nameof(PersonaDatosInput.PaisEmisor) => "Dni",
            nameof(PersonaDatosInput.Email) => "Correo",
            nameof(PersonaDatosInput.PrimerNombre) or nameof(PersonaDatosInput.SegundoNombre) or nameof(PersonaDatosInput.PrimerApellido)
                or nameof(PersonaDatosInput.SegundoApellido) or nameof(PersonaDatosInput.Sexo) or nameof(PersonaDatosInput.Telefono) => campo,
            _ => string.Empty
        };

        private static ResultadoEditarPaciente Error(string campo, string mensaje, IReadOnlyList<string>? advertencias = null) =>
            new(false, new[] { new ErrorValidacion(campo, mensaje) }, advertencias ?? Array.Empty<string>());
    }
}
