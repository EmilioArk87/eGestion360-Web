using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Services.Reportes
{
    /// <summary>
    /// Fuente de datos <c>psi.constancia</c> (ADR-031, RP1): arma las variables que recibe la plantilla de una constancia psicológica. Los datos
    /// los lee la pantalla con su propia consulta (la misma de siempre); aquí solo se les da la forma que la plantilla espera, con la misma
    /// redacción que <see cref="RedaccionDeConstancias"/>. La plantilla compone el párrafo y escapa cada valor con <c>html.escape</c>.
    /// </summary>
    public static class ConstanciaPsicologiaDataSource
    {
        public const string Fuente = "psi.constancia";

        /// <summary>Tabla de origen que queda en <c>dbo.rep_ejecuciones.entidad_origen</c>.</summary>
        public const string EntidadOrigen = "psi.constancias";

        /// <summary>
        /// Variables de la plantilla: <c>modo_guia</c>, <c>fecha.dia</c> / <c>fecha.mes</c> / <c>fecha.anio_2d</c>, <c>paciente.tratamiento</c> /
        /// <c>paciente.nombre_completo</c> / <c>paciente.dni</c> (nulo si no tiene DNI), <c>constancia.resultado</c> y <c>pruebas_texto</c>.
        /// </summary>
        /// <param name="dniFormateado">El DNI ya formateado (0801-1990-12345) o nulo.</param>
        public static IReadOnlyDictionary<string, object?> Modelo(DateOnly fechaEmision, string nombreCompleto, string? sexo, string? dniFormateado,
            string? resultado, IEnumerable<(string Nombre, string Codigo)> pruebas, bool modoGuia)
        {
            ArgumentNullException.ThrowIfNull(nombreCompleto);
            ArgumentNullException.ThrowIfNull(pruebas);
            return new Dictionary<string, object?>
            {
                ["modo_guia"] = modoGuia,
                ["fecha"] = new Dictionary<string, object?>
                {
                    ["dia"] = fechaEmision.Day.ToString(),
                    ["mes"] = RedaccionDeConstancias.MesEnLetras(fechaEmision.Month),
                    ["anio_2d"] = (fechaEmision.Year % 100).ToString("D2")
                },
                ["paciente"] = new Dictionary<string, object?>
                {
                    ["tratamiento"] = RedaccionDeConstancias.Tratamiento(sexo),
                    ["nombre_completo"] = nombreCompleto.Trim(),
                    ["dni"] = string.IsNullOrWhiteSpace(dniFormateado) ? null : dniFormateado
                },
                ["constancia"] = new Dictionary<string, object?> { ["resultado"] = resultado },
                ["pruebas_texto"] = string.Join(", ", pruebas.Select(p => $"{p.Nombre} ({p.Codigo})"))
            };
        }
    }
}
