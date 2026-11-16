using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>
    /// Un lote por cada archivo de Excel subido (psi.importaciones, script 042). El archivo no se guarda: solo su nombre y su huella
    /// SHA-256, que evita confirmar dos veces el mismo archivo en la empresa.
    /// </summary>
    [Table("importaciones", Schema = "psi")]
    public class Importacion : ITenantEntity
    {
        [Key]
        [Column("id_importacion")]
        public int IdImportacion { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>Jornada a la que se cargan las atenciones; obligatoria para confirmar (CK_importaciones_confirmacion).</summary>
        [Column("id_jornada")]
        public int? IdJornada { get; set; }

        [Required]
        [StringLength(260)]
        [Display(Name = "Archivo")]
        [Column("nombre_archivo")]
        public string NombreArchivo { get; set; } = string.Empty;

        /// <summary>SHA-256 del archivo en hexadecimal en minúsculas (64 caracteres).</summary>
        [Required]
        [StringLength(64)]
        [Column("hash_sha256", TypeName = "char(64)")]
        public string HashSha256 { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Hoja")]
        [Column("hoja")]
        public string? Hoja { get; set; }

        [Column("total_filas")]
        public int TotalFilas { get; set; }

        [Column("filas_nuevas")]
        public int FilasNuevas { get; set; }

        [Column("filas_reutilizadas")]
        public int FilasReutilizadas { get; set; }

        [Column("filas_rechazadas")]
        public int FilasRechazadas { get; set; }

        [Required]
        [StringLength(12)]
        [Column("estado", TypeName = "varchar(12)")]
        public string Estado { get; set; } = EstadosImportacion.Borrador;

        [Column("fecha_confirmacion")]
        public DateTime? FechaConfirmacion { get; set; }

        [StringLength(100)]
        [Column("confirmado_por")]
        public string? ConfirmadoPor { get; set; }

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

        public Jornada? Jornada { get; set; }
        public ICollection<ImportacionFila> Filas { get; set; } = new List<ImportacionFila>();
    }

    /// <summary>
    /// Cada fila del archivo con su resultado (psi.importacion_filas, script 042): los valores tal como venían, el reparto propuesto
    /// (o corregido) de nombres y apellidos y, al confirmar, el paciente que quedó. No pasa por la bitácora: trae el DNI completo.
    /// </summary>
    [Table("importacion_filas", Schema = "psi")]
    public class ImportacionFila : ITenantEntity
    {
        [Key]
        [Column("id_importacion_fila")]
        public int IdImportacionFila { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_importacion")]
        public int IdImportacion { get; set; }

        /// <summary>Número de fila en la hoja de Excel (la primera de datos suele ser la 2).</summary>
        [Column("numero_fila")]
        public int NumeroFila { get; set; }

        [StringLength(300)]
        [Column("nombre_original")]
        public string? NombreOriginal { get; set; }

        [StringLength(30)]
        [Column("dni_original")]
        public string? DniOriginal { get; set; }

        [StringLength(10)]
        [Column("edad_original")]
        public string? EdadOriginal { get; set; }

        [StringLength(10)]
        [Column("genero_original")]
        public string? GeneroOriginal { get; set; }

        [StringLength(50)]
        [Display(Name = "Primer nombre")]
        [Column("primer_nombre")]
        public string? PrimerNombre { get; set; }

        [StringLength(50)]
        [Display(Name = "Segundo nombre")]
        [Column("segundo_nombre")]
        public string? SegundoNombre { get; set; }

        [StringLength(50)]
        [Display(Name = "Primer apellido")]
        [Column("primer_apellido")]
        public string? PrimerApellido { get; set; }

        [StringLength(50)]
        [Display(Name = "Segundo apellido")]
        [Column("segundo_apellido")]
        public string? SegundoApellido { get; set; }

        [Required]
        [StringLength(12)]
        [Column("resultado", TypeName = "varchar(12)")]
        public string Resultado { get; set; } = ResultadosFilaImportacion.Pendiente;

        [StringLength(500)]
        [Column("mensaje")]
        public string? Mensaje { get; set; }

        [Column("id_paciente")]
        public int? IdPaciente { get; set; }

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

        public Importacion Importacion { get; set; } = null!;
        public Paciente? Paciente { get; set; }
    }
}
