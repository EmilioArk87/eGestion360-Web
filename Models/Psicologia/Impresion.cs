using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>
    /// Registro de cada documento impreso (psi.impresiones, script 042): qué atención o qué constancia (exactamente una de las dos,
    /// CK_impresiones_documento), con qué plantilla y versión, en qué papel, quién y cuándo. Solo se inserta; nunca se edita.
    /// </summary>
    [Table("impresiones", Schema = "psi")]
    public class Impresion : ITenantEntity
    {
        [Key]
        [Column("id_impresion")]
        public long IdImpresion { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_atencion")]
        public int? IdAtencion { get; set; }

        [Column("id_constancia")]
        public int? IdConstancia { get; set; }

        /// <summary>Mismo valor para todos los documentos impresos de una vez (por ejemplo, la jornada completa).</summary>
        [Column("id_lote")]
        public Guid? IdLote { get; set; }

        [Required]
        [StringLength(40)]
        [Column("plantilla", TypeName = "varchar(40)")]
        public string Plantilla { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("version_plantilla", TypeName = "varchar(20)")]
        public string VersionPlantilla { get; set; } = string.Empty;

        [Required]
        [StringLength(15)]
        [Column("tamano_papel", TypeName = "varchar(15)")]
        public string TamanoPapel { get; set; } = TamanosPapel.Carta;

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        public Atencion? Atencion { get; set; }
        public Constancia? Constancia { get; set; }
    }
}
