using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Importación del listado de una brigada (Excel) en tres pasos: subir (borrador con cada fila analizada), revisar (corregir el
    /// reparto de nombres u omitir filas) y confirmar (todo en una transacción: o entran todas las filas o ninguna). La empresa
    /// siempre es la de la sesión; un lote de otra empresa no se encuentra.
    /// </summary>
    public interface IImportacionPsicologiaService
    {
        /// <summary>Paso 1: analiza las filas del archivo y guarda el lote en borrador. No crea pacientes.</summary>
        Task<ResultadoImportacion> CrearBorradorAsync(int idEmpresa, int idJornada, ArchivoBrigada archivo, string usuario, CancellationToken ct = default);

        /// <summary>Paso 2: corrige el reparto de nombres y apellidos de una fila (o la omite) y la vuelve a analizar.</summary>
        Task<ResultadoImportacion> CorregirFilaAsync(int idEmpresa, int idImportacion, CorreccionFila correccion, string usuario, CancellationToken ct = default);

        /// <summary>Paso 3: crea las personas, vínculos, fichas y atenciones de las filas listas, en una sola transacción.</summary>
        Task<ResultadoImportacion> ConfirmarAsync(int idEmpresa, int idImportacion, string usuario, CancellationToken ct = default);

        /// <summary>Descarta un lote en borrador (no crea nada).</summary>
        Task<ResultadoImportacion> DescartarAsync(int idEmpresa, int idImportacion, string usuario, CancellationToken ct = default);

        /// <summary>El lote con sus filas en orden, o nulo si no es de esta empresa.</summary>
        Task<Importacion?> ObtenerAsync(int idEmpresa, int idImportacion, CancellationToken ct = default);
    }

    public sealed class CorreccionFila
    {
        public int IdImportacionFila { get; set; }
        public string? PrimerNombre { get; set; }
        public string? SegundoNombre { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }

        /// <summary>Verdadero para dejar la fila fuera del lote; falso para volver a incluirla.</summary>
        public bool Omitir { get; set; }
    }

    /// <param name="Ok">Verdadero si la operación se hizo.</param>
    /// <param name="Encontrado">Falso si el lote no existe en esta empresa.</param>
    /// <param name="Mensaje">Qué pasó, para mostrarlo.</param>
    /// <param name="Importacion">El lote, con sus contadores, tras la operación.</param>
    public sealed record ResultadoImportacion(bool Ok, bool Encontrado, string Mensaje, Importacion? Importacion)
    {
        public static ResultadoImportacion NoEncontrado() => new(false, false, "No se encontró la importación en esta empresa.", null);
        public static ResultadoImportacion Falla(string mensaje, Importacion? importacion = null) => new(false, true, mensaje, importacion);
    }
}
