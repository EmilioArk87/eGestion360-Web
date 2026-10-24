using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Plataforma
{
    // Planes, versiones, derechos de uso y el plan de cada tenant (paso F1.7; script 040; ADR-010 y ADR-013). Todo vive en el
    // esquema saas, el plano de control: sin id_tenant de aislamiento y sin Row-Level Security. El código de la aplicación nunca
    // nombra un plan: pregunta por derechos (por ejemplo "modulo.flota" o "limite.usuarios") del plan vigente de su tenant.

    /// <summary>Un plan comercial (por ejemplo «flota»). Sus condiciones están en sus versiones; el código es solo una etiqueta.</summary>
    [Table("planes", Schema = "saas")]
    public class Plan
    {
        [Key]
        [Column("id_plan")]
        public int IdPlan { get; set; }

        [Required, StringLength(30)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

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
    }

    /// <summary>Una versión de un plan: lo que concede mientras está vigente. Cambiar un plan es crear una versión nueva.</summary>
    [Table("plan_versiones", Schema = "saas")]
    public class PlanVersion
    {
        [Key]
        [Column("id_plan_version")]
        public int IdPlanVersion { get; set; }

        [Column("id_plan")]
        public int IdPlan { get; set; }

        [Column("numero")]
        public int Numero { get; set; }

        [Column("vigente_desde")]
        public DateOnly VigenteDesde { get; set; }

        [Column("vigente_hasta")]
        public DateOnly? VigenteHasta { get; set; }

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
    }

    /// <summary>
    /// Un derecho de uso: <c>modulo.&lt;codigo&gt;</c> (un módulo del ERP), <c>caracteristica.&lt;x&gt;</c> (una función) o
    /// <c>limite.&lt;x&gt;</c> (un tope numérico). El código empieza por su tipo; la base lo exige.
    /// </summary>
    [Table("derechos", Schema = "saas")]
    public class Derecho
    {
        [Key]
        [Column("id_derecho")]
        public int IdDerecho { get; set; }

        [Required, StringLength(60)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="TipoDerecho"/>.</summary>
        [Required, StringLength(15)]
        [Column("tipo")]
        public string Tipo { get; set; } = TipoDerecho.Modulo;

        [Required, StringLength(200)]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

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
    }

    /// <summary>Un derecho que concede una versión de plan. Una fila ausente significa derecho no concedido.</summary>
    [Table("plan_version_derechos", Schema = "saas")]
    public class PlanVersionDerecho
    {
        [Column("id_plan_version")]
        public int IdPlanVersion { get; set; }

        [Column("id_derecho")]
        public int IdDerecho { get; set; }

        /// <summary>Repite el tipo del derecho: la base lo comprueba con una clave foránea compuesta.</summary>
        [Required, StringLength(15)]
        [Column("tipo_derecho")]
        public string TipoDelDerecho { get; set; } = TipoDerecho.Modulo;

        /// <summary>Solo en los derechos de tipo <c>limite</c>. Nulo = sin tope.</summary>
        [Column("valor_limite")]
        public int? ValorLimite { get; set; }

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
    }

    /// <summary>El plan de un tenant en el tiempo: la fila con <c>vigente_hasta</c> nulo es el plan de hoy; las cerradas son su historial.</summary>
    [Table("tenant_planes", Schema = "saas")]
    public class TenantPlan
    {
        [Key]
        [Column("id_tenant_plan")]
        public int IdTenantPlan { get; set; }

        /// <summary>De quién es la fila. No separa filas entre tenants: esta tabla es del plano de control y no lleva RLS.</summary>
        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_plan_version")]
        public int IdPlanVersion { get; set; }

        [Column("vigente_desde")]
        public DateTime VigenteDesde { get; set; }

        [Column("vigente_hasta")]
        public DateTime? VigenteHasta { get; set; }

        [StringLength(300)]
        [Column("motivo")]
        public string? Motivo { get; set; }

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
    }

    /// <summary>Valores permitidos de <see cref="Derecho.Tipo"/> (la base los exige con un CHECK).</summary>
    public static class TipoDerecho
    {
        public const string Modulo = "modulo";
        public const string Caracteristica = "caracteristica";
        public const string Limite = "limite";

        public static readonly IReadOnlyList<string> Todos = new[] { Modulo, Caracteristica, Limite };
    }

    /// <summary>Códigos de los dos límites que la aplicación aplica hoy (D54); el resto se define cuando haya cobro.</summary>
    public static class CodigosDeDerecho
    {
        public const string PrefijoModulo = "modulo.";
        public const string LimiteUsuarios = "limite.usuarios";
        public const string LimiteEmpresas = "limite.empresas";

        /// <summary>El código del derecho de un módulo, por ejemplo <c>modulo.flota</c> para <c>flota</c>.</summary>
        public static string DeModulo(string moduloCodigo) => PrefijoModulo + moduloCodigo.Trim().ToLowerInvariant();
    }

    /// <summary>Qué hace una suspensión (<see cref="Tenant.ModoSuspension"/>); la base lo exige con un CHECK.</summary>
    public static class ModoDeSuspension
    {
        /// <summary>El tenant ve sus datos pero no puede guardar cambios. Es el valor por defecto (D53).</summary>
        public const string Lectura = "lectura";

        /// <summary>El tenant no entra a nada hasta que se reactive.</summary>
        public const string Bloqueo = "bloqueo";

        public static readonly IReadOnlyList<string> Todos = new[] { Lectura, Bloqueo };
    }
}
