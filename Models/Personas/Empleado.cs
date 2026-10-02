using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Personas
{
    /// <summary>
    /// Rol de empleado de una persona en una empresa (script 014). Cuelga de un vínculo
    /// <see cref="PersonaEmpresa"/> de tipo 'empleado'. En la BD, la clave foránea compuesta
    /// (id_persona_empresa, id_empresa, tipo_vinculo) obliga a que IdEmpresa sea la del vínculo y
    /// a que TipoVinculo sea 'empleado'; el servicio que guarda debe poner ambos valores.
    /// </summary>
    [Table("empleados")]
    public class Empleado
    {
        [Key]
        [Column("id_empleado")]
        public int IdEmpleado { get; set; }

        [Column("id_persona_empresa")]
        public int IdPersonaEmpresa { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(20)]
        [Column("tipo_vinculo", TypeName = "varchar(20)")]
        public string TipoVinculo { get; set; } = TiposVinculo.Empleado;

        /// <summary>Número de empleado (decisión D6). Único por empresa entre los empleados activos.</summary>
        [StringLength(30)]
        [Display(Name = "Código de empleado")]
        [Column("codigo_interno", TypeName = "varchar(30)")]
        public string? CodigoInterno { get; set; }

        /// <summary>Código de un cargo de la empresa (decisión D7: se valida contra dbo.cargos, sin CHECK).</summary>
        [StringLength(30)]
        [Display(Name = "Cargo")]
        [Column("cargo", TypeName = "varchar(30)")]
        public string? Cargo { get; set; }

        [Range(0, double.MaxValue)]
        [Display(Name = "Tarifa diaria")]
        [Column("tarifa_diaria")]
        public decimal? TarifaDiaria { get; set; }

        [StringLength(3)]
        [Display(Name = "Moneda de la tarifa")]
        [Column("moneda_tarifa", TypeName = "char(3)")]
        public string? MonedaTarifa { get; set; }

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

        public PersonaEmpresa Vinculo { get; set; } = null!;
    }
}
