using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Models.Psicologia
{
    /// <summary>Tipos de profesional (CK_profesionales_tipo del script 042).</summary>
    public static class TiposProfesional
    {
        /// <summary>Atiende, firma constancias y puede ser responsable de una brigada.</summary>
        public const string Psicologo = "psicologo";

        /// <summary>Administrador del centro: puede ser responsable de una brigada (decisión D2), pero no firma constancias.</summary>
        public const string Administrador = "administrador";

        public static readonly IReadOnlyList<string> Todos = new[] { Psicologo, Administrador };
    }

    /// <summary>
    /// Un profesional del centro (psi.profesionales, decisiones D2 y D11): psicólogo o administrador, sobre la persona maestra de la cuenta.
    /// Su usuario del sistema se enlaza por la persona (saas.membresias.id_persona): no hay columna propia.
    /// </summary>
    [Table("profesionales", Schema = "psi")]
    public class Profesional : ITenantEntity
    {
        [Key]
        [Column("id_profesional")]
        public int IdProfesional { get; set; }

        [Column("id_tenant")]
        public int IdTenant { get; set; }

        [Column("id_empresa")]
        public int IdEmpresa { get; set; }

        [Column("id_persona")]
        public int IdPersona { get; set; }

        [Required]
        [StringLength(15)]
        [Column("tipo", TypeName = "varchar(15)")]
        public string Tipo { get; set; } = TiposProfesional.Psicologo;

        /// <summary>Abreviatura que va antes del nombre en lo impreso (Lic., M.Sc., Dr.).</summary>
        [StringLength(30)]
        [Display(Name = "Título")]
        [Column("titulo")]
        public string? Titulo { get; set; }

        /// <summary>Nombre como sale en las constancias y en la firma.</summary>
        [Required]
        [StringLength(200)]
        [Display(Name = "Nombre para la firma")]
        [Column("nombre_firma")]
        public string NombreFirma { get; set; } = string.Empty;

        [StringLength(30)]
        [Display(Name = "Número de colegiación")]
        [Column("numero_colegiacion", TypeName = "varchar(30)")]
        public string? NumeroColegiacion { get; set; }

        [StringLength(150)]
        [Display(Name = "Especialidad")]
        [Column("especialidad")]
        public string? Especialidad { get; set; }

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

        public Persona Persona { get; set; } = null!;

        /// <summary>Cómo sale en lo impreso: "Lic. Ana Torres".</summary>
        [NotMapped]
        public string NombreImpreso => string.IsNullOrWhiteSpace(Titulo) ? NombreFirma : $"{Titulo} {NombreFirma}";
    }
}
