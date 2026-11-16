using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Catálogos del módulo por empresa: las pruebas psicológicas que aplica el centro (decisión D12) y los tipos de constancia que extiende
    /// (D14). Un registro del catálogo no se borra: se desactiva, para que las atenciones y constancias que ya lo usan lo sigan mostrando.
    /// La empresa siempre la pone el servidor (la de la sesión).
    /// </summary>
    public interface ICatalogoPsicologiaService
    {
        /// <summary>Crea (sin <see cref="ElementoCatalogoInput.Id"/>) o edita una prueba psicológica.</summary>
        Task<ResultadoPsicologia<int>> GuardarPruebaAsync(ElementoCatalogoInput input, CancellationToken ct = default);

        /// <summary>
        /// Crea o edita un tipo de constancia. Su código (hasta 10 caracteres) es el prefijo de la numeración de sus constancias (D13), así que
        /// no cambia una vez que tiene constancias. Aquí se elige también su formato de impresión (D14) y su talonario del Colegio.
        /// </summary>
        Task<ResultadoPsicologia<int>> GuardarTipoConstanciaAsync(ElementoCatalogoInput input, CancellationToken ct = default);

        Task<ResultadoPsicologia<bool>> CambiarEstadoPruebaAsync(int idEmpresa, int idPrueba, bool activo, string usuario, CancellationToken ct = default);

        Task<ResultadoPsicologia<bool>> CambiarEstadoTipoConstanciaAsync(int idEmpresa, int idTipo, bool activo, string usuario, CancellationToken ct = default);
    }

    public sealed class ElementoCatalogoInput
    {
        public int IdEmpresa { get; set; }

        /// <summary>Nulo o 0 para crear.</summary>
        public int? Id { get; set; }

        /// <summary>Código corto (MMPI2, RAVEN, ARMAS): se guarda en mayúsculas, sin tildes ni espacios.</summary>
        public string? Codigo { get; set; }

        public string? Nombre { get; set; }

        /// <summary>Solo para pruebas.</summary>
        public string? Descripcion { get; set; }

        /// <summary>Solo para tipos de constancia: código del formato de impresión (<see cref="PlantillasConstancia"/>) o vacío.</summary>
        public string? Plantilla { get; set; }

        /// <summary>Solo para tipos de constancia: talonario del Colegio en uso (los dos o ninguno).</summary>
        public int? FolioDesde { get; set; }
        public int? FolioHasta { get; set; }

        public string Usuario { get; set; } = "system";
    }

    /// <summary>Resultado de una operación del módulo: el valor si salió bien, o los errores (con su campo) si no.</summary>
    public sealed record ResultadoPsicologia<T>(T? Valor, IReadOnlyList<ErrorValidacion> Errores)
    {
        public bool Ok => Errores.Count == 0;

        public static ResultadoPsicologia<T> Exito(T valor) => new(valor, Array.Empty<ErrorValidacion>());

        public static ResultadoPsicologia<T> Error(string campo, string mensaje) => new(default, new[] { new ErrorValidacion(campo, mensaje) });

        public static ResultadoPsicologia<T> ConErrores(IReadOnlyList<ErrorValidacion> errores) => new(default, errores);
    }
}
