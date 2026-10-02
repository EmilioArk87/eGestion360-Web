namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Valida y normaliza los datos de una persona y, si se informa, su rol de empleado, según las
    /// reglas del plan de personas. Solo LEE la base de datos (catálogos, cargos, documentos y códigos
    /// existentes); no guarda nada. Todos los mensajes van en español y la lógica se aplica en el
    /// servidor: la pantalla puede repetirla en el navegador, pero esta es la que manda.
    ///
    /// Uso típico:
    ///   1. La pantalla arma un <see cref="PersonaDatosInput"/> con lo escrito por el usuario.
    ///   2. ValidarAsync devuelve los errores por campo, las advertencias y los datos ya normalizados.
    ///   3. Si Ok, quien guarda usa <see cref="ResultadoValidacionPersona.Datos"/> (no el texto crudo).
    ///
    /// Privacidad entre empresas (decisión D8): un documento que ya existe en OTRA empresa no produce
    /// error ni mensaje. Se informa solo en <see cref="ResultadoValidacionPersona.DocumentoExistente"/>,
    /// para que el flujo de alta pida la verificación del rol en lugar de revelar que la persona existe.
    /// </summary>
    public interface IPersonaValidacionService
    {
        Task<ResultadoValidacionPersona> ValidarAsync(PersonaDatosInput input, CancellationToken ct = default);
    }
}
