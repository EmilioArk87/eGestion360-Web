using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Catalogos
{
    [Table("proveedores")]
    public class Proveedor
    {
        [Key]
        [Column("id_proveedor")]
        public int IdProveedor { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(30)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre/razón social es requerido")]
        [StringLength(200)]
        [Display(Name = "Razón social")]
        [Column("razon_social")]
        public string RazonSocial { get; set; } = string.Empty;

        [StringLength(150)]
        [Display(Name = "Nombre comercial")]
        [Column("nombre_comercial")]
        public string? NombreComercial { get; set; }

        [StringLength(20)]
        [Display(Name = "Tipo")]
        [Column("tipo")]
        public string Tipo { get; set; } = "juridica";

        [StringLength(50)]
        [Display(Name = "Identificador fiscal (RTN)")]
        [Column("identificador_fiscal")]
        public string? IdentificadorFiscal { get; set; }

        [StringLength(100)]
        [Display(Name = "Correo electrónico")]
        [Column("email")]
        public string? Email { get; set; }

        [StringLength(30)]
        [Display(Name = "Teléfono")]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [StringLength(300)]
        [Display(Name = "Dirección")]
        [Column("direccion")]
        public string? Direccion { get; set; }

        [StringLength(100)]
        [Display(Name = "Ciudad")]
        [Column("ciudad")]
        public string? Ciudad { get; set; }

        [StringLength(3)]
        [Display(Name = "Moneda por defecto")]
        [Column("moneda_iso_default")]
        public string MonedaIsoDefault { get; set; } = "HNL";

        [Display(Name = "Condición de pago por defecto")]
        [Column("id_condicion_pago_default")]
        public int? IdCondicionPagoDefault { get; set; }

        [Display(Name = "Sujeto a retención ISR")]
        [Column("retencion_isr")]
        public bool RetencionIsr { get; set; }

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
        public CondicionPago? CondicionPagoDefault { get; set; }
    }
}
