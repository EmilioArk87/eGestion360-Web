using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Mantiene las cuentas globales al día con <c>dbo.Users</c> mientras Users siga siendo donde las pantallas de administración
    /// escriben (F1.5 a F1.9; se retira en F1.10). El inicio de sesión lee solo las cuentas, así que cada cambio hecho a un usuario
    /// —clave, estado, correo, empresa, rol, persona— tiene que llegar a ellas. Así también, volver al código anterior no pierde claves ni
    /// usuarios nuevos, porque Users nunca dejó de estar al día.
    /// </summary>
    public interface ISincronizadorDeCuentas
    {
        /// <summary>
        /// Copia a la cuenta del usuario todo lo que decide su acceso: correo, usuario, clave, estado, membresía, acceso a su empresa, rol y
        /// persona. Crea la cuenta si el usuario aún no tiene. Para el acceso a la empresa (tabla de tenant) fija el tenant de la empresa del
        /// usuario si la petición aún no lo tiene.
        /// </summary>
        Task SincronizarAsync(int idUser, CancellationToken ct = default);

        /// <summary>Solo la clave y el cambio de clave pendiente. No toca tablas de tenant: sirve sin sesión (restablecer una clave olvidada).</summary>
        Task SincronizarClaveAsync(int idUser, CancellationToken ct = default);
    }

    public sealed class SincronizadorDeCuentas : ISincronizadorDeCuentas
    {
        private readonly ApplicationDbContext _db;
        private readonly TenantContext _tenant;
        private readonly IValidadorDeIdentidad _validador;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<SincronizadorDeCuentas> _log;

        public SincronizadorDeCuentas(ApplicationDbContext db, TenantContext tenant, IValidadorDeIdentidad validador,
            ILogger<SincronizadorDeCuentas> log, TimeProvider? tiempo = null)
        {
            _db = db;
            _tenant = tenant;
            _validador = validador;
            _log = log;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>La cuenta a la que pertenece un usuario y si es su usuario principal (el que le dio origen) o un alias.</summary>
        private async Task<(Cuenta? Cuenta, bool EsPrincipal)> CuentaDeAsync(int idUser, CancellationToken ct)
        {
            var principal = await _db.Cuentas.FirstOrDefaultAsync(c => c.IdUserLegado == idUser, ct);
            if (principal != null) return (principal, true);

            var idCuenta = await _db.CuentaAlias.AsNoTracking()
                .Where(a => a.IdUserLegado == idUser && a.Tipo == TipoAlias.Usuario)
                .Select(a => (int?)a.IdCuenta)
                .FirstOrDefaultAsync(ct);
            return idCuenta == null ? (null, false) : (await _db.Cuentas.FirstOrDefaultAsync(c => c.IdCuenta == idCuenta, ct), false);
        }

        public async Task SincronizarClaveAsync(int idUser, CancellationToken ct = default)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == idUser, ct);
            if (user == null) return;

            var (cuenta, _) = await CuentaDeAsync(idUser, ct);
            if (cuenta == null) return;   // un usuario que nunca tuvo cuenta (por ejemplo, registrado sin persona) no entra por aquí

            cuenta.HashClave = user.Password;
            cuenta.DebeCambiarClave = user.RequirePasswordChange;
            cuenta.ModificadoPor = user.Username;
            cuenta.FechaModificacion = Ahora;

            // Una clave nueva (cambiada, restablecida o asignada por un administrador) levanta el bloqueo por intentos fallidos.
            var intentos = await _db.CuentaIntentos.FirstOrDefaultAsync(i => i.IdCuenta == cuenta.IdCuenta, ct);
            if (intentos != null) { intentos.IntentosFallidos = 0; intentos.BloqueadaHasta = null; }

            await _db.SaveChangesAsync(ct);
            _validador.Invalidar(cuenta.IdCuenta);
        }

        public async Task SincronizarAsync(int idUser, CancellationToken ct = default)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == idUser, ct);
            if (user == null) return;

            var ahora = Ahora;
            var quien = user.Username;
            var correo = user.Email.Trim().ToLowerInvariant();
            // Sin una persona vinculada no hay sesión (script 021): la cuenta nace y se mantiene inactiva hasta que la tenga.
            var puedeEntrar = user.IsActive && user.PersonaId != null;

            var (cuenta, esPrincipal) = await CuentaDeAsync(idUser, ct);
            if (cuenta == null)
            {
                if (await _db.Cuentas.AnyAsync(c => c.Correo == correo || c.NombreUsuario == user.Username, ct))
                {
                    _log.LogWarning("No se creó la cuenta del usuario {IdUser}: su correo o su usuario ya lo usa otra cuenta.", idUser);
                    return;
                }

                cuenta = new Cuenta
                {
                    UidPublico = Guid.NewGuid(),
                    Correo = correo, NombreUsuario = user.Username, HashClave = user.Password,
                    DebeCambiarClave = user.RequirePasswordChange,
                    Estado = puedeEntrar ? EstadoCuenta.Activa : EstadoCuenta.Inactiva,
                    EsOperador = user.Role == AuthHelper.AdminRole && user.EmpresaId == null,
                    IdUserLegado = user.Id, CreadoPor = quien, FechaCreacion = ahora
                };
                _db.Cuentas.Add(cuenta);
                await _db.SaveChangesAsync(ct);
                esPrincipal = true;
            }
            else
            {
                cuenta.HashClave = user.Password;
                cuenta.DebeCambiarClave = user.RequirePasswordChange;
                if (esPrincipal)
                {
                    // Solo el usuario que dio origen a la cuenta manda sobre su correo, su nombre y su estado; un alias (por ejemplo
                    // egaray y egaray.transgar, que son la cuenta de Emilio) cambia únicamente el acceso a su empresa.
                    cuenta.Correo = correo;
                    cuenta.NombreUsuario = user.Username;
                    cuenta.Estado = puedeEntrar ? EstadoCuenta.Activa : EstadoCuenta.Inactiva;
                    if (user.Role == AuthHelper.AdminRole && user.EmpresaId == null) cuenta.EsOperador = true;
                }
                cuenta.ModificadoPor = quien;
                cuenta.FechaModificacion = ahora;
                await _db.SaveChangesAsync(ct);
            }

            if (user.EmpresaId is { } idEmpresa)
                await SincronizarAccesoAsync(cuenta, user, idEmpresa, puedeEntrar, ahora, ct);

            _validador.Invalidar(cuenta.IdCuenta);
        }

        private async Task SincronizarAccesoAsync(Cuenta cuenta, User user, int idEmpresa, bool puedeEntrar, DateTime ahora, CancellationToken ct)
        {
            var idTenant = await _db.Empresas.AsNoTracking()
                .Where(e => e.IdEmpresa == idEmpresa && !e.Eliminado)
                .Select(e => (int?)e.IdTenant)
                .FirstOrDefaultAsync(ct);
            if (idTenant == null)
            {
                _log.LogWarning("No se sincronizó el acceso del usuario {IdUser}: la empresa {IdEmpresa} no se ve desde este tenant.", user.Id, idEmpresa);
                return;
            }

            var membresia = await _db.Membresias.FirstOrDefaultAsync(m => m.IdCuenta == cuenta.IdCuenta && m.IdTenant == idTenant, ct);
            if (membresia == null)
            {
                membresia = new Membresia
                {
                    IdCuenta = cuenta.IdCuenta, IdTenant = idTenant.Value, IdPersona = user.PersonaId,
                    Estado = puedeEntrar ? EstadoMembresia.Activa : EstadoMembresia.Suspendida,
                    FechaEstado = ahora, CreadoPor = user.Username, FechaCreacion = ahora
                };
                _db.Membresias.Add(membresia);
            }
            else
            {
                var estado = puedeEntrar ? EstadoMembresia.Activa : EstadoMembresia.Suspendida;
                if (membresia.Estado != estado) { membresia.Estado = estado; membresia.FechaEstado = ahora; }
                membresia.IdPersona = user.PersonaId;
                membresia.ModificadoPor = user.Username;
                membresia.FechaModificacion = ahora;
            }
            await _db.SaveChangesAsync(ct);

            // accesos_empresa es de tenant: la política RLS solo deja escribirla con el tenant de la fila fijado.
            if (_tenant.IdTenant != idTenant || _tenant.EsPlataforma)
                _tenant.Fijar(idTenant, user.EmpresaId);

            var rolLegado = user.Role is AuthHelper.AdminRole or AuthHelper.EmpresaAdminRole ? AuthHelper.EmpresaAdminRole : AuthHelper.EmpresaUserRole;
            var accesos = await _db.AccesosEmpresa.Where(a => a.IdMembresia == membresia.IdMembresia).ToListAsync(ct);

            // Si el usuario cambió de empresa, el acceso a la anterior deja de valer. Se guarda antes del nuevo para no chocar con el
            // índice que permite una sola empresa principal por membresía.
            var anteriores = accesos.Where(a => a.IdEmpresa != idEmpresa && (a.Activo || a.EsPrincipal)).ToList();
            foreach (var otro in anteriores)
            {
                otro.Activo = false;
                otro.EsPrincipal = false;
                otro.ModificadoPor = user.Username;
                otro.FechaModificacion = ahora;
            }
            if (anteriores.Count > 0) await _db.SaveChangesAsync(ct);

            var acceso = accesos.FirstOrDefault(a => a.IdEmpresa == idEmpresa);
            if (acceso == null)
            {
                acceso = new AccesoEmpresa
                {
                    IdTenant = idTenant.Value, IdMembresia = membresia.IdMembresia, IdEmpresa = idEmpresa, IdRol = user.EmpresaRolId,
                    RolLegado = rolLegado, EsPrincipal = !accesos.Any(a => a.EsPrincipal), Activo = puedeEntrar,
                    CreadoPor = user.Username, FechaCreacion = ahora
                };
                _db.AccesosEmpresa.Add(acceso);
            }
            else
            {
                acceso.IdRol = user.EmpresaRolId;
                acceso.RolLegado = rolLegado;
                acceso.Activo = puedeEntrar;
                acceso.EsPrincipal = acceso.EsPrincipal || !accesos.Any(a => a.EsPrincipal);
                acceso.ModificadoPor = user.Username;
                acceso.FechaModificacion = ahora;
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
