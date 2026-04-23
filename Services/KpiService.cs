using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;

namespace eGestion360Web.Services
{
    public class KpiService
    {
        private readonly ApplicationDbContext _db;

        public KpiService(ApplicationDbContext db) => _db = db;

        public async Task<List<KpiResumenVehiculo>> ObtenerKpiAsync(
            int idEmpresa, DateOnly fechaDesde, DateOnly fechaHasta, CancellationToken ct = default)
        {
            var vehiculos = await _db.Vehiculos
                .Where(v => v.IdEmpresa == idEmpresa && v.Activo && !v.Eliminado)
                .Include(v => v.Ruta)
                .OrderBy(v => v.Placa)
                .ToListAsync(ct);

            var kmPorVehiculo = await _db.OdometrosDiarios
                .Where(o => o.IdEmpresa == idEmpresa && !o.Eliminado
                         && o.Fecha >= fechaDesde && o.Fecha <= fechaHasta)
                .GroupBy(o => o.IdVehiculo)
                .Select(g => new { g.Key, Total = g.Sum(o => o.KmRecorridos) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

            var combustiblePorVehiculo = await _db.CargasCombustible
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado
                         && c.Fecha >= fechaDesde && c.Fecha <= fechaHasta)
                .GroupBy(c => c.IdVehiculo)
                .Select(g => new { g.Key, Total = g.Sum(c => c.Total) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

            var repuestosPorVehiculo = await _db.GastosRepuesto
                .Where(r => r.IdEmpresa == idEmpresa && !r.Eliminado
                         && r.Fecha >= fechaDesde && r.Fecha <= fechaHasta)
                .GroupBy(r => r.IdVehiculo)
                .Select(g => new { g.Key, Total = g.Sum(r => r.Subtotal) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

            var salariosPorVehiculo = await _db.SalariosDiarios
                .Where(s => s.IdEmpresa == idEmpresa && !s.Eliminado
                         && s.Fecha >= fechaDesde && s.Fecha <= fechaHasta)
                .GroupBy(s => s.IdVehiculo)
                .Select(g => new { g.Key, Total = g.Sum(s => s.Monto) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

            var mantenimientoPorVehiculo = await _db.OrdenesMantenimiento
                .Where(m => m.IdEmpresa == idEmpresa && !m.Eliminado
                         && m.Fecha >= fechaDesde && m.Fecha <= fechaHasta)
                .GroupBy(m => m.IdVehiculo)
                .Select(g => new { g.Key, Total = g.Sum(m => m.Total) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

            // Seguros: prorate by days of overlap within the requested period
            var polizas = await _db.PolizasSeguros
                .Where(p => p.IdEmpresa == idEmpresa && !p.Eliminado
                         && p.FechaInicio <= fechaHasta && p.FechaFin >= fechaDesde)
                .ToListAsync(ct);

            var segurosPorVehiculo = new Dictionary<int, decimal>();
            foreach (var poliza in polizas)
            {
                if (poliza.CostoDiario is null or 0) continue;
                var inicio = poliza.FechaInicio > fechaDesde ? poliza.FechaInicio : fechaDesde;
                var fin    = poliza.FechaFin    < fechaHasta ? poliza.FechaFin    : fechaHasta;
                int dias   = fin.DayNumber - inicio.DayNumber + 1;
                if (dias <= 0) continue;
                segurosPorVehiculo.TryGetValue(poliza.IdVehiculo, out var acum);
                segurosPorVehiculo[poliza.IdVehiculo] = acum + poliza.CostoDiario.Value * dias;
            }

            return vehiculos
                .Select(v => new KpiResumenVehiculo
                {
                    IdVehiculo         = v.IdVehiculo,
                    Placa              = v.Placa,
                    NombreRuta         = v.Ruta?.Nombre,
                    KmTotal            = kmPorVehiculo.GetValueOrDefault(v.IdVehiculo),
                    CostoCombustible   = combustiblePorVehiculo.GetValueOrDefault(v.IdVehiculo),
                    CostoRepuestos     = repuestosPorVehiculo.GetValueOrDefault(v.IdVehiculo),
                    CostoSalarios      = salariosPorVehiculo.GetValueOrDefault(v.IdVehiculo),
                    CostoSeguros       = segurosPorVehiculo.GetValueOrDefault(v.IdVehiculo),
                    CostoMantenimiento = mantenimientoPorVehiculo.GetValueOrDefault(v.IdVehiculo),
                })
                .Where(r => r.TieneActividad)
                .OrderBy(r => r.LempirasPorKm)
                .ToList();
        }
    }
}
