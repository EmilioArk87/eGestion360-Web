using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("productos_servicios")]
    public class ProductoServicio
    {
        [Key]
        [Column("id_producto")]
        public int IdProducto { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Descripción")]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = "producto";  // producto | servicio

        [StringLength(20)]
        [Display(Name = "Unidad de medida")]
        [Column("unidad_medida")]
        public string? UnidadMedida { get; set; }

        [Column("precio_default", TypeName = "decimal(18,2)")]
        [Display(Name = "Precio")]
        public decimal PrecioDefault { get; set; }

        [Column("costo_default", TypeName = "decimal(18,2)")]
        [Display(Name = "Costo")]
        public decimal CostoDefault { get; set; }

        [Display(Name = "Impuesto por defecto")]
        [Column("id_impuesto_default")]
        public int? IdImpuestoDefault { get; set; }

        [Display(Name = "Lleva inventario")]
        [Column("lleva_inventario")]
        public bool LlevaInventario { get; set; }

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
        public Impuesto? ImpuestoDefault { get; set; }
    }
}
