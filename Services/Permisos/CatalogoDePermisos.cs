namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// A quién se le puede dar un permiso (columna <c>ambito</c> de <c>saas.permisos</c>; paso F1.6, ADR-029). Un rol de empresa nunca
    /// recibe permisos de <see cref="Plataforma"/>: los tiene el operador de la plataforma por su cuenta (D60).
    /// </summary>
    public static class AmbitoDePermiso
    {
        /// <summary>Solo el operador de la plataforma (soporte, empresas, módulos, correo, cola de eventos…).</summary>
        public const string Plataforma = "plataforma";

        /// <summary>La administración de una empresa (usuarios, roles, invitaciones); no depende del plan ni de los módulos.</summary>
        public const string Empresa = "empresa";

        /// <summary>Una función de un módulo; solo vale si el plan del tenant concede el módulo y la empresa lo tiene activado.</summary>
        public const string Modulo = "modulo";

        public static readonly IReadOnlyList<string> Todos = new[] { Plataforma, Empresa, Modulo };
    }

    /// <summary>Las cuatro casillas por módulo que existían antes de F1.6 (<c>empresa_rol_permisos</c>).</summary>
    public static class CasillaLegada
    {
        public const string Ver = "ver";
        public const string Crear = "crear";
        public const string Editar = "editar";
        public const string Eliminar = "eliminar";

        public static readonly IReadOnlyList<string> Todas = new[] { Ver, Crear, Editar, Eliminar };
    }

    /// <summary>Un permiso del catálogo, con la forma <c>modulo.recurso.accion</c> (por ejemplo <c>flota.vehiculos.editar</c>).</summary>
    /// <param name="Modulo">Código del módulo (<c>flota</c>, <c>facturacion</c>…), o <c>plataforma</c> / <c>administracion</c> para lo que no es un módulo del plan.</param>
    /// <param name="Recurso">La pantalla o grupo de pantallas (<c>vehiculos</c>, <c>factura</c>…).</param>
    /// <param name="Accion">Lo que se hace (<c>ver</c>, <c>crear</c>, <c>editar</c>, <c>eliminar</c>, <c>anular</c>, <c>aplicar</c>…).</param>
    /// <param name="Ambito">Uno de <see cref="AmbitoDePermiso"/>.</param>
    /// <param name="CasillaLegada">La casilla de antes que concede este permiso al migrar los roles; nula si es un permiso nuevo que antes no existía
    /// o que nadie recibía por casilla (solo en ámbito módulo).</param>
    /// <param name="Descripcion">Para mostrar en la pantalla de roles.</param>
    public sealed record DefinicionDePermiso(string Modulo, string Recurso, string Accion, string Ambito, string? CasillaLegada, string Descripcion)
    {
        /// <summary>El código completo, por ejemplo <c>flota.vehiculos.editar</c>.</summary>
        public string Codigo => Modulo + "." + Recurso + "." + Accion;
    }

    /// <summary>
    /// El catálogo de permisos que siembra el script 041 (<c>saas.permisos</c>). Esta lista es la fuente única: el script la repite en SQL y
    /// una prueba (<c>ScriptsPermisosTests</c>) falla si dejan de coincidir. Agregar un permiso es agregarlo aquí y en un script nuevo.
    /// </summary>
    public static class CatalogoDePermisos
    {
        public static readonly IReadOnlyList<DefinicionDePermiso> Todos = new DefinicionDePermiso[]
        {
            new("plataforma", "soporte", "ver", AmbitoDePermiso.Plataforma, null, "Ver el panel de soporte y la lista de tenants"),
            new("plataforma", "soporte", "entrar", AmbitoDePermiso.Plataforma, null, "Entrar a un tenant o a una empresa para darle soporte"),
            new("plataforma", "empresas", "ver", AmbitoDePermiso.Plataforma, null, "Ver el directorio de empresas"),
            new("plataforma", "empresas", "crear", AmbitoDePermiso.Plataforma, null, "Crear empresas"),
            new("plataforma", "empresas", "editar", AmbitoDePermiso.Plataforma, null, "Editar los datos de una empresa"),
            new("plataforma", "empresas", "eliminar", AmbitoDePermiso.Plataforma, null, "Eliminar empresas"),
            new("plataforma", "modulos", "ver", AmbitoDePermiso.Plataforma, null, "Ver el catálogo de módulos y los módulos activados por empresa"),
            new("plataforma", "modulos", "editar", AmbitoDePermiso.Plataforma, null, "Activar y desactivar módulos en el catálogo y por empresa"),
            new("plataforma", "usuarios", "ver", AmbitoDePermiso.Plataforma, null, "Ver el directorio de usuarios de todo el sistema"),
            new("plataforma", "usuarios", "restablecer_clave", AmbitoDePermiso.Plataforma, null, "Restablecer la clave de un usuario"),
            new("plataforma", "personas", "ver", AmbitoDePermiso.Plataforma, null, "Ver las personas del sistema y su historial"),
            new("plataforma", "personas", "editar", AmbitoDePermiso.Plataforma, null, "Editar los datos personales de una persona"),
            new("plataforma", "outbox", "ver", AmbitoDePermiso.Plataforma, null, "Ver los eventos pendientes y fallidos de la cola de eventos"),
            new("plataforma", "outbox", "gestionar", AmbitoDePermiso.Plataforma, null, "Reintentar o marcar como procesados los eventos de la cola"),
            new("plataforma", "correo", "ver", AmbitoDePermiso.Plataforma, null, "Ver la configuración del correo de notificaciones"),
            new("plataforma", "correo", "editar", AmbitoDePermiso.Plataforma, null, "Guardar, probar, activar y borrar perfiles de correo"),
            new("plataforma", "diagnostico", "ver", AmbitoDePermiso.Plataforma, null, "Abrir las páginas de diagnóstico del sistema"),
            new("administracion", "usuarios", "ver", AmbitoDePermiso.Empresa, null, "Ver los usuarios de la empresa"),
            new("administracion", "usuarios", "crear", AmbitoDePermiso.Empresa, null, "Crear usuarios de la empresa"),
            new("administracion", "usuarios", "editar", AmbitoDePermiso.Empresa, null, "Editar, activar y desactivar usuarios de la empresa y vincularlos a una persona"),
            new("administracion", "roles", "ver", AmbitoDePermiso.Empresa, null, "Ver los roles de la empresa y sus permisos"),
            new("administracion", "roles", "crear", AmbitoDePermiso.Empresa, null, "Crear roles de la empresa"),
            new("administracion", "roles", "editar", AmbitoDePermiso.Empresa, null, "Editar roles, activarlos o desactivarlos y asignarles permisos"),
            new("administracion", "invitaciones", "ver", AmbitoDePermiso.Empresa, null, "Ver las invitaciones por correo de la empresa"),
            new("administracion", "invitaciones", "crear", AmbitoDePermiso.Empresa, null, "Invitar por correo a una persona y reenviar invitaciones"),
            new("administracion", "invitaciones", "anular", AmbitoDePermiso.Empresa, null, "Anular invitaciones pendientes"),
            new("flota", "vehiculos", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los vehículos"),
            new("flota", "vehiculos", "crear", AmbitoDePermiso.Modulo, "crear", "Crear vehículos"),
            new("flota", "vehiculos", "editar", AmbitoDePermiso.Modulo, "editar", "Editar vehículos"),
            new("flota", "vehiculos", "eliminar", AmbitoDePermiso.Modulo, "eliminar", "Eliminar vehículos"),
            new("flota", "personas", "ver", AmbitoDePermiso.Modulo, "ver", "Ver el personal de la flota y su historial"),
            new("flota", "personas", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar personal de la flota"),
            new("flota", "personas", "editar", AmbitoDePermiso.Modulo, "editar", "Editar personal de la flota"),
            new("flota", "catalogos", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los catálogos de flota: tipos de vehículo, rutas, talleres, cargos y categorías de repuesto"),
            new("flota", "catalogos", "crear", AmbitoDePermiso.Modulo, "crear", "Crear elementos de los catálogos de flota"),
            new("flota", "catalogos", "editar", AmbitoDePermiso.Modulo, "editar", "Editar elementos de los catálogos de flota"),
            new("flota", "mantenimiento", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las órdenes de mantenimiento"),
            new("flota", "mantenimiento", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar órdenes de mantenimiento"),
            new("flota", "repuestos", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los gastos en repuestos"),
            new("flota", "repuestos", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar gastos en repuestos"),
            new("flota", "seguros", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las pólizas de seguro"),
            new("flota", "seguros", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar pólizas de seguro"),
            new("flota", "combustible", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las cargas de combustible"),
            new("flota", "combustible", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar cargas de combustible"),
            new("flota", "peajes", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los peajes"),
            new("flota", "peajes", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar peajes"),
            new("flota", "peajes", "editar", AmbitoDePermiso.Modulo, "editar", "Editar peajes"),
            new("flota", "salarios", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los salarios diarios del personal"),
            new("flota", "salarios", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar salarios diarios"),
            new("flota", "salidas", "ver", AmbitoDePermiso.Modulo, "ver", "Ver el control de salidas y entradas de vehículos"),
            new("flota", "salidas", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar la salida de un vehículo"),
            new("flota", "salidas", "editar", AmbitoDePermiso.Modulo, "editar", "Registrar la entrada de un vehículo"),
            new("flota", "salidas", "anular", AmbitoDePermiso.Modulo, "eliminar", "Anular un registro de salida"),
            new("flota", "kpi", "ver", AmbitoDePermiso.Modulo, "ver", "Ver el tablero y los gráficos de indicadores de flota"),
            new("catalogos", "clientes", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los clientes"),
            new("catalogos", "clientes", "crear", AmbitoDePermiso.Modulo, "crear", "Crear clientes"),
            new("catalogos", "clientes", "editar", AmbitoDePermiso.Modulo, "editar", "Editar clientes"),
            new("catalogos", "clientes", "eliminar", AmbitoDePermiso.Modulo, "eliminar", "Eliminar clientes"),
            new("catalogos", "tasas_cambio", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las tasas de cambio, su histórico, su bitácora y sus tendencias"),
            new("catalogos", "tasas_cambio", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar una tasa de cambio propia de la empresa"),
            new("catalogos", "tasas_cambio", "aprobar", AmbitoDePermiso.Modulo, null, "Aprobar o rechazar tasas de cambio"),
            new("catalogos", "tasas_cambio", "configurar", AmbitoDePermiso.Modulo, null, "Configurar el proceso de la tasa oficial y ejecutarlo"),
            new("facturacion", "factura", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las facturas"),
            new("facturacion", "factura", "crear", AmbitoDePermiso.Modulo, "crear", "Emitir facturas"),
            new("facturacion", "factura", "anular", AmbitoDePermiso.Modulo, "eliminar", "Anular facturas"),
            new("facturacion", "nota", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las notas de crédito y de débito"),
            new("facturacion", "nota", "crear", AmbitoDePermiso.Modulo, "crear", "Emitir notas de crédito y de débito"),
            new("facturacion", "pago", "ver", AmbitoDePermiso.Modulo, "ver", "Ver los pagos recibidos"),
            new("facturacion", "pago", "crear", AmbitoDePermiso.Modulo, "crear", "Registrar pagos recibidos"),
            new("facturacion", "pago", "aplicar", AmbitoDePermiso.Modulo, "editar", "Aplicar un pago a facturas"),
            new("facturacion", "pago", "anular", AmbitoDePermiso.Modulo, "eliminar", "Anular pagos"),
            new("facturacion", "cxc", "ver", AmbitoDePermiso.Modulo, "ver", "Ver las cuentas por cobrar"),
        };

        private static readonly IReadOnlyDictionary<string, DefinicionDePermiso> PorCodigo
            = Todos.ToDictionary(p => p.Codigo, StringComparer.Ordinal);

        /// <summary>El permiso con ese código, o <c>null</c> si no está en el catálogo.</summary>
        public static DefinicionDePermiso? Buscar(string codigo) => PorCodigo.TryGetValue(codigo, out var p) ? p : null;

        public static bool Existe(string codigo) => PorCodigo.ContainsKey(codigo);

        /// <summary>
        /// Los permisos de módulo que concede cada casilla legada de un módulo: <c>(flota, ver)</c> concede todos los <c>flota.*.*</c> cuya
        /// casilla legada es <c>ver</c>. Es la regla de la migración de roles del script 041 y de la equivalencia con el sistema anterior.
        /// </summary>
        public static IEnumerable<DefinicionDePermiso> DeCasillaLegada(string modulo, string casilla)
            => Todos.Where(p => p.Ambito == AmbitoDePermiso.Modulo
                                && string.Equals(p.Modulo, modulo, StringComparison.Ordinal)
                                && string.Equals(p.CasillaLegada, casilla, StringComparison.Ordinal));

        /// <summary>Los códigos de los módulos que tienen permisos de ámbito módulo (hoy flota, catálogos y facturación).</summary>
        public static IReadOnlyList<string> ModulosConPermisos
            => Todos.Where(p => p.Ambito == AmbitoDePermiso.Modulo).Select(p => p.Modulo).Distinct(StringComparer.Ordinal).ToList();
    }
}
