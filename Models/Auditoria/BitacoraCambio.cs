using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models.Auditoria
{
    /// <summary>
    /// Historial de cambios por campo (script 014). Solo se agrega: el disparador
    /// TR_bitacora_cambios_inmutable rechaza cualquier UPDATE o DELETE. No tiene claves foráneas a
    /// propósito: la bitácora nunca debe bloquear ni ser afectada por cambios en otras tablas.
    /// </summary>
    [Table("bitacora_cambios")]
    public class BitacoraCambio
    {
        [Key]
        [Column("id_bitacora")]
        public long IdBitacora { get; set; }

        /// <summary>Agrupa las filas de un mismo guardado.</summary>
        [Column("id_transaccion")]
        public Guid IdTransaccion { get; set; }

        [Column("id_empresa")]
        public int? IdEmpresa { get; set; }

        /// <summary>Da el historial completo de una persona aunque el registro modificado sea un documento, un vínculo o un rol.</summary>
        [Column("id_persona")]
        public int? IdPersona { get; set; }

        [Required]
        [StringLength(60)]
        [Column("entidad", TypeName = "varchar(60)")]
        public string Entidad { get; set; } = string.Empty;

        [Column("id_registro")]
        public long IdRegistro { get; set; }

        /// <summary>Uno de <see cref="OperacionesBitacora"/>.</summary>
        [Required]
        [StringLength(10)]
        [Column("operacion", TypeName = "varchar(10)")]
        public string Operacion { get; set; } = OperacionesBitacora.Update;

        /// <summary>Nulo en altas y bajas (la fila lleva la foto del registro en ValorNuevo o ValorAnterior).</summary>
        [StringLength(100)]
        [Column("campo", TypeName = "varchar(100)")]
        public string? Campo { get; set; }

        [StringLength(2000)]
        [Column("valor_anterior")]
        public string? ValorAnterior { get; set; }

        [StringLength(2000)]
        [Column("valor_nuevo")]
        public string? ValorNuevo { get; set; }

        [Column("fecha_hora")]
        public DateTime FechaHora { get; set; }

        [Required]
        [StringLength(100)]
        [Column("usuario")]
        public string Usuario { get; set; } = string.Empty;

        /// <summary>'app' para la aplicación; 'script:NNN' para los scripts SQL.</summary>
        [Required]
        [StringLength(50)]
        [Column("origen", TypeName = "varchar(50)")]
        public string Origen { get; set; } = "app";
    }

    /// <summary>Valores permitidos de <see cref="BitacoraCambio.Operacion"/> (CK_bitacora_cambios_operacion).</summary>
    public static class OperacionesBitacora
    {
        public const string Insert = "INSERT";
        public const string Update = "UPDATE";
        public const string Delete = "DELETE";
    }
}
