using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>Un formato de impresión de constancia (D14): se imprime sobre la hoja del Colegio ya membretada (D15).</summary>
    /// <param name="PideResultado">El formato imprime la conclusión (apto o no apto): se elige al emitir.</param>
    /// <param name="Version">Sube cuando cambian las posiciones o los textos; queda en cada impresión (psi.impresiones).</param>
    public sealed record PlantillaConstancia(string Codigo, string Nombre, bool PideResultado, string TamanoPapel, string Version);

    /// <summary>
    /// Formatos de impresión disponibles. Cada tipo de constancia elige el suyo en Catálogos. Las posiciones están en
    /// Pages/Psicologia/Constancias/Imprimir.cshtml, en milímetros, para ajustarlas cuando lleguen las medidas reales de la hoja.
    /// </summary>
    public static class PlantillasConstancia
    {
        public const string LicenciaConducir = "licencia_conducir";

        public static readonly IReadOnlyList<PlantillaConstancia> Todas = new[]
        {
            // Hoja "Constancia de examen psicologico para solicitud de licencia de conducir" del Colegio de Psicologos de Honduras.
            // Tamaño carta provisional: la hoja es parecida a carta; se ajusta con las medidas reales.
            new PlantillaConstancia(LicenciaConducir, "Licencia de conducir (hoja del Colegio de Psicólogos)", true, TamanosPapel.Carta, "1")
        };

        public static PlantillaConstancia? Buscar(string? codigo) => Todas.FirstOrDefault(p => p.Codigo == codigo);
    }

    /// <summary>Lo que se escribe a mano en la hoja, ya armado para imprimirlo en su lugar.</summary>
    /// <param name="Dia">Día del mes (va antes de "de" en la hoja).</param>
    /// <param name="Mes">Mes en letras, en minúsculas (va entre "de" y "del").</param>
    /// <param name="Anio">Los dos últimos dígitos del año (la hoja trae impreso "20").</param>
    public sealed record TextoDeConstancia(string Dia, string Mes, string Anio, string Cuerpo, string Pruebas);

    public static class RedaccionDeConstancias
    {
        private static readonly string[] Meses =
        {
            "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
        };

        /// <summary>"el señor", "la señora" o, si no se sabe el sexo, "el (la) señor(a)".</summary>
        public static string Tratamiento(string? sexo) => sexo switch
        {
            "M" => "el señor",
            "F" => "la señora",
            _ => "el (la) señor(a)"
        };

        /// <summary>
        /// La constancia de examen psicológico para solicitud de licencia de conducir: la fecha, el párrafo con el nombre, el DNI y la
        /// conclusión, y las pruebas aplicadas ("Prueba Psicológica de Manejo (PPM)").
        /// </summary>
        public static TextoDeConstancia LicenciaConducir(DateOnly fecha, string nombre, string? sexo, string? dni, string? resultado,
            IEnumerable<(string Nombre, string Codigo)> pruebas)
        {
            var conclusion = resultado == ResultadosConstancia.NoApto
                ? "no reúne las condiciones psicológicas requeridas, NO APTO para conducir."
                : "posee personalidad estable, APTO para conducir.";
            var cuerpo = $"Por este medio se hace constar que {Tratamiento(sexo)} {nombre.Trim()}, con DNI {dni ?? "________________"}, {conclusion}";
            var aplicadas = string.Join(", ", pruebas.Select(p => $"{p.Nombre} ({p.Codigo})"));
            return new TextoDeConstancia(fecha.Day.ToString(), Meses[fecha.Month - 1], (fecha.Year % 100).ToString("D2"), cuerpo, aplicadas);
        }
    }
}
