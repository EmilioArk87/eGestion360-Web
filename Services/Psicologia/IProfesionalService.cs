using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Profesionales del centro (decisiones D2 y D11): psicólogos que atienden, firman constancias y son responsables de brigadas, y
    /// administradores que también pueden ser responsables. Cada profesional es una persona de la cuenta (persona maestra); si es
    /// nueva, se crea con un vínculo <c>otro</c> en la empresa. Su usuario del sistema se enlaza por esa persona (la invitación por correo
    /// de Administración › Invitaciones elige a la misma persona). La empresa siempre la pone el servidor.
    /// </summary>
    public interface IProfesionalService
    {
        /// <summary>Registra un profesional nuevo (con los datos de su persona) o edita sus datos profesionales (su persona no cambia).</summary>
        Task<ResultadoPsicologia<Profesional>> GuardarAsync(ProfesionalInput input, CancellationToken ct = default);

        Task<ResultadoPsicologia<bool>> CambiarEstadoAsync(int idEmpresa, int idProfesional, bool activo, string usuario, CancellationToken ct = default);
    }

    public sealed class ProfesionalInput
    {
        public int IdEmpresa { get; set; }

        /// <summary>Nulo o 0 para registrar uno nuevo.</summary>
        public int? IdProfesional { get; set; }

        // Persona (solo al registrar): el DNI identifica a la persona en la cuenta.
        public string? Dni { get; set; }
        public string? PrimerNombre { get; set; }
        public string? SegundoNombre { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }
        public string? Sexo { get; set; }
        public string? Telefono { get; set; }
        public string? Correo { get; set; }

        // Datos profesionales
        public string? Tipo { get; set; } = TiposProfesional.Psicologo;
        public string? Titulo { get; set; }

        /// <summary>Como sale en lo impreso; si viene vacío, el nombre de la persona.</summary>
        public string? NombreFirma { get; set; }

        public string? NumeroColegiacion { get; set; }
        public string? Especialidad { get; set; }

        public string Usuario { get; set; } = "system";
    }

    /// <summary>Responsables de jornada: asignar o quitar y llevarlo a las atenciones en borrador.</summary>
    public interface IJornadaService
    {
        /// <summary>
        /// Pone (o quita, con <paramref name="idProfesional"/> nulo) el responsable de la jornada. Las atenciones de la jornada que siguen
        /// en borrador y tenían el responsable anterior (o ninguno) pasan al nuevo. Devuelve cuántas atenciones cambiaron.
        /// </summary>
        Task<ResultadoPsicologia<int>> AsignarResponsableAsync(int idEmpresa, int idJornada, int? idProfesional, string usuario, CancellationToken ct = default);
    }
}
