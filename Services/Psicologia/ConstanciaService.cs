using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class ConstanciaService : IConstanciaService
    {
        /// <summary>Veces que se intenta de nuevo si otro usuario tomó el mismo número al mismo tiempo (lo rechaza UX_constancias_numero).</summary>
        private const int Intentos = 3;

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<ConstanciaService> _log;

        public ConstanciaService(ApplicationDbContext db, TimeProvider tiempo, ILogger<ConstanciaService> log)
        {
            _db = db;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>Hoy en Honduras (UTC-6).</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        public async Task<IReadOnlyDictionary<int, string>> SiguientesNumerosAsync(int idEmpresa, int anio, CancellationToken ct = default)
        {
            var tipos = await _db.TiposConstancia.AsNoTracking()
                .Where(t => t.IdEmpresa == idEmpresa && t.Activo)
                .Select(t => new { t.IdTipoConstancia, t.Codigo })
                .ToListAsync(ct);
            var siguientes = new Dictionary<int, string>();
            foreach (var t in tipos)
                siguientes[t.IdTipoConstancia] = NumeradorDeConstancias.Formato(t.Codigo, anio, await UltimoAsync(idEmpresa, t.IdTipoConstancia, t.Codigo, anio, ct) + 1);
            return siguientes;
        }

        /// <summary>El último correlativo usado por el tipo en el año (0 si ninguno), contando las anuladas.</summary>
        private async Task<int> UltimoAsync(int idEmpresa, int idTipo, string codigo, int anio, CancellationToken ct)
        {
            var prefijo = NumeradorDeConstancias.Prefijo(codigo, anio);
            var numeros = await _db.Constancias.AsNoTracking()
                .Where(c => c.IdEmpresa == idEmpresa && c.IdTipoConstancia == idTipo && c.NumeroConstancia.StartsWith(prefijo))
                .Select(c => c.NumeroConstancia)
                .ToListAsync(ct);
            return numeros.Select(n => NumeradorDeConstancias.Correlativo(n, codigo, anio) ?? 0).DefaultIfEmpty(0).Max();
        }

        public async Task<ResultadoPsicologia<Constancia>> EmitirAsync(EmitirConstanciaInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            var errores = new List<ErrorValidacion>();
            var idEmpresa = input.IdEmpresa;
            if (idEmpresa <= 0) return ResultadoPsicologia<Constancia>.Error(string.Empty, "Entra a una empresa para emitir constancias.");

            var paciente = await _db.Pacientes.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdPaciente == input.IdPaciente && p.IdEmpresa == idEmpresa && !p.Eliminado, ct);
            if (paciente == null) return ResultadoPsicologia<Constancia>.Error(string.Empty, "El paciente no existe en esta empresa.");
            if (!paciente.Activo) errores.Add(new ErrorValidacion(string.Empty, "El paciente está inactivo: actívalo antes de emitirle una constancia."));

            TipoConstancia? tipo = null;
            if (input.IdTipoConstancia is not { } idTipo)
                errores.Add(new ErrorValidacion("IdTipoConstancia", "Elige el tipo de constancia."));
            else
            {
                tipo = await _db.TiposConstancia.AsNoTracking().FirstOrDefaultAsync(t => t.IdTipoConstancia == idTipo && t.IdEmpresa == idEmpresa && t.Activo, ct);
                if (tipo == null) errores.Add(new ErrorValidacion("IdTipoConstancia", "El tipo de constancia no está en el catálogo activo de la empresa."));
            }

            Atencion? atencion = null;
            if (input.IdAtencion is { } idAtencion)
            {
                atencion = await _db.AtencionesPsicologia.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.IdAtencion == idAtencion && a.IdEmpresa == idEmpresa && a.IdPaciente == input.IdPaciente, ct);
                if (atencion == null) errores.Add(new ErrorValidacion("IdAtencion", "La atención no es de este paciente."));
                else if (atencion.Estado == EstadosAtencion.Anulada) errores.Add(new ErrorValidacion("IdAtencion", "La atención está anulada: elige otra."));
            }

            var fecha = input.FechaEmision ?? Hoy();
            if (fecha > Hoy()) errores.Add(new ErrorValidacion("FechaEmision", "La fecha de emisión no puede ser futura."));
            else if (fecha < paciente.FechaApertura)
                errores.Add(new ErrorValidacion("FechaEmision", $"La fecha de emisión no puede ser anterior a la apertura del expediente ({paciente.FechaApertura:dd/MM/yyyy})."));

            var pruebas = (input.IdsPruebas ?? Array.Empty<int>()).Distinct().ToList();
            if (pruebas.Count == 0)
                errores.Add(new ErrorValidacion("Pruebas", "Marca la prueba o las pruebas psicológicas aplicadas."));
            else
            {
                var validas = await _db.PruebasPsicologicas.AsNoTracking()
                    .CountAsync(p => p.IdEmpresa == idEmpresa && p.Activo && pruebas.Contains(p.IdPrueba), ct);
                if (validas != pruebas.Count)
                    errores.Add(new ErrorValidacion("Pruebas", "Alguna prueba elegida no está en el catálogo activo de la empresa."));
            }

            var profesional = NombresPersona.Limpiar(input.NombreProfesional);
            if (profesional.Length > 200) errores.Add(new ErrorValidacion("NombreProfesional", "El nombre del profesional no puede pasar de 200 caracteres."));

            // Quien firma, si se eligió de Profesionales: un psicólogo activo de la empresa (los administradores no firman constancias).
            Profesional? firmante = null;
            if (input.IdProfesional is { } idProfesional)
            {
                firmante = await _db.ProfesionalesPsicologia.AsNoTracking().FirstOrDefaultAsync(
                    p => p.IdProfesional == idProfesional && p.IdEmpresa == idEmpresa && p.Activo && p.Tipo == TiposProfesional.Psicologo, ct);
                if (firmante == null) errores.Add(new ErrorValidacion("IdProfesional", "Elige un psicólogo activo de la empresa."));
            }

            if (errores.Count > 0) return ResultadoPsicologia<Constancia>.ConErrores(errores);

            for (var intento = 1; ; intento++)
            {
                var ahora = Ahora();
                var constancia = new Constancia
                {
                    IdEmpresa = idEmpresa,
                    IdPaciente = paciente.IdPaciente,
                    IdAtencion = atencion?.IdAtencion,
                    IdTipoConstancia = tipo!.IdTipoConstancia,
                    NumeroConstancia = NumeradorDeConstancias.Formato(tipo.Codigo, fecha.Year,
                        await UltimoAsync(idEmpresa, tipo.IdTipoConstancia, tipo.Codigo, fecha.Year, ct) + 1),
                    FechaEmision = fecha,
                    IdPersonaProfesional = firmante?.IdPersona ?? (profesional.Length == 0 ? atencion?.IdPersonaProfesional : null),
                    NombreProfesional = firmante?.NombreImpreso ?? (profesional.Length > 0 ? profesional : atencion?.NombreProfesional),
                    Estado = EstadosConstancia.Emitida,
                    CreadoPor = input.Usuario,
                    FechaCreacion = ahora
                };
                foreach (var idPrueba in pruebas)
                    constancia.Pruebas.Add(new ConstanciaPrueba { IdEmpresa = idEmpresa, IdPrueba = idPrueba, CreadoPor = input.Usuario, FechaCreacion = ahora });
                _db.Constancias.Add(constancia);

                try
                {
                    await _db.SaveChangesAsync(ct);
                    return ResultadoPsicologia<Constancia>.Exito(constancia);
                }
                catch (DbUpdateException ex) when (intento < Intentos)
                {
                    _log.LogWarning(ex, "Constancia {Numero}: número tomado al mismo tiempo en la empresa {IdEmpresa}; intento {Intento}",
                        constancia.NumeroConstancia, idEmpresa, intento);
                    _db.ChangeTracker.Clear();
                }
                catch (DbUpdateException ex)
                {
                    _log.LogError(ex, "No se pudo emitir la constancia en la empresa {IdEmpresa}", idEmpresa);
                    _db.ChangeTracker.Clear();
                    return ResultadoPsicologia<Constancia>.Error(string.Empty, "No se pudo emitir la constancia. Intenta de nuevo.");
                }
            }
        }

        public async Task<ResultadoPsicologia<Constancia>> AnularAsync(int idEmpresa, int idConstancia, string? motivo, string usuario, CancellationToken ct = default)
        {
            var constancia = await _db.Constancias.FirstOrDefaultAsync(c => c.IdConstancia == idConstancia && c.IdEmpresa == idEmpresa, ct);
            if (constancia == null) return ResultadoPsicologia<Constancia>.Error(string.Empty, "La constancia no existe en esta empresa.");
            if (constancia.Estado == EstadosConstancia.Anulada)
                return ResultadoPsicologia<Constancia>.Error(string.Empty, $"La constancia {constancia.NumeroConstancia} ya estaba anulada.");

            var limpio = NombresPersona.Limpiar(motivo);
            if (limpio.Length == 0) return ResultadoPsicologia<Constancia>.Error("Motivo", "Escribe el motivo de la anulación.");
            if (limpio.Length > 300) return ResultadoPsicologia<Constancia>.Error("Motivo", "El motivo no puede pasar de 300 caracteres.");

            var ahora = Ahora();
            constancia.Estado = EstadosConstancia.Anulada;
            constancia.MotivoAnulacion = limpio;
            constancia.FechaAnulacion = ahora;
            constancia.AnuladaPor = usuario;
            constancia.ModificadoPor = usuario;
            constancia.FechaModificacion = ahora;
            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<Constancia>.Exito(constancia);
        }
    }
}
