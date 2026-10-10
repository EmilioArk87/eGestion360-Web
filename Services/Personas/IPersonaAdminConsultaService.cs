namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Consultas del administrador general sobre TODAS las personas del sistema, de cualquier empresa: el listado con
    /// las empresas a las que está asignada cada una y su rol (empleado, cliente, proveedor...), el detalle, los datos
    /// para editar y el historial completo. A diferencia de <see cref="IPersonaConsultaService"/>, aquí no se filtra por
    /// empresa y no se oculta nada de otras empresas: quien llama debe haber comprobado que el usuario es el
    /// administrador general (<c>AuthHelper.IsAdmin</c>); el servicio no mira la sesión. No devuelve tarifas ni
    /// salarios.
    /// </summary>
    public interface IPersonaAdminConsultaService
    {
        /// <summary>
        /// Una página de personas (no eliminadas ni fusionadas en otra), ordenadas por apellidos y nombres, cada una con sus
        /// vínculos con empresas. <paramref name="pagina"/> empieza en 1.
        /// </summary>
        Task<PersonaAdminPagina> ListarAsync(PersonaAdminFiltro filtro, int pagina, int tamano, CancellationToken ct = default);

        /// <summary>Todas las empresas, para el filtro del listado.</summary>
        Task<IReadOnlyList<OpcionCatalogo>> EmpresasAsync(CancellationToken ct = default);

        /// <summary>El detalle de una persona (documentos y todos sus vínculos, vigentes y terminados), o nulo si no existe, está eliminada o fue fusionada.</summary>
        Task<PersonaAdminDetalle?> ObtenerAsync(int idPersona, CancellationToken ct = default);

        /// <summary>
        /// Los datos personales de la persona listos para el formulario (sin datos de empleo), o nulo si no existe, está
        /// eliminada o fue fusionada.
        /// </summary>
        Task<PersonaEdicion?> ObtenerParaEdicionAsync(int idPersona, CancellationToken ct = default);

        /// <summary>El historial completo de cambios de la persona, con la empresa de cada cambio; nulo si la persona no existe.</summary>
        Task<HistorialPersona?> HistorialAsync(int idPersona, int maximo = 300, CancellationToken ct = default);
    }

    public enum EstadoVinculoFiltro
    {
        Todos,

        /// <summary>Personas con al menos un vínculo vigente (activo y sin fecha de fin).</summary>
        ConVinculoVigente,

        /// <summary>Personas sin ningún vínculo vigente: solo vínculos terminados, o ninguno.</summary>
        SinVinculoVigente
    }

    public sealed class PersonaAdminFiltro
    {
        /// <summary>Busca en el nombre (sin tildes ni mayúsculas), en el documento, en el código de empleado y en el código de cliente.</summary>
        public string? Texto { get; set; }

        /// <summary>Solo personas con un vínculo con esta empresa.</summary>
        public int? IdEmpresa { get; set; }

        /// <summary>Solo personas con un vínculo de este rol (<c>TiposVinculo</c>); con <see cref="IdEmpresa"/>, ese rol en esa empresa.</summary>
        public string? TipoVinculo { get; set; }

        public EstadoVinculoFiltro Estado { get; set; } = EstadoVinculoFiltro.Todos;

        public FiltroPerfilPersona Perfil { get; set; } = FiltroPerfilPersona.Todos;
    }

    /// <param name="Total">Personas que cumplen el filtro, en todas las páginas.</param>
    public sealed record PersonaAdminPagina(IReadOnlyList<PersonaAdminFila> Filas, int Total, int Pagina, int Tamano)
    {
        public int TotalPaginas => Tamano <= 0 ? 0 : (Total + Tamano - 1) / Tamano;
    }

    /// <param name="NombreCompleto">«Apellidos, Nombres», con las partes si están separadas y con el texto antiguo si no.</param>
    /// <param name="Documento">Número del documento principal SIN enmascarar: la pantalla lo muestra con <see cref="DocumentosIdentidad.Enmascarar"/>.</param>
    /// <param name="Vinculos">Los vínculos no eliminados, los vigentes primero.</param>
    /// <param name="Faltantes">Qué le falta al perfil; vacío si está completo.</param>
    public sealed record PersonaAdminFila(
        int IdPersona,
        string NombreCompleto,
        bool NombresSeparados,
        string? TipoDocumento,
        string? Documento,
        string? Telefono,
        string EstadoIdentidad,
        IReadOnlyList<PersonaAdminVinculo> Vinculos,
        IReadOnlyList<string> Faltantes)
    {
        public bool PerfilIncompleto => Faltantes.Count > 0;
        public bool TieneVinculoVigente => Vinculos.Any(v => v.Vigente);
    }

    /// <summary>Un rol de la persona en una empresa.</summary>
    /// <param name="TipoVinculo">«empleado», «cliente», «proveedor»... (<c>TiposVinculo</c>).</param>
    /// <param name="Detalle">Del empleado, su cargo y código; del cliente, su código. Nulo si no hay.</param>
    public sealed record PersonaAdminVinculo(
        int IdPersonaEmpresa,
        int IdEmpresa,
        string Empresa,
        string TipoVinculo,
        bool Activo,
        DateOnly? FechaInicio,
        DateOnly? FechaFin,
        string? Detalle)
    {
        /// <summary>Activo y sin fecha de fin.</summary>
        public bool Vigente => Activo && FechaFin == null;
    }

    public sealed record PersonaAdminDocumento(string TipoDocumento, string Numero, bool EsPrincipal);

    public sealed record PersonaAdminDetalle(
        int IdPersona,
        string NombreCompleto,
        bool NombresSeparados,
        string EstadoIdentidad,
        IReadOnlyList<PersonaAdminDocumento> Documentos,
        IReadOnlyList<PersonaAdminVinculo> Vinculos,
        string CreadoPor,
        DateTime FechaCreacion,
        string? ModificadoPor,
        DateTime? FechaModificacion,
        IReadOnlyList<string>? Usuarios = null)
    {
        /// <summary>Los usuarios del sistema vinculados a esta persona (script 021), por nombre de usuario.</summary>
        public IReadOnlyList<string> UsuariosDelSistema => Usuarios ?? Array.Empty<string>();
    }
}
