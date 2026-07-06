using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace eGestion360Web.Services
{
    public static class AuthHelper
    {
        public const string AdminRole        = "admin";
        public const string EmpresaAdminRole = "empresa_admin";
        public const string EmpresaUserRole  = "empresa_user";

        // ── Autenticación básica ──────────────────────────────────────────────

        public static bool IsAuthenticated(HttpContext context)
            => !string.IsNullOrEmpty(context.Session.GetString("UserId"))
               && !MustChangePassword(context);

        /// <summary>True si el usuario inició sesión con cambio de contraseña pendiente;
        /// bloquea el acceso al resto del sistema hasta completar el cambio.</summary>
        public static bool MustChangePassword(HttpContext context)
            => context.Session.GetString("MustChangePassword") == "1";

        // ── Nivel de rol ──────────────────────────────────────────────────────

        public static bool IsAdmin(HttpContext context)
            => string.Equals(context.Session.GetString("Role"), AdminRole, StringComparison.OrdinalIgnoreCase);

        public static bool IsEmpresaAdmin(HttpContext context)
            => string.Equals(context.Session.GetString("Role"), EmpresaAdminRole, StringComparison.OrdinalIgnoreCase);

        public static bool IsEmpresaUser(HttpContext context)
            => string.Equals(context.Session.GetString("Role"), EmpresaUserRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>True si es admin del sistema o admin de empresa.</summary>
        public static bool IsAnyAdmin(HttpContext context)
            => IsAdmin(context) || IsEmpresaAdmin(context);

        // ── Datos de tenant en sesión ─────────────────────────────────────────

        public static int? GetEmpresaId(HttpContext context)
        {
            var val = context.Session.GetString("EmpresaId");
            return int.TryParse(val, out var id) ? id : null;
        }

        public static int? GetEmpresaRolId(HttpContext context)
        {
            var val = context.Session.GetString("EmpresaRolId");
            return int.TryParse(val, out var id) ? id : null;
        }

        // ── Acceso a módulos ──────────────────────────────────────────────────

        /// <summary>Devuelve los códigos de módulo accesibles para el usuario en sesión.</summary>
        public static HashSet<string> GetModulos(HttpContext context)
        {
            var json = context.Session.GetString("Modulos");
            if (string.IsNullOrEmpty(json)) return new HashSet<string>();
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
        }

        /// <summary>True si el usuario tiene acceso al módulo indicado.</summary>
        public static bool HasModulo(HttpContext context, string moduloCodigo)
        {
            if (IsAdmin(context)) return true;
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

        // ── Escritura en sesión (usado en Login) ──────────────────────────────

        public static void SetSesionTenant(HttpContext context, int empresaId, int? rolId,
            IEnumerable<string> modulos,
            Dictionary<string, HashSet<string>> permisos)
        {
            context.Session.SetString("EmpresaId",    empresaId.ToString());
            context.Session.SetString("EmpresaRolId", rolId?.ToString() ?? "");
            context.Session.SetString("Modulos",      JsonSerializer.Serialize(modulos));
            context.Session.SetString("Permisos",     JsonSerializer.Serialize(permisos));
        }

        public static void ClearSesionTenant(HttpContext context)
        {
            context.Session.Remove("EmpresaId");
            context.Session.Remove("EmpresaRolId");
            context.Session.Remove("Modulos");
            context.Session.Remove("Permisos");
        }

        // ── Resolución de rol desde DB ────────────────────────────────────────

        public static string ResolveRole(string role)
            => string.IsNullOrWhiteSpace(role) ? EmpresaUserRole : role.ToLowerInvariant();
    }
}
