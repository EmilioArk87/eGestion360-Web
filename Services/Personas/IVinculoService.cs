namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Los roles de una persona en una empresa que no son el de empleado: por ahora el de cliente natural. Cada rol
    /// es una fila de persona_empresa con su tipo de vínculo, más una ficha propia (la de cliente vive en clientes).
    /// La persona se guarda una sola vez: registrar a un cliente busca primero a la persona por su documento y la
    /// reutiliza; solo la crea si no existe. Los cambios quedan en la bitácora por medio del interceptor.
    ///
    /// Privacidad entre empresas (decisión D8): una empresa solo ve a las personas con las que ya tiene un vínculo.
    /// Registrar un cliente con un documento que ya existe en otra empresa no lo revela: pide verificar al rol (para
    /// un cliente, el primer nombre y el primer apellido) y, si no coincide, responde con un mensaje genérico. Ser
    /// cliente de una empresa es un dato privado de ella: otra empresa que comparte a la persona no lo ve.
    ///
    /// Alcance de esta versión: el rol de cliente natural y la consulta de los vínculos de una persona. Los clientes
    /// jurídicos siguen con su razón social, y los clientes sin identificar (un «consumidor final») se registran sin
    /// ficha de persona, como hasta ahora. Los roles de proveedor y contacto llegan cuando existan sus tablas.
    /// </summary>
    public interface IVinculoService
    {
        /// <summary>
        /// Registra a una persona como cliente natural de la empresa. Si el documento ya existe, reutiliza a la persona
        /// (de esta empresa con otro rol, o de otra empresa si la verificación coincide) y le agrega el rol de cliente;
        /// si el cliente fue dado de baja, lo reactiva. Si no existe, la crea. Si hay personas parecidas en la empresa,
        /// pide confirmación antes de crear.
        /// </summary>
        Task<ResultadoRegistrarCliente> RegistrarClienteNaturalAsync(RegistrarClienteNaturalInput input, CancellationToken ct = default);

        /// <summary>
        /// Da de baja la relación comercial con un cliente natural enlazado a una persona: cierra su vínculo con la
        /// fecha y el motivo, y lo marca inactivo. No borra nada: el cliente puede volver con
        /// <see cref="RegistrarClienteNaturalAsync"/>.
        /// </summary>
        Task<ResultadoTerminarCliente> TerminarClienteAsync(TerminarClienteInput input, CancellationToken ct = default);

        /// <summary>
        /// Los vínculos que una persona tiene con ESTA empresa (empleado, cliente...). Vacío si la persona no es de la
        /// empresa: no se revelan los de otras.
        /// </summary>
        Task<IReadOnlyList<VinculoDePersona>> ListarVinculosAsync(int idEmpresa, int idPersona, CancellationToken ct = default);
    }

    // ── Registrar cliente ───────────────────────────────────────────────────

    /// <summary>Datos comerciales del cliente: son de la empresa, no de la persona (su contacto personal vive en la persona).</summary>
    public sealed class ClienteDatosInput
    {
        /// <summary>Código del cliente en la empresa; único dentro de ella.</summary>
        public string? Codigo { get; set; }

        public string? NombreComercial { get; set; }

        /// <summary>RTN u otro identificador fiscal.</summary>
        public string? IdentificadorFiscal { get; set; }

        public string? Email { get; set; }
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public string? Ciudad { get; set; }

        /// <summary>Moneda por defecto; si viene vacía, la de <see cref="PersonaValidacionOptions.MonedaPorDefecto"/>.</summary>
        public string? MonedaIsoDefault { get; set; }

        public int? IdCondicionPagoDefault { get; set; }

        /// <summary>Si viene vacío, cero.</summary>
        public decimal? LimiteCredito { get; set; }
    }

    public sealed class RegistrarClienteNaturalInput
    {
        /// <summary>Empresa de la sesión. Nunca debe venir del navegador.</summary>
        public int IdEmpresa { get; set; }

        /// <summary>
        /// Nombre y documento de la persona (obligatorio: un cliente con ficha se identifica por su documento) y, si
        /// se quiere, el resto de sus datos personales. Se valida en modo alta. <c>Empleado</c> se ignora.
        /// </summary>
        public PersonaDatosInput Persona { get; set; } = new();

        public ClienteDatosInput Cliente { get; set; } = new();

        /// <summary>Usuario de la sesión: queda en creado_por.</summary>
        public string Usuario { get; set; } = "system";

        /// <summary>El usuario vio la lista de personas parecidas y confirma que es otra persona.</summary>
        public bool ConfirmarQueEsOtraPersona { get; set; }
    }

    public enum EstadoRegistrarCliente
    {
        /// <summary>Se creó una persona nueva con su vínculo y su ficha de cliente.</summary>
        Creado,

        /// <summary>La persona ya existía (de esta empresa con otro rol, o de otra empresa y la verificación coincidió): se le agregó el rol de cliente.</summary>
        Vinculado,

        /// <summary>La persona ya había sido cliente de la empresa y se dio de baja: se reactivó con su misma ficha.</summary>
        Reactivado,

        /// <summary>Hay personas parecidas en la empresa: mostrar <see cref="ResultadoRegistrarCliente.Parecidas"/> y repetir con confirmación.</summary>
        RequiereConfirmacion,

        /// <summary>No se hizo nada: ver <see cref="ResultadoRegistrarCliente.Errores"/>.</summary>
        Rechazado
    }

    /// <param name="Errores">
    /// Campo con prefijo: «Persona.PrimerNombre», «Persona.Documento», «Cliente.Codigo»... listos para ModelState. Los que
    /// no son de un campo vienen con el campo vacío.
    /// </param>
    public sealed record ResultadoRegistrarCliente(
        EstadoRegistrarCliente Estado,
        int? IdPersona,
        int? IdCliente,
        int? IdPersonaEmpresa,
        IReadOnlyList<ErrorValidacion> Errores,
        IReadOnlyList<string> Advertencias,
        IReadOnlyList<PersonaParecida> Parecidas)
    {
        public bool Ok => Estado is EstadoRegistrarCliente.Creado or EstadoRegistrarCliente.Vinculado or EstadoRegistrarCliente.Reactivado;
    }

    // ── Terminar cliente ────────────────────────────────────────────────────

    public sealed class TerminarClienteInput
    {
        public int IdEmpresa { get; set; }
        public int IdCliente { get; set; }

        /// <summary>Último día de la relación comercial; si viene vacío, hoy (hora de Honduras).</summary>
        public DateOnly? FechaFin { get; set; }

        public string? Motivo { get; set; }
        public string Usuario { get; set; } = "system";
    }

    /// <param name="Encontrado">Falso si el cliente no existe, no es de la empresa o no está enlazado a una persona.</param>
    /// <param name="Mensaje">Qué pasó, para la pantalla: el error, o vacío si salió bien.</param>
    public sealed record ResultadoTerminarCliente(bool Ok, bool Encontrado, string Mensaje);

    // ── Consultar vínculos ──────────────────────────────────────────────────

    /// <param name="TipoVinculo">Uno de <see cref="eGestion360Web.Models.Personas.TiposVinculo"/>.</param>
    /// <param name="Vigente">Sin fecha de fin: la persona sigue teniendo ese rol en la empresa.</param>
    public sealed record VinculoDePersona(
        int IdPersonaEmpresa,
        string TipoVinculo,
        DateOnly? FechaInicio,
        DateOnly? FechaFin,
        string? MotivoFin,
        bool Activo,
        bool Vigente);
}
