using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models
{
    /// <summary>
    /// Un permiso del catálogo que tiene un rol de empresa (paso F1.6a; script 041). Reemplaza a <see cref="EmpresaRolPermiso"/>, que se
    /// conserva hasta F1.10. Es una tabla de tenant: lleva <c>id_tenant</c> y la política RLS. Un rol nunca recibe permisos de ámbito
    /// plataforma (la base lo exige).
    /// </summary>
    [Table("rol_permisos")]
    public class RolPermiso : ITenantEntity
    {
        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_rol")]
        public int IdRol { get; set; }

        [Column("id_permiso")]
        public int IdPermiso { get; set; }

        /// <summary>Repite el ámbito del permiso (<c>empresa</c> o <c>modulo</c>): la base lo comprueba con una clave foránea compuesta.</summary>
        [Required, StringLength(12)]
        [Column("ambito_permiso")]
        public string AmbitoPermiso { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }
    }
}
