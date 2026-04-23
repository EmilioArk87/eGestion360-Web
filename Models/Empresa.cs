using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    [Table("empresas")]
    public class Empresa
    {
        [Key]
        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(20)]
        [Display(Name = "Código")]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La razón social es requerida")]
        [StringLength(200)]
        [Display(Name = "Razón Social")]
        [Column("razon_social")]
        public string RazonSocial { get; set; } = string.Empty;

        [StringLength(150)]
        [Display(Name = "Nombre Comercial")]
        [Column("nombre_comercial")]
        public string? NombreComercial { get; set; }

        [StringLength(50)]
        [Display(Name = "Identificador Fiscal")]
        [Column("identificador_fiscal")]
        public string? IdentificadorFiscal { get; set; }

        [Required(ErrorMessage = "El país es requerido")]
        [StringLength(2)]
        [Display(Name = "País (ISO)")]
        [Column("pais_iso")]
        public string PaisIso { get; set; } = string.Empty;

        [Required(ErrorMessage = "La moneda es requerida")]
        [StringLength(3)]
        [Display(Name = "Moneda (ISO)")]
        [Column("moneda_iso")]
        public string MonedaIso { get; set; } = string.Empty;

        [Required(ErrorMessage = "La zona horaria es requerida")]
        [StringLength(100)]
        [Display(Name = "Zona horaria")]
        [Column("zona_horaria")]
        public string ZonaHoraria { get; set; } = string.Empty;

        [Display(Name = "Activa")]
        [Column("activa")]
        public bool Activa { get; set; } = true;

        [Display(Name = "Fecha de activación")]
        [Column("fecha_activacion")]
        public DateTime FechaActivacion { get; set; }

        [Display(Name = "Fecha de baja")]
        [Column("fecha_baja")]
        public DateTime? FechaBaja { get; set; }

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
