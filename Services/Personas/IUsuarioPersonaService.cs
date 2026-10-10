namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Vincula cada usuario del sistema (<c>Users</c>) con la persona que lo usa (script 021). Es el paso intermedio
    /// hasta F1, donde el vínculo pasa a la membresía de la cuenta en cada tenant (ADR-003).
    ///
    /// Reglas: el administrador general ve y vincula cualquier persona; un administrador de empresa solo ve y vincula
    /// personas que tienen relación con su empresa, y solo a los usuarios de su empresa. La persona queda siempre dentro
    /// de la empresa del usuario (o de la empresa dueña de la plataforma, para quien no tiene empresa): si no tiene
    /// ninguna relación con ella, se le registra un vínculo de tipo «usuario». Una persona puede tener varios usuarios
    /// hasta F1. Cada cambio queda en la bitácora.
    /// </summary>
    public interface IUsuarioPersonaService
    {
        /// <summary>Personas que quien opera puede vincular, por nombre o documento (mínimo 2 caracteres, máximo 20 resultados).</summary>
        Task<IReadOnlyList<PersonaParaUsuario>> BuscarPersonasAsync(QuienOperaUsuarios quien, string? texto, CancellationToken ct = default);

        /// <summary>Una persona por su id, solo si quien opera puede verla; si no, nulo.</summary>
        Task<PersonaParaUsuario?> PersonaVisibleAsync(QuienOperaUsuarios quien, int idPersona, CancellationToken ct = default);

        /// <summary>La persona vinculada al usuario, o nulo si no tiene o si quien opera no puede ver a ese usuario.</summary>
        Task<PersonaParaUsuario?> PersonaDelUsuarioAsync(QuienOperaUsuarios quien, int idUsuario, CancellationToken ct = default);

        /// <summary>Vincula al usuario con una persona que ya existe. Si ya era su persona, solo asegura su vínculo con la empresa.</summary>
        Task<ResultadoUsuarioPersona> VincularAsync(QuienOperaUsuarios quien, int idUsuario, int idPersona, CancellationToken ct = default);

        /// <summary>
        /// Crea la persona con los datos mínimos (nombres, apellidos, documento y fecha de nacimiento) y la vincula al
        /// usuario. El documento se valida y se verifica como en Personal y Clientes.
        /// </summary>
        Task<ResultadoUsuarioPersona> CrearPersonaYVincularAsync(
            QuienOperaUsuarios quien, int idUsuario, PersonaDatosInput datos, bool confirmarQueEsOtraPersona, CancellationToken ct = default);

        /// <summary>
        /// Quita la persona del usuario: desde ese momento no puede iniciar sesión. Nadie puede quitar la suya. Si era su
        /// único usuario en esa empresa y la relación con la empresa era solo la de «usuario», esa relación se cierra (no
        /// se borra nada).
        /// </summary>
        Task<ResultadoUsuarioPersona> QuitarAsync(QuienOperaUsuarios quien, int idUsuario, CancellationToken ct = default);
    }

    /// <summary>Quién opera la pantalla de usuarios: el administrador general, o un administrador de una empresa.</summary>
    public sealed record QuienOperaUsuarios(bool EsAdministradorGeneral, int? IdEmpresa, string Usuario);

    /// <param name="Documento">Documento principal ya enmascarado; nulo si no tiene.</param>
    /// <param name="Empresas">Empresas con las que tiene relación (solo se llena para el administrador general).</param>
    /// <param name="Usuarios">Nombres de usuario ya vinculados a esta persona.</param>
    public sealed record PersonaParaUsuario(
        int IdPersona, string NombreCompleto, string? Documento, IReadOnlyList<string> Empresas, IReadOnlyList<string> Usuarios);

    public enum EstadoUsuarioPersona
    {
        Vinculado,
        Creado,
        Quitado,
        SinCambios,
        RequiereConfirmacion,
        Rechazado,
        NoEncontrado
    }

    public sealed record ResultadoUsuarioPersona(
        EstadoUsuarioPersona Estado,
        int? IdPersona,
        IReadOnlyList<ErrorValidacion> Errores,
        IReadOnlyList<PersonaParecida> Parecidas)
    {
        public bool Ok => Estado is EstadoUsuarioPersona.Vinculado or EstadoUsuarioPersona.Creado
                              or EstadoUsuarioPersona.Quitado or EstadoUsuarioPersona.SinCambios;
    }

    /// <summary>Datos de la plataforma (sección <c>Plataforma</c> de la configuración).</summary>
    public sealed class PlataformaOptions
    {
        /// <summary>
        /// Empresa dueña de la plataforma (SIP Tecnología). Es donde queda la persona de un usuario sin empresa, como el
        /// administrador general: en F1, el tenant interno de SIP (ADR-006).
        /// </summary>
        public int IdEmpresaPropia { get; set; } = 1;
    }
}
