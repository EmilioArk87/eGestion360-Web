namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>Qué regla de aislamiento le toca a cada tabla en el modelo multi-tenant (ADR-001, ADR-013 y ADR-015).</summary>
    public enum CategoriaTabla
    {
        /// <summary>Datos de un tenant: <c>id_tenant</c> obligatorio y Row-Level Security solo por tenant.</summary>
        Tenant,

        /// <summary>Filas globales (<c>id_tenant</c> nulo) y filas propias del tenant: <c>id_tenant</c> nulable y RLS mixta.</summary>
        Mixta,

        /// <summary>Infraestructura del kernel que guarda datos de varios tenants y los procesa uno por uno: <c>id_tenant</c>
        /// obligatorio; si lleva RLS o no se decide en F1.</summary>
        Infraestructura,

        /// <summary>Catálogo de referencia igual para todos los tenants: sin <c>id_tenant</c> y sin RLS.</summary>
        Global,

        /// <summary>Plano de control, identidad global o configuración de la plataforma: sin <c>id_tenant</c> y sin RLS.</summary>
        Plataforma,

        /// <summary>Tabla que el código no usa (gemela o respaldo): el script 023 la retira, o se conserva hasta F1.</summary>
        Legado
    }

    /// <param name="Tabla">Nombre de la tabla en eBD_SPD (esquema dbo).</param>
    /// <param name="Categoria">Regla de aislamiento que le corresponde.</param>
    /// <param name="EnModeloEf">true si <c>ApplicationDbContext</c> la mapea; false si solo existe en la base.</param>
    /// <param name="EnBaseReal">true si existía en eBD_SPD el 2026-10-10 (consulta de solo lectura). Es una fecha fija: una tabla
    /// que crea un script posterior sigue en false y declara su script en <paramref name="CreadaPorScript"/>.</param>
    /// <param name="Motivo">Por qué tiene esa categoría y qué cambia en F1.</param>
    /// <param name="CreadaPorScript">Número del script que la crea cuando es POSTERIOR a la salida de F1 a producción (scripts 025 a
    /// 037), por ejemplo <c>"038"</c>; nulo en las demás. Esas tablas no entran en <see cref="ClasificacionDeTablas.DeLaSalidaDeF1"/>,
    /// que es el conjunto que recorren las verificaciones históricas de <c>1 - Documetacion/Operacion/F1</c> (41 tablas con
    /// <c>id_tenant</c>) y los scripts 026 a 037.</param>
    public sealed record TablaClasificada(string Tabla, CategoriaTabla Categoria, bool EnModeloEf, bool EnBaseReal, string Motivo,
        string? CreadaPorScript = null);

    /// <summary>
    /// Inventario de todas las tablas, con la regla de aislamiento de cada una (paso F0.6). Cubre las 60 tablas de eBD_SPD
    /// del 2026-10-10, las 6 del módulo contable que solo existen en el modelo de EF (el script 010 está en espera,
    /// ADR-014), las 9 de la identidad global que crea el script 038 (F1.5), las 5 de planes y derechos del 040 (F1.7) y las 4 de
    /// permisos del 041 (F1.6a). <see cref="ArnesAislamientoTests"/> falla si aparece una tabla sin clasificar.
    /// </summary>
    public static class ClasificacionDeTablas
    {
        private const CategoriaTabla T = CategoriaTabla.Tenant;
        private const CategoriaTabla M = CategoriaTabla.Mixta;
        private const CategoriaTabla I = CategoriaTabla.Infraestructura;
        private const CategoriaTabla G = CategoriaTabla.Global;
        private const CategoriaTabla P = CategoriaTabla.Plataforma;
        private const CategoriaTabla L = CategoriaTabla.Legado;

        public static readonly IReadOnlyList<TablaClasificada> Todas = new TablaClasificada[]
        {
            // ── Datos de los tenants ─────────────────────────────────────────────────────────────────────────────
            new("empresas", T, true, true, "Entidad legal dentro del tenant (ADR-002)."),
            new("empresa_modulos", T, true, true, "Módulos activados por empresa, debajo de los derechos del plan."),
            new("empresa_roles", T, true, true, "Roles del tenant (ADR-003)."),
            new("empresa_rol_permisos", T, true, true, "Detalle de los roles; hoy no lleva ni id_empresa."),
            new("personas", T, true, true, "Persona maestra por tenant (ADR-004)."),
            new("persona_documentos", T, true, true, "Documentos de la persona; la unicidad pasa a ser por tenant."),
            new("persona_empresa", T, true, true, "Vínculos de la persona con las empresas del mismo tenant."),
            new("empleados", T, true, true, "Ficha de empleado del vínculo."),
            new("clientes", T, true, true, "Ficha de cliente."),
            new("proveedores", T, true, true, "Ficha de proveedor."),
            new("productos_servicios", T, true, true, "Catálogo de productos del tenant."),
            new("impuestos", T, true, true, "Impuestos configurados, copiados de la plantilla del país."),
            new("formas_pago", T, true, true, "Copiadas de la plantilla del país."),
            new("condiciones_pago", T, true, true, "Copiadas de la plantilla del país."),
            new("condiciones_pago_cuotas", T, true, true, "Detalle de la condición de pago; hoy no lleva ni id_empresa."),
            new("cargos", T, true, true, "Cargos del personal."),
            new("bitacora_cambios", T, true, true, "Historial de cambios del tenant; hoy id_empresa es nulable."),
            new("facturas", T, true, true, "Facturación."),
            new("factura_detalle", T, true, true, "Detalle de la factura; hoy no lleva ni id_empresa."),
            new("factura_secuencias", T, true, true, "Correlativos con CAI; pasan al servicio de numeración en F2."),
            new("notas", T, true, true, "Notas de crédito y débito."),
            new("pagos", T, true, true, "Pagos recibidos."),
            new("pago_aplicaciones", T, true, true, "Aplicación del pago a facturas; hoy no lleva ni id_empresa."),
            new("vehiculos", T, true, true, "Flota."),
            new("tipos_vehiculo", T, true, true, "Flota."),
            new("rutas", T, true, true, "Flota."),
            new("talleres", T, true, true, "Flota."),
            new("categorias_repuesto", T, true, true, "Flota."),
            new("cargas_combustible", T, true, true, "Flota: operación."),
            new("odometro_diario", T, true, true, "Flota: operación."),
            new("control_salidas", T, true, true, "Flota: operación."),
            new("peajes", T, true, true, "Flota: operación."),
            new("salarios_diarios", T, true, true, "Flota: operación."),
            new("gastos_repuestos", T, true, true, "Flota: gastos."),
            new("ordenes_mantenimiento", T, true, true, "Flota: gastos."),
            new("polizas_seguros", T, true, true, "Flota: gastos."),
            new("ct_cuentas", T, true, false, "Contabilidad: catálogo por empresa (ADR-005); el 010 está en espera (ADR-014)."),
            new("ct_ejercicios", T, true, false, "Contabilidad; el 010 está en espera (ADR-014)."),
            new("ct_periodos", T, true, false, "Contabilidad; pasa al calendario fiscal del Core en F2 (ADR-014)."),
            new("ct_centros_costo", T, true, false, "Dimensión que sale del módulo contable al Core en F2 (ADR-014)."),
            new("ct_asientos", T, true, false, "Contabilidad; el 010 está en espera (ADR-014)."),
            new("ct_asiento_movimientos", T, true, false, "Contabilidad; el 010 está en espera (ADR-014)."),
            new("ingresos_operativos", T, false, true,
                "Tabla del KPI que nunca se conectó (0 filas, sin código); se conserva por decisión del 2026-10-10 y recibe id_tenant en F1."),
            new("precios_combustible", T, false, true,
                "Tabla del KPI que nunca se conectó (0 filas, sin código; enlaza boletines del SEN); se conserva por decisión del 2026-10-10."),

            // ── Filas globales y propias ─────────────────────────────────────────────────────────────────────────
            new("tasas_cambio", M, true, true, "Tasa oficial sin tenant y tasas propias del tenant (script 020)."),
            new("sen_boletines", M, false, true,
                "Boletines públicos de precios de combustible; hoy se guardan por empresa con SQL directo (SenCombustibleService) y pasan a fila global."),

            // ── Infraestructura del kernel ───────────────────────────────────────────────────────────────────────
            new("domain_events", I, true, true,
                "Outbox: el dispatcher reclama eventos de todos los tenants y procesa cada uno con el contexto de su tenant. Si lleva RLS se decide en F1."),

            // ── Catálogos globales ───────────────────────────────────────────────────────────────────────────────
            new("catalogo_paises", G, true, true, "ISO 3166."),
            new("catalogo_departamentos", G, true, true, "División territorial de Honduras."),
            new("catalogo_municipios", G, true, true, "División territorial de Honduras."),
            new("catalogo_tipos_documento", G, true, true, "Tipos de documento de identidad por país."),
            new("catalogo_tipos_licencia", G, true, true, "Licencias de conducir (DNVT)."),
            new("monedas", G, true, true, "ISO 4217; cada empresa activa las que usa."),
            new("modulos", G, true, true, "Catálogo de módulos; en F1 se integra al catálogo de derechos de uso."),

            // ── Plataforma ───────────────────────────────────────────────────────────────────────────────────────
            new("tenants", P, true, false, "La cuenta comercial (esquema saas, script 025, F1.1): define el tenant, así que no lleva id_tenant ni RLS. Existe desde el script 025."),
            new("planes", P, true, false, "Catálogo de planes (esquema saas, script 040, F1.7): plano de control, sin RLS (ADR-013). El código de la aplicación nunca nombra un plan: pregunta por derechos.", "040"),
            new("plan_versiones", P, true, false, "Versiones de cada plan, con vigencia (esquema saas, script 040, F1.7): plano de control, sin RLS.", "040"),
            new("derechos", P, true, false, "Catálogo de derechos de uso: modulo.*, caracteristica.* y limite.* (esquema saas, script 040, F1.7): plano de control, sin RLS.", "040"),
            new("plan_version_derechos", P, true, false, "Derechos de cada versión de plan, con el tope de los límites (esquema saas, script 040, F1.7): plano de control, sin RLS.", "040"),
            new("tenant_planes", P, true, false,
                "Plan vigente de cada tenant y su historial (esquema saas, script 040, F1.7): su id_tenant apunta a saas.tenants para saber de quién es la fila, no para filtrarla; plano de control, sin RLS.", "040"),
            new("Users", P, true, true, "Pasa a ser la cuenta de acceso global (ADR-003); empresa y rol se mueven a membresías y accesos por empresa, que serán tablas de tenant."),
            new("PasswordResetCodes", P, true, true, "Identidad: códigos de la cuenta global."),
            new("EmailConfigurations", P, true, true, "Correo de notificaciones de la plataforma."),
            new("tasas_cambio_ejecuciones", P, true, true, "Bitácora del job de la tasa oficial."),
            new("tasas_cambio_ejecuciones_detalle", P, true, true, "Detalle de la bitácora del job de la tasa oficial."),
            new("tasas_cambio_configuracion", P, true, true, "Opciones del job de la tasa oficial que edita el administrador del sistema (script 024); una sola fila."),
            new("__EFMigrationsHistory", P, false, true, "Historial de migraciones de EF."),

            // ── Identidad global con membresías (F1.5, script 038): posteriores a la salida de F1 ────────────────────
            new("accesos_empresa", T, true, false,
                "Membresía ↔ empresa ↔ rol (ADR-003, D19): tabla de TENANT con RLS; entra a seg.pol_tenant en el script 038 con las mismas funciones " +
                "(1 filtro y 4 bloqueos).", "038"),
            new("cuentas", P, true, false, "Cuenta de acceso global, una por ser humano (ADR-003, D19); sin id_tenant ni RLS: se lee antes de conocer el tenant.", "038"),
            new("cuenta_alias", P, true, false, "Nombres de usuario de transición y correos secundarios de la cuenta (D44/D45).", "038"),
            new("membresias", P, true, false, "Cuenta ↔ tenant con estado y persona del tenant; lleva id_tenant como referencia, pero es plano de control: sin RLS.", "038"),
            new("cuenta_intentos", P, true, false, "Intentos fallidos y bloqueo temporal de la cuenta (ADR-008).", "038"),
            new("cuenta_mfa", P, true, false, "Secreto TOTP protegido por la aplicación (D22).", "038"),
            new("cuenta_mfa_recuperacion", P, true, false, "Hash de los códigos de recuperación de MFA.", "038"),
            new("invitaciones", P, true, false, "Invitaciones a un tenant; solo el hash del token. Lleva id_tenant como referencia, sin RLS.", "038"),
            new("llaves_proteccion_datos", P, true, false, "Almacén de llaves de ASP.NET Core Data Protection (ADR-008, D19).", "038"),

            // ── Permisos por acción (F1.6a, script 041): posteriores a la salida de F1 ───────────────────────────────
            new("rol_permisos", T, true, false,
                "Permisos del catálogo que tiene cada rol de empresa (ADR-029): tabla de TENANT con RLS; entra a seg.pol_tenant en el script 041 con las mismas " +
                "funciones (1 filtro y 4 bloqueos). Reemplaza a empresa_rol_permisos, que se conserva hasta F1.10.", "041"),
            new("permisos", P, true, false, "Catálogo de permisos modulo.recurso.accion (esquema saas, script 041, F1.6a): plano de control, sin RLS (ADR-013).", "041"),
            new("roles_plantilla", P, true, false, "Roles de partida para tenants nuevos, tres por módulo (esquema saas, script 041, D64): plano de control, sin RLS.", "041"),
            new("roles_plantilla_permisos", P, true, false, "Permisos de cada rol plantilla, del mismo módulo que la plantilla (esquema saas, script 041): plano de control, sin RLS.", "041"),

            // ── Legado: no las usa el código (paso F0.7, script 023) ──────────────────────────────────────────────
            new("usuarios", L, false, true, "Gemela vieja de Users; 0 filas. El script 023 la mueve al esquema retirado."),
            new("usuarios_empresas", L, false, true, "Detalle de la gemela vieja de Users; 0 filas. El script 023 la mueve al esquema retirado."),
            new("paises", L, false, true, "Gemela vieja de catalogo_paises. El script 023 la mueve al esquema retirado."),
            new("EmailConfiguration", L, false, true,
                "Gemela de EmailConfigurations que solo escribían 5 procedimientos y la página /ConfigurarHostinger. El script 023 la mueve, con sus procedimientos, al esquema retirado."),
            new("respaldo_personas_018", L, false, true, "Copia de la migración 018, con datos personales. El script 023 la borra."),
            new("respaldo_personas_019", L, false, true, "Copia de la migración 019 para su reversa, con datos personales. Se conserva hasta F1."),
        };

        /// <summary>
        /// Tablas de tenant, mixtas o de infraestructura que todavía no tienen <c>id_tenant</c> en el modelo de EF. En F0 eran
        /// 43; el paso F1.2 las agregó todas y la lista quedó vacía: una tabla de tenant nueva que no nazca con la columna
        /// hace fallar la prueba. Ver <see cref="ArnesAislamientoTests"/>.
        /// </summary>
        public static readonly IReadOnlySet<string> PendientesDeIdTenant = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        /// <summary>
        /// Las tablas que ya existían en eBD_SPD al salir F1 a producción (scripts 025 a 037): las que clasifican las verificaciones
        /// históricas de <c>1 - Documetacion/Operacion/F1</c> y las que cubren los scripts 026 a 037. Las tablas posteriores
        /// (<see cref="PosterioresASalidaDeF1"/>) no entran aquí: esos archivos describen el estado exacto de su ventana y no se editan.
        /// </summary>
        public static IEnumerable<TablaClasificada> DeLaSalidaDeF1 => Todas.Where(t => t.EnBaseReal);

        /// <summary>Las tablas que crea un script posterior a la salida de F1 (038 en adelante), con su <c>CreadaPorScript</c>.</summary>
        public static IEnumerable<TablaClasificada> PosterioresASalidaDeF1 => Todas.Where(t => t.CreadaPorScript != null);

        public static bool LlevaIdTenant(CategoriaTabla categoria)
            => categoria is CategoriaTabla.Tenant or CategoriaTabla.Mixta or CategoriaTabla.Infraestructura;

        public static bool IdTenantNulable(CategoriaTabla categoria) => categoria == CategoriaTabla.Mixta;
    }
}
