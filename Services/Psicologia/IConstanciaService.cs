using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Constancias de examen psicológico del expediente del paciente: se emiten con su tipo, la prueba o pruebas aplicadas, la fecha y su
    /// número (decisión D13): el folio preimpreso de la hoja del talonario del Colegio si el tipo usa talonario, o un correlativo propio
    /// del tipo, <c>CODIGO-AAAA-NNNNN</c>. Se anulan con motivo. Una constancia no se borra ni se edita: si tiene un error, se anula
    /// (su hoja queda inutilizada) y se emite otra con el número siguiente. Lo único que se corrige es su fecha de emisión.
    /// </summary>
    public interface IConstanciaService
    {
        Task<ResultadoPsicologia<Constancia>> EmitirAsync(EmitirConstanciaInput input, CancellationToken ct = default);

        Task<ResultadoPsicologia<Constancia>> AnularAsync(int idEmpresa, int idConstancia, string? motivo, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Corrige la fecha de emisión (la que sale impresa) de una constancia emitida, dentro de <see cref="RangoFechaEmision"/> y sin
        /// cambiar el año de su correlativo. Queda en la bitácora; la fecha y el usuario de creación no cambian.
        /// </summary>
        Task<ResultadoPsicologia<Constancia>> CorregirFechaEmisionAsync(int idEmpresa, int idConstancia, DateOnly? nuevaFecha, string usuario,
            CancellationToken ct = default);

        /// <summary>
        /// El número que llevaría la próxima constancia de cada tipo activo, por id de tipo (solo para mostrarlo; el definitivo se asigna
        /// al guardar): el siguiente folio libre del talonario, o el correlativo del año si el tipo no usa talonario.
        /// </summary>
        Task<IReadOnlyDictionary<int, string>> SiguientesNumerosAsync(int idEmpresa, int anio, CancellationToken ct = default);

        /// <summary>
        /// Deja constancia de una impresión (psi.impresiones): solo de una constancia emitida cuyo tipo tiene formato. Si el formato sale del
        /// motor de reportes (ADR-031), guarda además la fila de dbo.rep_ejecuciones en la misma transacción, y la impresión lleva el número
        /// de la versión publicada que se usó.
        /// </summary>
        /// <param name="conElMotor">Falso cuando la pantalla no pudo generar la hoja del motor y va a imprimir la de siempre: la impresión se
        /// anota con esa hoja y sin ejecución.</param>
        Task<ResultadoPsicologia<Impresion>> RegistrarImpresionAsync(int idEmpresa, int idConstancia, string usuario, int? idCuenta = null,
            bool conElMotor = true, CancellationToken ct = default);
    }

    public sealed class EmitirConstanciaInput
    {
        public int IdEmpresa { get; set; }
        public int IdPaciente { get; set; }
        public int? IdTipoConstancia { get; set; }

        /// <summary>La evaluación de la que sale, si la hay (debe ser del mismo paciente).</summary>
        public int? IdAtencion { get; set; }

        /// <summary>
        /// La que sale impresa, dentro de <see cref="RangoFechaEmision"/>. Si no viene, la última posible: hoy (Honduras) o el último día
        /// de la brigada de la evaluación, si ya terminó.
        /// </summary>
        public DateOnly? FechaEmision { get; set; }

        /// <summary>La prueba o pruebas aplicadas que declara la constancia: al menos una.</summary>
        public IReadOnlyCollection<int> IdsPruebas { get; set; } = Array.Empty<int>();

        /// <summary>El psicólogo que la firma (de Profesionales). Tiene prioridad sobre <see cref="NombreProfesional"/>.</summary>
        public int? IdProfesional { get; set; }

        /// <summary>Folio de la hoja del talonario (si el tipo usa talonario). Si no viene, el siguiente libre.</summary>
        public int? Folio { get; set; }

        /// <summary><see cref="ResultadosConstancia"/>: obligatorio si el formato del tipo imprime la conclusión.</summary>
        public string? Resultado { get; set; }

        /// <summary>Quién la firma, como sale impreso, si no se eligió un profesional. Si tampoco viene, el profesional de la atención.</summary>
        public string? NombreProfesional { get; set; }

        public string Usuario { get; set; } = "system";
    }

    /// <summary>
    /// El correlativo de las constancias (D13): uno por tipo, por empresa y año, con el código del tipo como prefijo (ARMAS-2026-00001,
    /// EMPLEO-2026-00001). No reutiliza números: cuenta también las anuladas. Por eso el código de un tipo no cambia una vez que tiene constancias.
    /// </summary>
    public static class NumeradorDeConstancias
    {
        /// <summary>Largo máximo del código de un tipo de constancia: el número completo cabe en psi.constancias.numero_constancia (30).</summary>
        public const int LargoMaximoCodigo = 10;

        public static string Prefijo(string codigoTipo, int anio) => $"{codigoTipo}-{anio:D4}-";

        public static string Formato(string codigoTipo, int anio, int numero) => $"{Prefijo(codigoTipo, anio)}{numero:D5}";

        /// <summary>El correlativo de un número CODIGO-AAAA-NNNNN del tipo y año pedidos; nulo si no tiene esa forma.</summary>
        public static int? Correlativo(string numero, string codigoTipo, int anio)
        {
            var prefijo = Prefijo(codigoTipo, anio);
            return numero.StartsWith(prefijo, StringComparison.Ordinal) && int.TryParse(numero.AsSpan(prefijo.Length), out var n) ? n : null;
        }
    }
}
