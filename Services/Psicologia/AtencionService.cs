using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class AtencionService : IAtencionService
    {
        public const string MensajeNoEditable = "La atención está cerrada o anulada: ya no se puede cambiar.";
        public const string MensajeConcurrencia = "Otra persona cambió esta evaluación al mismo tiempo: vuelve a abrirla y repite el cambio.";
        public const int LargoResultado = 200;
        public const int LargoObservaciones = 4000;
        public const int LargoMotivo = 300;

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;

        public AtencionService(ApplicationDbContext db, TimeProvider tiempo)
        {
            _db = db;
            _tiempo = tiempo;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private Task<Atencion?> BuscarAsync(int idEmpresa, int idAtencion, CancellationToken ct) =>
            _db.AtencionesPsicologia
                .Include(a => a.Pruebas).ThenInclude(p => p.Prueba)
                .FirstOrDefaultAsync(a => a.IdAtencion == idAtencion && a.IdEmpresa == idEmpresa, ct);

        public async Task<ResultadoPsicologia<Atencion>> GuardarEvaluacionAsync(int idEmpresa, int idAtencion, EvaluacionInput input, string usuario,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            var atencion = await BuscarAsync(idEmpresa, idAtencion, ct);
            if (atencion == null) return ResultadoPsicologia<Atencion>.Error(string.Empty, "La atención no existe en esta empresa.");
            if (atencion.Estado != EstadosAtencion.Borrador) return ResultadoPsicologia<Atencion>.Error(string.Empty, MensajeNoEditable);

            var errores = new List<ErrorValidacion>();

            // Una prueba una sola vez; si viene repetida, vale la primera.
            var pedidas = new Dictionary<int, string?>();
            foreach (var p in input.Pruebas ?? Array.Empty<PruebaAplicadaInput>())
                pedidas.TryAdd(p.IdPrueba, Limpio(p.Resultado));
            if (pedidas.Values.Any(r => r != null && r.Length > LargoResultado))
                errores.Add(new ErrorValidacion("Pruebas", $"El resultado de una prueba no puede pasar de {LargoResultado} caracteres."));

            var conclusion = Limpio(input.Conclusion)?.ToLowerInvariant();
            if (conclusion != null && !ResultadosConstancia.Todos.Contains(conclusion))
                errores.Add(new ErrorValidacion("Conclusion", "Elige si resultó apto o no apto."));

            var observaciones = Limpio(input.Observaciones);
            if (observaciones != null && observaciones.Length > LargoObservaciones)
                errores.Add(new ErrorValidacion("Observaciones", $"Las observaciones no pueden pasar de {LargoObservaciones} caracteres."));

            var actuales = atencion.Pruebas.ToDictionary(p => p.IdPrueba);
            var nuevas = pedidas.Keys.Where(id => !actuales.ContainsKey(id)).ToList();
            if (nuevas.Count > 0)
            {
                var validas = await _db.PruebasPsicologicas.AsNoTracking()
                    .Where(p => p.IdEmpresa == idEmpresa && p.Activo && nuevas.Contains(p.IdPrueba))
                    .CountAsync(ct);
                if (validas != nuevas.Count)
                    errores.Add(new ErrorValidacion("Pruebas", "Alguna prueba elegida no está en el catálogo activo de la empresa."));
            }

            if (errores.Count > 0) return ResultadoPsicologia<Atencion>.ConErrores(errores);

            var ahora = Ahora();
            foreach (var quitar in atencion.Pruebas.Where(p => !pedidas.ContainsKey(p.IdPrueba)).ToList())
                _db.AtencionPruebas.Remove(quitar);
            foreach (var (idPrueba, resultado) in pedidas)
            {
                if (actuales.TryGetValue(idPrueba, out var aplicada))
                {
                    if (aplicada.Resultado != resultado) aplicada.Resultado = resultado;
                }
                else
                {
                    _db.AtencionPruebas.Add(new AtencionPrueba
                    {
                        IdEmpresa = idEmpresa, IdAtencion = atencion.IdAtencion, IdPrueba = idPrueba, Resultado = resultado,
                        CreadoPor = usuario, FechaCreacion = ahora
                    });
                }
            }
            if (atencion.Conclusion != conclusion) atencion.Conclusion = conclusion;
            if (atencion.Observaciones != observaciones) atencion.Observaciones = observaciones;

            if (_db.ChangeTracker.HasChanges())
            {
                atencion.ModificadoPor = usuario;
                atencion.FechaModificacion = ahora;
                var error = await GuardarAsync(ct);
                if (error != null) return error;
            }
            return ResultadoPsicologia<Atencion>.Exito(atencion);
        }

        public async Task<ResultadoPsicologia<Atencion>> CerrarAsync(int idEmpresa, int idAtencion, string usuario, CancellationToken ct = default)
        {
            var atencion = await BuscarAsync(idEmpresa, idAtencion, ct);
            if (atencion == null) return ResultadoPsicologia<Atencion>.Error(string.Empty, "La atención no existe en esta empresa.");
            if (atencion.Estado != EstadosAtencion.Borrador) return ResultadoPsicologia<Atencion>.Error(string.Empty, MensajeNoEditable);

            var errores = new List<ErrorValidacion>();
            if (atencion.Pruebas.Count == 0)
                errores.Add(new ErrorValidacion("Pruebas", "Registra al menos una prueba aplicada antes de cerrar la evaluación."));
            var sinResultado = atencion.Pruebas.Where(p => p.Resultado == null).Select(p => p.Prueba.Codigo).OrderBy(c => c).ToList();
            if (sinResultado.Count > 0)
                errores.Add(new ErrorValidacion("Pruebas", $"Anota el resultado de cada prueba antes de cerrar (falta: {string.Join(", ", sinResultado)})."));
            if (atencion.Conclusion == null)
                errores.Add(new ErrorValidacion("Conclusion", "Elige la conclusión (apto o no apto) antes de cerrar la evaluación."));
            if (errores.Count > 0) return ResultadoPsicologia<Atencion>.ConErrores(errores);

            var ahora = Ahora();
            atencion.Estado = EstadosAtencion.Cerrada;
            atencion.FechaCierre = ahora;
            atencion.CerradoPor = usuario;
            atencion.ModificadoPor = usuario;
            atencion.FechaModificacion = ahora;
            return await GuardarAsync(ct) ?? ResultadoPsicologia<Atencion>.Exito(atencion);
        }

        public async Task<ResultadoPsicologia<Atencion>> AnularAsync(int idEmpresa, int idAtencion, string? motivo, string usuario, CancellationToken ct = default)
        {
            var atencion = await BuscarAsync(idEmpresa, idAtencion, ct);
            if (atencion == null) return ResultadoPsicologia<Atencion>.Error(string.Empty, "La atención no existe en esta empresa.");
            if (atencion.Estado == EstadosAtencion.Anulada) return ResultadoPsicologia<Atencion>.Error(string.Empty, "La atención ya está anulada.");

            var limpio = Limpio(motivo);
            if (limpio == null) return ResultadoPsicologia<Atencion>.Error("Motivo", "Escribe el motivo de la anulación.");
            if (limpio.Length > LargoMotivo)
                return ResultadoPsicologia<Atencion>.Error("Motivo", $"El motivo no puede pasar de {LargoMotivo} caracteres.");

            var constancias = await _db.Constancias.AsNoTracking()
                .Where(c => c.IdEmpresa == idEmpresa && c.IdAtencion == idAtencion && c.Estado == EstadosConstancia.Emitida)
                .OrderBy(c => c.NumeroConstancia)
                .Select(c => c.NumeroConstancia)
                .ToListAsync(ct);
            if (constancias.Count == 1)
                return ResultadoPsicologia<Atencion>.Error(string.Empty, $"De esta evaluación salió la constancia {constancias[0]}: anúlala primero.");
            if (constancias.Count > 1)
                return ResultadoPsicologia<Atencion>.Error(string.Empty,
                    $"De esta evaluación salieron las constancias {string.Join(", ", constancias)}: anúlalas primero.");

            atencion.Estado = EstadosAtencion.Anulada;
            atencion.MotivoAnulacion = limpio;
            atencion.ModificadoPor = usuario;
            atencion.FechaModificacion = Ahora();
            return await GuardarAsync(ct) ?? ResultadoPsicologia<Atencion>.Exito(atencion);
        }

        /// <summary>Guarda; nulo si salió bien, o el error si otra persona cambió la atención al mismo tiempo.</summary>
        private async Task<ResultadoPsicologia<Atencion>?> GuardarAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return null;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                return ResultadoPsicologia<Atencion>.Error(string.Empty, MensajeConcurrencia);
            }
        }
    }
}
