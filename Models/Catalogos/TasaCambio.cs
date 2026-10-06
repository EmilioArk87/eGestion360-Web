using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Tasa de cambio (script 020). Convención única: 1 <see cref="MonedaOrigen"/> = <see cref="Tasa"/>
    /// <see cref="MonedaDestino"/>; por ejemplo 1 USD = 26.8925 HNL.
    ///
    /// El valor de una fila nunca se sobrescribe: una corrección de la fuente crea una versión nueva
    /// (<see cref="Version"/> + 1, <see cref="IdTasaAnterior"/>) y la anterior pasa a REEMPLAZADA.
    /// Con <see cref="IdEmpresa"/> nulo es la tasa oficial, común a todas las empresas; con valor es una
    /// tasa propia de esa empresa, que prevalece sobre la oficial para ella.
    /// </summary>
    [Table("tasas_cambio")]
    public class TasaCambio
    {
        [Key]
        [Column("id_tasa_cambio")]
        public int IdTasaCambio { get; set; }

        [Column("id_empresa")]
        public int? IdEmpresa { get; set; }

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda origen")]
        [Column("moneda_origen", TypeName = "char(3)")]
        public string MonedaOrigen { get; set; } = string.Empty;

        [Required]
        [StringLength(3)]
        [Display(Name = "Moneda destino")]
        [Column("moneda_destino", TypeName = "char(3)")]
        public string MonedaDestino { get; set; } = string.Empty;

        /// <summary>COMPRA, VENTA o REFERENCIA (<see cref="TasasCambioCatalogo.TipoTasa"/>).</summary>
        [Required]
        [StringLength(10)]
        [Display(Name = "Tipo")]
        [Column("tipo_tasa", TypeName = "varchar(10)")]
        public string TipoTasa { get; set; } = string.Empty;

        [Display(Name = "Tasa")]
        [Column("tasa", TypeName = "decimal(18,8)")]
        public decimal Tasa { get; set; }

        [Display(Name = "Vigencia")]
        [Column("fecha_vigencia")]
        public DateOnly FechaVigencia { get; set; }

        /// <summary>Momento (UTC) en que se leyó de la fuente.</summary>
        [Column("fecha_hora_obtencion")]
        public DateTime FechaHoraObtencion { get; set; }

        /// <summary>BCH_API, BCH_XLSX, BCE, DERIVADA, MANUAL o LEGADO_WINFORMS (<see cref="TasasCambioCatalogo.Fuente"/>).</summary>
        [Required]
        [StringLength(20)]
        [Column("fuente", TypeName = "varchar(20)")]
        public string Fuente { get; set; } = string.Empty;

        /// <summary>Indicador, URL o fórmula de la derivada. Nunca incluye claves.</summary>
        [StringLength(400)]
        [Column("referencia_fuente")]
        public string? ReferenciaFuente { get; set; }

        [Column("es_derivada")]
        public bool EsDerivada { get; set; }

        /// <summary>VIGENTE, REEMPLAZADA, EN_REVISION o RECHAZADA (<see cref="TasasCambioCatalogo.EstadoTasa"/>).</summary>
        [Required]
        [StringLength(12)]
        [Column("estado", TypeName = "varchar(12)")]
        public string Estado { get; set; } = TasasCambioCatalogo.EstadoTasa.Vigente;

        [Column("version")]
        public short Version { get; set; } = 1;

        [Column("id_tasa_anterior")]
        public int? IdTasaAnterior { get; set; }

        /// <summary>Ejecución del job que la obtuvo; nulo si fue captura manual.</summary>
        [Column("id_ejecucion")]
        public long? IdEjecucion { get; set; }

        [Column("eliminado")]
        public bool Eliminado { get; set; }

        [Column("fecha_eliminado")]
        public DateTime? FechaEliminado { get; set; }

        [Required]
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

        public Empresa? Empresa { get; set; }
        public TasaCambio? TasaAnterior { get; set; }
        public TasaCambioEjecucion? Ejecucion { get; set; }
    }
}
