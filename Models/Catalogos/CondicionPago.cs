using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("condiciones_pago")]
    public class CondicionPago
    {
        [Key]
        [Column("id_condicion_pago")]
        public int IdCondicionPago { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(80)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        // contado | credito_dias | cuotas
        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = "contado";

        [Display(Name = "Días de crédito")]
        [Column("dias_credito")]
        public int DiasCredito { get; set; }

        [Display(Name = "Número de cuotas")]
        [Column("numero_cuotas")]
        public int NumeroCuotas { get; set; } = 1;

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
        public ICollection<CondicionPagoCuota> Cuotas { get; set; } = new List<CondicionPagoCuota>();
    }

    [Table("condiciones_pago_cuotas")]
    public class CondicionPagoCuota
    {
        [Key]
        [Column("id_cuota")]
        public int IdCuota { get; set; }

        [Column("id_condicion_pago")]
        public int IdCondicionPago { get; set; }

        [Column("numero_cuota")]
        public int NumeroCuota { get; set; }

        [Column("dias_vencimiento")]
        public int DiasVencimiento { get; set; }

        [Column("porcentaje", TypeName = "decimal(9,4)")]
        public decimal Porcentaje { get; set; }

        public CondicionPago CondicionPago { get; set; } = null!;
    }
}
