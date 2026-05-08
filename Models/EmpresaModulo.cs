using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eGestion360Web.Models
{
    [Table("empresa_modulos")]
    public class EmpresaModulo
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_modulo")]
        public int IdModulo { get; set; }

        [Column("fecha_activacion")]
        public DateTime FechaActivacion { get; set; } = DateTime.UtcNow;

        [Column("fecha_vencimiento")]
        public DateTime? FechaVencimiento { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        public Empresa Empresa { get; set; } = null!;
        public Modulo Modulo { get; set; } = null!;
    }
}
