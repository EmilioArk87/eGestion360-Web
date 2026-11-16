using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// La evaluación (atención) de un paciente con su contenido clínico mínimo para el examen de licencia de conducir (decisión D17):
    /// las pruebas aplicadas con el resultado de cada una, la conclusión apto o no apto y las observaciones. Se completa en borrador,
    /// se cierra cuando está completa (ya no se cambia) y se anula con motivo. Nada se borra.
    /// </summary>
    public interface IAtencionService
    {
        /// <summary>
        /// Guarda la evaluación en borrador: deja exactamente las pruebas indicadas con su resultado (agrega, quita y actualiza), la
        /// conclusión y las observaciones. Las pruebas nuevas deben ser del catálogo activo de la empresa; una que se desactivó después
        /// puede seguir en la atención.
        /// </summary>
        Task<ResultadoPsicologia<Atencion>> GuardarEvaluacionAsync(int idEmpresa, int idAtencion, EvaluacionInput input, string usuario,
            CancellationToken ct = default);

        /// <summary>
        /// Cierra la evaluación: exige al menos una prueba aplicada, el resultado de cada una y la conclusión. Cerrada ya no se cambia.
        /// </summary>
        Task<ResultadoPsicologia<Atencion>> CerrarAsync(int idEmpresa, int idAtencion, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Anula la evaluación (en borrador o cerrada) con su motivo. No se puede si una constancia vigente salió de ella: primero se
        /// anula la constancia.
        /// </summary>
        Task<ResultadoPsicologia<Atencion>> AnularAsync(int idEmpresa, int idAtencion, string? motivo, string usuario, CancellationToken ct = default);
    }

    /// <summary>Una prueba aplicada en la evaluación y su resultado (texto del psicólogo, hasta 200 caracteres).</summary>
    public sealed record PruebaAplicadaInput(int IdPrueba, string? Resultado);

    public sealed class EvaluacionInput
    {
        public IReadOnlyCollection<PruebaAplicadaInput> Pruebas { get; init; } = Array.Empty<PruebaAplicadaInput>();

        /// <summary><see cref="ResultadosConstancia"/> (apto o no apto), o vacío mientras no se define.</summary>
        public string? Conclusion { get; init; }

        public string? Observaciones { get; init; }
    }
}
