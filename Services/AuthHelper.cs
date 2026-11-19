using Microsoft.AspNetCore.Http;

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

        // ── Quién es: personal de la plataforma o de una empresa ──────────────
        // Ninguna de estas preguntas decide qué puede hacer la persona: eso lo decide el catálogo de permisos (ADR-029). Aquí solo se
        // responde QUIÉN es y en qué contexto trabaja, para acotar de qué datos se ocupa una pantalla.

        private static bool TieneRol(HttpContext context, string rol)
            => HayCuenta(context) && string.Equals(Reclamo(context, System.Security.Claims.ClaimTypes.Role), rol, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Personal de la plataforma (operador), en cualquier contexto: la marca de su cuenta o, en el contexto de plataforma, el rol
        /// «admin» de su acceso. Dentro de una empresa el operador entra con el rol de ese acceso, así que el rol solo no basta (D66).
        /// El operador actúa sobre cualquiera de las empresas que ve su contexto (el filtro de tenant ya las acota); el resto de las
        /// personas, solo sobre la empresa de su sesión (<see cref="PuedeActuarSobreEmpresa"/>).
        /// </summary>
        public static bool EsOperador(HttpContext context) => IsOperador(context) || TieneRol(context, AdminRole);

        /// <summary>
        /// El operador en el contexto de plataforma, sin empresa elegida: entra con el rol «admin» (lo que antes se llamaba
        /// <c>IsAdmin</c>). Decide el ámbito de tenant de la petición y el tenant de soporte; no es un permiso.
        /// </summary>
        public static bool EnPlataforma(HttpContext context) => TieneRol(context, AdminRole);

        /// <summary>
        /// Rol de texto del acceso con el que se evalúan los permisos: <c>empresa_admin</c> o <c>empresa_user</c>. Lo usa solo el
        /// armado del contexto del evaluador (<see cref="Permisos.ContextoDePermisosHttp"/>); ninguna pantalla decide con él.
        /// </summary>
        public static string RolLegadoDeAcceso(HttpContext context)
            => TieneRol(context, EmpresaAdminRole) ? EmpresaAdminRole : EmpresaUserRole;

        /// <summary>
        /// ¿Puede quien hace la petición actuar sobre los datos de esa empresa? El operador, sobre cualquiera de las que ve su contexto;
        /// el resto de las personas, solo sobre la empresa de su sesión. Es el alcance de los datos, no un permiso: el permiso de la
        /// pantalla lo comprueba antes el filtro de permisos. Sin esta pregunta, un rol con permisos de administración podría tocar
        /// las demás empresas de su tenant.
        /// </summary>
        public static bool PuedeActuarSobreEmpresa(HttpContext context, int? idEmpresa)
            => EsOperador(context) || (idEmpresa is > 0 && idEmpresa == GetEmpresaId(context));

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
            if (!EnPlataforma(context) || GetEmpresaId(context) is > 0) return null;
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

        /// <summary>
        /// True si el usuario tiene acceso al módulo indicado. Lo deciden, en cada petición, el plan del tenant (el derecho
        /// <c>modulo.&lt;código&gt;</c>) y la activación vigente de la empresa, que <see cref="Derechos.EstadoDelTenantMiddleware"/> deja
        /// en la petición (F1.7). Ya no hay una copia de los módulos en la sesión (F1.6e-3): una petición que no pasó por ese middleware
        /// no tiene ningún módulo, así que se ofrece menos, nunca más. El operador en el contexto de plataforma no depende de ninguna
        /// empresa y entra a todos.
        /// </summary>
        public static bool HasModulo(HttpContext context, string moduloCodigo)
        {
            if (EnPlataforma(context)) return true;
            return context.Items.TryGetValue(Derechos.EstadoDelTenantMiddleware.ClaveDerechos, out var evaluados)
                   && evaluados is Derechos.DerechosDeLaPeticion derechos
                   && derechos.TieneModulo(moduloCodigo);
        }

        // Los permisos por acción (ver, crear, editar, eliminar y las propias de cada pantalla) ya no viven aquí: los decide el
        // evaluador de permisos en cada petición y las pantallas los piden con [RequierePermiso] y this.Puede(...).

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
