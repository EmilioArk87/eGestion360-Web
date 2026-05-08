using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    [Table("empresa_roles")]
    public class EmpresaRol
    {
        [Key]
        [Column("id_rol")]
        public int IdRol { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "Descripción")]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Display(Name = "Admin de empresa")]
        [Column("es_admin")]
        public bool EsAdmin { get; set; }

        [Display(Name = "Activo")]
        [Column("activo")]
        public bool Activo { get; set; } = true;

        public Empresa Empresa { get; set; } = null!;
        public ICollection<EmpresaRolPermiso> Permisos { get; set; } = new List<EmpresaRolPermiso>();
        public ICollection<User> Usuarios { get; set; } = new List<User>();
    }
}
