using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Quién es la persona que tiene la cookie, y en qué contexto trabaja (paso F1.5, ADR-003 y D50). La identidad sale de la cookie
    /// y no de la sesión: la sesión en memoria se pierde con cada reinicio del sitio, la cookie no.
    /// </summary>
    /// <param name="IdCuenta">Cuenta global (<c>saas.cuentas</c>).</param>
    /// <param name="IdUserLegado">Id de <c>dbo.Users</c> que usan las pantallas anteriores a F1.5 (espejo en la sesión); se retira en F1.10.</param>
    /// <param name="NombreUsuario">Con qué nombre aparece en la auditoría («creado por»): el usuario con el que entró, o su correo.</param>
    /// <param name="Correo">Correo de la cuenta.</param>
    /// <param name="NombrePersona">Nombre corto de la persona en el tenant donde trabaja.</param>
    /// <param name="Rol">Rol de texto que entienden las pantallas (<c>admin</c>, <c>empresa_admin</c> o <c>empresa_user</c>); en F1.6 pasa a permisos por acción.</param>
    /// <param name="EsOperador">Personal de la plataforma (el antiguo administrador general).</param>
    /// <param name="DebeCambiarClave">Cambio de clave pendiente: no se entra al resto del sistema hasta hacerlo.</param>
    /// <param name="IdMembresia">Membresía con la que trabaja; nulo en el contexto de plataforma del operador.</param>
    /// <param name="IdTenant">Tenant de la membresía. Nulo en el contexto de plataforma.</param>
    /// <param name="CodigoTenant">Código del tenant, solo para mostrarlo.</param>
    /// <param name="IdEmpresa">Empresa con la que trabaja; nula en el contexto de plataforma.</param>
    /// <param name="IdRolEmpresa">Rol de la empresa (<c>empresa_roles</c>); nulo en un administrador de empresa.</param>
    /// <param name="MfaVerificada">Verdadero si en este inicio de sesión probó un segundo factor.</param>
    /// <param name="Sello">Huella de lo que se leyó de la base al entrar; si cambia, la cookie se renueva con lo nuevo.</param>
    public sealed record DatosDeIdentidad(
        int IdCuenta,
        int? IdUserLegado,
        string NombreUsuario,
        string Correo,
        string NombrePersona,
        string Rol,
        bool EsOperador,
        bool DebeCambiarClave,
        int? IdMembresia,
        int? IdTenant,
        string? CodigoTenant,
        int? IdEmpresa,
        int? IdRolEmpresa,
        bool MfaVerificada,
        string Sello);

    /// <summary>Nombres de las reclamaciones (claims) de la cookie de autenticación.</summary>
    public static class ReclamosDeIdentidad
    {
        /// <summary>Esquema de autenticación por cookie.</summary>
        public const string Esquema = "eg360";

        public const string Cuenta = "eg360:cuenta";
        public const string UserLegado = "eg360:user_legado";
        public const string NombrePersona = "eg360:persona";
        public const string Operador = "eg360:operador";
        public const string DebeCambiarClave = "eg360:cambiar_clave";
        public const string Membresia = "eg360:membresia";
        public const string Tenant = "eg360:tenant";
        public const string CodigoTenant = "eg360:tenant_codigo";
        public const string Empresa = "eg360:empresa";
        public const string RolEmpresa = "eg360:rol_empresa";
        public const string MfaVerificada = "eg360:mfa";
        public const string Sello = "eg360:sello";

        /// <summary>Arma la identidad de la cookie. Los valores nulos no generan reclamación.</summary>
        public static ClaimsPrincipal Crear(DatosDeIdentidad d)
        {
            var reclamos = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Texto(d.IdCuenta)),
                new(Cuenta, Texto(d.IdCuenta)),
                new(ClaimTypes.Name, d.NombreUsuario),
                new(ClaimTypes.Email, d.Correo),
                new(ClaimTypes.Role, d.Rol),
                new(NombrePersona, d.NombrePersona),
                new(Sello, d.Sello)
            };
            if (d.IdUserLegado is { } legado) reclamos.Add(new Claim(UserLegado, Texto(legado)));
            if (d.EsOperador) reclamos.Add(new Claim(Operador, "1"));
            if (d.DebeCambiarClave) reclamos.Add(new Claim(DebeCambiarClave, "1"));
            if (d.MfaVerificada) reclamos.Add(new Claim(MfaVerificada, "1"));
            if (d.IdMembresia is { } membresia) reclamos.Add(new Claim(Membresia, Texto(membresia)));
            if (d.IdTenant is { } tenant) reclamos.Add(new Claim(Tenant, Texto(tenant)));
            if (!string.IsNullOrEmpty(d.CodigoTenant)) reclamos.Add(new Claim(CodigoTenant, d.CodigoTenant));
            if (d.IdEmpresa is { } empresa) reclamos.Add(new Claim(Empresa, Texto(empresa)));
            if (d.IdRolEmpresa is { } rol) reclamos.Add(new Claim(RolEmpresa, Texto(rol)));

            return new ClaimsPrincipal(new ClaimsIdentity(reclamos, Esquema, ClaimTypes.Name, ClaimTypes.Role));
        }

        /// <summary>Lee la identidad de la cookie; <c>null</c> si el usuario no está autenticado o le falta la cuenta.</summary>
        public static DatosDeIdentidad? Leer(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true) return null;
            if (Entero(principal, Cuenta) is not { } idCuenta) return null;

            return new DatosDeIdentidad(
                idCuenta,
                Entero(principal, UserLegado),
                principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                principal.FindFirstValue(NombrePersona) ?? string.Empty,
                principal.FindFirstValue(ClaimTypes.Role) ?? AuthHelper.EmpresaUserRole,
                principal.FindFirstValue(Operador) == "1",
                principal.FindFirstValue(DebeCambiarClave) == "1",
                Entero(principal, Membresia),
                Entero(principal, Tenant),
                principal.FindFirstValue(CodigoTenant),
                Entero(principal, Empresa),
                Entero(principal, RolEmpresa),
                principal.FindFirstValue(MfaVerificada) == "1",
                principal.FindFirstValue(Sello) ?? string.Empty);
        }

        /// <summary>
        /// Huella de lo que decide qué puede hacer la persona. Si la cuenta se desactiva, cambia su rol o se le pide cambiar la clave,
        /// la huella cambia y la cookie se renueva en la petición siguiente, sin cerrar la sesión.
        /// </summary>
        public static string CalcularSello(string estadoCuenta, bool esOperador, bool debeCambiarClave, string? estadoMembresia,
            int? idPersona, string? rolLegado, int? idRol, bool accesoActivo)
        {
            var texto = string.Join('|', estadoCuenta, esOperador ? "1" : "0", debeCambiarClave ? "1" : "0", estadoMembresia ?? "-",
                idPersona?.ToString(CultureInfo.InvariantCulture) ?? "-", rolLegado ?? "-",
                idRol?.ToString(CultureInfo.InvariantCulture) ?? "-", accesoActivo ? "1" : "0");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..16];
        }

        private static string Texto(int valor) => valor.ToString(CultureInfo.InvariantCulture);

        private static int? Entero(ClaimsPrincipal principal, string tipo)
            => int.TryParse(principal.FindFirstValue(tipo), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
