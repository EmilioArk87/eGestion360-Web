using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    [Table("modulos")]
    public class Modulo
    {
        [Key]
        [Column("id_modulo")]
        public int IdModulo { get; set; }

        [Required]
        [StringLength(50)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [StringLength(50)]
        [Column("icono")]
        public string? Icono { get; set; }

        [Column("orden")]
        public int Orden { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        public ICollection<EmpresaModulo> EmpresaModulos { get; set; } = new List<EmpresaModulo>();
        public ICollection<EmpresaRolPermiso> RolPermisos { get; set; } = new List<EmpresaRolPermiso>();
    }
}
