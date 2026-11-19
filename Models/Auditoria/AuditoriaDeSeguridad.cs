using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Auditoria
{
    /// <summary>Valores permitidos de <c>resultado</c> en las dos tablas de auditoría (CK_auditoria_*_resultado).</summary>
    public static class ResultadosDeEvento
    {
        public const string Ok = "ok";
        public const string Fallo = "fallo";
        public const string Denegado = "denegado";
    }

    /// <summary>
    /// Lo que cada tabla de auditoría guarda de un evento de seguridad (ADR-030, F1.8). Las dos tablas, la de tenant y la de plataforma,
    /// tienen las mismas columnas; se diferencian en <c>id_tenant</c> y en que la de tenant lleva RLS. Solo se agrega: un disparador
    /// rechaza cualquier UPDATE o DELETE. Nunca lleva contraseñas, códigos, tokens ni documentos completos.
    /// </summary>
    public abstract class EventoDeAuditoria
    {
        [Key]
        [Column("id_evento")]
        public long IdEvento { get; set; }

        /// <summary>Instante UTC, con el reloj del servicio. La columna también tiene <c>SYSUTCDATETIME()</c> por omisión en el script, para quien inserte sin fecha.</summary>
        [Column("fecha_hora")]
        public DateTime FechaHora { get; set; }

        /// <summary><c>grupo.accion</c>, uno de <see cref="Services.Auditoria.TiposDeEvento"/>.</summary>
        [Required]
        [StringLength(60)]
        [Column("tipo", TypeName = "varchar(60)")]
        public string Tipo { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="ResultadosDeEvento"/>.</summary>
        [Required]
        [StringLength(10)]
        [Column("resultado", TypeName = "varchar(10)")]
        public string Resultado { get; set; } = ResultadosDeEvento.Ok;

        /// <summary>Cuenta global de quien lo hizo; nulo si no se sabe (por ejemplo, una cuenta que no existe). Sin clave foránea a propósito.</summary>
        [Column("id_cuenta")]
        public int? IdCuenta { get; set; }

        /// <summary>Con qué nombre entró o se intentó entrar (enmascarado si la cuenta no existe).</summary>
        [StringLength(100)]
        [Column("usuario")]
        public string? Usuario { get; set; }

        [Column("es_operador")]
        public bool EsOperador { get; set; }

        [Column("id_empresa")]
        public int? IdEmpresa { get; set; }

        [StringLength(40)]
        [Column("objeto_tipo", TypeName = "varchar(40)")]
        public string? ObjetoTipo { get; set; }

        [StringLength(60)]
        [Column("objeto_id")]
        public string? ObjetoId { get; set; }

        /// <summary>JSON breve sin secretos (CHECK ISJSON).</summary>
        [StringLength(2000)]
        [Column("detalle")]
        public string? Detalle { get; set; }

        [StringLength(45)]
        [Column("ip", TypeName = "varchar(45)")]
        public string? Ip { get; set; }

        [StringLength(200)]
        [Column("agente")]
        public string? Agente { get; set; }

        /// <summary><c>HttpContext.TraceIdentifier</c>: el mismo valor de <c>X-Correlation-Id</c> y del ámbito de los logs.</summary>
        [Required]
        [StringLength(64)]
        [Column("id_correlacion", TypeName = "varchar(64)")]
        public string IdCorrelacion { get; set; } = string.Empty;
    }

    /// <summary>Auditoría de lo que ocurre dentro de un tenant (tabla de tenant con RLS, script 045).</summary>
    [Table("auditoria_seguridad")]
    public class AuditoriaSeguridad : EventoDeAuditoria, ITenantEntity
    {
        [Column("id_tenant")]
        public int IdTenant { get; set; }
    }

    /// <summary>
    /// Auditoría de las cuentas y del operador (esquema saas, script 045): plano de control, sin RLS. <c>IdTenant</c> es el tenant
    /// sobre el que se actuó, si lo hay; no filtra nada.
    /// </summary>
    [Table("auditoria_plataforma", Schema = "saas")]
    public class AuditoriaPlataforma : EventoDeAuditoria
    {
        [Column("id_tenant")]
        public int? IdTenant { get; set; }
    }
}
