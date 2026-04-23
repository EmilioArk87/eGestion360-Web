using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Flota
{
    [Table("talleres")]
    public class Taller
    {
        [Key]
        [Column("id_taller")]
        public int IdTaller { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(20)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(150)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(30)]
        [Display(Name = "RTN")]
        [Column("rtn")]
        public string? Rtn { get; set; }

        [StringLength(250)]
        [Display(Name = "Dirección")]
        [Column("direccion")]
        public string? Direccion { get; set; }

        [StringLength(30)]
        [Display(Name = "Teléfono")]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [StringLength(150)]
        [Display(Name = "Email")]
        [Column("email")]
        public string? Email { get; set; }

        [StringLength(100)]
        [Display(Name = "Contacto")]
        [Column("contacto")]
        public string? Contacto { get; set; }

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
    }
}
