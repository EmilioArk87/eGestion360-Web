using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>Estados de una constancia (CK_constancias_estado del script 042). Una constancia emitida no se borra: se anula.</summary>
    public static class EstadosConstancia
    {
        public const string Emitida = "emitida";
        public const string Anulada = "anulada";

        public static readonly IReadOnlyList<string> Todos = new[] { Emitida, Anulada };
    }

    /// <summary>
    /// Conclusión del examen: la que imprimen algunos formatos de constancia (CK_constancias_resultado del script 042) y la de la
    /// atención (CK_atenciones_conclusion, decisión D17).
    /// </summary>
    public static class ResultadosConstancia
    {
        public const string Apto = "apto";
        public const string NoApto = "no_apto";

        public static readonly IReadOnlyList<string> Todos = new[] { Apto, NoApto };
    }

    /// <summary>Una prueba psicológica del catálogo de la empresa (psi.pruebas, decisión D12): MMPI-2, 16PF, Raven, Bender...</summary>
    [Table("pruebas", Schema = "psi")]
    public class Prueba : ITenantEntity
    {
        [Key]
        [Column("id_prueba")]
        public int IdPrueba { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "Código")]
        [Column("codigo", TypeName = "varchar(30)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Descripción")]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

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

    /// <summary>Una prueba aplicada en una atención (psi.atencion_pruebas).</summary>
    [Table("atencion_pruebas", Schema = "psi")]
    public class AtencionPrueba : ITenantEntity
    {
        [Key]
        [Column("id_atencion_prueba")]
        public int IdAtencionPrueba { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_atencion")]
        public int IdAtencion { get; set; }

        [Column("id_prueba")]
        public int IdPrueba { get; set; }

        /// <summary>Resultado de la prueba tal como lo anota el psicólogo (puntaje, rango, "dentro de lo normal"...), decisión D17.</summary>
        [StringLength(200)]
        [Display(Name = "Resultado")]
        [Column("resultado")]
        public string? Resultado { get; set; }

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        public Atencion Atencion { get; set; } = null!;
        public Prueba Prueba { get; set; } = null!;
    }

    /// <summary>
    /// Un tipo de constancia de examen psicológico (psi.tipos_constancia, decisión D14). Cada tipo tiene su propio formato de impresión
    /// (<see cref="Plantilla"/>), que se imprime sobre papel ya membretado (D15).
    /// </summary>
    [Table("tipos_constancia", Schema = "psi")]
    public class TipoConstancia : ITenantEntity
    {
        [Key]
        [Column("id_tipo_constancia")]
        public int IdTipoConstancia { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "Código")]
        [Column("codigo", TypeName = "varchar(30)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Display(Name = "Nombre")]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Formato con que se imprime; nulo hasta que se define (cada tipo tiene el suyo).</summary>
        [StringLength(40)]
        [Display(Name = "Formato de impresión")]
        [Column("plantilla", TypeName = "varchar(40)")]
        public string? Plantilla { get; set; }

        /// <summary>Primer folio del talonario del Colegio en uso (hojas con número preimpreso); nulo si el tipo no usa talonario.</summary>
        [Display(Name = "Folio inicial del talonario")]
        [Column("folio_desde")]
        public int? FolioDesde { get; set; }

        /// <summary>Último folio del talonario en uso.</summary>
        [Display(Name = "Folio final del talonario")]
        [Column("folio_hasta")]
        public int? FolioHasta { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

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

        /// <summary>Usa los folios preimpresos de un talonario del Colegio en vez del correlativo interno.</summary>
        [NotMapped]
        public bool UsaTalonario => FolioDesde != null && FolioHasta != null;
    }

    /// <summary>
    /// Una constancia de examen psicológico extendida al paciente (psi.constancias): su tipo, la prueba o pruebas aplicadas, la fecha y
    /// su número: un correlativo por tipo (CODIGO-AAAA-NNNNN) que no se repite, tampoco el de una constancia anulada (decisión D13).
    /// </summary>
    [Table("constancias", Schema = "psi")]
    public class Constancia : ITenantEntity
    {
        [Key]
        [Column("id_constancia")]
        public int IdConstancia { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_paciente")]
        public int IdPaciente { get; set; }

        /// <summary>La atención (evaluación) de la que sale, si la hay.</summary>
        [Column("id_atencion")]
        public int? IdAtencion { get; set; }

        [Column("id_tipo_constancia")]
        public int IdTipoConstancia { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "Número de constancia")]
        [Column("numero_constancia", TypeName = "varchar(30)")]
        public string NumeroConstancia { get; set; } = string.Empty;

        [Display(Name = "Fecha de emisión")]
        [Column("fecha_emision")]
        public DateOnly FechaEmision { get; set; }

        [Column("id_persona_profesional")]
        public int? IdPersonaProfesional { get; set; }

        [StringLength(200)]
        [Display(Name = "Profesional")]
        [Column("nombre_profesional")]
        public string? NombreProfesional { get; set; }

        [Required]
        [StringLength(10)]
        [Column("estado", TypeName = "varchar(10)")]
        public string Estado { get; set; } = EstadosConstancia.Emitida;

        /// <summary>Conclusión del examen (<see cref="ResultadosConstancia"/>) cuando el formato la imprime.</summary>
        [StringLength(10)]
        [Display(Name = "Resultado")]
        [Column("resultado", TypeName = "varchar(10)")]
        public string? Resultado { get; set; }

        [StringLength(300)]
        [Display(Name = "Motivo de anulación")]
        [Column("motivo_anulacion")]
        public string? MotivoAnulacion { get; set; }

        [Column("fecha_anulacion")]
        public DateTime? FechaAnulacion { get; set; }

        [StringLength(100)]
        [Column("anulada_por")]
        public string? AnuladaPor { get; set; }

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

        public Paciente Paciente { get; set; } = null!;
        public Atencion? Atencion { get; set; }
        public TipoConstancia Tipo { get; set; } = null!;
        public Persona? Profesional { get; set; }
        public ICollection<ConstanciaPrueba> Pruebas { get; set; } = new List<ConstanciaPrueba>();
    }

    /// <summary>Una prueba que declara la constancia (psi.constancia_pruebas).</summary>
    [Table("constancia_pruebas", Schema = "psi")]
    public class ConstanciaPrueba : ITenantEntity
    {
        [Key]
        [Column("id_constancia_prueba")]
        public int IdConstanciaPrueba { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_constancia")]
        public int IdConstancia { get; set; }

        [Column("id_prueba")]
        public int IdPrueba { get; set; }

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        public Constancia Constancia { get; set; } = null!;
        public Prueba Prueba { get; set; } = null!;
    }
}
