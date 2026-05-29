using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("impuestos")]
    public class Impuesto
    {
        [Key]
        [Column("id_impuesto")]
        public int IdImpuesto { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = "isv";  // isv | retencion_isr | retencion_isv | otro

        [Column("tasa", TypeName = "decimal(9,4)")]
        [Display(Name = "Tasa (%)")]
        public decimal Tasa { get; set; }

        [Display(Name = "Es retención")]
        [Column("es_retencion")]
        public bool EsRetencion { get; set; }

        [Display(Name = "Vigente desde")]
        [Column("vigente_desde")]
        public DateTime VigenteDesde { get; set; }

        [Display(Name = "Vigente hasta")]
        [Column("vigente_hasta")]
        public DateTime? VigenteHasta { get; set; }

        [Display(Name = "Activo")]
        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("eliminado")]
        public bool Eliminado { get; set; }

        [Column("fecha_eliminado")]
        public DateTime? FechaEliminado { get; set; }

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

        public Empresa Empresa { get; set; } = null!;
    }
}
