namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Consultas de tasas de cambio para las pantallas y para los demás módulos (facturación, KPI). Reciben la empresa
    /// de la sesión: devuelven las tasas oficiales (id_empresa nulo) y las propias de ESA empresa, nunca las de otra.
    /// </summary>
    public interface ITasaCambioConsultaService
    {
        /// <summary>
        /// La tasa que rige en <paramref name="fecha"/>: la VIGENTE con la última fecha de vigencia menor o igual (un
        /// domingo usa la del viernes). A igual fecha, la propia de la empresa gana sobre la oficial. Nulo si no hay.
        /// </summary>
        /// <param name="idEmpresa">Empresa de la sesión; nulo = solo la oficial.</param>
        Task<TasaVigente?> ObtenerVigenteAsync(string monedaOrigen, string tipoTasa, DateOnly fecha, int? idEmpresa = null,
            string monedaDestino = "HNL", CancellationToken ct = default);

        /// <summary>Para cada moneda del job (USD, EUR) y tipo (COMPRA, VENTA), la tasa que rige hoy en Honduras.</summary>
        Task<IReadOnlyList<TasaVigenteActual>> VigentesActualesAsync(int? idEmpresa = null, CancellationToken ct = default);

        /// <summary>Histórico filtrable y paginado, de la fecha más reciente a la más antigua.</summary>
        Task<PaginaTasas> HistoricoAsync(FiltroHistoricoTasas filtro, CancellationToken ct = default);

        /// <summary>Las últimas ejecuciones del job (la más reciente primero), con su detalle por moneda/tipo.</summary>
        Task<IReadOnlyList<EjecucionTasasResumen>> UltimasEjecucionesAsync(int maximo = 20, CancellationToken ct = default);

        /// <summary>Tasas EN_REVISION (oficiales y, si se indica, de la empresa), con la vigente actual para comparar.</summary>
        Task<IReadOnlyList<TasaPendienteRevision>> PendientesRevisionAsync(int? idEmpresa = null, CancellationToken ct = default);
    }

    /// <param name="EsPropiaEmpresa">Verdadero si es una tasa manual de la empresa y no la oficial.</param>
    public sealed record TasaVigente(
        int IdTasaCambio,
        int? IdEmpresa,
        string MonedaOrigen,
        string MonedaDestino,
        string TipoTasa,
        decimal Tasa,
        DateOnly FechaVigencia,
        string Fuente,
        string? ReferenciaFuente,
        bool EsDerivada,
        short Version,
        DateTime FechaHoraObtencionUtc)
    {
        public bool EsPropiaEmpresa => IdEmpresa != null;
    }

    /// <param name="Vigente">Nulo si todavía no hay ninguna tasa para esa moneda y tipo.</param>
    public sealed record TasaVigenteActual(string MonedaOrigen, string TipoTasa, TasaVigente? Vigente);

    public sealed class FiltroHistoricoTasas
    {
        /// <summary>Empresa de la sesión: se incluyen sus tasas manuales. Nulo = solo las oficiales.</summary>
        public int? IdEmpresa { get; set; }

        /// <summary>Falso = solo las tasas propias de la empresa (requiere <see cref="IdEmpresa"/>).</summary>
        public bool IncluirOficiales { get; set; } = true;

        public string? MonedaOrigen { get; set; }
        public string? TipoTasa { get; set; }

        /// <summary>VIGENTE, REEMPLAZADA, EN_REVISION o RECHAZADA; nulo = todos.</summary>
        public string? Estado { get; set; }

        public string? Fuente { get; set; }
        public DateOnly? Desde { get; set; }
        public DateOnly? Hasta { get; set; }

        /// <summary>Desde 1.</summary>
        public int Pagina { get; set; } = 1;

        /// <summary>Entre 1 y 500.</summary>
        public int TamanoPagina { get; set; } = 50;
    }

    public sealed record TasaHistorialFila(
        int IdTasaCambio,
        int? IdEmpresa,
        string MonedaOrigen,
        string MonedaDestino,
        string TipoTasa,
        decimal Tasa,
        DateOnly FechaVigencia,
        string Fuente,
        string? ReferenciaFuente,
        bool EsDerivada,
        string Estado,
        short Version,
        int? IdTasaAnterior,
        long? IdEjecucion,
        DateTime FechaHoraObtencionUtc,
        string CreadoPor,
        DateTime FechaCreacionUtc,
        string? ModificadoPor,
        DateTime? FechaModificacionUtc);

    public sealed record PaginaTasas(IReadOnlyList<TasaHistorialFila> Filas, int Total, int Pagina, int TamanoPagina)
    {
        public int TotalPaginas => TamanoPagina <= 0 ? 0 : (Total + TamanoPagina - 1) / TamanoPagina;
    }

    public sealed record EjecucionTasasDetalleFila(
        long IdDetalle,
        string MonedaOrigen,
        string MonedaDestino,
        string TipoTasa,
        DateOnly? FechaVigencia,
        decimal? ValorLeido,
        string Resultado,
        string? Motivo,
        int? IdTasaCambio);

    /// <param name="DetalleError">Errores técnicos de las fuentes (sin claves); para administradores.</param>
    public sealed record EjecucionTasasResumen(
        long IdEjecucion,
        string Disparador,
        DateOnly FechaObjetivo,
        byte Intento,
        long? IdEjecucionOrigen,
        string Estado,
        string? Fuente,
        string? Endpoint,
        short? HttpStatus,
        int Leidos,
        int Insertados,
        int Reemplazados,
        int Duplicados,
        int Invalidos,
        string? Mensaje,
        string? DetalleError,
        DateTime? ProximoIntentoUtc,
        bool Notificado,
        string Servidor,
        string EjecutadoPor,
        DateTime InicioUtc,
        DateTime? FinUtc,
        IReadOnlyList<EjecucionTasasDetalleFila> Detalles);

    /// <param name="Motivo">Por qué no pasó la validación (de la bitácora).</param>
    /// <param name="TasaVigenteActual">La VIGENTE de la misma moneda, tipo y fecha, si hay (la que reemplazaría al aprobar).</param>
    public sealed record TasaPendienteRevision(
        int IdTasaCambio,
        int? IdEmpresa,
        string MonedaOrigen,
        string MonedaDestino,
        string TipoTasa,
        decimal Tasa,
        DateOnly FechaVigencia,
        string Fuente,
        string? ReferenciaFuente,
        long? IdEjecucion,
        DateTime FechaCreacionUtc,
        string? Motivo,
        decimal? TasaVigenteActual);
}
