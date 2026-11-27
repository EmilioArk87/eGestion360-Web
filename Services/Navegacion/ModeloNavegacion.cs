namespace eGestion360Web.Services.Navegacion
{
    /// <summary>Cómo se agrupan los módulos en todos los estilos de menú.</summary>
    public enum GrupoDeNavegacion
    {
        /// <summary>Los módulos con los que trabaja la empresa (Flota, Facturación, Psicología…).</summary>
        Operacion,

        /// <summary>La administración de la propia empresa (invitaciones, roles).</summary>
        Empresa,

        /// <summary>La administración de la plataforma (solo su personal).</summary>
        Plataforma,
    }

    /// <summary>Una pantalla a la que se llega desde el menú.</summary>
    /// <param name="Ruta">Ruta Razor absoluta (<c>/Flota/KPI/Dashboard</c>): la misma que pide el filtro de permisos.</param>
    /// <param name="Titulo">Cómo se llama en el menú.</param>
    /// <param name="Descripcion">Una línea que dice para qué sirve.</param>
    /// <param name="Icono">Clase de Font Awesome (<c>fa-chart-bar</c>).</param>
    /// <param name="Seccion">Subgrupo dentro del módulo (columna del mega-menú); nulo si el módulo no se divide.</param>
    /// <param name="Proximamente">La pantalla aún no existe: se muestra deshabilitada y no entra al buscador.</param>
    /// <param name="Palabras">Otras palabras con las que el buscador la encuentra.</param>
    /// <param name="Color">Color propio de la tarjeta en el centro del módulo; si falta, el del módulo.</param>
    /// <param name="RutasQueLaMuestran">Para un centro intermedio que no pide permiso propio: las pantallas a las que lleva. La entrada se muestra
    /// solo a quien puede abrir alguna; si falta, decide la propia <paramref name="Ruta"/>.</param>
    public sealed record PantallaDeNavegacion(
        string Ruta,
        string Titulo,
        string Descripcion,
        string Icono,
        string? Seccion = null,
        bool Proximamente = false,
        string? Palabras = null,
        string? Color = null,
        IReadOnlyList<string>? RutasQueLaMuestran = null);

    /// <summary>Un módulo (o un bloque de administración) con sus pantallas.</summary>
    /// <param name="Clave">Identificador estable (<c>flota</c>, <c>empresa</c>): lo usan los filtros del hub y el menú activo.</param>
    /// <param name="Titulo">Nombre del módulo.</param>
    /// <param name="Descripcion">Una línea que dice qué contiene.</param>
    /// <param name="Icono">Clase de Font Awesome.</param>
    /// <param name="Color">Color del icono (hexadecimal).</param>
    /// <param name="Grupo">Operación, Empresa o Plataforma.</param>
    /// <param name="Pantallas">Sus pantallas, en el orden del menú.</param>
    /// <param name="CodigoModulo">Código de <c>dbo.modulos</c> que la empresa debe tener activo (ADR-026); nulo en la administración.</param>
    /// <param name="PrefijoDePermiso">Si no es nulo, el bloque se muestra solo a quien tiene algún permiso con ese prefijo (<c>plataforma.</c>).</param>
    /// <param name="RutaIndice">Centro del módulo (la página índice que ya existía), si lo tiene.</param>
    /// <param name="PrefijoDeRuta">Las pantallas que empiezan con este prefijo pertenecen al módulo (para marcarlo como activo).</param>
    public sealed record ModuloDeNavegacion(
        string Clave,
        string Titulo,
        string Descripcion,
        string Icono,
        string Color,
        GrupoDeNavegacion Grupo,
        IReadOnlyList<PantallaDeNavegacion> Pantallas,
        string? CodigoModulo = null,
        string? PrefijoDePermiso = null,
        string? RutaIndice = null,
        string? PrefijoDeRuta = null)
    {
        /// <summary>True si el módulo no tiene ninguna pantalla lista todavía (Bancos, Contabilidad…).</summary>
        public bool Proximamente => Pantallas.All(p => p.Proximamente);

        /// <summary>Las pantallas listas (las que entran al buscador y llevan a algún lado).</summary>
        public IEnumerable<PantallaDeNavegacion> PantallasListas => Pantallas.Where(p => !p.Proximamente);

        /// <summary>Las secciones del módulo en orden (una sola sin nombre si el módulo no se divide).</summary>
        public IEnumerable<IGrouping<string?, PantallaDeNavegacion>> Secciones => Pantallas.GroupBy(p => p.Seccion);

        /// <summary>True si la pantalla <paramref name="ruta"/> es de este módulo.</summary>
        public bool Contiene(string? ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return false;
            if (PrefijoDeRuta != null && ruta.StartsWith(PrefijoDeRuta, StringComparison.OrdinalIgnoreCase)) return true;
            if (RutaIndice != null && string.Equals(ruta, RutaIndice, StringComparison.OrdinalIgnoreCase)) return true;
            return Pantallas.Any(p => !p.Proximamente && EsDeLaPantalla(ruta, p.Ruta));
        }

        /// <summary>La ruta es la pantalla o una de sus hermanas de carpeta (<c>/Admin/Invitaciones/Create</c> es de <c>/Admin/Invitaciones/Index</c>).</summary>
        internal static bool EsDeLaPantalla(string ruta, string pantalla)
        {
            if (string.Equals(ruta, pantalla, StringComparison.OrdinalIgnoreCase)) return true;
            var i = pantalla.LastIndexOf('/');
            if (i <= 0 || !pantalla.EndsWith("/Index", StringComparison.OrdinalIgnoreCase)) return false;
            return ruta.StartsWith(pantalla[..(i + 1)], StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// El catálogo ÚNICO de navegación (plan secundario del menú configurable, etapa 2). Todos los estilos de menú (hub, barra lateral y barra
    /// superior), el buscador Ctrl+K y los centros de Catálogos y Psicología salen de aquí: un módulo nuevo se registra en un solo lugar y aparece
    /// en todos. Esta lista es la definición completa; <see cref="CatalogoDeNavegacion"/> la recorta para cada petición con los módulos activos
    /// de la empresa y los permisos de la persona (ADR-026, ADR-029).
    /// </summary>
    public static class ModeloNavegacion
    {
        public static readonly IReadOnlyList<ModuloDeNavegacion> Modulos = new[]
        {
            // ── Operación ─────────────────────────────────────────────────────────────────────────────────────────────────────
            new ModuloDeNavegacion("flota", "Control de Flota", "Vehículos, operación, gastos y KPIs", "fa-truck", "#6610f2", GrupoDeNavegacion.Operacion,
                new[]
                {
                    new PantallaDeNavegacion("/Flota/KPI/Dashboard", "Dashboard KPI", "Costo por kilómetro por vehículo y período", "fa-chart-bar", "Análisis", Palabras: "kpi indicadores costo km"),
                    new PantallaDeNavegacion("/Flota/KPI/Graficos", "Gráficos de flota", "Tendencias y comparativos", "fa-chart-line", "Análisis"),
                    new PantallaDeNavegacion("/Flota/Operacion/ControlSalidas/Index", "Control de salidas", "Salidas y entradas de garita", "fa-door-open", "Operación diaria", Palabras: "garita entradas"),
                    new PantallaDeNavegacion("/Flota/Operacion/CargasCombustible/Index", "Cargas de combustible", "Galones y odómetro", "fa-gas-pump", "Operación diaria", Palabras: "gasolina diesel"),
                    new PantallaDeNavegacion("/Flota/Operacion/SalariosDiarios/Index", "Salarios diarios", "Motoristas y ayudantes", "fa-money-bill-wave", "Operación diaria"),
                    new PantallaDeNavegacion("/Flota/Operacion/Peajes/Index", "Peajes", "Casetas por ruta", "fa-road", "Operación diaria"),
                    new PantallaDeNavegacion("/Flota/Gastos/Repuestos/Index", "Repuestos", "Compras de repuestos por vehículo", "fa-cogs", "Gastos periódicos"),
                    new PantallaDeNavegacion("/Flota/Gastos/Mantenimiento/Index", "Mantenimiento", "Servicios y reparaciones", "fa-wrench", "Gastos periódicos"),
                    new PantallaDeNavegacion("/Flota/Gastos/Seguros/Index", "Seguros", "Pólizas y primas", "fa-shield-alt", "Gastos periódicos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/Vehiculos/Index", "Vehículos", "La flota activa", "fa-truck-moving", "Catálogos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/Personas/Index", "Personal", "Motoristas, ayudantes y su historial", "fa-id-card", "Catálogos", Palabras: "conductores motoristas"),
                    new PantallaDeNavegacion("/Flota/Catalogos/TiposVehiculo/Index", "Tipos de vehículo", "Clasificación de la flota", "fa-shapes", "Mantenimiento de datos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/Rutas/Index", "Rutas", "Rutas y destinos", "fa-route", "Mantenimiento de datos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/Talleres/Index", "Talleres", "Talleres y proveedores de servicio", "fa-warehouse", "Mantenimiento de datos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/CategoriasRepuesto/Index", "Categorías de repuesto", "Clasificación de repuestos", "fa-tags", "Mantenimiento de datos"),
                    new PantallaDeNavegacion("/Flota/Catalogos/Cargos/Index", "Cargos", "Puestos del personal", "fa-user-tie", "Mantenimiento de datos"),
                },
                CodigoModulo: "flota", RutaIndice: "/Flota/Index", PrefijoDeRuta: "/Flota/"),

            new ModuloDeNavegacion("facturacion", "Facturación", "Facturas, notas de crédito/débito, pagos y CxC", "fa-file-invoice-dollar", "#1B63C9", GrupoDeNavegacion.Operacion,
                new[]
                {
                    new PantallaDeNavegacion("/Facturacion/Index", "Facturas", "Contado y crédito, con filtros por estado", "fa-file-invoice"),
                    new PantallaDeNavegacion("/Facturacion/Create", "Nueva factura", "Emitir un documento fiscal", "fa-plus-circle", Palabras: "emitir crear"),
                    new PantallaDeNavegacion("/Facturacion/Pagos/Index", "Pagos", "Recibos y abonos aplicados", "fa-hand-holding-usd", Palabras: "recibos abonos cobros"),
                    new PantallaDeNavegacion("/Facturacion/CxC/Index", "Cuentas por cobrar", "Saldos y antigüedad de cartera", "fa-balance-scale", Palabras: "cxc cartera saldos"),
                    new PantallaDeNavegacion("/Facturacion/Notas/Index", "Notas de crédito / débito", "Ajustes sobre facturas emitidas", "fa-file-medical"),
                },
                CodigoModulo: "facturacion", RutaIndice: "/Facturacion/Index", PrefijoDeRuta: "/Facturacion/"),

            new ModuloDeNavegacion("catalogos", "Catálogos", "Clientes, proveedores, productos, impuestos y formas de pago", "fa-book", "#1e293b", GrupoDeNavegacion.Operacion,
                new[]
                {
                    new PantallaDeNavegacion("/Catalogos/Clientes/Index", "Clientes", "Catálogo de clientes y su información fiscal", "fa-user-tie", Palabras: "rtn", Color: "#0d6efd"),
                    new PantallaDeNavegacion("/Catalogos/TiposCambio/Index", "Tipos de cambio", "Dólar y euro en lempiras: tasas vigentes, histórico y bitácora", "fa-exchange-alt", Palabras: "dolar euro tasa", Color: "#6c757d"),
                    new PantallaDeNavegacion("/Catalogos/Proveedores/Index", "Proveedores", "Catálogo de proveedores y datos de pago", "fa-truck-loading", Proximamente: true, Color: "#fd7e14"),
                    new PantallaDeNavegacion("/Catalogos/Productos/Index", "Productos / Servicios", "Bienes y servicios facturables, precios e impuestos", "fa-boxes-stacked", Proximamente: true, Color: "#198754"),
                    new PantallaDeNavegacion("/Catalogos/Impuestos/Index", "Impuestos", "ISV, retenciones y otros tributos configurables", "fa-percent", Proximamente: true, Color: "#dc3545"),
                    new PantallaDeNavegacion("/Catalogos/FormasPago/Index", "Formas de pago", "Efectivo, cheque, transferencia, tarjeta", "fa-money-bill", Proximamente: true, Color: "#20c997"),
                    new PantallaDeNavegacion("/Catalogos/CondicionesPago/Index", "Condiciones de pago", "Contado, crédito 15/30/60 días, planes de cuotas", "fa-calendar-check", Proximamente: true, Color: "#6610f2"),
                },
                CodigoModulo: "catalogos", RutaIndice: "/Catalogos/Index", PrefijoDeRuta: "/Catalogos/"),

            new ModuloDeNavegacion("psicologia", "Psicología", "Pacientes, jornadas y brigadas, historial clínico e impresión", "fa-brain", "#6f42c1", GrupoDeNavegacion.Operacion,
                new[]
                {
                    new PantallaDeNavegacion("/Psicologia/Pacientes/Index", "Pacientes", "Expedientes, datos del paciente y su historial de atenciones", "fa-user-injured", "Atención", Palabras: "expedientes", Color: "#0d6efd"),
                    new PantallaDeNavegacion("/Psicologia/Jornadas/Index", "Jornadas y brigadas", "Eventos de atención grupal y su responsable", "fa-users", "Atención", Color: "#198754"),
                    new PantallaDeNavegacion("/Psicologia/Importar/Subir", "Importar pacientes", "Cargar el listado de una brigada desde Excel en tres pasos", "fa-file-import", "Atención", Palabras: "excel", Color: "#fd7e14"),
                    new PantallaDeNavegacion("/Psicologia/Constancias/Index", "Constancias", "Libro de constancias de examen psicológico por número", "fa-file-signature", "Atención", Color: "#d63384"),
                    new PantallaDeNavegacion("/Psicologia/Catalogos/Index", "Catálogos", "Pruebas psicológicas y tipos de constancia del centro", "fa-clipboard-list", "Configuración", Palabras: "pruebas tipos de constancia", Color: "#20c997",
                        RutasQueLaMuestran: new[] { "/Psicologia/Catalogos/Pruebas", "/Psicologia/Catalogos/TiposConstancia" }),
                    new PantallaDeNavegacion("/Psicologia/Profesionales/Index", "Profesionales", "Psicólogos con su usuario, responsables de las brigadas", "fa-user-doctor", "Configuración", Color: "#6f42c1"),
                },
                CodigoModulo: "psicologia", RutaIndice: "/Psicologia/Index", PrefijoDeRuta: "/Psicologia/"),

            new ModuloDeNavegacion("bancos", "Bancos", "Cuentas bancarias, depósitos, cheques y conciliación", "fa-university", "#0dcaf0", GrupoDeNavegacion.Operacion,
                Array.Empty<PantallaDeNavegacion>(), CodigoModulo: "bancos"),
            new ModuloDeNavegacion("contabilidad", "Contabilidad", "Plan de cuentas, asientos y estados financieros", "fa-calculator", "#6f42c1", GrupoDeNavegacion.Operacion,
                Array.Empty<PantallaDeNavegacion>(), CodigoModulo: "contabilidad"),
            new ModuloDeNavegacion("inventario", "Inventario", "Control de productos, stock y movimientos", "fa-boxes", "#198754", GrupoDeNavegacion.Operacion,
                Array.Empty<PantallaDeNavegacion>(), CodigoModulo: "inventario"),
            new ModuloDeNavegacion("ventas", "Ventas", "Registro y seguimiento de ventas realizadas", "fa-shopping-cart", "#0dcaf0", GrupoDeNavegacion.Operacion,
                Array.Empty<PantallaDeNavegacion>(), CodigoModulo: "ventas"),
            new ModuloDeNavegacion("reportes", "Reportes", "Genera reportes y análisis de datos", "fa-chart-pie", "#F5A524", GrupoDeNavegacion.Operacion,
                Array.Empty<PantallaDeNavegacion>(), CodigoModulo: "reportes"),

            // ── Administración de la empresa ──────────────────────────────────────────────────────────────────────────────────
            new ModuloDeNavegacion("empresa", "Administración de la empresa", "Accesos de las personas de tu empresa", "fa-building", "#0ea5e9", GrupoDeNavegacion.Empresa,
                new[]
                {
                    new PantallaDeNavegacion("/Admin/Invitaciones/Index", "Invitaciones", "Invita por correo a las personas de tu empresa", "fa-envelope"),
                    new PantallaDeNavegacion("/Admin/RolesPermisos/Index", "Roles y permisos", "Crea, edita y gestiona roles por empresa", "fa-user-tag", Palabras: "seguridad accesos"),
                    new PantallaDeNavegacion("/Admin/Auditoria/Index", "Auditoría", "Quién cambió usuarios, roles y permisos, y los accesos denegados de tu empresa", "fa-clipboard-list", Palabras: "bitacora historial seguridad"),
                },
                PrefijoDePermiso: "administracion."),

            // ── Administración de la plataforma ───────────────────────────────────────────────────────────────────────────────
            new ModuloDeNavegacion("sistema", "Administración del sistema", "Operación de la plataforma multitenant", "fa-tools", "#475569", GrupoDeNavegacion.Plataforma,
                new[]
                {
                    new PantallaDeNavegacion("/Admin/Suscriptores/Index", "Suscriptores", "Estado y plan de cada cliente de la plataforma", "fa-id-card", Palabras: "tenant cliente plan suspender"),
                    new PantallaDeNavegacion("/Empresas/Index", "Empresas", "Alta, baja y modificación de empresas", "fa-building"),
                    new PantallaDeNavegacion("/Admin/Soporte/Index", "Soporte por tenant", "Elige el tenant con el que trabajas", "fa-life-ring", Palabras: "tenant soporte"),
                    new PantallaDeNavegacion("/Admin/Personas/Index", "Personas del sistema", "Las personas del tenant de soporte, con sus empresas y roles", "fa-address-book"),
                    new PantallaDeNavegacion("/Admin/Modulos/Index", "Módulos", "Catálogo de módulos disponibles", "fa-puzzle-piece"),
                    new PantallaDeNavegacion("/Admin/Outbox/Index", "Outbox / Bus de eventos", "Eventos de dominio publicados por los módulos", "fa-stream"),
                    new PantallaDeNavegacion("/Admin/AuditoriaPlataforma/Index", "Auditoría de la plataforma", "Entradas, fallos, bloqueos, soporte y acciones del personal de la plataforma", "fa-shield-alt", Palabras: "bitacora historial seguridad"),
                    new PantallaDeNavegacion("/Admin/EmailConfig", "Correo corporativo", "Servidor SMTP y notificaciones", "fa-envelope-open-text", Palabras: "smtp email"),
                    new PantallaDeNavegacion("/UserManagement", "Gestión de usuarios", "Administra usuarios, roles y permisos", "fa-users"),
                },
                PrefijoDePermiso: "plataforma."),
        };

        /// <summary>El módulo de la definición con esa clave, o nulo.</summary>
        public static ModuloDeNavegacion? Modulo(string clave)
            => Modulos.FirstOrDefault(m => string.Equals(m.Clave, clave, StringComparison.Ordinal));

        /// <summary>Nombre visible de un grupo.</summary>
        public static string NombreDelGrupo(GrupoDeNavegacion grupo) => grupo switch
        {
            GrupoDeNavegacion.Empresa => "Empresa",
            GrupoDeNavegacion.Plataforma => "Plataforma",
            _ => "Operación",
        };
    }
}
