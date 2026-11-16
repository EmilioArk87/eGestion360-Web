using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class AtencionService : IAtencionService
    {
        public const string MensajeNoEditable = "La atención está cerrada o anulada: ya no se puede cambiar.";

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;

        public AtencionService(ApplicationDbContext db, TimeProvider tiempo)
        {
            _db = db;
            _tiempo = tiempo;
        }

        public async Task<ResultadoPsicologia<int>> GuardarPruebasAsync(int idEmpresa, int idAtencion, IReadOnlyCollection<int> idsPruebas, string usuario,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(idsPruebas);

            var atencion = await _db.AtencionesPsicologia
                .Include(a => a.Pruebas)
                .FirstOrDefaultAsync(a => a.IdAtencion == idAtencion && a.IdEmpresa == idEmpresa, ct);
            if (atencion == null) return ResultadoPsicologia<int>.Error(string.Empty, "La atención no existe en esta empresa.");
            if (atencion.Estado != EstadosAtencion.Borrador) return ResultadoPsicologia<int>.Error(string.Empty, MensajeNoEditable);

            var pedidas = idsPruebas.Distinct().ToHashSet();
            var actuales = atencion.Pruebas.Select(p => p.IdPrueba).ToHashSet();
            var nuevas = pedidas.Except(actuales).ToList();

            if (nuevas.Count > 0)
            {
                var validas = await _db.PruebasPsicologicas.AsNoTracking()
                    .Where(p => p.IdEmpresa == idEmpresa && p.Activo && nuevas.Contains(p.IdPrueba))
                    .Select(p => p.IdPrueba)
                    .ToListAsync(ct);
                if (validas.Count != nuevas.Count)
                    return ResultadoPsicologia<int>.Error("Pruebas", "Alguna prueba elegida no está en el catálogo activo de la empresa.");
            }

            var ahora = _tiempo.GetUtcNow().UtcDateTime;
            foreach (var quitar in atencion.Pruebas.Where(p => !pedidas.Contains(p.IdPrueba)).ToList())
                _db.AtencionPruebas.Remove(quitar);
            foreach (var idPrueba in nuevas)
                _db.AtencionPruebas.Add(new AtencionPrueba
                {
                    IdEmpresa = idEmpresa, IdAtencion = atencion.IdAtencion, IdPrueba = idPrueba, CreadoPor = usuario, FechaCreacion = ahora
                });

            if (_db.ChangeTracker.HasChanges())
            {
                atencion.ModificadoPor = usuario;
                atencion.FechaModificacion = ahora;
                await _db.SaveChangesAsync(ct);
            }
            return ResultadoPsicologia<int>.Exito(pedidas.Count);
        }
    }
}
