using System.Globalization;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class ImportacionPsicologiaService : IImportacionPsicologiaService
    {
        /// <summary>Mensaje de la fila de una persona que ya tiene la atención de la jornada: se omite sola (reimportar no duplica).</summary>
        public const string MensajeYaAtendida = "Ya tiene la atención de esta jornada.";

        private readonly ApplicationDbContext _db;
        private readonly IPacienteService _pacientes;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<ImportacionPsicologiaService> _log;

        public ImportacionPsicologiaService(ApplicationDbContext db, IPacienteService pacientes, TimeProvider tiempo, ILogger<ImportacionPsicologiaService> log)
        {
            _db = db;
            _pacientes = pacientes;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        // ── Paso 1: borrador ─────────────────────────────────────────────────

        public async Task<ResultadoImportacion> CrearBorradorAsync(int idEmpresa, int idJornada, ArchivoBrigada archivo, string usuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(archivo);

            var jornada = await _db.JornadasPsicologia.AsNoTracking().FirstOrDefaultAsync(j => j.IdJornada == idJornada && j.IdEmpresa == idEmpresa, ct);
            if (jornada == null) return ResultadoImportacion.Falla("Elige una jornada de esta empresa para la importación.");
            if (jornada.Estado == EstadosJornada.Anulada) return ResultadoImportacion.Falla("La jornada está anulada: elige otra.");

            var yaConfirmada = await _db.ImportacionesPsicologia.AsNoTracking()
                .Where(i => i.IdEmpresa == idEmpresa && i.HashSha256 == archivo.HashSha256 && i.Estado == EstadosImportacion.Confirmada)
                .Select(i => (DateTime?)i.FechaConfirmacion)
                .FirstOrDefaultAsync(ct);
            if (yaConfirmada != null)
                return ResultadoImportacion.Falla($"Este mismo archivo ya se importó el {yaConfirmada.Value.AddHours(-6):dd/MM/yyyy}. No hace falta subirlo otra vez.");

            var ahora = Ahora();
            var importacion = new Importacion
            {
                IdEmpresa = idEmpresa,
                IdJornada = idJornada,
                NombreArchivo = Cortar(archivo.NombreArchivo, 260)!,
                HashSha256 = archivo.HashSha256,
                Hoja = Cortar(archivo.Hoja, 100),
                Estado = EstadosImportacion.Borrador,
                CreadoPor = usuario,
                FechaCreacion = ahora
            };

            foreach (var f in archivo.Filas)
            {
                var reparto = RepartoDeNombres.Repartir(f.Nombre);
                importacion.Filas.Add(new ImportacionFila
                {
                    IdEmpresa = idEmpresa,
                    NumeroFila = f.NumeroFila,
                    NombreOriginal = Cortar(f.Nombre, 300),
                    DniOriginal = Cortar(f.Dni, 30),
                    EdadOriginal = Cortar(f.Edad, 10),
                    GeneroOriginal = Cortar(f.Genero, 10),
                    PrimerNombre = Cortar(reparto.PrimerNombre, 50),
                    SegundoNombre = Cortar(reparto.SegundoNombre, 50),
                    PrimerApellido = Cortar(reparto.PrimerApellido, 50),
                    SegundoApellido = Cortar(reparto.SegundoApellido, 50),
                    CreadoPor = usuario,
                    FechaCreacion = ahora
                });
            }

            await AnalizarAsync(importacion, repartoConfirmado: _ => false, ct);
            Contar(importacion);

            _db.ImportacionesPsicologia.Add(importacion);
            await _db.SaveChangesAsync(ct);

            var listas = importacion.Filas.Count(f => f.Resultado is ResultadosFilaImportacion.Nueva or ResultadosFilaImportacion.Reutilizada);
            var revisar = importacion.Filas.Count(f => f.Resultado == ResultadosFilaImportacion.Revisar);
            var errores = importacion.Filas.Count(f => f.Resultado == ResultadosFilaImportacion.Error);
            return new ResultadoImportacion(true, true,
                $"{importacion.TotalFilas} filas leídas: {listas} listas, {revisar} por revisar y {errores} con error.", importacion);
        }

        // ── Paso 2: correcciones ─────────────────────────────────────────────

        public async Task<ResultadoImportacion> CorregirFilaAsync(int idEmpresa, int idImportacion, CorreccionFila correccion, string usuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(correccion);

            var importacion = await CargarAsync(idEmpresa, idImportacion, ct);
            if (importacion == null) return ResultadoImportacion.NoEncontrado();
            if (importacion.Estado != EstadosImportacion.Borrador)
                return ResultadoImportacion.Falla("La importación ya no está en borrador: no se puede cambiar.", importacion);

            var fila = importacion.Filas.FirstOrDefault(f => f.IdImportacionFila == correccion.IdImportacionFila);
            if (fila == null) return ResultadoImportacion.Falla("La fila no es de esta importación.", importacion);
            if (fila.Resultado == ResultadosFilaImportacion.Omitida && !correccion.Omitir && fila.Mensaje == MensajeYaAtendida)
                return ResultadoImportacion.Falla($"La fila {fila.NumeroFila} ya tiene la atención de esta jornada: no hace falta importarla.", importacion);

            var ahora = Ahora();
            fila.ModificadoPor = usuario;
            fila.FechaModificacion = ahora;
            importacion.ModificadoPor = usuario;
            importacion.FechaModificacion = ahora;

            if (correccion.Omitir)
            {
                fila.Resultado = ResultadosFilaImportacion.Omitida;
                fila.Mensaje = "Omitida por quien importa.";
            }
            else
            {
                // Siempre llegan los cuatro campos (la pantalla manda lo que se ve): un campo vacío es un nombre que no tiene.
                fila.PrimerNombre = Opcional(correccion.PrimerNombre);
                fila.SegundoNombre = Opcional(correccion.SegundoNombre);
                fila.PrimerApellido = Opcional(correccion.PrimerApellido);
                fila.SegundoApellido = Opcional(correccion.SegundoApellido);
                fila.Resultado = ResultadosFilaImportacion.Pendiente;
                fila.Mensaje = null;
            }

            // Todo el lote se vuelve a analizar: omitir una de dos filas con el mismo DNI deja lista a la otra. El reparto de las filas
            // que ya corrigió una persona (ModificadoPor) cuenta como confirmado.
            await AnalizarAsync(importacion, repartoConfirmado: f => f.ModificadoPor != null, ct);
            Contar(importacion);
            await _db.SaveChangesAsync(ct);
            return new ResultadoImportacion(true, true, $"Fila {fila.NumeroFila}: {fila.Mensaje ?? "lista"}", importacion);
        }

        // ── Paso 3: confirmar ────────────────────────────────────────────────

        public async Task<ResultadoImportacion> ConfirmarAsync(int idEmpresa, int idImportacion, string usuario, CancellationToken ct = default)
        {
            var importacion = await CargarAsync(idEmpresa, idImportacion, ct);
            if (importacion == null) return ResultadoImportacion.NoEncontrado();
            if (importacion.Estado != EstadosImportacion.Borrador)
                return ResultadoImportacion.Falla("La importación ya no está en borrador.", importacion);
            if (importacion.IdJornada == null)
                return ResultadoImportacion.Falla("Elige la jornada de la importación antes de confirmar.", importacion);
            if (await _db.ImportacionesPsicologia.AnyAsync(i => i.IdEmpresa == idEmpresa && i.HashSha256 == importacion.HashSha256
                                                                && i.Estado == EstadosImportacion.Confirmada && i.IdImportacion != idImportacion, ct))
                return ResultadoImportacion.Falla("Este mismo archivo ya se importó desde otro borrador. Descarta este.", importacion);

            // Se vuelve a analizar: entre la subida y la confirmación otra persona pudo registrar a alguien.
            await AnalizarAsync(importacion, repartoConfirmado: f => f.ModificadoPor != null, ct);
            Contar(importacion);

            var pendientes = importacion.Filas.Count(f => f.Resultado is ResultadosFilaImportacion.Revisar or ResultadosFilaImportacion.Error or ResultadosFilaImportacion.Pendiente);
            if (pendientes > 0)
            {
                await _db.SaveChangesAsync(ct);
                return ResultadoImportacion.Falla(
                    $"Quedan {pendientes} filas por revisar o con error. Corrige el reparto de nombres u omítelas antes de confirmar.", importacion);
            }

            var listas = importacion.Filas
                .Where(f => f.Resultado is ResultadosFilaImportacion.Nueva or ResultadosFilaImportacion.Reutilizada)
                .OrderBy(f => f.NumeroFila).ToList();
            if (listas.Count == 0)
                return ResultadoImportacion.Falla("No hay filas para importar: todas están omitidas.", importacion);

            var ahora = Ahora();
            var numerador = _pacientes.NuevoNumerador(idEmpresa);
            int nuevas = 0, reutilizadas = 0;

            await using var transaccion = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                foreach (var fila in listas)
                {
                    var r = await _pacientes.PrepararAsync(new RegistrarPacienteInput
                    {
                        IdEmpresa = idEmpresa,
                        IdJornada = importacion.IdJornada,
                        EdadDeclarada = Edad(fila.EdadOriginal).Valor,
                        Usuario = usuario,
                        Persona = new PersonaDatosInput
                        {
                            PrimerNombre = fila.PrimerNombre,
                            SegundoNombre = fila.SegundoNombre,
                            PrimerApellido = fila.PrimerApellido,
                            SegundoApellido = fila.SegundoApellido,
                            TipoDocumento = DocumentosIdentidad.Dni,
                            PaisEmisor = DocumentosIdentidad.PaisHonduras,
                            Documento = fila.DniOriginal,
                            Sexo = Sexo(fila.GeneroOriginal)
                        }
                    }, numerador, ct);

                    if (!r.Ok)
                    {
                        // Nada se guarda: se revierte y se deja la fila marcada para que la corrijan.
                        var detalle = string.Join(" ", r.Errores.Select(e => e.Mensaje));
                        if (transaccion != null) await transaccion.RollbackAsync(ct);
                        _db.ChangeTracker.Clear();
                        await MarcarErrorAsync(idEmpresa, idImportacion, fila.IdImportacionFila, detalle, usuario, ct);
                        return ResultadoImportacion.Falla($"La fila {fila.NumeroFila} no se pudo importar: {detalle} No se guardó ninguna fila.",
                            await ObtenerAsync(idEmpresa, idImportacion, ct));
                    }

                    fila.Paciente = r.Paciente;
                    fila.Resultado = r.Estado == EstadoPaciente.Creado ? ResultadosFilaImportacion.Nueva : ResultadosFilaImportacion.Reutilizada;
                    fila.Mensaje = r.Estado switch
                    {
                        EstadoPaciente.Creado => "Persona y paciente nuevos.",
                        EstadoPaciente.Vinculado => "La persona ya existía: se le abrió su expediente.",
                        _ => r.AtencionNueva ? "Ya era paciente: se le abrió la atención de la jornada." : "Ya era paciente y ya tenía la atención de esta jornada."
                    };
                    fila.ModificadoPor = usuario;
                    fila.FechaModificacion = ahora;
                    if (r.Estado == EstadoPaciente.Creado) nuevas++; else reutilizadas++;
                }

                importacion.FilasNuevas = nuevas;
                importacion.FilasReutilizadas = reutilizadas;
                importacion.FilasRechazadas = importacion.Filas.Count(f => f.Resultado == ResultadosFilaImportacion.Omitida);
                importacion.Estado = EstadosImportacion.Confirmada;
                importacion.FechaConfirmacion = ahora;
                importacion.ConfirmadoPor = usuario;
                importacion.ModificadoPor = usuario;
                importacion.FechaModificacion = ahora;

                await _db.SaveChangesAsync(ct);
                if (transaccion != null) await transaccion.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo confirmar la importación {IdImportacion} de la empresa {IdEmpresa}", idImportacion, idEmpresa);
                if (transaccion != null) await transaccion.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return ResultadoImportacion.Falla(
                    "No se pudo guardar la importación (quizá otra persona importó al mismo tiempo). No se guardó ninguna fila: inténtalo de nuevo.",
                    await ObtenerAsync(idEmpresa, idImportacion, ct));
            }

            return new ResultadoImportacion(true, true,
                $"Importación confirmada: {nuevas} pacientes nuevos y {reutilizadas} que ya existían, con su atención en la jornada.", importacion);
        }

        public async Task<ResultadoImportacion> DescartarAsync(int idEmpresa, int idImportacion, string usuario, CancellationToken ct = default)
        {
            var importacion = await CargarAsync(idEmpresa, idImportacion, ct);
            if (importacion == null) return ResultadoImportacion.NoEncontrado();
            if (importacion.Estado != EstadosImportacion.Borrador)
                return ResultadoImportacion.Falla("Solo se puede descartar una importación en borrador.", importacion);

            importacion.Estado = EstadosImportacion.Descartada;
            importacion.ModificadoPor = usuario;
            importacion.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
            return new ResultadoImportacion(true, true, "Importación descartada: no se creó ningún paciente.", importacion);
        }

        public Task<Importacion?> ObtenerAsync(int idEmpresa, int idImportacion, CancellationToken ct = default) =>
            _db.ImportacionesPsicologia.AsNoTracking()
                .Include(i => i.Filas.OrderBy(f => f.NumeroFila))
                .FirstOrDefaultAsync(i => i.IdImportacion == idImportacion && i.IdEmpresa == idEmpresa, ct);

        // ── Análisis de las filas ────────────────────────────────────────────

        private Task<Importacion?> CargarAsync(int idEmpresa, int idImportacion, CancellationToken ct) =>
            _db.ImportacionesPsicologia
                .Include(i => i.Filas)
                .FirstOrDefaultAsync(i => i.IdImportacion == idImportacion && i.IdEmpresa == idEmpresa, ct);

        /// <summary>
        /// Decide el resultado de cada fila no omitida: error (DNI inválido o repetido en el archivo, sexo, edad o nombre que no
        /// sirven), revisar (reparto de nombres por confirmar o el DNI es de una persona con otro nombre), omitida (ya tiene la
        /// atención de esta jornada) o lista (nueva o reutilizada).
        /// </summary>
        /// <param name="repartoConfirmado">Filas cuyo reparto de nombres ya escribió o confirmó una persona: no se revisa otra vez.</param>
        private async Task AnalizarAsync(Importacion importacion, Func<ImportacionFila, bool> repartoConfirmado, CancellationToken ct)
        {
            var anioActual = DateOnly.FromDateTime(Ahora().AddHours(-6)).Year;
            var activas = importacion.Filas.Where(f => f.Resultado != ResultadosFilaImportacion.Omitida).ToList();

            // DNI de cada fila y los que se repiten dentro del archivo.
            var dniPorFila = activas.ToDictionary(f => f, f => DocumentosIdentidad.NormalizarNumero(f.DniOriginal));
            var repetidos = dniPorFila.Where(x => x.Value.Length > 0).GroupBy(x => x.Value).Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Key.NumeroFila).Order().ToList());

            // Personas de la cuenta que ya tienen esos DNI (el filtro de tenant deja ver solo las de la cuenta).
            var dnis = dniPorFila.Values.Where(d => d.Length == 13).Distinct().ToList();
            var existentes = await _db.PersonaDocumentos.AsNoTracking()
                .Where(d => !d.Eliminado && d.TipoDocumento == DocumentosIdentidad.Dni && d.PaisEmisor == DocumentosIdentidad.PaisHonduras
                            && d.NumeroNormalizado != null && dnis.Contains(d.NumeroNormalizado))
                .Select(d => new { NumeroNormalizado = d.NumeroNormalizado!, d.IdPersona, d.Persona.PrimerNombre, d.Persona.PrimerApellido, d.Persona.Nombres, d.Persona.Apellidos })
                .ToListAsync(ct);
            var personaPorDni = existentes.GroupBy(e => e.NumeroNormalizado, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var idsPersonas = existentes.Select(e => e.IdPersona).Distinct().ToList();
            var enLaEmpresa = await _db.PersonaEmpresas.AsNoTracking()
                .Where(v => idsPersonas.Contains(v.IdPersona) && v.IdEmpresa == importacion.IdEmpresa && !v.Eliminado)
                .Select(v => v.IdPersona).Distinct().ToListAsync(ct);
            var conAtencionEnLaJornada = importacion.IdJornada == null ? new List<int>() : await (
                from a in _db.AtencionesPsicologia.AsNoTracking()
                join p in _db.Pacientes.AsNoTracking() on a.IdPaciente equals p.IdPaciente
                join v in _db.PersonaEmpresas.AsNoTracking() on p.IdPersonaEmpresa equals v.IdPersonaEmpresa
                where a.IdJornada == importacion.IdJornada && a.Estado != EstadosAtencion.Anulada && idsPersonas.Contains(v.IdPersona)
                select v.IdPersona).Distinct().ToListAsync(ct);

            foreach (var fila in activas)
            {
                var (resultado, mensaje) = Evaluar(fila, dniPorFila[fila], repetidos, anioActual, repartoConfirmado(fila));
                if (resultado == null)
                {
                    if (personaPorDni.TryGetValue(dniPorFila[fila], out var p))
                    {
                        var coincide = NombresPersona.Normalizar(p.PrimerNombre) == NombresPersona.Normalizar(fila.PrimerNombre)
                                       && NombresPersona.Normalizar(p.PrimerApellido) == NombresPersona.Normalizar(fila.PrimerApellido);
                        if (conAtencionEnLaJornada.Contains(p.IdPersona))
                            (resultado, mensaje) = (ResultadosFilaImportacion.Omitida, MensajeYaAtendida);
                        else if (!coincide && enLaEmpresa.Contains(p.IdPersona))
                            (resultado, mensaje) = (ResultadosFilaImportacion.Revisar,
                                $"El DNI ya es de {p.Nombres} {p.Apellidos} en la empresa: corrige el nombre o confirma que es esa persona.");
                        else if (!coincide)
                            // Está solo en otra empresa de la cuenta: no se revela su nombre (decisión D8).
                            (resultado, mensaje) = (ResultadosFilaImportacion.Revisar, "El DNI ya está registrado con otro nombre: verifica el nombre y el DNI.");
                        else
                            (resultado, mensaje) = (ResultadosFilaImportacion.Reutilizada, "La persona ya existe: se reutiliza.");
                    }
                    else
                    {
                        (resultado, mensaje) = (ResultadosFilaImportacion.Nueva, null);
                    }
                }

                fila.Resultado = resultado;
                fila.Mensaje = Cortar(mensaje, 500);
            }
        }

        /// <summary>Lo que se decide con la fila sola; nulo si está bien y falta ver si la persona ya existe.</summary>
        private static (string? Resultado, string? Mensaje) Evaluar(
            ImportacionFila fila, string dni, IReadOnlyDictionary<string, List<int>> repetidos, int anioActual, bool repartoConfirmado)
        {
            if (dni.Length == 0) return (ResultadosFilaImportacion.Error, "La fila no trae DNI.");
            if (!DocumentosIdentidad.TryAnalizarDni(dni, out var partes))
                return (ResultadosFilaImportacion.Error, "El DNI debe tener 13 dígitos (por ejemplo 0501-1990-12345).");
            if (DocumentosIdentidad.ValidarPartesDni(partes, anioActual) is { } errorDni)
                return (ResultadosFilaImportacion.Error, errorDni);
            if (repetidos.TryGetValue(dni, out var filas))
                return (ResultadosFilaImportacion.Error, $"El DNI se repite en las filas {string.Join(", ", filas)} del archivo.");

            if (fila.GeneroOriginal != null && Sexo(fila.GeneroOriginal) == null)
                return (ResultadosFilaImportacion.Error, "El género debe ser M o F.");
            var edad = Edad(fila.EdadOriginal);
            if (!edad.Ok) return (ResultadosFilaImportacion.Error, "La edad debe ser un número entre 0 y 120.");

            if (string.IsNullOrWhiteSpace(fila.PrimerNombre) || string.IsNullOrWhiteSpace(fila.PrimerApellido))
                return (ResultadosFilaImportacion.Revisar, "Escribe al menos el primer nombre y el primer apellido.");
            foreach (var parte in new[] { fila.PrimerNombre, fila.SegundoNombre, fila.PrimerApellido, fila.SegundoApellido })
                if (!string.IsNullOrWhiteSpace(parte) && (!NombresPersona.EsValido(NombresPersona.Limpiar(parte)) || parte.Length > 50))
                    return (ResultadosFilaImportacion.Revisar, "Los nombres y apellidos solo admiten letras, espacios, apóstrofo y guion, hasta 50 caracteres.");

            if (!repartoConfirmado)
            {
                var reparto = RepartoDeNombres.Repartir(fila.NombreOriginal);
                if (reparto.RequiereRevision) return (ResultadosFilaImportacion.Revisar, reparto.Motivo);
            }

            return (null, null);
        }

        private static void Contar(Importacion importacion)
        {
            importacion.TotalFilas = importacion.Filas.Count;
            importacion.FilasNuevas = importacion.Filas.Count(f => f.Resultado == ResultadosFilaImportacion.Nueva);
            importacion.FilasReutilizadas = importacion.Filas.Count(f => f.Resultado == ResultadosFilaImportacion.Reutilizada);
            importacion.FilasRechazadas = importacion.Filas.Count(f => f.Resultado is ResultadosFilaImportacion.Error or ResultadosFilaImportacion.Omitida);
        }

        private async Task MarcarErrorAsync(int idEmpresa, int idImportacion, int idFila, string detalle, string usuario, CancellationToken ct)
        {
            var fila = await _db.ImportacionFilasPsicologia.FirstOrDefaultAsync(
                f => f.IdImportacionFila == idFila && f.IdImportacion == idImportacion && f.IdEmpresa == idEmpresa, ct);
            if (fila == null) return;
            fila.Resultado = ResultadosFilaImportacion.Revisar;
            fila.Mensaje = Cortar(detalle, 500);
            fila.ModificadoPor = usuario;
            fila.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
        }

        // ── Valores ──────────────────────────────────────────────────────────

        /// <summary>M o F desde lo que traiga el archivo (M, F, Masculino, Femenino, Hombre, Mujer); nulo si no se reconoce.</summary>
        public static string? Sexo(string? genero) => LectorExcelBrigada.Normalizar(genero) switch
        {
            "m" or "masculino" or "hombre" or "h" => "M",
            "f" or "femenino" or "mujer" => "F",
            _ => null
        };

        /// <summary>La edad como número; vacía es válida (sin edad).</summary>
        public static (bool Ok, byte? Valor) Edad(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return (true, null);
            if (decimal.TryParse(texto.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == decimal.Truncate(d) && d is >= 0 and <= 120)
                return (true, (byte)d);
            return (false, null);
        }

        private static string? Opcional(string? valor)
        {
            var limpio = NombresPersona.Limpiar(valor);
            return limpio.Length == 0 ? null : limpio;
        }

        private static string? Cortar(string? valor, int largo) => valor == null ? null : valor.Length <= largo ? valor : valor[..largo];
    }
}
