using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Contabilidad
{
    /// <summary>
    /// Cuenta del plan único de cuentas, jerárquica (tabla <c>ct_cuentas</c>).
    /// La jerarquía se arma con <see cref="IdCuentaPadre"/> (auto-referencia).
    /// </summary>
    [Table("ct_cuentas")]
    public class CuentaContable
    {
        [Key]
        [Column("id_cuenta")]
        public int IdCuenta { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Required, StringLength(30)]
        [Column("codigo")]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(200)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Column("id_cuenta_padre")]
        public int? IdCuentaPadre { get; set; }

        [Column("nivel")]
        public int Nivel { get; set; } = 1;

        /// <summary>deudora | acreedora</summary>
        [Required, StringLength(10)]
        [Column("naturaleza")]
        public string Naturaleza { get; set; } = CuentaNaturaleza.Deudora;

        /// <summary>activo | pasivo | patrimonio | ingreso | gasto | orden</summary>
        [Required, StringLength(20)]
        [Column("tipo")]
        public string Tipo { get; set; } = CuentaTipo.Activo;

        /// <summary>1 = cuenta de movimiento (acepta asientos); 0 = cuenta de agrupación.</summary>
        [Column("es_movimiento")]
        public bool EsMovimiento { get; set; } = true;

        [Required, StringLength(3)]
        [Column("moneda")]
        public string Moneda { get; set; } = "HNL";

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

        // ── Navegación ───────────────────────────────────────────────────────

        public CuentaContable? CuentaPadre { get; set; }
        public ICollection<CuentaContable> SubCuentas { get; set; } = new List<CuentaContable>();
    }

    public static class CuentaNaturaleza
    {
        public const string Deudora   = "deudora";
        public const string Acreedora = "acreedora";
    }

    public static class CuentaTipo
    {
        public const string Activo     = "activo";
        public const string Pasivo     = "pasivo";
        public const string Patrimonio = "patrimonio";
        public const string Ingreso    = "ingreso";
        public const string Gasto      = "gasto";
        public const string Orden      = "orden";
    }
}
