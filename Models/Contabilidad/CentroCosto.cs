using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>Centro de costo (opcional por línea de asiento) — tabla <c>ct_centros_costo</c>.</summary>
    [Table("ct_centros_costo")]
    public class CentroCosto
    {
        [Key]
        [Column("id_centro_costo")]
        public int IdCentroCosto { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required, StringLength(30)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(200)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        // ── Auditoría ────────────────────────────────────────────────────────

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
    }
}
