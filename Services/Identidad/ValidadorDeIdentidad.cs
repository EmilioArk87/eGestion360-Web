using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Qué hacer con la cookie de una petición después de comparar con la base.</summary>
    /// <param name="Valida">Falso: la cuenta, su membresía o su acceso ya no sirven; hay que cerrar la sesión.</param>
    /// <param name="Renovada">Si no es nula, la identidad cambió (rol, cambio de clave...) y la cookie se reemplaza por esta.</param>
    public sealed record ResultadoDeValidacion(bool Valida, ClaimsPrincipal? Renovada = null)
    {
        public static readonly ResultadoDeValidacion SinCambios = new(true);
        public static readonly ResultadoDeValidacion Invalida = new(false);
    }

    public interface IValidadorDeIdentidad
    {
        /// <summary>
        /// Compara la identidad de la cookie con la base, como mucho una vez cada <see cref="OpcionesDeIdentidad.SegundosDeValidacion"/>
        /// segundos por cuenta y contexto. Una cuenta desactivada o sin acceso deja de entrar; un rol distinto se aplica en la petición siguiente.
        /// </summary>
        Task<ResultadoDeValidacion> ValidarAsync(ClaimsPrincipal principal, CancellationToken ct = default);

        /// <summary>Olvida lo recordado de una cuenta (por ejemplo, tras cambiarle la clave o el rol) para que la próxima petición lea la base.</summary>
        void Invalidar(int idCuenta);
    }

    /// <summary>
    /// Mantiene viva la cookie sin guardar el permiso en ella para siempre (D50). Corre en cada petición, pero solo toca la base cuando
    /// vence lo recordado: 3 consultas pequeñas cada 30 s por persona. Consulta en un ámbito aparte con el tenant de la identidad
    /// fijado, porque <c>accesos_empresa</c> es una tabla de tenant y la política RLS no la entrega sin él.
    /// </summary>
    public sealed class ValidadorDeIdentidad : IValidadorDeIdentidad
    {
        private readonly IServiceScopeFactory _ambitos;
        private readonly IMemoryCache _cache;
        private readonly OpcionesDeIdentidad _opciones;

        public ValidadorDeIdentidad(IServiceScopeFactory ambitos, IMemoryCache cache, IOptions<OpcionesDeIdentidad> opciones)
        {
            _ambitos = ambitos;
            _cache = cache;
            _opciones = opciones.Value;
        }

        private static string Clave(int idCuenta) => $"identidad:cuenta:{idCuenta}";

        public void Invalidar(int idCuenta)
        {
            // La clave de la cuenta apunta a la del último contexto leído (cuenta + membresía + empresa).
            if (_cache.TryGetValue(Clave(idCuenta), out string? contexto) && contexto != null) _cache.Remove(contexto);
            _cache.Remove(Clave(idCuenta));
        }

        public async Task<ResultadoDeValidacion> ValidarAsync(ClaimsPrincipal principal, CancellationToken ct = default)
        {
            var datos = ReclamosDeIdentidad.Leer(principal);
            if (datos == null) return ResultadoDeValidacion.Invalida;

            // La huella actual se recuerda por cuenta y contexto; si coincide con la de la cookie no hay nada que renovar.
            var clave = Clave(datos.IdCuenta) + $":{datos.IdMembresia}:{datos.IdEmpresa}";
            if (_cache.TryGetValue(clave, out EstadoLeido? recordado) && recordado != null)
                return Resolver(datos, recordado);

            var leido = await LeerAsync(datos, ct);
            _cache.Set(clave, leido, TimeSpan.FromSeconds(Math.Max(1, _opciones.SegundosDeValidacion)));
            // Para poder olvidar todos los contextos de una cuenta con Invalidar, se apunta también la clave de la cuenta.
            _cache.Set(Clave(datos.IdCuenta), clave, TimeSpan.FromSeconds(Math.Max(1, _opciones.SegundosDeValidacion)));
            return Resolver(datos, leido);
        }

        private static ResultadoDeValidacion Resolver(DatosDeIdentidad datos, EstadoLeido leido)
        {
            if (!leido.Existe) return ResultadoDeValidacion.Invalida;
            if (leido.Sello == datos.Sello) return ResultadoDeValidacion.SinCambios;

            // Cambió algo que decide qué puede hacer: la cookie se renueva con lo que dice la base. El operador no puede dejar de serlo
            // por esta vía (si se le quita, la huella cambia y entra como persona de empresa o no entra).
            var renovada = datos with
            {
                Rol = leido.Rol,
                EsOperador = leido.EsOperador,
                DebeCambiarClave = leido.DebeCambiarClave,
                IdRolEmpresa = leido.IdRolEmpresa,
                Sello = leido.Sello
            };
            return new ResultadoDeValidacion(true, ReclamosDeIdentidad.Crear(renovada));
        }

        /// <summary>Lo leído de la base para una identidad.</summary>
        private sealed record EstadoLeido(bool Existe, string Sello, string Rol, bool EsOperador, bool DebeCambiarClave, int? IdRolEmpresa);

        private async Task<EstadoLeido> LeerAsync(DatosDeIdentidad datos, CancellationToken ct)
        {
            using var ambito = _ambitos.CreateScope();
            var servicios = ambito.ServiceProvider;
            var db = servicios.GetRequiredService<ApplicationDbContext>();

            var cuenta = await db.Cuentas.AsNoTracking()
                .Where(c => c.IdCuenta == datos.IdCuenta)
                .Select(c => new { c.Estado, c.EsOperador, c.DebeCambiarClave })
                .FirstOrDefaultAsync(ct);
            if (cuenta == null || cuenta.Estado != EstadoCuenta.Activa) return Invalido();

            // Contexto de plataforma del operador: no hay membresía ni empresa que comprobar.
            if (datos.IdMembresia == null)
            {
                if (!cuenta.EsOperador) return Invalido();
                var sello = ReclamosDeIdentidad.CalcularSello(cuenta.Estado, cuenta.EsOperador, cuenta.DebeCambiarClave, null, null, null, null, true);
                return new EstadoLeido(true, sello, AuthHelper.AdminRole, true, cuenta.DebeCambiarClave, null);
            }

            var membresia = await db.Membresias.AsNoTracking()
                .Where(m => m.IdMembresia == datos.IdMembresia && m.IdCuenta == datos.IdCuenta && m.IdTenant == datos.IdTenant)
                .Select(m => new { m.Estado, m.IdPersona })
                .FirstOrDefaultAsync(ct);
            if (membresia == null || membresia.Estado != EstadoMembresia.Activa) return Invalido();

            if (datos.IdEmpresa is not { } idEmpresa) return Invalido();

            // accesos_empresa es de tenant: se lee con el tenant de la identidad fijado en este ámbito.
            servicios.GetRequiredService<TenantContext>().Fijar(datos.IdTenant, idEmpresa);
            var acceso = await db.AccesosEmpresa.AsNoTracking()
                .Where(a => a.IdMembresia == datos.IdMembresia && a.IdEmpresa == idEmpresa)
                .Select(a => new { a.Activo, a.RolLegado, a.IdRol })
                .FirstOrDefaultAsync(ct);
            if (acceso == null || !acceso.Activo) return Invalido();

            var rol = AuthHelper.RolDeAcceso(false, acceso.RolLegado);
            var huella = ReclamosDeIdentidad.CalcularSello(cuenta.Estado, cuenta.EsOperador, cuenta.DebeCambiarClave, membresia.Estado,
                membresia.IdPersona, acceso.RolLegado, acceso.IdRol, acceso.Activo);
            return new EstadoLeido(true, huella, rol, cuenta.EsOperador, cuenta.DebeCambiarClave, acceso.IdRol);
        }

        private static EstadoLeido Invalido() => new(false, string.Empty, string.Empty, false, false, null);
    }

    /// <summary>Conecta la cookie con <see cref="IValidadorDeIdentidad"/>: en cada petición comprueba la identidad y la renueva o la cierra.</summary>
    public static class EventosDeCookieDeIdentidad
    {
        /// <summary>Clave de <c>HttpContext.Items</c> que avisa que la identidad cambió y la caché de permisos de la sesión hay que rehacerla.</summary>
        public const string ClaveIdentidadRenovada = "eg360.identidad_renovada";

        public static async Task AlValidarAsync(CookieValidatePrincipalContext contexto)
        {
            var validador = contexto.HttpContext.RequestServices.GetRequiredService<IValidadorDeIdentidad>();
            var resultado = await validador.ValidarAsync(contexto.Principal!, contexto.HttpContext.RequestAborted);

            if (!resultado.Valida)
            {
                contexto.RejectPrincipal();
                await contexto.HttpContext.SignOutAsync(ReclamosDeIdentidad.Esquema);
                return;
            }

            if (resultado.Renovada != null)
            {
                contexto.ReplacePrincipal(resultado.Renovada);
                contexto.ShouldRenew = true;
                contexto.HttpContext.Items[ClaveIdentidadRenovada] = true;
            }
        }
    }
}
