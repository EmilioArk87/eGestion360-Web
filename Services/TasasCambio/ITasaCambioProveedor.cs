namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Una fuente de tasas (API del BCH, Excel del BCH, BCE). Solo obtiene y traduce: no toca la base de datos ni
    /// decide qué se guarda; eso lo hace <see cref="TasaCambioSyncService"/>.
    /// </summary>
    public interface ITasaCambioProveedor
    {
        /// <summary>Valor de <see cref="Models.Catalogos.TasasCambioCatalogo.Fuente"/> (BCH_API, BCH_XLSX, BCE).</summary>
        string Fuente { get; }

        /// <summary>Falso si le falta configuración (clave, indicadores): el orquestador ni lo llama.</summary>
        bool Habilitado { get; }

        /// <summary>Nunca lanza por errores de la fuente: los devuelve clasificados en <see cref="ResultadoProveedor.Error"/>.</summary>
        Task<ResultadoProveedor> ObtenerAsync(SolicitudLectura solicitud, CancellationToken ct = default);
    }

    /// <summary>Rango de fechas de vigencia que se pide a la fuente (incluye ambos extremos).</summary>
    public sealed record SolicitudLectura(DateOnly Desde, DateOnly Hasta);

    /// <summary>
    /// Una tasa tal como la publica la fuente: 1 <see cref="MonedaOrigen"/> = <see cref="Valor"/>
    /// <see cref="MonedaDestino"/> el día <see cref="Fecha"/>.
    /// </summary>
    public sealed record LecturaTasa(
        string MonedaOrigen,
        string MonedaDestino,
        string TipoTasa,
        DateOnly Fecha,
        decimal Valor,
        string Fuente,
        string? Referencia,
        bool EsDerivada = false);

    public enum TipoErrorFuente
    {
        /// <summary>Tiempo agotado, 5xx, 408, 429 o red: puede funcionar más tarde.</summary>
        Transitorio,

        /// <summary>401/403, 404, otro 4xx o formato inesperado: no se arregla reintentando.</summary>
        Permanente
    }

    /// <summary>Un error de la fuente, ya clasificado. El mensaje nunca incluye la clave del API.</summary>
    public sealed record ErrorFuente(TipoErrorFuente Tipo, string Mensaje, int? HttpStatus = null, string? Detalle = null)
    {
        public bool EsTransitorio => Tipo == TipoErrorFuente.Transitorio;
    }

    /// <summary>Lo que devolvió una fuente: lecturas o error, más los datos de la consulta para la bitácora.</summary>
    public sealed class ResultadoProveedor
    {
        public string Fuente { get; init; } = string.Empty;
        public IReadOnlyList<LecturaTasa> Lecturas { get; init; } = Array.Empty<LecturaTasa>();

        /// <summary>URL consultada, sin la clave.</summary>
        public string? Endpoint { get; init; }
        public int? HttpStatus { get; init; }

        /// <summary>SHA-256 (hex en minúsculas, 64 caracteres) de la respuesta.</summary>
        public string? HashContenido { get; init; }

        public ErrorFuente? Error { get; init; }

        public bool Exitoso => Error == null;

        public static ResultadoProveedor Fallido(string fuente, ErrorFuente error, string? endpoint) =>
            new() { Fuente = fuente, Error = error, Endpoint = endpoint, HttpStatus = error.HttpStatus };
    }

    /// <summary>Error de la fuente que viaja como excepción dentro de un proveedor hasta convertirse en <see cref="ErrorFuente"/>.</summary>
    public sealed class ErrorFuenteException : Exception
    {
        public ErrorFuente Error { get; }

        public ErrorFuenteException(ErrorFuente error, Exception? interna = null) : base(error.Mensaje, interna) => Error = error;

        public static ErrorFuenteException FormatoInesperado(string mensaje, Exception? interna = null) =>
            new(new ErrorFuente(TipoErrorFuente.Permanente, "Formato inesperado: " + mensaje, Detalle: interna?.Message), interna);
    }
}
