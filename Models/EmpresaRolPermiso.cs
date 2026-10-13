using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models
{
    [Table("empresa_rol_permisos")]
    public class EmpresaRolPermiso : ITenantEntity
    {
        [Column("id_rol")]
        public int IdRol { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_modulo")]
        public int IdModulo { get; set; }

        [Column("puede_ver")]
        public bool PuedeVer { get; set; }

        [Column("puede_crear")]
        public bool PuedeCrear { get; set; }

        [Column("puede_editar")]
        public bool PuedeEditar { get; set; }

        [Column("puede_eliminar")]
        public bool PuedeEliminar { get; set; }

        public EmpresaRol Rol { get; set; } = null!;
        public Modulo Modulo { get; set; } = null!;
    }
}
