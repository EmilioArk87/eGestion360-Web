using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("formas_pago")]
    public class FormaPago
    {
        [Key]
        [Column("id_forma_pago")]
        public int IdFormaPago { get; set; }

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

        // efectivo | cheque | transferencia | tarjeta | deposito | credito | otro
        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = "efectivo";

        [Display(Name = "Afecta caja")]
        [Column("afecta_caja")]
        public bool AfectaCaja { get; set; } = true;

        [Display(Name = "Afecta banco")]
        [Column("afecta_banco")]
        public bool AfectaBanco { get; set; }

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
