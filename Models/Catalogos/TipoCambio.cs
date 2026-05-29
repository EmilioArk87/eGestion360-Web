using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("tipos_cambio")]
    public class TipoCambio
    {
        [Key]
        [Column("id_tipo_cambio")]
        public int IdTipoCambio { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda origen")]
        [Column("moneda_origen")]
        public string MonedaOrigen { get; set; } = string.Empty;

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda destino")]
        [Column("moneda_destino")]
        public string MonedaDestino { get; set; } = string.Empty;

        [Display(Name = "Fecha")]
        [Column("fecha")]
        public DateTime Fecha { get; set; }

        [Column("tasa", TypeName = "decimal(18,8)")]
        [Display(Name = "Tasa")]
        public decimal Tasa { get; set; }

        // bch | manual | api
        [StringLength(20)]
        [Column("fuente")]
        public string Fuente { get; set; } = "manual";

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        public Empresa Empresa { get; set; } = null!;
    }
}
