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

        /// <summary>Tabla que el código no usa (gemela, respaldo o resto de scripts viejos): se decide su retiro en F0.7.</summary>
        Legado
    }

    /// <param name="Tabla">Nombre de la tabla en eBD_SPD (esquema dbo).</param>
    /// <param name="Categoria">Regla de aislamiento que le corresponde.</param>
    /// <param name="EnModeloEf">true si <c>ApplicationDbContext</c> la mapea; false si solo existe en la base.</param>
    /// <param name="EnBaseReal">true si existía en eBD_SPD el 2026-10-10 (consulta de solo lectura).</param>
    /// <param name="Motivo">Por qué tiene esa categoría y qué cambia en F1.</param>
    public sealed record TablaClasificada(string Tabla, CategoriaTabla Categoria, bool EnModeloEf, bool EnBaseReal, string Motivo);

    /// <summary>
    /// Inventario de todas las tablas, con la regla de aislamiento de cada una (paso F0.6). Cubre las 60 tablas de eBD_SPD
    /// del 2026-10-10 y las 6 del módulo contable que solo existen en el modelo de EF (el script 010 está en espera,
    /// ADR-014). <see cref="ArnesAislamientoTests"/> falla si aparece una tabla sin clasificar.
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
            new("Users", P, true, true, "Pasa a ser la cuenta de acceso global (ADR-003); empresa y rol se mueven a membresías y accesos por empresa, que serán tablas de tenant."),
            new("PasswordResetCodes", P, true, true, "Identidad: códigos de la cuenta global."),
            new("EmailConfigurations", P, true, true, "Correo de notificaciones de la plataforma."),
            new("tasas_cambio_ejecuciones", P, true, true, "Bitácora del job de la tasa oficial."),
            new("tasas_cambio_ejecuciones_detalle", P, true, true, "Detalle de la bitácora del job de la tasa oficial."),
            new("__EFMigrationsHistory", P, false, true, "Historial de migraciones de EF."),

            // ── Legado: no las usa el código; su retiro se decide en F0.7 ────────────────────────────────────────
            new("usuarios", L, false, true, "Gemela vieja de Users; 0 filas."),
            new("paises", L, false, true, "Gemela vieja de catalogo_paises."),
            new("EmailConfiguration", L, false, true, "Gemela de EmailConfigurations que solo escriben los procedimientos sp_*."),
            new("usuarios_empresas", L, false, true, "Resto de los scripts KPI; 0 filas."),
            new("ingresos_operativos", L, false, true, "Resto de los scripts KPI; 0 filas."),
            new("precios_combustible", L, false, true, "Resto de los scripts KPI; 0 filas."),
            new("respaldo_personas_018", L, false, true, "Copia de la migración 018, con datos personales."),
            new("respaldo_personas_019", L, false, true, "Copia de la migración 019 para su reversa, con datos personales."),
        };

        /// <summary>
        /// Tablas de tenant, mixtas o de infraestructura que todavía no tienen <c>id_tenant</c> en el modelo de EF. En F0 son
        /// todas; en F1 la lista solo puede bajar, hasta quedar vacía. Ver <see cref="ArnesAislamientoTests"/>.
        /// </summary>
        public static readonly IReadOnlySet<string> PendientesDeIdTenant = new HashSet<string>(StringComparer.Ordinal)
        {
            "empresas", "empresa_modulos", "empresa_roles", "empresa_rol_permisos", "personas", "persona_documentos",
            "persona_empresa", "empleados", "clientes", "proveedores", "productos_servicios", "impuestos", "formas_pago",
            "condiciones_pago", "condiciones_pago_cuotas", "cargos", "bitacora_cambios", "facturas", "factura_detalle",
            "factura_secuencias", "notas", "pagos", "pago_aplicaciones", "vehiculos", "tipos_vehiculo", "rutas", "talleres",
            "categorias_repuesto", "cargas_combustible", "odometro_diario", "control_salidas", "peajes", "salarios_diarios",
            "gastos_repuestos", "ordenes_mantenimiento", "polizas_seguros", "ct_cuentas", "ct_ejercicios", "ct_periodos",
            "ct_centros_costo", "ct_asientos", "ct_asiento_movimientos", "tasas_cambio", "domain_events",
        };

        public static bool LlevaIdTenant(CategoriaTabla categoria)
            => categoria is CategoriaTabla.Tenant or CategoriaTabla.Mixta or CategoriaTabla.Infraestructura;

        public static bool IdTenantNulable(CategoriaTabla categoria) => categoria == CategoriaTabla.Mixta;
    }
}
