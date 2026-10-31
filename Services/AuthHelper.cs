using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace eGestion360Web.Services
{
    public static class AuthHelper
    {
        public const string AdminRole        = "admin";
        public const string EmpresaAdminRole = "empresa_admin";
        public const string EmpresaUserRole  = "empresa_user";

        // Desde F1.5 (ADR-003, D50) la identidad sale de la cookie de autenticación (HttpContext.User) y no de la sesión: la sesión en
        // memoria se pierde con cada reinicio del sitio y la cookie no. La sesión sigue existiendo para el estado de pantalla (tenant de
        // soporte, mensajes) y como espejo para el código anterior (SesionDesdeIdentidadMiddleware); nada decide aquí por lo que
        // diga la sesión.

        // ── Autenticación básica ──────────────────────────────────────────────

        /// <summary>True si hay una cookie válida y la persona no tiene un cambio de clave pendiente.</summary>
        public static bool IsAuthenticated(HttpContext context)
            => HayCuenta(context) && !MustChangePassword(context);

        /// <summary>True si hay una cookie válida, aunque la persona deba cambiar su clave antes de seguir.</summary>
        public static bool HayCuenta(HttpContext context)
            => context.User.Identity?.IsAuthenticated == true && Reclamo(context, Identidad.ReclamosDeIdentidad.Cuenta) != null;

        /// <summary>True si el usuario inició sesión con cambio de contraseña pendiente;
        /// bloquea el acceso al resto del sistema hasta completar el cambio.</summary>
        public static bool MustChangePassword(HttpContext context)
            => HayCuenta(context) && Reclamo(context, Identidad.ReclamosDeIdentidad.DebeCambiarClave) == "1";

        // ── Nivel de rol ──────────────────────────────────────────────────────

        private static bool TieneRol(HttpContext context, string rol)
            => HayCuenta(context) && string.Equals(Reclamo(context, System.Security.Claims.ClaimTypes.Role), rol, StringComparison.OrdinalIgnoreCase);

        public static bool IsAdmin(HttpContext context) => TieneRol(context, AdminRole);

        public static bool IsEmpresaAdmin(HttpContext context) => TieneRol(context, EmpresaAdminRole);

        public static bool IsEmpresaUser(HttpContext context) => TieneRol(context, EmpresaUserRole);

        /// <summary>True si es admin del sistema o admin de empresa.</summary>
        public static bool IsAnyAdmin(HttpContext context)
            => IsAdmin(context) || IsEmpresaAdmin(context);

        // ── Datos de la identidad ─────────────────────────────────────────────

        private static string? Reclamo(HttpContext context, string tipo) => context.User.FindFirst(tipo)?.Value;

        private static int? ReclamoEntero(HttpContext context, string tipo)
            => HayCuenta(context) && int.TryParse(Reclamo(context, tipo), System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

        /// <summary>Cuenta global (<c>saas.cuentas</c>) de quien tiene la cookie.</summary>
        public static int? GetCuentaId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.Cuenta);

        /// <summary>Id de <c>dbo.Users</c> que usan las pantallas anteriores a F1.5; nulo si la cuenta no viene de un usuario.</summary>
        public static int? GetUserLegadoId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.UserLegado);

        /// <summary>Con qué nombre aparece en la auditoría («creado por»).</summary>
        public static string? GetNombreUsuario(HttpContext context) => HayCuenta(context) ? Reclamo(context, System.Security.Claims.ClaimTypes.Name) : null;

        public static string? GetCorreo(HttpContext context) => HayCuenta(context) ? Reclamo(context, System.Security.Claims.ClaimTypes.Email) : null;

        /// <summary>Personal de la plataforma (operador).</summary>
        public static bool IsOperador(HttpContext context) => HayCuenta(context) && Reclamo(context, Identidad.ReclamosDeIdentidad.Operador) == "1";

        public static int? GetMembresiaId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.Membresia);

        /// <summary>Tenant de la identidad (el de su membresía); nulo para el operador en el contexto de plataforma.</summary>
        public static int? GetTenantId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.Tenant);

        // ── Datos de tenant ───────────────────────────────────────────────────

        public static int? GetEmpresaId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.Empresa);

        /// <summary>
        /// Empresa de la sesión para las pantallas que operan sobre los datos de UNA empresa. No hay empresa por
        /// defecto: si la sesión no trae una (por ejemplo el administrador general), falla en lugar de trabajar sobre
        /// otra empresa. <see cref="EmpresaRequeridaPageFilter"/> ya desvía antes a esas sesiones; esto es la segunda
        /// barrera por si una página se saltara el filtro.
        /// </summary>
        public static int GetEmpresaIdRequerida(HttpContext context)
        {
            var id = GetEmpresaId(context);
            if (id is > 0) return id.Value;
            throw new InvalidOperationException(
                "La sesión no tiene empresa: esta pantalla solo puede operar sobre los datos de una empresa.");
        }

        public static int? GetEmpresaRolId(HttpContext context) => ReclamoEntero(context, Identidad.ReclamosDeIdentidad.RolEmpresa);

        // ── Tenant de soporte del administrador general (F1.4c, ADR-025) ──────

        /// <summary>Clave de sesión con el id del tenant con el que trabaja el administrador general.</summary>
        public const string ClaveTenantDeSoporte = "TenantDeSoporte";

        /// <summary>Clave de sesión con el código del tenant de soporte, solo para mostrarlo.</summary>
        public const string ClaveCodigoTenantDeSoporte = "TenantDeSoporteCodigo";

        /// <summary>
        /// El tenant de soporte: el que el administrador general eligió para ver y mantener los datos de un tenant. Solo existe
        /// para el administrador sin empresa en la sesión (con empresa, el tenant es el de su empresa); para cualquier otra
        /// sesión es nulo aunque la clave esté en la sesión.
        /// </summary>
        public static int? GetTenantDeSoporte(HttpContext context)
        {
            if (!IsAdmin(context) || GetEmpresaId(context) is > 0) return null;
            return int.TryParse(context.Session.GetString(ClaveTenantDeSoporte), out var id) && id > 0 ? id : null;
        }

        /// <summary>Código del tenant de soporte, para mostrarlo en pantalla; nulo si no hay tenant de soporte.</summary>
        public static string? GetCodigoTenantDeSoporte(HttpContext context)
            => GetTenantDeSoporte(context) != null ? context.Session.GetString(ClaveCodigoTenantDeSoporte) : null;

        public static void EntrarEnSoporte(HttpContext context, int idTenant, string codigoTenant)
        {
            context.Session.SetString(ClaveTenantDeSoporte, idTenant.ToString());
            context.Session.SetString(ClaveCodigoTenantDeSoporte, codigoTenant);
        }

        public static void SalirDeSoporte(HttpContext context)
        {
            context.Session.Remove(ClaveTenantDeSoporte);
            context.Session.Remove(ClaveCodigoTenantDeSoporte);
        }

        // ── Acceso a módulos ──────────────────────────────────────────────────

        /// <summary>Devuelve los códigos de módulo accesibles para el usuario en sesión.</summary>
        public static HashSet<string> GetModulos(HttpContext context)
        {
            var json = context.Session.GetString("Modulos");
            if (string.IsNullOrEmpty(json)) return new HashSet<string>();
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
        }

        /// <summary>
        /// True si el usuario tiene acceso al módulo indicado. Desde F1.7 lo deciden, en cada petición, el plan del tenant (el derecho
        /// <c>modulo.&lt;código&gt;</c>) y la activación vigente de la empresa; ya no la copia de módulos que el inicio de sesión dejó
        /// en la sesión. Si la petición no pasó por <see cref="Derechos.EstadoDelTenantMiddleware"/> (pruebas, procesos sin petición
        /// web), se usa la copia de la sesión como antes.
        /// </summary>
        public static bool HasModulo(HttpContext context, string moduloCodigo)
        {
            if (IsAdmin(context)) return true;
            if (context.Items.TryGetValue(Derechos.EstadoDelTenantMiddleware.ClaveDerechos, out var evaluados)
                && evaluados is Derechos.DerechosDeLaPeticion derechos)
                return derechos.TieneModulo(moduloCodigo);

            return GetModulos(context).Contains(moduloCodigo.ToLowerInvariant());
        }

        // ── Permisos granulares ───────────────────────────────────────────────

        /// <summary>Carga el mapa de permisos desde sesión: { "flota": ["ver","crear",...] }</summary>
        public static Dictionary<string, HashSet<string>> GetPermisos(HttpContext context)
        {
            var json = context.Session.GetString("Permisos");
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, HashSet<string>>();

            return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json)
                   ?? new Dictionary<string, HashSet<string>>();
        }

        public static bool PuedeVer(HttpContext context, string moduloCodigo)
            => TienePermiso(context, moduloCodigo, "ver");

        public static bool PuedeCrear(HttpContext context, string moduloCodigo)
            => TienePermiso(context, moduloCodigo, "crear");

        public static bool PuedeEditar(HttpContext context, string moduloCodigo)
            => TienePermiso(context, moduloCodigo, "editar");

        public static bool PuedeEliminar(HttpContext context, string moduloCodigo)
            => TienePermiso(context, moduloCodigo, "eliminar");

        private static bool TienePermiso(HttpContext context, string moduloCodigo, string permiso)
        {
            if (IsAdmin(context)) return true;
            if (IsEmpresaAdmin(context)) return HasModulo(context, moduloCodigo);

            var permisos = GetPermisos(context);
            return permisos.TryGetValue(moduloCodigo.ToLowerInvariant(), out var set)
                   && set.Contains(permiso);
        }

        // ── Persona de la cuenta (script 021) ─────────────────────────────────

        /// <summary>Clave de sesión (espejo) con el nombre corto de la persona que usa la cuenta.</summary>
        public const string ClaveNombrePersona = "NombrePersona";

        /// <summary>
        /// Cómo se muestra a quien inició sesión: el nombre de su persona («Emilio Garay») o, si no lo hay, su usuario.
        /// El nombre de usuario sigue siendo el que se guarda en la auditoría.
        /// </summary>
        public static string NombreVisible(HttpContext context) =>
            Reclamo(context, Identidad.ReclamosDeIdentidad.NombrePersona) is { Length: > 0 } nombre
                ? nombre
                : GetNombreUsuario(context) ?? "Usuario";

        /// <summary>
        /// Rol de texto con el que entra un acceso a una empresa. El operador de la plataforma entra como <c>admin</c>; el resto,
        /// con el rol que tiene en su empresa (<c>empresa_admin</c> o <c>empresa_user</c>).
        /// </summary>
        public static string RolDeAcceso(bool esOperadorEnPlataforma, string? rolLegado)
            => esOperadorEnPlataforma ? AdminRole : ResolveRole(rolLegado ?? string.Empty);

        // ── Resolución de rol desde DB ────────────────────────────────────────

        public static string ResolveRole(string role)
            => string.IsNullOrWhiteSpace(role) ? EmpresaUserRole : role.ToLowerInvariant();
    }
}
