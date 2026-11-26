using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Reportes;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class ConstanciaService : IConstanciaService
    {
        /// <summary>Veces que se intenta de nuevo si otro usuario tomó el mismo número al mismo tiempo (lo rechaza UX_constancias_numero).</summary>
        private const int Intentos = 3;

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<ConstanciaService> _log;
        private readonly IMotorReportes? _motor;

        /// <param name="motor">Motor de reportes versionados (ADR-031). Sin él, la impresión se registra como siempre, con la versión en código.</param>
        public ConstanciaService(ApplicationDbContext db, TimeProvider tiempo, ILogger<ConstanciaService> log, IMotorReportes? motor = null)
        {
            _db = db;
            _tiempo = tiempo;
            _log = log;
            _motor = motor;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>Hoy en Honduras (UTC-6).</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        public async Task<IReadOnlyDictionary<int, string>> SiguientesNumerosAsync(int idEmpresa, int anio, CancellationToken ct = default)
        {
            var tipos = await _db.TiposConstancia.AsNoTracking()
                .Where(t => t.IdEmpresa == idEmpresa && t.Activo)
                .ToListAsync(ct);
            var siguientes = new Dictionary<int, string>();
            foreach (var t in tipos)
            {
                if (t.UsaTalonario)
                    siguientes[t.IdTipoConstancia] = (await SiguienteFolioAsync(idEmpresa, t, ct))?.ToString() ?? MensajeTalonarioAgotado(t);
                else
                    siguientes[t.IdTipoConstancia] = NumeradorDeConstancias.Formato(t.Codigo, anio, await UltimoAsync(idEmpresa, t.IdTipoConstancia, t.Codigo, anio, ct) + 1);
            }
            return siguientes;
        }

        public static string MensajeTalonarioAgotado(TipoConstancia tipo) =>
            $"Talonario {tipo.FolioDesde}-{tipo.FolioHasta} agotado: registra el siguiente en Catálogos.";

        /// <summary>El siguiente folio libre del talonario del tipo (el mayor usado + 1, o el primero); nulo si el talonario se agotó.</summary>
        private async Task<int?> SiguienteFolioAsync(int idEmpresa, TipoConstancia tipo, CancellationToken ct)
        {
            var usados = await FoliosUsadosAsync(idEmpresa, tipo, ct);
            var siguiente = usados.Count == 0 ? tipo.FolioDesde!.Value : usados.Max() + 1;
            return siguiente <= tipo.FolioHasta ? siguiente : null;
        }

        /// <summary>Los folios del talonario en uso que ya tienen constancia (emitida o anulada: una hoja anulada no se vuelve a usar).</summary>
        private async Task<List<int>> FoliosUsadosAsync(int idEmpresa, TipoConstancia tipo, CancellationToken ct)
        {
            var numeros = await _db.Constancias.AsNoTracking()
                .Where(c => c.IdEmpresa == idEmpresa && c.IdTipoConstancia == tipo.IdTipoConstancia)
                .Select(c => c.NumeroConstancia)
                .ToListAsync(ct);
            return numeros.Select(n => int.TryParse(n, out var f) ? f : 0)
                .Where(f => f >= tipo.FolioDesde && f <= tipo.FolioHasta)
                .ToList();
        }

        public async Task<ResultadoPsicologia<Impresion>> RegistrarImpresionAsync(int idEmpresa, int idConstancia, string usuario, int? idCuenta = null,
            bool conElMotor = true, CancellationToken ct = default)
        {
            var constancia = await _db.Constancias.AsNoTracking().Include(c => c.Tipo)
                .FirstOrDefaultAsync(c => c.IdConstancia == idConstancia && c.IdEmpresa == idEmpresa, ct);
            if (constancia == null) return ResultadoPsicologia<Impresion>.Error(string.Empty, "La constancia no existe en esta empresa.");
            if (constancia.Estado != EstadosConstancia.Emitida)
                return ResultadoPsicologia<Impresion>.Error(string.Empty, $"La constancia {constancia.NumeroConstancia} está anulada: no se imprime.");
            var plantilla = PlantillasConstancia.Buscar(constancia.Tipo.Plantilla);
            if (plantilla == null)
                return ResultadoPsicologia<Impresion>.Error(string.Empty, "Este tipo de constancia todavía no tiene formato de impresión.");

            var impresion = new Impresion
            {
                IdEmpresa = idEmpresa, IdConstancia = idConstancia, Plantilla = plantilla.Codigo, VersionPlantilla = plantilla.Version,
                TamanoPapel = plantilla.TamanoPapel, CreadoPor = usuario, FechaCreacion = Ahora()
            };
            _db.ImpresionesPsicologia.Add(impresion);

            // Formato del motor de reportes: la impresión lleva el número de la versión publicada y deja su ejecución en la misma transacción.
            var version = conElMotor && _motor != null && plantilla.CodigoReporte != null ? await _motor.ResolverAsync(plantilla.CodigoReporte, ct) : null;
            if (version != null)
            {
                impresion.VersionPlantilla = version.NumeroVersion.ToString();
                _db.ReporteEjecuciones.Add(_motor!.NuevaEjecucion(version, idEmpresa, ConstanciaPsicologiaDataSource.EntidadOrigen, idConstancia,
                    idCuenta, usuario));
            }

            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<Impresion>.Exito(impresion);
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

            // Sin fecha, la última posible: hoy o, si la evaluación fue en una brigada ya terminada, su último día.
            var rango = await RangoAsync(idEmpresa, paciente.FechaApertura, atencion, ct);
            var fecha = input.FechaEmision ?? rango.Hasta;
            if (rango.Error(fecha) is { } errorFecha) errores.Add(new ErrorValidacion("FechaEmision", errorFecha));

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

            // Resultado: lo pide el formato de impresión del tipo (licencia de conducir: apto o no apto).
            var resultado = string.IsNullOrWhiteSpace(input.Resultado) ? null : input.Resultado.Trim().ToLowerInvariant();
            if (resultado != null && !ResultadosConstancia.Todos.Contains(resultado))
                errores.Add(new ErrorValidacion("Resultado", "Elige si resultó apto o no apto."));
            else if (resultado == null && PlantillasConstancia.Buscar(tipo?.Plantilla)?.PideResultado == true)
                errores.Add(new ErrorValidacion("Resultado", "Elige si resultó apto o no apto: el formato de esta constancia lo imprime."));
            // La constancia no puede decir lo contrario de la conclusión de la evaluación de la que sale (D17).
            else if (resultado != null && atencion?.Conclusion != null && atencion.Conclusion != resultado)
                errores.Add(new ErrorValidacion("Resultado",
                    $"La evaluación concluyó {(atencion.Conclusion == ResultadosConstancia.Apto ? "apto" : "no apto")}: la constancia no puede decir lo contrario."));

            // Folio de la hoja del talonario del Colegio: el que se escribió (dentro del talonario y sin usar) o el siguiente libre.
            if (tipo is { UsaTalonario: true } && input.Folio is { } folio)
            {
                if (folio < tipo.FolioDesde || folio > tipo.FolioHasta)
                    errores.Add(new ErrorValidacion("Folio", $"El folio {folio} no es del talonario en uso ({tipo.FolioDesde} a {tipo.FolioHasta})."));
                else if (await _db.Constancias.AnyAsync(c => c.IdEmpresa == idEmpresa && c.NumeroConstancia == folio.ToString(), ct))
                    errores.Add(new ErrorValidacion("Folio", $"El folio {folio} ya se usó en otra constancia."));
            }

            if (errores.Count > 0) return ResultadoPsicologia<Constancia>.ConErrores(errores);

            for (var intento = 1; ; intento++)
            {
                string numero;
                if (tipo!.UsaTalonario)
                {
                    var folioElegido = input.Folio ?? await SiguienteFolioAsync(idEmpresa, tipo, ct);
                    if (folioElegido == null) return ResultadoPsicologia<Constancia>.Error("Folio", MensajeTalonarioAgotado(tipo));
                    numero = folioElegido.Value.ToString();
                }
                else
                {
                    numero = NumeradorDeConstancias.Formato(tipo.Codigo, fecha.Year, await UltimoAsync(idEmpresa, tipo.IdTipoConstancia, tipo.Codigo, fecha.Year, ct) + 1);
                }

                var ahora = Ahora();
                var constancia = new Constancia
                {
                    IdEmpresa = idEmpresa,
                    IdPaciente = paciente.IdPaciente,
                    IdAtencion = atencion?.IdAtencion,
                    IdTipoConstancia = tipo.IdTipoConstancia,
                    NumeroConstancia = numero,
                    Resultado = resultado,
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
                catch (DbUpdateException ex) when (input.Folio != null)
                {
                    // El folio escrito lo tomó otro usuario al mismo tiempo: no se reintenta con otro número.
                    _log.LogWarning(ex, "Constancia: el folio {Folio} se usó al mismo tiempo en la empresa {IdEmpresa}", input.Folio, idEmpresa);
                    _db.ChangeTracker.Clear();
                    return ResultadoPsicologia<Constancia>.Error("Folio", $"El folio {input.Folio} ya se usó en otra constancia.");
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

        /// <summary>Las fechas posibles para la constancia del paciente (y de la evaluación, si sale de una): ver <see cref="RangoFechaEmision"/>.</summary>
        private async Task<RangoFechaEmision> RangoAsync(int idEmpresa, DateOnly apertura, Atencion? atencion, CancellationToken ct)
        {
            DateOnly? inicio = null, fin = null;
            if (atencion?.IdJornada is { } idJornada)
            {
                var jornada = await _db.JornadasPsicologia.AsNoTracking()
                    .Where(j => j.IdJornada == idJornada && j.IdEmpresa == idEmpresa)
                    .Select(j => new { j.FechaInicio, j.FechaFin })
                    .FirstOrDefaultAsync(ct);
                if (jornada != null) { inicio = jornada.FechaInicio; fin = jornada.FechaFin; }
            }
            return RangoFechaEmision.Calcular(Hoy(), apertura, atencion?.FechaAtencion, inicio, fin);
        }

        public async Task<ResultadoPsicologia<Constancia>> CorregirFechaEmisionAsync(int idEmpresa, int idConstancia, DateOnly? nuevaFecha, string usuario,
            CancellationToken ct = default)
        {
            var constancia = await _db.Constancias.FirstOrDefaultAsync(c => c.IdConstancia == idConstancia && c.IdEmpresa == idEmpresa, ct);
            if (constancia == null) return ResultadoPsicologia<Constancia>.Error(string.Empty, "La constancia no existe en esta empresa.");
            if (constancia.Estado == EstadosConstancia.Anulada)
                return ResultadoPsicologia<Constancia>.Error(string.Empty, $"La constancia {constancia.NumeroConstancia} está anulada: su fecha ya no se corrige.");
            if (nuevaFecha is not { } fecha) return ResultadoPsicologia<Constancia>.Error("FechaEmision", "Elige la fecha de emisión correcta.");
            if (fecha == constancia.FechaEmision)
                return ResultadoPsicologia<Constancia>.Error("FechaEmision", $"La constancia {constancia.NumeroConstancia} ya tiene la fecha {fecha:dd/MM/yyyy}.");

            var apertura = await _db.Pacientes.AsNoTracking()
                .Where(p => p.IdPaciente == constancia.IdPaciente && p.IdEmpresa == idEmpresa)
                .Select(p => p.FechaApertura).FirstAsync(ct);
            var atencion = constancia.IdAtencion is { } idAtencion
                ? await _db.AtencionesPsicologia.AsNoTracking().FirstOrDefaultAsync(a => a.IdAtencion == idAtencion && a.IdEmpresa == idEmpresa, ct)
                : null;
            var rango = await RangoAsync(idEmpresa, apertura, atencion, ct);
            if (rango.Error(fecha) is { } errorFecha) return ResultadoPsicologia<Constancia>.Error("FechaEmision", errorFecha);

            // El correlativo lleva el año (CODIGO-AAAA-NNNNN): la fecha corregida no puede ser de otro año que su número.
            if (AnioDelCorrelativo(constancia.NumeroConstancia) is { } anio && anio != fecha.Year)
                return ResultadoPsicologia<Constancia>.Error("FechaEmision",
                    $"El número {constancia.NumeroConstancia} es del año {anio}: la fecha corregida tiene que ser de ese mismo año.");

            // Solo cambia la fecha de emisión; la bitácora guarda la anterior, la nueva, quién y cuándo. La fecha y el usuario de creación
            // no se tocan (EF rechaza guardarlos cambiados: ver ConstanciaConfiguracion).
            constancia.FechaEmision = fecha;
            constancia.ModificadoPor = usuario;
            constancia.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<Constancia>.Exito(constancia);
        }

        /// <summary>El año de un número CODIGO-AAAA-NNNNN; nulo si el número es un folio del talonario (sin año).</summary>
        internal static int? AnioDelCorrelativo(string numero)
        {
            var m = System.Text.RegularExpressions.Regex.Match(numero, @"-(\d{4})-\d+$");
            return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
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
