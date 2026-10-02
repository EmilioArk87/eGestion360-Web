namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Alta, edición y fusión de personas con la persona maestra: una persona real es una sola fila,
    /// compartida por las empresas con las que se vincula. Valida con <see cref="IPersonaValidacionService"/>
    /// y guarda todo (persona, documento, vínculo y ficha de empleado) en un solo guardado atómico. Los
    /// cambios quedan en la bitácora por medio del interceptor de auditoría.
    ///
    /// Alcance: el rol de empleado. El de cliente lo registra <see cref="IVinculoService"/>, que reutiliza a la
    /// misma persona: una persona puede ser empleado y cliente de la misma empresa con una sola ficha, y en cada
    /// sentido se le agrega el rol que le falta sin crear a nadie dos veces.
    ///
    /// Compatibilidad: mientras existan las columnas legadas de dbo.personas (se retiran en el script
    /// 019), el servicio las mantiene al día (nombres, apellidos, documento, cargo, tarifa, fechas), porque
    /// las pantallas y los consumidores actuales todavía las leen. Una persona compartida entre empresas no
    /// se puede representar en esas columnas, que son de una sola empresa: hasta que los consumidores lean
    /// el vínculo (fase F6), una persona vinculada a una segunda empresa no aparece en las listas viejas de
    /// esa empresa.
    ///
    /// Privacidad entre empresas (decisión D8): una empresa solo ve a las personas con las que tiene un
    /// vínculo. Registrar un documento que ya existe en otra empresa no lo revela: pide verificar al rol
    /// (para un empleado, el primer apellido y la fecha de nacimiento) y, si no coincide, responde con el
    /// mismo mensaje genérico de siempre.
    /// </summary>
    public interface IPersonaService
    {
        /// <summary>
        /// Registra a una persona como empleado de la empresa. Si el documento ya existe en otra empresa y la
        /// verificación coincide, vincula a la persona existente en lugar de crear otra. Si hay personas
        /// parecidas en la empresa, pide confirmación antes de crear.
        /// </summary>
        Task<ResultadoCrearPersona> CrearAsync(CrearPersonaInput input, CancellationToken ct = default);

        /// <summary>
        /// Edita los datos de una persona de la empresa y su ficha de empleado. Solo encuentra a personas con
        /// un vínculo en la empresa de la sesión. Si el documento escrito ya pertenece a otra persona de otra
        /// empresa, ofrece la fusión (tras verificar).
        /// </summary>
        Task<ResultadoActualizarPersona> ActualizarAsync(ActualizarPersonaInput input, CancellationToken ct = default);

        /// <summary>
        /// Fusiona dos fichas de la misma persona: la sobrante queda marcada como fusionada y apunta a la
        /// principal; sus documentos, vínculos y registros operativos pasan a la principal, todo en una
        /// transacción. Las dos personas deben pertenecer a la empresa de la sesión.
        /// </summary>
        Task<ResultadoFusion> FusionarAsync(FusionarPersonasInput input, CancellationToken ct = default);

        /// <summary>
        /// Activa o desactiva a un empleado en la empresa de la sesión: cambia su vínculo y su ficha de esta
        /// empresa. La columna legada personas.activo queda activa mientras la persona tenga algún vínculo
        /// activo en cualquier empresa. No termina el vínculo ni toca las fechas.
        /// </summary>
        Task<ResultadoCambioEstado> CambiarEstadoAsync(CambiarEstadoPersonaInput input, CancellationToken ct = default);
    }

    // ── Cambiar estado ──────────────────────────────────────────────────────

    public sealed class CambiarEstadoPersonaInput
    {
        public int IdEmpresa { get; set; }
        public int IdPersona { get; set; }
        public bool Activo { get; set; }
        public string Usuario { get; set; } = "system";
    }

    /// <param name="Encontrada">Falso si la persona no existe, no es de la empresa o no tiene ficha de empleado en ella.</param>
    /// <param name="NombreCompleto">Para el mensaje de la pantalla; vacío si no se encontró.</param>
    public sealed record ResultadoCambioEstado(bool Ok, bool Encontrada, string NombreCompleto);

    // ── Crear ───────────────────────────────────────────────────────────────

    public sealed class CrearPersonaInput
    {
        /// <summary>Los datos de la persona y su rol de empleado (obligatorio). Se valida en modo alta.</summary>
        public PersonaDatosInput Datos { get; set; } = new();

        /// <summary>Usuario de la sesión: queda en creado_por.</summary>
        public string Usuario { get; set; } = "system";

        /// <summary>
        /// El usuario vio la lista de personas parecidas y confirma que es otra persona. Sin esto, si hay
        /// parecidas, el alta no se hace.
        /// </summary>
        public bool ConfirmarQueEsOtraPersona { get; set; }
    }

    public enum EstadoCrearPersona
    {
        /// <summary>Se creó una persona nueva con su vínculo y su ficha.</summary>
        Creada,

        /// <summary>El documento ya existía en otra empresa y la verificación coincidió: se vinculó a la persona existente.</summary>
        Vinculada,

        /// <summary>Hay personas parecidas en la empresa: mostrar <see cref="ResultadoCrearPersona.Parecidas"/> y repetir con confirmación.</summary>
        RequiereConfirmacion,

        /// <summary>No se hizo nada: ver <see cref="ResultadoCrearPersona.Errores"/>.</summary>
        Rechazada
    }

    /// <summary>Una persona de la empresa con el mismo nombre normalizado (y la misma fecha de nacimiento, si ambas la tienen).</summary>
    /// <param name="EsPersonal">Si tiene ficha de empleado en la empresa: solo entonces la pantalla de personal puede abrirla.</param>
    public sealed record PersonaParecida(int IdPersona, string NombreCompleto, DateOnly? FechaNacimiento, bool Activa, bool EsPersonal = true);

    public sealed record ResultadoCrearPersona(
        EstadoCrearPersona Estado,
        int? IdPersona,
        int? IdPersonaEmpresa,
        IReadOnlyList<ErrorValidacion> Errores,
        IReadOnlyList<string> Advertencias,
        IReadOnlyList<PersonaParecida> Parecidas)
    {
        public bool Ok => Estado is EstadoCrearPersona.Creada or EstadoCrearPersona.Vinculada;
    }

    // ── Actualizar ──────────────────────────────────────────────────────────

    public sealed class ActualizarPersonaInput
    {
        /// <summary>
        /// Debe traer IdPersona y la empresa de la sesión. Se valida en modo edición. Lo que viene vacío borra
        /// el valor guardado (la pantalla debe enviar siempre lo que ya tiene), salvo el documento: vacío lo deja como está.
        /// </summary>
        public PersonaDatosInput Datos { get; set; } = new();

        public string Usuario { get; set; } = "system";

        /// <summary>Token de concurrencia que leyó la pantalla. Si no coincide con el actual, no se guarda.</summary>
        public byte[]? TokenConcurrencia { get; set; }

        /// <summary>El usuario confirma la fusión que se le ofreció (<see cref="EstadoActualizarPersona.RequiereFusion"/>).</summary>
        public bool ConfirmarFusion { get; set; }
    }

    public enum EstadoActualizarPersona
    {
        Actualizada,

        /// <summary>La persona no existe o no pertenece a la empresa de la sesión (no se distingue).</summary>
        NoEncontrada,

        /// <summary>
        /// El documento escrito pertenece a otra ficha y la verificación coincidió: se ofrece fusionar. No se
        /// guardó nada; repetir con <see cref="ActualizarPersonaInput.ConfirmarFusion"/> para fusionar.
        /// </summary>
        RequiereFusion,

        /// <summary>Se fusionó la ficha en la otra; <see cref="ResultadoActualizarPersona.IdPersona"/> es la persona principal. No se aplicaron otras ediciones.</summary>
        Fusionada,

        Rechazada
    }

    public sealed record ResultadoActualizarPersona(
        EstadoActualizarPersona Estado,
        int? IdPersona,
        IReadOnlyList<ErrorValidacion> Errores,
        IReadOnlyList<string> Advertencias)
    {
        public bool Ok => Estado is EstadoActualizarPersona.Actualizada or EstadoActualizarPersona.Fusionada;
    }

    // ── Fusionar ────────────────────────────────────────────────────────────

    public sealed class FusionarPersonasInput
    {
        /// <summary>Empresa de la sesión. Las dos personas deben tener un vínculo en ella.</summary>
        public int IdEmpresa { get; set; }

        /// <summary>La ficha que sobra.</summary>
        public int IdPersonaSobrante { get; set; }

        /// <summary>La ficha que se queda.</summary>
        public int IdPersonaPrincipal { get; set; }

        public string Usuario { get; set; } = "system";
    }

    /// <param name="Documentos">Documentos que pasaron a la principal.</param>
    /// <param name="DocumentosRepetidos">Documentos de la sobrante que la principal ya tenía (se dieron de baja).</param>
    /// <param name="Vinculos">Vínculos que pasaron a la principal.</param>
    /// <param name="VinculosEnConflicto">Vínculos vigentes de la sobrante que la principal ya tenía en la misma empresa y rol (se dieron de baja).</param>
    /// <param name="RegistrosOperativos">Cargas de combustible, salidas, odómetros, peajes y salarios que se reasignaron.</param>
    public sealed record ResumenFusion(
        int Documentos, int DocumentosRepetidos, int Vinculos, int VinculosEnConflicto, int RegistrosOperativos);

    public sealed record ResultadoFusion(
        bool Ok,
        int? IdPersonaPrincipal,
        IReadOnlyList<string> Errores,
        ResumenFusion? Resumen);
}
