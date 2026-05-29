using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Facturacion
{
    /// <summary>
    /// Correlativo de numeración por (empresa, tipo_documento, serie). Soporta el
    /// modelo CAI de Honduras: un CAI cubre un rango (rango_inicial..rango_final)
    /// hasta una fecha_limite. Cuando se emite una factura, se hace UPDATE atómico
    /// para reservar el siguiente número.
    /// </summary>
    [Table("factura_secuencias")]
    public class FacturaSecuencia
    {
        [Key]
        [Column("id_secuencia")]
        public int IdSecuencia { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>factura | nota_credito | nota_debito</summary>
        [Required, StringLength(20)]
        [Column("tipo_documento")]
        public string TipoDocumento { get; set; } = "factura";

        [Required, StringLength(20)]
        [Column("serie")]
        public string Serie { get; set; } = "F-01";

        [Column("proximo_numero")]
        public int ProximoNumero { get; set; } = 1;

        [Column("rango_inicial")]
        public int? RangoInicial { get; set; }

        [Column("rango_final")]
        public int? RangoFinal { get; set; }

        [StringLength(50)]
        [Column("cai_numero")]
        public string? CaiNumero { get; set; }

        [Column("fecha_limite_emision")]
        public DateTime? FechaLimiteEmision { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }
}
