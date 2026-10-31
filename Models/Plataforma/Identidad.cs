using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Plataforma
{
    // Identidad global (paso F1.5; scripts 038 y 039; ADR-003): una cuenta por ser humano, con membresías en los tenants donde
    // trabaja. Las tablas viven en el esquema saas (plano de control, sin RLS) porque se leen ANTES de conocer el tenant. Lo único de
    // tenant es dbo.accesos_empresa (membresía ↔ empresa ↔ rol), que entra a la política RLS.

    /// <summary>La cuenta de acceso global de una persona: correo, clave y estado. Una sola para todos los tenants donde trabaja.</summary>
    [Table("cuentas", Schema = "saas")]
    public class Cuenta
    {
        [Key]
        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Column("uid_publico")]
        public Guid UidPublico { get; set; }

        [Required, StringLength(256)]
        [Column("correo")]
        public string Correo { get; set; } = string.Empty;

        /// <summary>Nombre de usuario de transición (D44): se sigue aceptando para entrar mientras dure la migración al correo.</summary>
        [StringLength(50)]
        [Column("nombre_usuario")]
        public string? NombreUsuario { get; set; }

        [Required, StringLength(255)]
        [Column("hash_clave")]
        public string HashClave { get; set; } = string.Empty;

        [Column("debe_cambiar_clave")]
        public bool DebeCambiarClave { get; set; }

        /// <summary>Uno de <see cref="EstadoCuenta"/>.</summary>
        [Required, StringLength(12)]
        [Column("estado")]
        public string Estado { get; set; } = EstadoCuenta.Activa;

        /// <summary>Personal de la plataforma (el antiguo «administrador general», D23). Exige MFA desde el primer día (D47).</summary>
        [Column("es_operador")]
        public bool EsOperador { get; set; }

        [Column("fecha_ultimo_acceso")]
        public DateTime? FechaUltimoAcceso { get; set; }

        /// <summary>Id de <c>dbo.Users</c> de donde salió la cuenta (script 039). Se retira junto con <c>Users</c> en F1.10.</summary>
        [Column("id_user_legado")]
        public int? IdUserLegado { get; set; }

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Un nombre con el que la cuenta puede entrar (usuario de transición) o un correo secundario que NO inicia sesión.</summary>
    [Table("cuenta_alias", Schema = "saas")]
    public class CuentaAlias
    {
        [Key]
        [Column("id_alias")]
        public int IdAlias { get; set; }

        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Required, StringLength(256)]
        [Column("alias")]
        public string Alias { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="TipoAlias"/>.</summary>
        [Required, StringLength(10)]
        [Column("tipo")]
        public string Tipo { get; set; } = TipoAlias.Usuario;

        [Column("puede_iniciar_sesion")]
        public bool PuedeIniciarSesion { get; set; } = true;

        [Column("id_user_legado")]
        public int? IdUserLegado { get; set; }

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Pertenencia de una cuenta a un tenant, con la persona que es en él. Es plano de control: sin RLS.</summary>
    [Table("membresias", Schema = "saas")]
    public class Membresia
    {
        [Key]
        [Column("id_membresia")]
        public int IdMembresia { get; set; }

        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        /// <summary>Tenant de la membresía. Referencia al tenant: no es el id_tenant de aislamiento de una tabla de tenant.</summary>
        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_persona")]
        public int? IdPersona { get; set; }

        /// <summary>Uno de <see cref="EstadoMembresia"/>.</summary>
        [Required, StringLength(12)]
        [Column("estado")]
        public string Estado { get; set; } = EstadoMembresia.Activa;

        [Column("fecha_estado")]
        public DateTime FechaEstado { get; set; }

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// A qué empresa de su tenant entra una membresía y con qué rol. Es tabla DE TENANT: lleva <c>id_tenant</c> y la política RLS la
    /// protege, así que leerla exige fijar antes el tenant de la membresía.
    /// </summary>
    [Table("accesos_empresa")]
    public class AccesoEmpresa : ITenantEntity
    {
        [Key]
        [Column("id_acceso_empresa")]
        public int IdAccesoEmpresa { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_membresia")]
        public int IdMembresia { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>Rol de la empresa (<c>empresa_roles</c>); nulo en un administrador de empresa, que tiene todos los permisos.</summary>
        [Column("id_rol")]
        public int? IdRol { get; set; }

        /// <summary>Rol de texto que traía <c>Users.Role</c> (<c>empresa_admin</c> o <c>empresa_user</c>); se retira en F1.6.</summary>
        [Required, StringLength(20)]
        [Column("rol_legado")]
        public string RolLegado { get; set; } = "empresa_user";

        /// <summary>La empresa que se abre al iniciar sesión cuando la membresía tiene varias.</summary>
        [Column("es_principal")]
        public bool EsPrincipal { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Fallos de clave seguidos de una cuenta y hasta cuándo está bloqueada (D46).</summary>
    [Table("cuenta_intentos", Schema = "saas")]
    public class CuentaIntentos
    {
        [Key]
        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Column("intentos_fallidos")]
        public int IntentosFallidos { get; set; }

        [Column("bloqueada_hasta")]
        public DateTime? BloqueadaHasta { get; set; }

        [Column("ultimo_fallo")]
        public DateTime? UltimoFallo { get; set; }
    }

    /// <summary>Secreto TOTP de la cuenta. Nunca en claro: lo protege Data Protection.</summary>
    [Table("cuenta_mfa", Schema = "saas")]
    public class CuentaMfa
    {
        [Key]
        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Required, StringLength(500)]
        [Column("secreto_protegido")]
        public string SecretoProtegido { get; set; } = string.Empty;

        /// <summary>Verdadero cuando la persona ya probó un código de su aplicación. Antes de eso el MFA no se exige al entrar.</summary>
        [Column("confirmada")]
        public bool Confirmada { get; set; }

        [Column("fecha_confirmacion")]
        public DateTime? FechaConfirmacion { get; set; }

        /// <summary>Último paso de 30 s aceptado: un código ya usado no sirve otra vez.</summary>
        [Column("ultimo_paso_usado")]
        public long? UltimoPasoUsado { get; set; }

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Un código de recuperación de MFA de un solo uso: solo se guarda su hash SHA-256 en hexadecimal.</summary>
    [Table("cuenta_mfa_recuperacion", Schema = "saas")]
    public class CuentaMfaRecuperacion
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Required, StringLength(64)]
        [Column("hash_codigo")]
        public string HashCodigo { get; set; } = string.Empty;

        [Column("usado_en")]
        public DateTime? UsadoEn { get; set; }

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>Invitación a unirse a un tenant. Del token solo se guarda el hash; el token viaja una vez, por correo.</summary>
    [Table("invitaciones", Schema = "saas")]
    public class Invitacion
    {
        [Key]
        [Column("id_invitacion")]
        public int IdInvitacion { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Required, StringLength(256)]
        [Column("correo")]
        public string Correo { get; set; } = string.Empty;

        [Column("id_empresa")]
        public int? IdEmpresa { get; set; }

        [Column("id_rol")]
        public int? IdRol { get; set; }

        [Required, StringLength(64)]
        [Column("token_hash")]
        public string TokenHash { get; set; } = string.Empty;

        [Column("vence_en")]
        public DateTime VenceEn { get; set; }

        [Column("usada_en")]
        public DateTime? UsadaEn { get; set; }

        [Column("id_cuenta")]
        public int? IdCuenta { get; set; }

        [Required, StringLength(100)]
        [Column("creada_por")]
        public string CreadaPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>Una llave de ASP.NET Core Data Protection (D48): la cookie y los secretos de MFA siguen válidos tras reiniciar el sitio.</summary>
    [Table("llaves_proteccion_datos", Schema = "saas")]
    public class LlaveProteccionDatos
    {
        [Key]
        [Column("id_llave")]
        public int IdLlave { get; set; }

        [StringLength(200)]
        [Column("nombre_amistoso")]
        public string? NombreAmistoso { get; set; }

        [Column("xml")]
        public string? Xml { get; set; }
    }

    /// <summary>Valores permitidos de <see cref="Cuenta.Estado"/> (la base los exige con un CHECK).</summary>
    public static class EstadoCuenta
    {
        public const string Activa = "activa";
        public const string Inactiva = "inactiva";
        public const string Invitada = "invitada";
    }

    /// <summary>Valores permitidos de <see cref="Membresia.Estado"/>.</summary>
    public static class EstadoMembresia
    {
        public const string Invitada = "invitada";
        public const string Activa = "activa";
        public const string Suspendida = "suspendida";
    }

    /// <summary>Valores permitidos de <see cref="CuentaAlias.Tipo"/>.</summary>
    public static class TipoAlias
    {
        public const string Usuario = "usuario";
        public const string Correo = "correo";
    }
}
