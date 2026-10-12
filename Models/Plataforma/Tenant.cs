using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Plataforma
{
    /// <summary>
    /// Tenant: la cuenta comercial de la plataforma. Es quien paga, la frontera de aislamiento de los datos y la
    /// unidad de respaldo y de límites (ADR-002). Cada empresa pertenece a un tenant y un tenant puede tener varias
    /// empresas. Vive en el esquema <c>saas</c> (plano de control, ADR-013): no lleva <c>id_tenant</c> ni Row-Level
    /// Security porque es la tabla que los define. Paso F1.1 del plan de arquitectura; script 025.
    /// </summary>
    [Table("tenants", Schema = "saas")]
    public class Tenant
    {
        [Key]
        [Column("id_tenant")]
        public int IdTenant { get; set; }

        /// <summary>Identificador que se puede mostrar fuera de la aplicación; los ids numéricos nunca salen (ADR-011).</summary>
        [Column("uid_publico")]
        public Guid UidPublico { get; set; }

        /// <summary>Código corto y único, en mayúsculas (por ejemplo <c>TRANSGAR</c>).</summary>
        [Required]
        [StringLength(30)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="TipoTenant"/>. Como mucho hay un tenant interno: el de la propia plataforma (ADR-006).</summary>
        [Required]
        [StringLength(10)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = TipoTenant.Cliente;

        /// <summary>Uno de <see cref="EstadoTenant"/>.</summary>
        [Required]
        [StringLength(12)]
        [Display(Name = "Estado")]
        [Column("estado")]
        public string Estado { get; set; } = EstadoTenant.Activo;

        /// <summary>Cuándo cambió por última vez el estado.</summary>
        [Column("fecha_estado")]
        public DateTime FechaEstado { get; set; }

        [StringLength(300)]
        [Display(Name = "Motivo del estado")]
        [Column("motivo_estado")]
        public string? MotivoEstado { get; set; }

        [StringLength(100)]
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

    /// <summary>Valores permitidos de <see cref="Tenant.Tipo"/> (la base los exige con un CHECK).</summary>
    public static class TipoTenant
    {
        /// <summary>El de la propia plataforma, que usa el ERP para facturar el SaaS (ADR-006). Solo puede haber uno.</summary>
        public const string Interno = "interno";
        public const string Cliente = "cliente";
        public const string Demo = "demo";

        public static readonly IReadOnlyList<string> Todos = new[] { Interno, Cliente, Demo };
    }

    /// <summary>Valores permitidos de <see cref="Tenant.Estado"/> (la base los exige con un CHECK).</summary>
    public static class EstadoTenant
    {
        public const string Activo = "activo";
        /// <summary>No opera: se conservan sus datos y nadie de ese tenant entra (F1.7 lo aplica en cada petición).</summary>
        public const string Suspendido = "suspendido";
        public const string Cancelado = "cancelado";

        public static readonly IReadOnlyList<string> Todos = new[] { Activo, Suspendido, Cancelado };
    }
}
