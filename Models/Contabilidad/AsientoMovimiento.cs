using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>
    /// Línea (partida) de un asiento contable (tabla <c>ct_asiento_movimientos</c>).
    /// Regla: exactamente uno de <see cref="Debito"/> / <see cref="Credito"/> es &gt; 0.
    /// </summary>
    [Table("ct_asiento_movimientos")]
    public class AsientoMovimiento
    {
        [Key]
        [Column("id_movimiento")]
        public int IdMovimiento { get; set; }

        [Column("id_asiento")]
        public int IdAsiento { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("numero_linea")]
        public int NumeroLinea { get; set; }

        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Column("id_centro_costo")]
        public int? IdCentroCosto { get; set; }

        [StringLength(300)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Column("debito", TypeName = "decimal(18,2)")]
        public decimal Debito { get; set; }

        [Column("credito", TypeName = "decimal(18,2)")]
        public decimal Credito { get; set; }

        // ── Navegación ───────────────────────────────────────────────────────

        public Asiento Asiento { get; set; } = null!;
        public CuentaContable Cuenta { get; set; } = null!;
        public CentroCosto? CentroCosto { get; set; }
    }
}
