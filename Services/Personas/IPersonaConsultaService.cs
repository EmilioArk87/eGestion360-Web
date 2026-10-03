namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Consultas de las pantallas de Personas: listado, datos para editar, historial y catálogos. Todas
    /// reciben la empresa de la sesión y solo devuelven personas con un vínculo en ella: el filtro por
    /// empresa vive aquí, en un solo lugar, para que ninguna pantalla pueda olvidarlo.
    /// </summary>
    public interface IPersonaConsultaService
    {
        /// <summary>Empleados de la empresa (no eliminados ni fusionados), con lo que falta de su perfil.</summary>
        Task<IReadOnlyList<PersonaFila>> ListarAsync(int idEmpresa, PersonaFiltro filtro, CancellationToken ct = default);

        /// <summary>
        /// Los datos de una persona de la empresa listos para el formulario de edición, o nulo si no existe o no es
        /// un empleado de la empresa (no se distingue una cosa de la otra).
        /// </summary>
        Task<PersonaEdicion?> ObtenerParaEdicionAsync(int idEmpresa, int idPersona, CancellationToken ct = default);

        /// <summary>
        /// El historial de cambios de una persona de la empresa, o nulo si no es de la empresa. De otras empresas solo
        /// se muestran los cambios de los datos personales y de los documentos, con el usuario como «otra empresa»:
        /// los datos de su relación laboral son privados de ella.
        /// </summary>
        Task<HistorialPersona?> HistorialAsync(int idEmpresa, int idPersona, int maximo = 300, CancellationToken ct = default);

        /// <summary>Los cargos activos de la empresa, para el filtro del listado.</summary>
        Task<IReadOnlyList<OpcionCatalogo>> CargosAsync(int idEmpresa, CancellationToken ct = default);

        /// <summary>
        /// El personal activo de la empresa, listo para elegirlo en un formulario de otro módulo (conductores de
        /// combustible, peajes y salidas; personas de los salarios). Sale del vínculo de empleado y de su ficha, no de
        /// las columnas viejas de personas, así que incluye también a quien trabaja en más de una empresa. Ordenado por
        /// apellidos y nombres.
        /// </summary>
        /// <param name="cargo">Código del cargo (por ejemplo «CONDUCTOR»); nulo para todo el personal activo.</param>
        Task<IReadOnlyList<OpcionPersona>> PersonalParaSeleccionAsync(int idEmpresa, string? cargo = null, CancellationToken ct = default);

        /// <summary>Las listas del formulario: cargos de la empresa y catálogos globales.</summary>
        Task<PersonaCatalogos> CatalogosAsync(int idEmpresa, CancellationToken ct = default);
    }

    // ── Listado ─────────────────────────────────────────────────────────────

    public enum FiltroPerfilPersona
    {
        Todos,

        /// <summary>Les falta algún dato: nombres por separar, fecha de nacimiento, documento de identidad o licencia de conductor.</summary>
        Incompleto,

        /// <summary>Sin primer nombre o primer apellido: la lista de revisión de las personas migradas con reparto ambiguo.</summary>
        NombresPorRevisar
    }

    public sealed class PersonaFiltro
    {
        /// <summary>Busca en el nombre (sin tildes ni mayúsculas), en el código de empleado y en el documento.</summary>
        public string? Texto { get; set; }

        public string? Cargo { get; set; }
        public bool? Activo { get; set; }
        public FiltroPerfilPersona Perfil { get; set; } = FiltroPerfilPersona.Todos;
    }

    /// <param name="NombreCompleto">«Apellidos, Nombres», con las partes si están separadas y con el texto antiguo si no.</param>
    /// <param name="NombresSeparados">Falso para las personas que esperan revisión de su nombre.</param>
    /// <param name="TipoDocumento">Tipo del documento principal; nulo si no tiene.</param>
    /// <param name="Documento">Número del documento principal SIN enmascarar: la pantalla lo muestra con <see cref="DocumentosIdentidad.Enmascarar"/>.</param>
    /// <param name="Faltantes">Qué le falta al perfil, en palabras para el usuario; vacío si está completo.</param>
    public sealed record PersonaFila(
        int IdPersona,
        string NombreCompleto,
        bool NombresSeparados,
        string? TipoDocumento,
        string? Documento,
        string? CodigoInterno,
        string? Cargo,
        string? Telefono,
        decimal? TarifaDiaria,
        string? Moneda,
        DateOnly? FechaIngreso,
        DateOnly? FechaBaja,
        bool Activo,
        string EstadoIdentidad,
        IReadOnlyList<string> Faltantes)
    {
        public bool PerfilIncompleto => Faltantes.Count > 0;
    }

    // ── Edición ─────────────────────────────────────────────────────────────

    /// <param name="Datos">Lo que ya tiene la persona, en la forma que usa el formulario y el servicio. Sin el documento enmascarado.</param>
    /// <param name="NombresRegistrados">El texto antiguo «nombres apellidos», para mostrarlo a quien debe separarlo.</param>
    public sealed record PersonaEdicion(
        PersonaDatosInput Datos,
        int? IdDepartamentoNacimiento,
        int? IdDepartamentoResidencia,
        byte[] TokenConcurrencia,
        string EstadoIdentidad,
        string NombresRegistrados,
        bool NombresSeparados,
        bool Activo,
        string CreadoPor,
        DateTime FechaCreacion,
        string? ModificadoPor,
        DateTime? FechaModificacion);

    // ── Historial ───────────────────────────────────────────────────────────

    /// <param name="Truncado">Hay más cambios que los mostrados.</param>
    public sealed record HistorialPersona(string NombrePersona, IReadOnlyList<HistorialFila> Filas, bool Truncado);

    /// <param name="FechaHora">Hora de Honduras (UTC-6).</param>
    /// <param name="Entidad">«Persona», «Documento», «Vínculo con la empresa» o «Ficha de empleado».</param>
    /// <param name="Operacion">«Alta», «Cambio» o «Baja».</param>
    /// <param name="Campo">Nombre del campo en español; nulo en altas y bajas.</param>
    /// <param name="Detalle">En altas y bajas, un resumen de lo que se creó o se borró; nulo si no se puede mostrar.</param>
    /// <param name="Usuario">Quien hizo el cambio, o «otra empresa» si fue desde otra empresa.</param>
    /// <param name="Empresa">La empresa desde la que se hizo el cambio. Solo la informa el historial del administrador general; en el de una empresa va nulo.</param>
    public sealed record HistorialFila(
        DateTime FechaHora,
        string Entidad,
        string Operacion,
        string? Campo,
        string? ValorAnterior,
        string? ValorNuevo,
        string? Detalle,
        string Usuario,
        Guid IdTransaccion,
        bool DeOtraEmpresa,
        string? Empresa = null);

    // ── Catálogos del formulario ────────────────────────────────────────────

    public sealed record OpcionCatalogo(string Valor, string Texto);

    /// <param name="NombreCompleto">Nombres y apellidos, tal como se muestran en las listas de elegir persona.</param>
    public sealed record OpcionPersona(int IdPersona, string NombreCompleto);
    public sealed record OpcionDepartamento(int Id, string Nombre);
    public sealed record OpcionMunicipio(int Id, int IdDepartamento, string Nombre);

    public sealed record PersonaCatalogos(
        IReadOnlyList<OpcionCatalogo> Cargos,
        IReadOnlyList<OpcionCatalogo> TiposDocumento,
        IReadOnlyList<OpcionCatalogo> CategoriasLicencia,
        IReadOnlyList<OpcionCatalogo> Paises,
        IReadOnlyList<OpcionCatalogo> Monedas,
        IReadOnlyList<OpcionDepartamento> Departamentos,
        IReadOnlyList<OpcionMunicipio> Municipios)
    {
        public static PersonaCatalogos Vacios { get; } = new(
            Array.Empty<OpcionCatalogo>(), Array.Empty<OpcionCatalogo>(), Array.Empty<OpcionCatalogo>(),
            Array.Empty<OpcionCatalogo>(), Array.Empty<OpcionCatalogo>(),
            Array.Empty<OpcionDepartamento>(), Array.Empty<OpcionMunicipio>());
    }
}
