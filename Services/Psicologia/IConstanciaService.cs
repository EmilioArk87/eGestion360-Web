using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Constancias de examen psicológico del expediente del paciente: se emiten con su tipo, la prueba o pruebas aplicadas, la fecha y un
    /// correlativo propio de su tipo, <c>CODIGO-AAAA-NNNNN</c> (decisión D13: uno por tipo, por empresa y año), y se anulan con motivo. Una
    /// constancia no se borra ni se edita: si tiene un error, se anula y se emite otra con el número siguiente de su tipo.
    /// </summary>
    public interface IConstanciaService
    {
        Task<ResultadoPsicologia<Constancia>> EmitirAsync(EmitirConstanciaInput input, CancellationToken ct = default);

        Task<ResultadoPsicologia<Constancia>> AnularAsync(int idEmpresa, int idConstancia, string? motivo, string usuario, CancellationToken ct = default);

        /// <summary>
        /// El número que llevaría la próxima constancia de cada tipo activo en el año, por id de tipo (solo para mostrarlo; el definitivo se
        /// asigna al guardar).
        /// </summary>
        Task<IReadOnlyDictionary<int, string>> SiguientesNumerosAsync(int idEmpresa, int anio, CancellationToken ct = default);
    }

    public sealed class EmitirConstanciaInput
    {
        public int IdEmpresa { get; set; }
        public int IdPaciente { get; set; }
        public int? IdTipoConstancia { get; set; }

        /// <summary>La evaluación de la que sale, si la hay (debe ser del mismo paciente).</summary>
        public int? IdAtencion { get; set; }

        /// <summary>Si no viene, hoy (Honduras).</summary>
        public DateOnly? FechaEmision { get; set; }

        /// <summary>La prueba o pruebas aplicadas que declara la constancia: al menos una.</summary>
        public IReadOnlyCollection<int> IdsPruebas { get; set; } = Array.Empty<int>();

        /// <summary>El psicólogo que la firma (de Profesionales). Tiene prioridad sobre <see cref="NombreProfesional"/>.</summary>
        public int? IdProfesional { get; set; }

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
