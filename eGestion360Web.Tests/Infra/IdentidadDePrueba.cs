using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>Cuentas, membresías y accesos de prueba (F1.5), con las mismas reglas que el script 039.</summary>
    public static class IdentidadDePrueba
    {
        public static readonly DateTime Ahora = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

        /// <summary>Hash BCrypt de costo bajo, para que las pruebas no tarden.</summary>
        public static string Hash(string clave) => BCrypt.Net.BCrypt.HashPassword(clave, 4);

        public static IOptions<OpcionesDeIdentidad> Opciones(Action<OpcionesDeIdentidad>? ajustar = null)
        {
            var o = new OpcionesDeIdentidad();
            ajustar?.Invoke(o);
            return Microsoft.Extensions.Options.Options.Create(o);
        }

        public static TenantContext Tenant() => new(new HttpContextAccessor());

        public static Cuenta NuevaCuenta(ApplicationDbContext db, string correo, string clave = "Clave-Segura-1", string? usuario = null,
            bool operador = false, string estado = EstadoCuenta.Activa, int? idUserLegado = null, bool debeCambiarClave = false)
        {
            var cuenta = new Cuenta
            {
                UidPublico = Guid.NewGuid(), Correo = correo, NombreUsuario = usuario, HashClave = Hash(clave), Estado = estado,
                EsOperador = operador, IdUserLegado = idUserLegado, DebeCambiarClave = debeCambiarClave,
                CreadoPor = "pruebas", FechaCreacion = Ahora
            };
            db.Cuentas.Add(cuenta);
            db.SaveChanges();
            return cuenta;
        }

        public static Membresia NuevaMembresia(ApplicationDbContext db, Cuenta cuenta, int idTenant, int? idPersona, string estado = EstadoMembresia.Activa)
        {
            var membresia = new Membresia
            {
                IdCuenta = cuenta.IdCuenta, IdTenant = idTenant, IdPersona = idPersona, Estado = estado,
                FechaEstado = Ahora, CreadoPor = "pruebas", FechaCreacion = Ahora
            };
            db.Membresias.Add(membresia);
            db.SaveChanges();
            return membresia;
        }

        public static AccesoEmpresa NuevoAcceso(ApplicationDbContext db, Membresia membresia, int idEmpresa, string rolLegado = "empresa_user",
            int? idRol = null, bool activo = true, bool principal = true)
        {
            var acceso = new AccesoEmpresa
            {
                IdTenant = membresia.IdTenant, IdMembresia = membresia.IdMembresia, IdEmpresa = idEmpresa, IdRol = idRol, RolLegado = rolLegado,
                EsPrincipal = principal, Activo = activo, CreadoPor = "pruebas", FechaCreacion = Ahora
            };
            db.AccesosEmpresa.Add(acceso);
            db.SaveChanges();
            return acceso;
        }

        /// <summary>Una persona vinculada a la empresa, para la membresía.</summary>
        public static int NuevaPersona(ApplicationDbContext db, int idEmpresa, string nombre = "Emilio", string apellido = "Garay")
            => PersonasDePrueba.Insertar(db, idEmpresa, nombre, apellido, codigoInterno: "P-" + Guid.NewGuid().ToString("N")[..8]).IdPersona;

        /// <summary>
        /// Una cuenta completa que trabaja en una empresa: persona, membresía activa en el tenant de la empresa y acceso activo.
        /// </summary>
        public static (Cuenta Cuenta, Membresia Membresia, AccesoEmpresa Acceso) CuentaDeEmpresa(ApplicationDbContext db, string correo,
            string clave = "Clave-Segura-1", int idEmpresa = DatosBase.EmpresaA, string rolLegado = "empresa_user", string? usuario = null)
        {
            var idPersona = NuevaPersona(db, idEmpresa);
            var cuenta = NuevaCuenta(db, correo, clave, usuario);
            var membresia = NuevaMembresia(db, cuenta, DatosBase.TenantDe(idEmpresa), idPersona);
            var acceso = NuevoAcceso(db, membresia, idEmpresa, rolLegado);
            return (cuenta, membresia, acceso);
        }
    }
}
