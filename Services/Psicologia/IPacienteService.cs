using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Alta de pacientes del módulo de Psicología sobre la persona maestra (ADR-004): busca a la persona por su DNI dentro de la cuenta,
    /// la crea si no existe, le agrega el vínculo 'paciente' con la empresa, abre su ficha con un número de expediente y, si viene de
    /// una jornada, su atención en borrador. La empresa siempre la pone el servidor (la de la sesión), nunca el formulario.
    /// </summary>
    public interface IPacienteService
    {
        /// <summary>Registra un paciente y guarda.</summary>
        Task<ResultadoPaciente> RegistrarAsync(RegistrarPacienteInput input, CancellationToken ct = default);

        /// <summary>
        /// Igual que <see cref="RegistrarAsync"/> pero SIN guardar: deja los cambios en el contexto para que quien llama guarde muchos
        /// juntos en una transacción (la importación de una brigada). El numerador lleva los expedientes que ya se repartieron.
        /// </summary>
        Task<ResultadoPaciente> PrepararAsync(RegistrarPacienteInput input, NumeradorDeExpedientes numerador, CancellationToken ct = default);

        /// <summary>Un numerador de expedientes para una empresa, que arranca después del último número guardado.</summary>
        NumeradorDeExpedientes NuevoNumerador(int idEmpresa);
    }

    public sealed class RegistrarPacienteInput
    {
        /// <summary>Empresa de la sesión.</summary>
        public int IdEmpresa { get; set; }

        /// <summary>Datos de la persona: nombres, apellidos, DNI y sexo. El DNI es obligatorio: identifica al paciente.</summary>
        public PersonaDatosInput Persona { get; set; } = new();

        /// <summary>Jornada de la que viene; con ella se abre una atención en borrador (decisión D7).</summary>
        public int? IdJornada { get; set; }

        /// <summary>Edad que declaró o que trae la fuente (decisión D3).</summary>
        public byte? EdadDeclarada { get; set; }

        /// <summary>Fecha de apertura del expediente; si no viene, la de la jornada o la de hoy.</summary>
        public DateOnly? FechaApertura { get; set; }

        public string Usuario { get; set; } = "system";
    }

    public enum EstadoPaciente
    {
        /// <summary>Persona nueva, vínculo nuevo y ficha nueva.</summary>
        Creado,
        /// <summary>La persona ya existía en la cuenta: se le agregó (o reactivó) el vínculo de paciente y su ficha.</summary>
        Vinculado,
        /// <summary>Ya era paciente de la empresa: no se creó otra ficha (si venía de una jornada, se le abrió la atención).</summary>
        YaEraPaciente,
        /// <summary>No se registró: ver los errores.</summary>
        Rechazado
    }

    /// <param name="Paciente">La ficha (nueva o la que ya tenía). Con <see cref="IPacienteService.PrepararAsync"/> su id es 0 hasta guardar.</param>
    /// <param name="AtencionNueva">Verdadero si se abrió una atención en la jornada; falso si ya tenía una.</param>
    public sealed record ResultadoPaciente(
        EstadoPaciente Estado, Paciente? Paciente, bool AtencionNueva, IReadOnlyList<ErrorValidacion> Errores, IReadOnlyList<string> Advertencias)
    {
        public bool Ok => Estado != EstadoPaciente.Rechazado;
    }

    /// <summary>
    /// Reparte los números de expediente PSI-AAAA-NNNNN de una empresa (decisión D5): correlativo por año, sin reutilizar números
    /// (cuenta también las fichas eliminadas). Un número repetido por otra importación al mismo tiempo lo rechaza
    /// UX_pacientes_expediente al guardar.
    /// </summary>
    public sealed class NumeradorDeExpedientes
    {
        private readonly Dictionary<int, int> _ultimoPorAnio = new();
        private readonly Func<int, CancellationToken, Task<int>> _ultimoGuardado;

        public NumeradorDeExpedientes(int idEmpresa, Func<int, CancellationToken, Task<int>> ultimoGuardado)
        {
            IdEmpresa = idEmpresa;
            _ultimoGuardado = ultimoGuardado;
        }

        public int IdEmpresa { get; }

        public async Task<string> SiguienteAsync(int anio, CancellationToken ct = default)
        {
            if (!_ultimoPorAnio.TryGetValue(anio, out var ultimo))
                ultimo = await _ultimoGuardado(anio, ct);

            ultimo++;
            _ultimoPorAnio[anio] = ultimo;
            return Formato(anio, ultimo);
        }

        public static string Formato(int anio, int numero) => $"PSI-{anio:D4}-{numero:D5}";

        /// <summary>El correlativo de un expediente PSI-AAAA-NNNNN del año pedido; nulo si no tiene esa forma.</summary>
        public static int? Correlativo(string expediente, int anio)
        {
            var prefijo = $"PSI-{anio:D4}-";
            return expediente.StartsWith(prefijo, StringComparison.Ordinal) && int.TryParse(expediente.AsSpan(prefijo.Length), out var n) ? n : null;
        }
    }
}
