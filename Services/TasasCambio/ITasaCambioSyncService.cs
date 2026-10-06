namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Obtención y mantenimiento de las tasas de cambio: el job (programado, externo, reintento o manual), la
    /// revisión de las tasas EN_REVISION y la captura manual de tasas propias de una empresa.
    /// </summary>
    public interface ITasaCambioSyncService
    {
        /// <summary>
        /// Una ejecución del job: puesta al día desde la última tasa vigente (menos DiasPuestaAlDia) hasta hoy en
        /// Honduras. No hace nada si el job está deshabilitado o si ya hay otra ejecución en curso
        /// (<see cref="ResultadoEjecucionTasas.Ejecutada"/> = falso). Puede tardar varios minutos si las fuentes no
        /// responden (30 s por intento, con reintentos): desde una pantalla conviene encolarla con
        /// <see cref="ColaEjecucionTasasCambio.Solicitar"/>.
        /// </summary>
        /// <param name="disparador">Ver <see cref="Models.Catalogos.TasasCambioCatalogo.Disparador"/>.</param>
        /// <param name="ejecutadoPor">"job" o el usuario que la pidió.</param>
        Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, CancellationToken ct = default);

        /// <summary>
        /// Carga manual de un rango (backfill): lee de <paramref name="desde"/> a <paramref name="hasta"/> sin la regla de
        /// "fecha anterior a la puesta al día" y sin programar reintentos. La fecha objetivo es <paramref name="hasta"/>.
        /// </summary>
        Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, DateOnly desde, DateOnly hasta,
            CancellationToken ct = default);

        /// <summary>EN_REVISION → VIGENTE; la vigente de la misma clave (si hay) pasa a REEMPLAZADA.</summary>
        Task<ResultadoOperacionTasa> AprobarAsync(int idTasaCambio, string usuario, CancellationToken ct = default);

        /// <summary>EN_REVISION → RECHAZADA. El motivo queda en la bitácora de la ejecución que la obtuvo.</summary>
        Task<ResultadoOperacionTasa> RechazarAsync(int idTasaCambio, string usuario, string motivo, CancellationToken ct = default);

        /// <summary>
        /// Tasa propia de una empresa (fuente MANUAL), que para ella prevalece sobre la oficial de la misma fecha. Se
        /// versiona igual que las oficiales: un valor distinto para la misma moneda, tipo y fecha reemplaza al anterior.
        /// La moneda destino es la moneda local (HNL).
        /// </summary>
        Task<ResultadoOperacionTasa> RegistrarManualAsync(int idEmpresa, string monedaOrigen, string tipoTasa, DateOnly fecha,
            decimal tasa, string usuario, CancellationToken ct = default);
    }

    /// <param name="Ejecutada">Falso si el job está deshabilitado o había otra ejecución en curso: no se escribió nada.</param>
    /// <param name="IdEjecucion">Fila de tasas_cambio_ejecuciones; nulo si no se ejecutó.</param>
    /// <param name="Estado">Ver <see cref="Models.Catalogos.TasasCambioCatalogo.EstadoEjecucion"/>.</param>
    /// <param name="Invalidos">Lecturas que no quedaron vigentes por validación (inválidas o EN_REVISION).</param>
    public sealed record ResultadoEjecucionTasas(
        bool Ejecutada,
        long? IdEjecucion,
        string? Estado,
        DateOnly? FechaObjetivo,
        int Leidos,
        int Insertados,
        int Reemplazados,
        int Duplicados,
        int Invalidos,
        string Mensaje)
    {
        public static ResultadoEjecucionTasas NoEjecutada(string mensaje) =>
            new(false, null, null, null, 0, 0, 0, 0, 0, mensaje);
    }

    /// <param name="Exito">Falso si no se pudo (no existe, no está en revisión, dato inválido, conflicto): <paramref name="Mensaje"/> dice por qué.</param>
    /// <param name="Mensaje">Listo para mostrar al usuario.</param>
    /// <param name="IdTasaCambio">La tasa afectada o creada.</param>
    public sealed record ResultadoOperacionTasa(bool Exito, string Mensaje, int? IdTasaCambio = null);
}
