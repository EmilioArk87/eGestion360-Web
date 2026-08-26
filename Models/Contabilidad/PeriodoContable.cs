using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>Período contable (mes) dentro de un ejercicio fiscal (tabla <c>ct_periodos</c>).</summary>
    [Table("ct_periodos")]
    public class PeriodoContable
    {
        [Key]
        [Column("id_periodo")]
        public int IdPeriodo { get; set; }

        [Column("id_ejercicio")]
        public int IdEjercicio { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        /// <summary>1..12 (13 reservado para ajustes/cierre).</summary>
        [Column("numero")]
        public int Numero { get; set; }

        [Column("fecha_inicio", TypeName = "date")]
        public DateTime FechaInicio { get; set; }

        [Column("fecha_fin", TypeName = "date")]
        public DateTime FechaFin { get; set; }

        /// <summary>abierto | cerrado</summary>
        [Required, StringLength(20)]
        [Column("estado")]
        public string Estado { get; set; } = PeriodoEstado.Abierto;

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

        public EjercicioFiscal Ejercicio { get; set; } = null!;
    }

    public static class PeriodoEstado
    {
        public const string Abierto = "abierto";
        public const string Cerrado = "cerrado";
    }
}
