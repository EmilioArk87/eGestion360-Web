using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class PacienteService : IPacienteService
    {
        /// <summary>Se usa siempre que no coincide la verificación: no revela si el documento existe en otra empresa (decisión D8).</summary>
        public const string MensajeNoVerificado =
            "No se pudo registrar al paciente con esos datos. Verifica el DNI, el primer nombre y el primer apellido.";

        private const string MensajeNoGuardado = "No se pudo guardar al paciente. Revisa los datos e intenta de nuevo.";

        private readonly ApplicationDbContext _db;
        private readonly IPersonaValidacionService _validacion;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<PacienteService> _log;

        public PacienteService(ApplicationDbContext db, IPersonaValidacionService validacion, TimeProvider tiempo, ILogger<PacienteService> log)
        {
            _db = db;
            _validacion = validacion;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>Hoy en Honduras (UTC-6).</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        public NumeradorDeExpedientes NuevoNumerador(int idEmpresa) => new(idEmpresa, async (anio, ct) =>
        {
            var prefijo = $"PSI-{anio:D4}-";
            var numeros = await _db.Pacientes.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && p.NumeroExpediente.StartsWith(prefijo))
                .Select(p => p.NumeroExpediente)
                .ToListAsync(ct);
            return numeros.Select(n => NumeradorDeExpedientes.Correlativo(n, anio) ?? 0).DefaultIfEmpty(0).Max();
        });

        public async Task<ResultadoPaciente> RegistrarAsync(RegistrarPacienteInput input, CancellationToken ct = default)
        {
            var r = await PrepararAsync(input, NuevoNumerador(input.IdEmpresa), ct);
            if (!r.Ok) return r;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo registrar al paciente en la empresa {IdEmpresa}", input.IdEmpresa);
                _db.ChangeTracker.Clear();
                return Rechazado(new ErrorValidacion(string.Empty, MensajeNoGuardado), r.Advertencias);
            }
            return r;
        }

        public async Task<ResultadoPaciente> PrepararAsync(RegistrarPacienteInput input, NumeradorDeExpedientes numerador, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(numerador);

            if (input.IdEmpresa <= 0)
                return Rechazado(new ErrorValidacion(string.Empty, "Empresa requerida."));
            if (numerador.IdEmpresa != input.IdEmpresa)
                throw new InvalidOperationException("El numerador de expedientes es de otra empresa.");

            var datos = input.Persona ?? new PersonaDatosInput();
            datos.IdEmpresa = input.IdEmpresa;
            datos.IdPersona = null;
            datos.Modo = ModoValidacionPersona.Alta;
            datos.Empleado = null;                           // un paciente no es empleado: no se exige fecha de nacimiento
            datos.DocumentoDeLaEmpresaEsDuplicado = false;   // si la persona ya está en la empresa, se le agrega el rol
            datos.TipoDocumento ??= DocumentosIdentidad.Dni;
            datos.PaisEmisor ??= DocumentosIdentidad.PaisHonduras;

            var errores = new List<ErrorValidacion>();
            if (string.IsNullOrWhiteSpace(datos.Documento))
                errores.Add(new ErrorValidacion("Persona.Documento", "Escribe el DNI del paciente."));

            var validacion = await _validacion.ValidarAsync(datos, ct);
            errores.AddRange(validacion.Errores.Select(e => e with { Campo = string.IsNullOrEmpty(e.Campo) ? string.Empty : "Persona." + e.Campo }));
            var advertencias = validacion.Advertencias.ToList();
            var n = validacion.Datos;

            // La jornada, si viene, es de esta empresa (la base lo exige además con la clave compuesta).
            Jornada? jornada = null;
            if (input.IdJornada is { } idJornada)
            {
                jornada = await _db.JornadasPsicologia.FirstOrDefaultAsync(j => j.IdJornada == idJornada && j.IdEmpresa == input.IdEmpresa, ct);
                if (jornada == null)
                    errores.Add(new ErrorValidacion("IdJornada", "La jornada no existe en esta empresa."));
                else if (jornada.Estado == EstadosJornada.Anulada)
                    errores.Add(new ErrorValidacion("IdJornada", "La jornada está anulada: elige otra."));
            }

            if (input.EdadDeclarada is > 120)
                errores.Add(new ErrorValidacion("EdadDeclarada", "La edad no puede pasar de 120 años."));

            if (errores.Count > 0)
                return Rechazado(errores, advertencias);

            var ahora = Ahora();
            var usuario = input.Usuario;

            // 1. La persona: la que ya tiene ese DNI en la cuenta o una nueva.
            Persona? personaExistente = null;
            if (validacion.DocumentoExistente is { } existente)
            {
                personaExistente = await _db.Personas.FirstAsync(p => p.IdPersona == existente.IdPersona, ct);

                // Solo está en otras empresas de la cuenta: se verifica antes de vincular nada, sin revelar nada (D8).
                if (!existente.TieneVinculoEnEstaEmpresa && !CoincideVerificacion(personaExistente, n))
                    return Rechazado(new ErrorValidacion("Persona.Documento", MensajeNoVerificado), advertencias);

                if (existente.TieneVinculoEnEstaEmpresa && !CoincideVerificacion(personaExistente, n))
                    advertencias.Add($"El DNI ya es de {personaExistente.Nombres} {personaExistente.Apellidos}: se usan sus datos registrados.");
            }

            Persona persona;
            EstadoPaciente estado;
            if (personaExistente == null)
            {
                persona = PersonasConstructor.Nueva(n, usuario, ahora);
                _db.Personas.Add(persona);
                estado = EstadoPaciente.Creado;
            }
            else
            {
                persona = personaExistente;   // sus datos no se tocan: solo se le agrega el rol
                estado = EstadoPaciente.Vinculado;
            }

            // 2. El vínculo de paciente con la empresa y su ficha.
            Paciente? paciente = null;
            PersonaEmpresa? vinculo = null;
            if (personaExistente != null)
            {
                vinculo = await _db.PersonaEmpresas
                    .Where(v => v.IdPersona == personaExistente.IdPersona && v.IdEmpresa == input.IdEmpresa
                                && v.TipoVinculo == TiposVinculo.Paciente && !v.Eliminado)
                    .OrderBy(v => v.FechaFin == null ? 0 : 1)
                    .ThenByDescending(v => v.IdPersonaEmpresa)
                    .FirstOrDefaultAsync(ct);

                if (vinculo != null)
                {
                    var idVinculo = vinculo.IdPersonaEmpresa;
                    paciente = await _db.Pacientes.FirstOrDefaultAsync(p => p.IdPersonaEmpresa == idVinculo && !p.Eliminado, ct);

                    if (vinculo.FechaFin != null || !vinculo.Activo)
                    {
                        vinculo.FechaFin = null;
                        vinculo.MotivoFin = null;
                        vinculo.Activo = true;
                        vinculo.ModificadoPor = usuario;
                        vinculo.FechaModificacion = ahora;
                    }
                    if (paciente is { Activo: false })
                    {
                        paciente.Activo = true;
                        paciente.ModificadoPor = usuario;
                        paciente.FechaModificacion = ahora;
                    }
                }
            }

            if (vinculo == null)
            {
                vinculo = new PersonaEmpresa
                {
                    IdEmpresa = input.IdEmpresa,
                    TipoVinculo = TiposVinculo.Paciente,
                    FechaInicio = jornada?.FechaInicio ?? input.FechaApertura ?? Hoy(),
                    Activo = true,
                    CreadoPor = usuario,
                    FechaCreacion = ahora
                };
                if (personaExistente == null) persona.Vinculos.Add(vinculo);
                else
                {
                    vinculo.IdPersona = personaExistente.IdPersona;
                    _db.PersonaEmpresas.Add(vinculo);
                }
            }

            if (paciente == null)
            {
                var apertura = input.FechaApertura ?? jornada?.FechaInicio ?? Hoy();
                paciente = new Paciente
                {
                    IdEmpresa = input.IdEmpresa,
                    TipoVinculo = TiposVinculo.Paciente,
                    NumeroExpediente = await numerador.SiguienteAsync(apertura.Year, ct),
                    FechaApertura = apertura,
                    Activo = true,
                    Vinculo = vinculo,
                    CreadoPor = usuario,
                    FechaCreacion = ahora
                };
                _db.Pacientes.Add(paciente);
            }
            else
            {
                estado = EstadoPaciente.YaEraPaciente;
            }

            // 3. La atención de la jornada, si no la tiene ya (reimportar no la duplica, decisión D7).
            var atencionNueva = false;
            if (jornada != null)
            {
                var yaTiene = paciente.IdPaciente > 0 && await _db.AtencionesPsicologia.AnyAsync(
                    a => a.IdPaciente == paciente.IdPaciente && a.IdJornada == jornada.IdJornada && a.Estado != EstadosAtencion.Anulada, ct);
                if (!yaTiene)
                {
                    _db.AtencionesPsicologia.Add(new Atencion
                    {
                        IdEmpresa = input.IdEmpresa,
                        Paciente = paciente,
                        IdJornada = jornada.IdJornada,
                        FechaAtencion = jornada.FechaInicio,
                        EdadDeclarada = input.EdadDeclarada,
                        IdPersonaProfesional = jornada.IdPersonaResponsable,
                        NombreProfesional = jornada.NombreResponsable,
                        Estado = EstadosAtencion.Borrador,
                        CreadoPor = usuario,
                        FechaCreacion = ahora
                    });
                    atencionNueva = true;
                }
            }

            return new ResultadoPaciente(estado, paciente, atencionNueva, Array.Empty<ErrorValidacion>(), advertencias);
        }

        /// <summary>
        /// Verificación del rol (decisión D8): el primer nombre y el primer apellido escritos coinciden con los de la persona existente.
        /// Si ella no los tiene registrados, no se puede verificar.
        /// </summary>
        internal static bool CoincideVerificacion(Persona existente, PersonaDatosNormalizados escritos) =>
            !string.IsNullOrWhiteSpace(existente.PrimerNombre)
            && !string.IsNullOrWhiteSpace(existente.PrimerApellido)
            && NombresPersona.Normalizar(existente.PrimerNombre) == NombresPersona.Normalizar(escritos.PrimerNombre)
            && NombresPersona.Normalizar(existente.PrimerApellido) == NombresPersona.Normalizar(escritos.PrimerApellido);

        private static ResultadoPaciente Rechazado(ErrorValidacion error, IReadOnlyList<string>? advertencias = null) =>
            Rechazado(new[] { error }, advertencias);

        private static ResultadoPaciente Rechazado(IReadOnlyList<ErrorValidacion> errores, IReadOnlyList<string>? advertencias = null) =>
            new(EstadoPaciente.Rechazado, null, false, errores, advertencias ?? Array.Empty<string>());
    }
}
