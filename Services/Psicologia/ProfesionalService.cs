using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class ProfesionalService : IProfesionalService, IJornadaService
    {
        /// <summary>Igual que en el alta de pacientes (D8): no revela si el documento existe en otra empresa de la cuenta.</summary>
        public const string MensajeNoVerificado =
            "No se pudo registrar al profesional con esos datos. Verifica el DNI, el primer nombre y el primer apellido.";

        private readonly ApplicationDbContext _db;
        private readonly IPersonaValidacionService _validacion;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<ProfesionalService> _log;

        public ProfesionalService(ApplicationDbContext db, IPersonaValidacionService validacion, TimeProvider tiempo, ILogger<ProfesionalService> log)
        {
            _db = db;
            _validacion = validacion;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        public async Task<ResultadoPsicologia<Profesional>> GuardarAsync(ProfesionalInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.IdEmpresa <= 0) return ResultadoPsicologia<Profesional>.Error(string.Empty, "Entra a una empresa para registrar profesionales.");

            var errores = new List<ErrorValidacion>();
            var tipo = (input.Tipo ?? string.Empty).Trim().ToLowerInvariant();
            if (!TiposProfesional.Todos.Contains(tipo)) errores.Add(new ErrorValidacion("Tipo", "Elige si es psicólogo o administrador."));

            var titulo = Opcional(input.Titulo, 30, "Titulo", "El título", errores);
            var especialidad = Opcional(input.Especialidad, 150, "Especialidad", "La especialidad", errores);
            var firma = Opcional(input.NombreFirma, 200, "NombreFirma", "El nombre para la firma", errores);
            var colegiacion = NombresPersona.Limpiar(input.NumeroColegiacion).ToUpperInvariant();
            if (colegiacion.Length > 30) errores.Add(new ErrorValidacion("NumeroColegiacion", "El número de colegiación no puede pasar de 30 caracteres."));
            var numeroColegiacion = colegiacion.Length == 0 ? null : colegiacion;

            var id = input.IdProfesional ?? 0;
            if (numeroColegiacion != null && await _db.ProfesionalesPsicologia.AnyAsync(
                    p => p.IdEmpresa == input.IdEmpresa && p.NumeroColegiacion == numeroColegiacion && p.IdProfesional != id, ct))
                errores.Add(new ErrorValidacion("NumeroColegiacion", $"Ya hay un profesional con la colegiación {numeroColegiacion}."));

            var ahora = Ahora();
            Profesional profesional;
            if (id > 0)
            {
                if (errores.Count > 0) return ResultadoPsicologia<Profesional>.ConErrores(errores);
                var existente = await _db.ProfesionalesPsicologia.Include(p => p.Persona)
                    .FirstOrDefaultAsync(p => p.IdProfesional == id && p.IdEmpresa == input.IdEmpresa, ct);
                if (existente == null) return ResultadoPsicologia<Profesional>.Error(string.Empty, "El profesional no existe en esta empresa.");
                profesional = existente;
                profesional.ModificadoPor = input.Usuario;
                profesional.FechaModificacion = ahora;
                firma ??= $"{existente.Persona.Nombres} {existente.Persona.Apellidos}".Trim();
            }
            else
            {
                var persona = await PersonaAsync(input, errores, ahora, ct);
                if (errores.Count > 0 || persona == null) return ResultadoPsicologia<Profesional>.ConErrores(errores);
                firma ??= $"{persona.Nombres} {persona.Apellidos}".Trim();

                profesional = new Profesional { IdEmpresa = input.IdEmpresa, Persona = persona, Activo = true, CreadoPor = input.Usuario, FechaCreacion = ahora };
                if (persona.IdPersona > 0) profesional.IdPersona = persona.IdPersona;
                _db.ProfesionalesPsicologia.Add(profesional);
            }

            profesional.Tipo = tipo;
            profesional.Titulo = titulo;
            profesional.NombreFirma = firma;
            profesional.NumeroColegiacion = numeroColegiacion;
            profesional.Especialidad = especialidad;

            try
            {
                await _db.SaveChangesAsync(ct);
                return ResultadoPsicologia<Profesional>.Exito(profesional);
            }
            catch (DbUpdateException ex)
            {
                _log.LogWarning(ex, "No se pudo guardar el profesional en la empresa {IdEmpresa}", input.IdEmpresa);
                _db.ChangeTracker.Clear();
                return ResultadoPsicologia<Profesional>.Error(string.Empty, "No se pudo guardar al profesional. Revisa que no esté repetido e intenta de nuevo.");
            }
        }

        /// <summary>
        /// La persona del profesional nuevo: la que ya tiene ese DNI en la cuenta (verificando el nombre si solo está en otra empresa, D8)
        /// o una nueva con un vínculo <c>otro</c> en la empresa. Agrega a <paramref name="errores"/> lo que impida seguir.
        /// </summary>
        private async Task<Persona?> PersonaAsync(ProfesionalInput input, List<ErrorValidacion> errores, DateTime ahora, CancellationToken ct)
        {
            var datos = new PersonaDatosInput
            {
                Modo = ModoValidacionPersona.Alta,
                IdEmpresa = input.IdEmpresa,
                DocumentoDeLaEmpresaEsDuplicado = false,   // si ya está en la empresa (empleado, usuario...), se reutiliza
                PrimerNombre = input.PrimerNombre,
                SegundoNombre = input.SegundoNombre,
                PrimerApellido = input.PrimerApellido,
                SegundoApellido = input.SegundoApellido,
                TipoDocumento = DocumentosIdentidad.Dni,
                PaisEmisor = DocumentosIdentidad.PaisHonduras,
                Documento = input.Dni,
                Sexo = input.Sexo,
                Telefono = input.Telefono,
                Email = input.Correo
            };
            if (string.IsNullOrWhiteSpace(input.Dni)) errores.Add(new ErrorValidacion("Dni", "Escribe el DNI del profesional."));

            var validacion = await _validacion.ValidarAsync(datos, ct);
            errores.AddRange(validacion.Errores.Select(e => e with { Campo = CampoDePersona(e.Campo) }));
            if (errores.Count > 0) return null;

            var n = validacion.Datos;
            Persona persona;
            var tieneVinculo = false;
            if (validacion.DocumentoExistente is { } existente)
            {
                persona = await _db.Personas.FirstAsync(p => p.IdPersona == existente.IdPersona, ct);
                if (!existente.TieneVinculoEnEstaEmpresa && !PacienteService.CoincideVerificacion(persona, n))
                {
                    errores.Add(new ErrorValidacion("Dni", MensajeNoVerificado));
                    return null;
                }
                if (await _db.ProfesionalesPsicologia.AnyAsync(p => p.IdEmpresa == input.IdEmpresa && p.IdPersona == persona.IdPersona, ct))
                {
                    errores.Add(new ErrorValidacion("Dni", "Esa persona ya está registrada como profesional."));
                    return null;
                }
                tieneVinculo = existente.TieneVinculoEnEstaEmpresa;
            }
            else
            {
                persona = PersonasConstructor.Nueva(n, input.Usuario, ahora);
                _db.Personas.Add(persona);
            }

            if (!tieneVinculo)
            {
                var vinculo = new PersonaEmpresa
                {
                    IdEmpresa = input.IdEmpresa, TipoVinculo = TiposVinculo.Otro, FechaInicio = Hoy(), Activo = true,
                    CreadoPor = input.Usuario, FechaCreacion = ahora
                };
                if (persona.IdPersona > 0) { vinculo.IdPersona = persona.IdPersona; _db.PersonaEmpresas.Add(vinculo); }
                else persona.Vinculos.Add(vinculo);
            }
            return persona;
        }

        public async Task<ResultadoPsicologia<bool>> CambiarEstadoAsync(int idEmpresa, int idProfesional, bool activo, string usuario, CancellationToken ct = default)
        {
            var profesional = await _db.ProfesionalesPsicologia.FirstOrDefaultAsync(p => p.IdProfesional == idProfesional && p.IdEmpresa == idEmpresa, ct);
            if (profesional == null) return ResultadoPsicologia<bool>.Error(string.Empty, "El profesional no existe en esta empresa.");
            if (profesional.Activo == activo) return ResultadoPsicologia<bool>.Exito(activo);

            profesional.Activo = activo;
            profesional.ModificadoPor = usuario;
            profesional.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<bool>.Exito(activo);
        }

        public async Task<ResultadoPsicologia<int>> AsignarResponsableAsync(int idEmpresa, int idJornada, int? idProfesional, string usuario, CancellationToken ct = default)
        {
            var jornada = await _db.JornadasPsicologia.FirstOrDefaultAsync(j => j.IdJornada == idJornada && j.IdEmpresa == idEmpresa, ct);
            if (jornada == null) return ResultadoPsicologia<int>.Error(string.Empty, "La jornada no existe en esta empresa.");
            if (jornada.Estado == EstadosJornada.Anulada) return ResultadoPsicologia<int>.Error(string.Empty, "La jornada está anulada.");

            Profesional? profesional = null;
            if (idProfesional is { } id)
            {
                profesional = await _db.ProfesionalesPsicologia.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdProfesional == id && p.IdEmpresa == idEmpresa && p.Activo, ct);
                if (profesional == null) return ResultadoPsicologia<int>.Error("IdProfesional", "Elige un profesional activo de la empresa.");
            }

            var (personaAntes, nombreAntes) = (jornada.IdPersonaResponsable, jornada.NombreResponsable);
            var ahora = Ahora();
            jornada.IdPersonaResponsable = profesional?.IdPersona;
            jornada.NombreResponsable = profesional?.NombreImpreso;
            jornada.ModificadoPor = usuario;
            jornada.FechaModificacion = ahora;

            // Las atenciones en borrador que seguían al responsable anterior pasan al nuevo; las que ya tienen otro profesional, no.
            var atenciones = await _db.AtencionesPsicologia
                .Where(a => a.IdJornada == idJornada && a.IdEmpresa == idEmpresa && a.Estado == EstadosAtencion.Borrador
                            && a.IdPersonaProfesional == personaAntes && a.NombreProfesional == nombreAntes)
                .ToListAsync(ct);
            foreach (var a in atenciones)
            {
                a.IdPersonaProfesional = profesional?.IdPersona;
                a.NombreProfesional = profesional?.NombreImpreso;
                a.ModificadoPor = usuario;
                a.FechaModificacion = ahora;
            }

            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<int>.Exito(atenciones.Count);
        }

        private static string? Opcional(string? valor, int largo, string campo, string etiqueta, List<ErrorValidacion> errores)
        {
            var limpio = NombresPersona.Limpiar(valor);
            if (limpio.Length > largo) errores.Add(new ErrorValidacion(campo, $"{etiqueta} no puede pasar de {largo} caracteres."));
            return limpio.Length == 0 ? null : limpio;
        }

        /// <summary>Del campo de la validación de personas ("Documento") al del formulario de profesionales ("Dni").</summary>
        private static string CampoDePersona(string campo) => campo switch
        {
            nameof(PersonaDatosInput.Documento) or nameof(PersonaDatosInput.TipoDocumento) or nameof(PersonaDatosInput.PaisEmisor) => "Dni",
            nameof(PersonaDatosInput.Email) => "Correo",
            nameof(PersonaDatosInput.PrimerNombre) or nameof(PersonaDatosInput.SegundoNombre) or nameof(PersonaDatosInput.PrimerApellido)
                or nameof(PersonaDatosInput.SegundoApellido) or nameof(PersonaDatosInput.Sexo) or nameof(PersonaDatosInput.Telefono) => campo,
            _ => string.Empty
        };
    }
}
