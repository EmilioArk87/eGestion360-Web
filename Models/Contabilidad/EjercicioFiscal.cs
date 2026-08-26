using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>Ejercicio fiscal anual (tabla <c>ct_ejercicios</c>). Agrupa los períodos contables.</summary>
    [Table("ct_ejercicios")]
    public class EjercicioFiscal
    {
        [Key]
        [Column("id_ejercicio")]
        public int IdEjercicio { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("anio")]
        public int Anio { get; set; }

        [Column("fecha_inicio", TypeName = "date")]
        public DateTime FechaInicio { get; set; }

        [Column("fecha_fin", TypeName = "date")]
        public DateTime FechaFin { get; set; }

        /// <summary>abierto | cerrado</summary>
        [Required, StringLength(20)]
        [Column("estado")]
        public string Estado { get; set; } = EjercicioEstado.Abierto;

        // ── Auditoría ────────────────────────────────────────────────────────

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

        // ── Navegación ───────────────────────────────────────────────────────

        public ICollection<PeriodoContable> Periodos { get; set; } = new List<PeriodoContable>();
    }

    public static class EjercicioEstado
    {
        public const string Abierto = "abierto";
        public const string Cerrado = "cerrado";
    }
}
