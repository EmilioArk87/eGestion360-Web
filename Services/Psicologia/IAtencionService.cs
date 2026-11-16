namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Operaciones sobre las atenciones (evaluaciones) de un paciente. Hoy: registrar qué pruebas psicológicas se aplicaron (decisión D12).
    /// El formulario clínico llega con el contenido que defina Emilio (F6). Una atención cerrada o anulada ya no se edita.
    /// </summary>
    public interface IAtencionService
    {
        /// <summary>
        /// Deja en la atención exactamente las pruebas indicadas (agrega las nuevas y quita las que ya no están). Las pruebas nuevas deben
        /// ser del catálogo activo de la empresa; una que se desactivó después puede seguir en la atención.
        /// </summary>
        Task<ResultadoPsicologia<int>> GuardarPruebasAsync(int idEmpresa, int idAtencion, IReadOnlyCollection<int> idsPruebas, string usuario,
            CancellationToken ct = default);
    }
}
