using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Un sitio donde una cuenta puede trabajar: la plataforma (operador) o una empresa de un tenant con un rol.</summary>
    /// <param name="Clave">Identificador estable para elegirlo en un formulario: <c>plataforma</c> o <c>membresia:empresa</c>.</param>
    /// <param name="Rol">Rol de texto con el que entra (<c>admin</c>, <c>empresa_admin</c> o <c>empresa_user</c>).</param>
    public sealed record ContextoDeAcceso(
        string Clave,
        bool EsPlataforma,
        int? IdMembresia,
        int? IdTenant,
        string? CodigoTenant,
        string? NombreTenant,
        int? IdEmpresa,
        string? NombreEmpresa,
        int? IdRolEmpresa,
        string? NombreRol,
        string RolLegado,
        string Rol,
        int? IdPersona,
        string NombrePersona,
        bool AccesoActivo = true)
    {
        public const string ClaveDePlataforma = "plataforma";

        public static string ClaveDe(int idMembresia, int idEmpresa) => $"{idMembresia}:{idEmpresa}";
    }

    /// <summary>Lo que se sabe de una cuenta al entrar: dónde puede trabajar y por qué un sitio quedó fuera.</summary>
    /// <param name="Contextos">Sitios donde puede trabajar ahora.</param>
    /// <param name="ExcluidosPorFaltaDePersona">Cuántos accesos activos se dejaron fuera porque su membresía no tiene una persona vigente.</param>
    public sealed record ContextosDeLaCuenta(IReadOnlyList<ContextoDeAcceso> Contextos, int ExcluidosPorFaltaDePersona);

    public interface IDirectorioDeCuentas
    {
        /// <summary>
        /// Busca la cuenta con la que alguien intenta entrar: por correo, por nombre de usuario o por un alias que pueda iniciar sesión
        /// (D44). Devuelve la cuenta (sin importar su estado: quien llama decide) y, si entró con un alias, ese alias para la auditoría.
        /// </summary>
        Task<Cuenta?> BuscarParaEntrarAsync(string identificador, CancellationToken ct = default);

        /// <summary>Los sitios donde la cuenta puede trabajar: membresías activas de tenants que no están cancelados, con acceso activo a una empresa activa.</summary>
        Task<ContextosDeLaCuenta> ContextosAsync(Cuenta cuenta, CancellationToken ct = default);
    }

    public sealed class DirectorioDeCuentas : IDirectorioDeCuentas
    {
        private readonly ApplicationDbContext _db;
        private readonly TenantContext _tenant;

        public DirectorioDeCuentas(ApplicationDbContext db, TenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public async Task<Cuenta?> BuscarParaEntrarAsync(string identificador, CancellationToken ct = default)
        {
            var id = (identificador ?? string.Empty).Trim();
            if (id.Length == 0) return null;

            var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.Correo == id || c.NombreUsuario == id, ct);
            if (cuenta != null) return cuenta;

            var idCuenta = await _db.CuentaAlias.AsNoTracking()
                .Where(a => a.Alias == id && a.PuedeIniciarSesion)
                .Select(a => (int?)a.IdCuenta)
                .FirstOrDefaultAsync(ct);
            return idCuenta == null ? null : await _db.Cuentas.FirstOrDefaultAsync(c => c.IdCuenta == idCuenta, ct);
        }

        public async Task<ContextosDeLaCuenta> ContextosAsync(Cuenta cuenta, CancellationToken ct = default)
        {
            var contextos = new List<ContextoDeAcceso>();
            var excluidos = 0;

            var membresias = await (from m in _db.Membresias.AsNoTracking()
                                    join t in _db.Tenants.AsNoTracking() on m.IdTenant equals t.IdTenant
                                    where m.IdCuenta == cuenta.IdCuenta && m.Estado == EstadoMembresia.Activa && t.Estado != EstadoTenant.Cancelado
                                    orderby t.Codigo
                                    select new { m.IdMembresia, m.IdTenant, m.IdPersona, t.Codigo, t.Nombre }).ToListAsync(ct);

            string? nombreDelOperador = null;
            try
            {
                foreach (var m in membresias)
                {
                    // Los accesos son de tenant (política RLS): se leen con el tenant de cada membresía fijado, uno por uno.
                    _tenant.Fijar(m.IdTenant, null);

                    var persona = m.IdPersona is { } idPersona
                        ? await _db.Personas.AsNoTracking()
                            .Where(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null)
                            .Select(p => new { p.PrimerNombre, p.PrimerApellido, p.Nombres, p.Apellidos })
                            .FirstOrDefaultAsync(ct)
                        : null;
                    var nombrePersona = persona == null
                        ? null
                        : NombresPersona.Corto(persona.PrimerNombre, persona.PrimerApellido, persona.Nombres, persona.Apellidos);
                    nombreDelOperador ??= nombrePersona;

                    var accesos = await (from a in _db.AccesosEmpresa.AsNoTracking()
                                         join e in _db.Empresas.AsNoTracking() on a.IdEmpresa equals e.IdEmpresa
                                         join r in _db.EmpresaRoles.AsNoTracking() on a.IdRol equals (int?)r.IdRol into roles
                                         from r in roles.DefaultIfEmpty()
                                         where a.IdMembresia == m.IdMembresia && a.Activo && e.Activa && !e.Eliminado
                                         orderby a.EsPrincipal descending, e.RazonSocial
                                         select new
                                         {
                                             a.IdEmpresa, a.IdRol, a.RolLegado,
                                             Empresa = e.NombreComercial ?? e.RazonSocial,
                                             NombreRol = r == null ? null : r.Nombre
                                         }).ToListAsync(ct);

                    if (persona == null)
                    {
                        // Sin una persona vigente no hay sesión (script 021): el acceso queda fuera y se avisa si era el único.
                        excluidos += accesos.Count;
                        continue;
                    }

                    foreach (var a in accesos)
                    {
                        contextos.Add(new ContextoDeAcceso(
                            ContextoDeAcceso.ClaveDe(m.IdMembresia, a.IdEmpresa), false, m.IdMembresia, m.IdTenant, m.Codigo, m.Nombre,
                            a.IdEmpresa, a.Empresa, a.IdRol, a.NombreRol, a.RolLegado, AuthHelper.RolDeAcceso(false, a.RolLegado),
                            m.IdPersona, nombrePersona!));
                    }
                }
            }
            finally
            {
                // La petición de inicio de sesión no tiene tenant hasta que se elige un sitio.
                _tenant.Fijar(null, null);
            }

            if (cuenta.EsOperador)
            {
                contextos.Insert(0, new ContextoDeAcceso(
                    ContextoDeAcceso.ClaveDePlataforma, true, null, null, null, null, null, null, null, null,
                    AuthHelper.AdminRole, AuthHelper.AdminRole, null, nombreDelOperador ?? cuenta.NombreUsuario ?? cuenta.Correo));
            }

            return new ContextosDeLaCuenta(contextos, excluidos);
        }
    }
}
