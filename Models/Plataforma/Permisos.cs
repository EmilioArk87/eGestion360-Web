using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Plataforma
{
    // Catálogo de permisos y roles plantilla (paso F1.6a; script 041; ADR-003 y ADR-029). Todo vive en el esquema saas, el plano de
    // control: sin id_tenant de aislamiento y sin Row-Level Security. Los permisos de cada rol de empresa (RolPermiso) sí son de tenant.

    /// <summary>
    /// Un permiso del catálogo con la forma <c>modulo.recurso.accion</c> (por ejemplo <c>flota.vehiculos.editar</c>). La lista que
    /// siembra el script 041 es <c>Services/Permisos/CatalogoDePermisos.cs</c>.
    /// </summary>
    [Table("permisos", Schema = "saas")]
    public class Permiso
    {
        [Key]
        [Column("id_permiso")]
        public int IdPermiso { get; set; }

        [Required, StringLength(80)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Código del módulo, o <c>plataforma</c> / <c>administracion</c> para lo que no es un módulo del plan.</summary>
        [Required, StringLength(30)]
        [Column("modulo")]
        public string Modulo { get; set; } = string.Empty;

        [Required, StringLength(40)]
        [Column("recurso")]
        public string Recurso { get; set; } = string.Empty;

        [Required, StringLength(30)]
        [Column("accion")]
        public string Accion { get; set; } = string.Empty;

        /// <summary>Uno de <c>AmbitoDePermiso</c>: <c>plataforma</c>, <c>empresa</c> o <c>modulo</c>.</summary>
        [Required, StringLength(12)]
        [Column("ambito")]
        public string Ambito { get; set; } = string.Empty;

        /// <summary>La casilla de antes (<c>ver</c>, <c>crear</c>, <c>editar</c>, <c>eliminar</c>) que concede este permiso al migrar los roles; nula si no corresponde.</summary>
        [StringLength(10)]
        [Column("accion_legada")]
        public string? AccionLegada { get; set; }

        [Required, StringLength(200)]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Column("orden")]
        public int Orden { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

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

    /// <summary>Un rol de partida para tenants nuevos (D64): administrador, operador o consulta de un módulo. F1.9 los copia al crear un tenant.</summary>
    [Table("roles_plantilla", Schema = "saas")]
    public class RolPlantilla
    {
        [Key]
        [Column("id_rol_plantilla")]
        public int IdRolPlantilla { get; set; }

        /// <summary>Por ejemplo <c>flota_operador</c>.</summary>
        [Required, StringLength(40)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(30)]
        [Column("modulo")]
        public string Modulo { get; set; } = string.Empty;

        [Required, StringLength(50)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(200)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Column("orden")]
        public int Orden { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

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

    /// <summary>Un permiso de una plantilla de rol. La base obliga a que sea del mismo módulo que la plantilla.</summary>
    [Table("roles_plantilla_permisos", Schema = "saas")]
    public class RolPlantillaPermiso
    {
        [Column("id_rol_plantilla")]
        public int IdRolPlantilla { get; set; }

        [Column("id_permiso")]
        public int IdPermiso { get; set; }

        /// <summary>Repite el módulo de la plantilla y del permiso: la base lo comprueba con dos claves foráneas compuestas.</summary>
        [Required, StringLength(30)]
        [Column("modulo")]
        public string Modulo { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }
    }
}
