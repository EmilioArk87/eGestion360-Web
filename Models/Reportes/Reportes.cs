using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Reportes
{
    /// <summary>Valores de <c>estado</c> de una versión (CK_rep_reporte_versiones_estado, ADR-031 RP5).</summary>
    public static class EstadosVersionReporte
    {
        public const string Borrador = "borrador";
        public const string Publicada = "publicada";
        public const string Archivada = "archivada";
    }

    /// <summary>Valores de <c>tipo_salida</c> (CK_rep_reportes_tipo_salida y CK_rep_ejecuciones_tipo_salida). Sin PDF nativo en la Fase 1 (RP3).</summary>
    public static class TiposSalidaReporte
    {
        public const string ImpresionHtml = "impresion_html";
        public const string Excel = "excel";
        public const string Pdf = "pdf";
    }

    /// <summary>Valores de <c>accion</c> de la bitácora de reportes (CK_rep_reporte_bitacora_accion).</summary>
    public static class AccionesBitacoraReporte
    {
        public const string CrearReporte = "CREAR_REPORTE";
        public const string CrearBorrador = "CREAR_BORRADOR";
        public const string EditarBorrador = "EDITAR_BORRADOR";
        public const string DescartarBorrador = "DESCARTAR_BORRADOR";
        public const string Publicar = "PUBLICAR";
        public const string Archivar = "ARCHIVAR";
        public const string ClonarGlobal = "CLONAR_GLOBAL";
        public const string Revertir = "REVERTIR";
        public const string Activar = "ACTIVAR";
        public const string Desactivar = "DESACTIVAR";
    }

    /// <summary>
    /// Un reporte (dbo.rep_reportes, script 047, ADR-031). Tabla mixta: <see cref="IdTenant"/> nulo es el reporte global de la plataforma;
    /// con valor, la personalización o el reporte propio de un tenant con el mismo <see cref="Codigo"/>, que prevalece sobre el global (RP4).
    /// </summary>
    [Table("rep_reportes")]
    public class Reporte : ITenantOpcionalEntity
    {
        [Key]
        [Column("id_reporte")]
        public int IdReporte { get; set; }

        [Column("id_tenant")]
        public int? IdTenant { get; set; }

        /// <summary>Código estable (a-z, 0-9 y _), p. ej. <c>psi_constancia_licencia</c>. No cambia nunca: para personalizar se clona.</summary>
        [Required]
        [StringLength(60)]
        [Column("codigo", TypeName = "varchar(60)")]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Required]
        [StringLength(40)]
        [Column("modulo", TypeName = "varchar(40)")]
        public string Modulo { get; set; } = string.Empty;

        /// <summary>La fuente de datos en código que alimenta la plantilla (RP1), p. ej. <c>psi.constancia</c>. Nunca SQL.</summary>
        [Required]
        [StringLength(80)]
        [Column("fuente_datos", TypeName = "varchar(80)")]
        public string FuenteDatos { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="TiposSalidaReporte"/>.</summary>
        [Required]
        [StringLength(20)]
        [Column("tipo_salida", TypeName = "varchar(20)")]
        public string TipoSalida { get; set; } = TiposSalidaReporte.ImpresionHtml;

        /// <summary>La versión que se usa al generar: una versión publicada de este mismo reporte (RP7, lo valida un disparador).</summary>
        [Column("id_version_activa")]
        public int? IdVersionActiva { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Required]
        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        public ReporteVersion? VersionActiva { get; set; }
        public ICollection<ReporteVersion> Versiones { get; set; } = new List<ReporteVersion>();
    }

    /// <summary>
    /// Una versión de la plantilla de un reporte (dbo.rep_reporte_versiones). Nace borrador o publicada; publicada o archivada es inmutable y
    /// solo pasa de publicada a archivada (RP5). <see cref="HashSha256"/> la calcula la base (RP6).
    /// </summary>
    [Table("rep_reporte_versiones")]
    public class ReporteVersion : ITenantOpcionalEntity
    {
        [Key]
        [Column("id_version")]
        public int IdVersion { get; set; }

        [Column("id_reporte")]
        public int IdReporte { get; set; }

        /// <summary>El mismo tenant que su reporte (lo exige un disparador).</summary>
        [Column("id_tenant")]
        public int? IdTenant { get; set; }

        [Column("numero_version")]
        public int NumeroVersion { get; set; }

        /// <summary>Uno de <see cref="EstadosVersionReporte"/>.</summary>
        [Required]
        [StringLength(15)]
        [Column("estado", TypeName = "varchar(15)")]
        public string Estado { get; set; } = EstadosVersionReporte.Borrador;

        /// <summary>HTML y CSS con Scriban (RP2).</summary>
        [Required]
        [Column("contenido_plantilla")]
        public string ContenidoPlantilla { get; set; } = string.Empty;

        /// <summary>JSON con medidas en mm, papel y textos; llega a la plantilla como <c>cfg</c>.</summary>
        [Column("configuracion_json")]
        public string? ConfiguracionJson { get; set; }

        /// <summary>SHA-256 de <c>contenido_plantilla + "|" + configuracion_json</c> en UTF-16LE, hexadecimal mayúscula. Calculada y persistida por la base.</summary>
        [Column("hash_sha256", TypeName = "char(64)")]
        public string HashSha256 { get; set; } = string.Empty;

        [StringLength(500)]
        [Column("motivo_cambio")]
        public string? MotivoCambio { get; set; }

        /// <summary>La versión de la que se clonó (de este reporte o de su global).</summary>
        [Column("id_version_origen")]
        public int? IdVersionOrigen { get; set; }

        [Column("id_cuenta")]
        public int? IdCuenta { get; set; }

        [Required]
        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [Column("fecha_publicacion")]
        public DateTime? FechaPublicacion { get; set; }

        public Reporte? Reporte { get; set; }
        public ReporteVersion? VersionOrigen { get; set; }
    }

    /// <summary>Quién hizo qué sobre un reporte o una versión (dbo.rep_reporte_bitacora). Inmutable (RP8); sin claves foráneas a propósito.</summary>
    [Table("rep_reporte_bitacora")]
    public class ReporteBitacora : ITenantOpcionalEntity
    {
        [Key]
        [Column("id_bitacora")]
        public long IdBitacora { get; set; }

        [Column("id_tenant")]
        public int? IdTenant { get; set; }

        [Column("id_reporte")]
        public int IdReporte { get; set; }

        [Required]
        [StringLength(60)]
        [Column("codigo_reporte", TypeName = "varchar(60)")]
        public string CodigoReporte { get; set; } = string.Empty;

        /// <summary>Uno de <see cref="AccionesBitacoraReporte"/>.</summary>
        [Required]
        [StringLength(30)]
        [Column("accion", TypeName = "varchar(30)")]
        public string Accion { get; set; } = string.Empty;

        [Column("id_version_anterior")]
        public int? IdVersionAnterior { get; set; }

        [Column("id_version_nueva")]
        public int? IdVersionNueva { get; set; }

        [StringLength(1000)]
        [Column("observaciones")]
        public string? Observaciones { get; set; }

        [Column("detalle_json")]
        public string? DetalleJson { get; set; }

        [Column("id_cuenta")]
        public int? IdCuenta { get; set; }

        [Required]
        [StringLength(100)]
        [Column("usuario")]
        public string Usuario { get; set; } = string.Empty;

        [StringLength(64)]
        [Column("id_correlacion", TypeName = "varchar(64)")]
        public string? IdCorrelacion { get; set; }

        [Column("fecha_hora")]
        public DateTime FechaHora { get; set; }
    }

    /// <summary>
    /// Cada generación o impresión de un reporte con la versión exacta usada (dbo.rep_ejecuciones). Tabla de tenant, inmutable (RP8).
    /// La vista previa de un borrador no deja fila.
    /// </summary>
    [Table("rep_ejecuciones")]
    public class ReporteEjecucion : ITenantEntity
    {
        [Key]
        [Column("id_ejecucion")]
        public long IdEjecucion { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_reporte")]
        public int IdReporte { get; set; }

        [Column("id_version")]
        public int IdVersion { get; set; }

        [Required]
        [StringLength(60)]
        [Column("codigo_reporte", TypeName = "varchar(60)")]
        public string CodigoReporte { get; set; } = string.Empty;

        [Column("numero_version")]
        public int NumeroVersion { get; set; }

        /// <summary>De qué tabla sale el documento, p. ej. <c>psi.constancias</c>.</summary>
        [StringLength(60)]
        [Column("entidad_origen", TypeName = "varchar(60)")]
        public string? EntidadOrigen { get; set; }

        [Column("id_registro_origen")]
        public long? IdRegistroOrigen { get; set; }

        [Column("parametros_json")]
        public string? ParametrosJson { get; set; }

        [Required]
        [StringLength(20)]
        [Column("tipo_salida", TypeName = "varchar(20)")]
        public string TipoSalida { get; set; } = TiposSalidaReporte.ImpresionHtml;

        [Column("id_cuenta")]
        public int? IdCuenta { get; set; }

        [Required]
        [StringLength(100)]
        [Column("usuario")]
        public string Usuario { get; set; } = string.Empty;

        [StringLength(64)]
        [Column("id_correlacion", TypeName = "varchar(64)")]
        public string? IdCorrelacion { get; set; }

        [Column("fecha_hora")]
        public DateTime FechaHora { get; set; }

        public Reporte? Reporte { get; set; }
        public ReporteVersion? Version { get; set; }
    }
}
