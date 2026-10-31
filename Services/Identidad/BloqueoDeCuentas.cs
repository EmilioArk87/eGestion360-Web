using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Bloqueo temporal de una cuenta tras varios fallos de clave seguidos (D46).</summary>
    public interface IBloqueoDeCuentas
    {
        /// <summary>Hasta cuándo (UTC) está bloqueada la cuenta; <c>null</c> si no lo está (o el bloqueo ya venció).</summary>
        Task<DateTime?> BloqueadaHastaAsync(int idCuenta, CancellationToken ct = default);

        /// <summary>
        /// Anota un fallo de clave. Al llegar a <see cref="OpcionesDeIdentidad.IntentosParaBloquear"/> seguidos, bloquea la cuenta
        /// <see cref="OpcionesDeIdentidad.MinutosDeBloqueo"/> minutos y empieza a contar de cero. Devuelve hasta cuándo queda bloqueada
        /// si este fallo la bloqueó, o <c>null</c>.
        /// </summary>
        Task<DateTime?> RegistrarFalloAsync(int idCuenta, CancellationToken ct = default);

        /// <summary>Un acierto limpia el contador y cualquier bloqueo que hubiera.</summary>
        Task LimpiarAsync(int idCuenta, CancellationToken ct = default);
    }

    public sealed class BloqueoDeCuentas : IBloqueoDeCuentas
    {
        private readonly ApplicationDbContext _db;
        private readonly OpcionesDeIdentidad _opciones;
        private readonly TimeProvider _tiempo;

        public BloqueoDeCuentas(ApplicationDbContext db, IOptions<OpcionesDeIdentidad> opciones, TimeProvider? tiempo = null)
        {
            _db = db;
            _opciones = opciones.Value;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        public async Task<DateTime?> BloqueadaHastaAsync(int idCuenta, CancellationToken ct = default)
        {
            var hasta = await _db.CuentaIntentos.AsNoTracking()
                .Where(i => i.IdCuenta == idCuenta)
                .Select(i => i.BloqueadaHasta)
                .FirstOrDefaultAsync(ct);
            return hasta is { } h && h > Ahora ? h : null;
        }

        public async Task<DateTime?> RegistrarFalloAsync(int idCuenta, CancellationToken ct = default)
        {
            var ahora = Ahora;
            var fila = await _db.CuentaIntentos.FirstOrDefaultAsync(i => i.IdCuenta == idCuenta, ct);
            if (fila == null)
            {
                fila = new CuentaIntentos { IdCuenta = idCuenta };
                _db.CuentaIntentos.Add(fila);
            }

            // Un bloqueo vencido no cuenta: la persona vuelve a tener todos sus intentos.
            if (fila.BloqueadaHasta is { } vencido && vencido <= ahora)
            {
                fila.BloqueadaHasta = null;
                fila.IntentosFallidos = 0;
            }

            fila.IntentosFallidos++;
            fila.UltimoFallo = ahora;

            DateTime? bloqueada = null;
            if (fila.IntentosFallidos >= Math.Max(1, _opciones.IntentosParaBloquear))
            {
                bloqueada = ahora.AddMinutes(Math.Max(1, _opciones.MinutosDeBloqueo));
                fila.BloqueadaHasta = bloqueada;
                fila.IntentosFallidos = 0;
            }

            await _db.SaveChangesAsync(ct);
            return bloqueada;
        }

        public async Task LimpiarAsync(int idCuenta, CancellationToken ct = default)
        {
            var fila = await _db.CuentaIntentos.FirstOrDefaultAsync(i => i.IdCuenta == idCuenta, ct);
            if (fila == null || (fila.IntentosFallidos == 0 && fila.BloqueadaHasta == null)) return;

            fila.IntentosFallidos = 0;
            fila.BloqueadaHasta = null;
            await _db.SaveChangesAsync(ct);
        }
    }
}
