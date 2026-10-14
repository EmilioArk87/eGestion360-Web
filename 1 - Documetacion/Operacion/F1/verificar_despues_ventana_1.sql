-- ============================================================
-- Archivo  : verificar_despues_ventana_1.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de la ventana 1 (scripts 025 a 030) y ANTES de
--            la ventana 2 (031 a 035). Es tambien la "vigilancia diaria" de la semana de espera: las filas sin tenant tienen
--            que dar 0 todos los dias (el script 031 lo vuelve a exigir antes de contraer).
--            Comprueba: los 4 tenants (SIP interno, DEMO demo, IISE suspendido, TRANSGAR cliente) y que cada una de las 4
--            empresas cuelga del suyo; que las 41 tablas tienen id_tenant (nulable, fase "expandir"), su clave foranea a
--            saas.tenants (FK_<tabla>_tenants) y su indice IX_<tabla>_tenant; y, tabla por tabla, que no queda ninguna fila
--            sin tenant ni con un tenant distinto del de su empresa. Las 2 tablas mixtas (tasas_cambio y sen_boletines)
--            pueden tener filas globales con id_tenant nulo (id_empresa nulo): eso es correcto y solo se informa.
--            Tambien exige el estado EXACTO de esta ventana: todavia no hay claves compuestas FKT_, ni claves unicas
--            con id_tenant, ni politicas; por eso da FALLA despues de aplicar 031 en adelante.
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME.md). No escribe nada.
-- Nota     : las cuentas de filas por tabla no nombran la columna id_tenant: leen la fila como JSON y buscan la propiedad.
--            Asi el archivo se puede correr tambien ANTES de la ventana (cuando la columna aun no existe) y da FALLA
--            en lugar de un error de compilacion. Con las tablas de hoy (menos de 10 000 filas) el costo es de segundos.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

DECLARE @tn_filas INT, @tn_internos INT,
        @tn_sip NVARCHAR(100), @tn_demo NVARCHAR(100), @tn_iise NVARCHAR(100), @tn_transgar NVARCHAR(100),
        @emp1 NVARCHAR(100), @emp2 NVARCHAR(100), @emp3 NVARCHAR(100), @emp4 NVARCHAR(100);

-- Los tenants se leen solo si saas.tenants existe (antes de 025 no existe y la comprobacion da FALLA).
IF OBJECT_ID(N'saas.tenants', N'U') IS NOT NULL
BEGIN
    SELECT @tn_filas = COUNT(*),
           @tn_internos = COALESCE(SUM(CASE WHEN tipo = N'interno' THEN 1 ELSE 0 END), 0),
           @tn_sip      = MAX(CASE WHEN codigo = N'SIP'      THEN CONCAT(id_tenant, N'/', tipo, N'/', estado) END),
           @tn_demo     = MAX(CASE WHEN codigo = N'DEMO'     THEN CONCAT(id_tenant, N'/', tipo, N'/', estado) END),
           @tn_iise     = MAX(CASE WHEN codigo = N'IISE'     THEN CONCAT(id_tenant, N'/', tipo, N'/', estado) END),
           @tn_transgar = MAX(CASE WHEN codigo = N'TRANSGAR' THEN CONCAT(id_tenant, N'/', tipo, N'/', estado) END)
    FROM saas.tenants;

    WITH em AS (
        SELECT e.id_empresa, TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS id_tenant
        FROM dbo.empresas e
        CROSS APPLY (SELECT e.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    )
    SELECT @emp1 = MAX(CASE WHEN em.id_empresa = 1 THEN t.codigo END),
           @emp2 = MAX(CASE WHEN em.id_empresa = 2 THEN t.codigo END),
           @emp3 = MAX(CASE WHEN em.id_empresa = 3 THEN t.codigo END),
           @emp4 = MAX(CASE WHEN em.id_empresa = 4 THEN t.codigo END)
    FROM em
    JOIN saas.tenants t ON t.id_tenant = em.id_tenant;
END;

WITH
-- Las 41 tablas con id_tenant: n, nombre y tipo (T tenant, M mixta, I infraestructura). Salen de ClasificacionDeTablas.cs.
t41 AS (
    SELECT t.n, t.nombre, t.tipo
    FROM (VALUES
        (1, N'empresas', N'T'),
        (2, N'empresa_modulos', N'T'),
        (3, N'empresa_roles', N'T'),
        (4, N'empresa_rol_permisos', N'T'),
        (5, N'cargos', N'T'),
        (6, N'personas', N'T'),
        (7, N'persona_documentos', N'T'),
        (8, N'persona_empresa', N'T'),
        (9, N'empleados', N'T'),
        (10, N'clientes', N'T'),
        (11, N'proveedores', N'T'),
        (12, N'bitacora_cambios', N'T'),
        (13, N'vehiculos', N'T'),
        (14, N'tipos_vehiculo', N'T'),
        (15, N'rutas', N'T'),
        (16, N'talleres', N'T'),
        (17, N'categorias_repuesto', N'T'),
        (18, N'odometro_diario', N'T'),
        (19, N'cargas_combustible', N'T'),
        (20, N'salarios_diarios', N'T'),
        (21, N'ordenes_mantenimiento', N'T'),
        (22, N'gastos_repuestos', N'T'),
        (23, N'polizas_seguros', N'T'),
        (24, N'peajes', N'T'),
        (25, N'control_salidas', N'T'),
        (26, N'notas', N'T'),
        (27, N'ingresos_operativos', N'T'),
        (28, N'precios_combustible', N'T'),
        (29, N'condiciones_pago', N'T'),
        (30, N'condiciones_pago_cuotas', N'T'),
        (31, N'formas_pago', N'T'),
        (32, N'impuestos', N'T'),
        (33, N'productos_servicios', N'T'),
        (34, N'facturas', N'T'),
        (35, N'factura_detalle', N'T'),
        (36, N'factura_secuencias', N'T'),
        (37, N'pagos', N'T'),
        (38, N'pago_aplicaciones', N'T'),
        (39, N'tasas_cambio', N'M'),
        (40, N'sen_boletines', N'M'),
        (41, N'domain_events', N'I')
    ) t (n, nombre, tipo)
),
-- Estado de id_tenant en cada tabla, solo por catalogo (no nombra la columna): columna, FK a saas.tenants, indice y si es obligatoria o nulable.
est AS (
    SELECT t.n, t.nombre, t.tipo,
           CASE WHEN COL_LENGTH(N'dbo.' + t.nombre, N'id_tenant') IS NULL THEN 0 ELSE 1 END AS con_columna,
           CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys f
                             WHERE f.name = N'FK_' + t.nombre + N'_tenants'
                               AND f.parent_object_id = OBJECT_ID(N'dbo.' + t.nombre)
                               AND f.referenced_object_id = OBJECT_ID(N'saas.tenants')) THEN 1 ELSE 0 END AS con_fk,
           CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i
                             JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
                             JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                             WHERE i.name = N'IX_' + t.nombre + N'_tenant'
                               AND i.object_id = OBJECT_ID(N'dbo.' + t.nombre)
                               AND c.name = N'id_tenant') THEN 1 ELSE 0 END AS con_indice,
           CASE WHEN EXISTS (SELECT 1 FROM sys.columns c
                             WHERE c.object_id = OBJECT_ID(N'dbo.' + t.nombre)
                               AND c.name = N'id_tenant' AND c.is_nullable = 0) THEN 1 ELSE 0 END AS obligatoria,
           CASE WHEN EXISTS (SELECT 1 FROM sys.columns c
                             WHERE c.object_id = OBJECT_ID(N'dbo.' + t.nombre)
                               AND c.name = N'id_tenant' AND c.is_nullable = 1) THEN 1 ELSE 0 END AS nulable
    FROM t41 t
),
-- Una sola pasada sobre est: cuantas tablas cumplen cada condicion y cuales no.
ag AS (
    SELECT SUM(con_columna) AS col_ok,
           COUNT(*) AS col_n,
           LEFT(STRING_AGG(CASE WHEN con_columna = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS col_mal,
           SUM(con_fk) AS fk_ok,
           COUNT(*) AS fk_n,
           LEFT(STRING_AGG(CASE WHEN con_fk = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS fk_mal,
           SUM(con_indice) AS ix_ok,
           COUNT(*) AS ix_n,
           LEFT(STRING_AGG(CASE WHEN con_indice = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS ix_mal,
           SUM(nulable) AS nul_ok,
           COUNT(*) AS nul_n,
           LEFT(STRING_AGG(CASE WHEN nulable = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS nul_mal
    FROM est
),
-- Tenant de cada empresa, leido sin nombrar la columna (JSON de la fila).
em AS (
    SELECT e.id_empresa, TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS id_tenant
    FROM dbo.empresas e
    CROSS APPLY (SELECT e.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
),
-- Por tabla: filas, filas sin tenant y filas con un tenant distinto del de su empresa (en las mixtas, las globales tienen id_empresa nulo).
dt AS (
    SELECT 1 AS n, N'empresas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.empresas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 2 AS n, N'empresa_modulos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.empresa_modulos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 3 AS n, N'empresa_roles' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.empresa_roles x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 4 AS n, N'empresa_rol_permisos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.empresa_rol_permisos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 5 AS n, N'cargos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.cargos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 6 AS n, N'personas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.personas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 7 AS n, N'persona_documentos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.persona_documentos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 8 AS n, N'persona_empresa' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.persona_empresa x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 9 AS n, N'empleados' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.empleados x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 10 AS n, N'clientes' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.clientes x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 11 AS n, N'proveedores' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.proveedores x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 12 AS n, N'bitacora_cambios' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.bitacora_cambios x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 13 AS n, N'vehiculos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.vehiculos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 14 AS n, N'tipos_vehiculo' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.tipos_vehiculo x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 15 AS n, N'rutas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.rutas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 16 AS n, N'talleres' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.talleres x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 17 AS n, N'categorias_repuesto' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.categorias_repuesto x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 18 AS n, N'odometro_diario' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.odometro_diario x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 19 AS n, N'cargas_combustible' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.cargas_combustible x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 20 AS n, N'salarios_diarios' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.salarios_diarios x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 21 AS n, N'ordenes_mantenimiento' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.ordenes_mantenimiento x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 22 AS n, N'gastos_repuestos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.gastos_repuestos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 23 AS n, N'polizas_seguros' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.polizas_seguros x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 24 AS n, N'peajes' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.peajes x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 25 AS n, N'control_salidas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.control_salidas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 26 AS n, N'notas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.notas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 27 AS n, N'ingresos_operativos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.ingresos_operativos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 28 AS n, N'precios_combustible' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.precios_combustible x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 29 AS n, N'condiciones_pago' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.condiciones_pago x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 30 AS n, N'condiciones_pago_cuotas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.condiciones_pago_cuotas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 31 AS n, N'formas_pago' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.formas_pago x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 32 AS n, N'impuestos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.impuestos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 33 AS n, N'productos_servicios' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.productos_servicios x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 34 AS n, N'facturas' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.facturas x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 35 AS n, N'factura_detalle' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.factura_detalle x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 36 AS n, N'factura_secuencias' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.factura_secuencias x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 37 AS n, N'pagos' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.pagos x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 38 AS n, N'pago_aplicaciones' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           0 AS incoherentes,
           0 AS globales
    FROM dbo.pago_aplicaciones x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT 39 AS n, N'tasas_cambio' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN x.id_empresa IS NOT NULL AND j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN (x.id_empresa IS NULL AND j.t IS NOT NULL)
                               OR (j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t) THEN 1 ELSE 0 END), 0) AS incoherentes,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS globales
    FROM dbo.tasas_cambio x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 40 AS n, N'sen_boletines' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN x.id_empresa IS NOT NULL AND j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN (x.id_empresa IS NULL AND j.t IS NOT NULL)
                               OR (j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t) THEN 1 ELSE 0 END), 0) AS incoherentes,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS globales
    FROM dbo.sen_boletines x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
    UNION ALL
    SELECT 41 AS n, N'domain_events' AS tabla, COUNT(*) AS filas,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL AND em.id_tenant IS NOT NULL AND em.id_tenant <> j.t THEN 1 ELSE 0 END), 0) AS incoherentes,
           0 AS globales
    FROM dbo.domain_events x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    LEFT JOIN em ON em.id_empresa = x.id_empresa
),
c AS (
    SELECT (SELECT COUNT(*) FROM sys.schemas WHERE name = N'saas') AS esq_saas,
           (SELECT COUNT(*) FROM sys.schemas WHERE name = N'seg') AS esq_seg,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'saas') AND parent_object_id = 0) AS obj_saas,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg') AND parent_object_id = 0) AS obj_seg,
           (SELECT COUNT(*) FROM sys.columns c INNER JOIN sys.tables tb ON tb.object_id = c.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
            WHERE c.name = N'id_tenant') AS cols_dbo,
           (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) AS tablas_dbo,
           (SELECT COUNT(*) FROM sys.foreign_keys) AS fks,
           (SELECT COUNT(*) FROM sys.foreign_keys WHERE name LIKE N'FKT[_]%') AS fkt,
           (SELECT COUNT(*) FROM sys.indexes i
              INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              INNER JOIN sys.tables tb ON tb.object_id = i.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
            WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND c.name = N'id_tenant') AS claves_unicas_tenant,
           (SELECT COUNT(*) FROM sys.security_policies) AS politicas
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM c
    CROSS JOIN ag
    CROSS APPLY (VALUES
        (110, N'Esquema saas existe',                                             N'1', CAST(c.esq_saas AS NVARCHAR(400))),
        (120, N'Esquema seg existe',                                              N'1', CAST(c.esq_seg AS NVARCHAR(400))),
        (130, N'Objetos propios del esquema saas (solo la tabla tenants)',        N'1', CAST(c.obj_saas AS NVARCHAR(400))),
        (140, N'Objetos propios del esquema seg (vacio hasta el script 036)',     N'0', CAST(c.obj_seg AS NVARCHAR(400))),
        (150, N'Filas de saas.tenants',                                           N'4', COALESCE(CAST(@tn_filas AS NVARCHAR(400)), N'no existe saas.tenants')),
        (160, N'Tenant SIP (id/tipo/estado)',                                     N'1/interno/activo',     COALESCE(@tn_sip, N'no existe')),
        (161, N'Tenant DEMO (id/tipo/estado)',                                    N'2/demo/activo',        COALESCE(@tn_demo, N'no existe')),
        (162, N'Tenant IISE (id/tipo/estado)',                                    N'3/cliente/suspendido', COALESCE(@tn_iise, N'no existe')),
        (163, N'Tenant TRANSGAR (id/tipo/estado)',                                N'4/cliente/activo',     COALESCE(@tn_transgar, N'no existe')),
        (170, N'Tenants de tipo interno (como mucho uno: la plataforma)',         N'1', COALESCE(CAST(@tn_internos AS NVARCHAR(400)), N'no existe saas.tenants')),
        (180, N'Empresa 1 (SIP Tecnologia) cuelga del tenant',                    N'SIP',      COALESCE(@emp1, N'sin tenant')),
        (181, N'Empresa 2 (Demo) cuelga del tenant',                              N'DEMO',     COALESCE(@emp2, N'sin tenant')),
        (182, N'Empresa 3 (IISE) cuelga del tenant',                              N'IISE',     COALESCE(@emp3, N'sin tenant')),
        (183, N'Empresa 4 (Transgar) cuelga del tenant',                          N'TRANSGAR', COALESCE(@emp4, N'sin tenant')),
        (200, N'Columna id_tenant en las 41 tablas', N'41 de 41', CONCAT(ag.col_ok, N' de ', ag.col_n, CASE WHEN ag.col_ok < ag.col_n THEN CONCAT(N' (fallan: ', ag.col_mal, N')') ELSE N'' END)),
        (210, N'Clave foranea FK_<tabla>_tenants hacia saas.tenants en las 41 tablas', N'41 de 41', CONCAT(ag.fk_ok, N' de ', ag.fk_n, CASE WHEN ag.fk_ok < ag.fk_n THEN CONCAT(N' (fallan: ', ag.fk_mal, N')') ELSE N'' END)),
        (220, N'Indice IX_<tabla>_tenant (primera columna id_tenant) en las 41 tablas', N'41 de 41', CONCAT(ag.ix_ok, N' de ', ag.ix_n, CASE WHEN ag.ix_ok < ag.ix_n THEN CONCAT(N' (fallan: ', ag.ix_mal, N')') ELSE N'' END)),
        (230, N'id_tenant nulable en las 41 tablas (fase expandir; 031 a 035 la vuelven obligatoria)', N'41 de 41', CONCAT(ag.nul_ok, N' de ', ag.nul_n, CASE WHEN ag.nul_ok < ag.nul_n THEN CONCAT(N' (fallan: ', ag.nul_mal, N')') ELSE N'' END)),
        (240, N'Columnas id_tenant en el esquema dbo (solo las 41 tablas)',       N'41',  CAST(c.cols_dbo AS NVARCHAR(400))),
        (250, N'Tablas en el esquema dbo (sin cambios)',                          N'56',  CAST(c.tablas_dbo AS NVARCHAR(400))),
        (260, N'Claves foraneas en total (125 de hoy + 41 a saas.tenants)',       N'166', CAST(c.fks AS NVARCHAR(400))),
        (270, N'Claves foraneas compuestas FKT_ (las crea 031 a 035, aun no)',    N'0',   CAST(c.fkt AS NVARCHAR(400))),
        (280, N'Claves unicas que empiezan por id_tenant (las crea 031 a 035, aun no)', N'0', CAST(c.claves_unicas_tenant AS NVARCHAR(400))),
        (290, N'Politicas de seguridad (RLS; las crea 036, aun no)',              N'0',   CAST(c.politicas AS NVARCHAR(400)))
    ) v (orden, comprobacion, esperado, obtenido)
    UNION ALL
    SELECT 300 + n,
           CONCAT(N'Datos ', tabla, N': ', filas, N' filas', CASE WHEN globales > 0 THEN CONCAT(N', ', globales, N' globales') ELSE N'' END),
           N'0 sin tenant, 0 incoherentes',
           CONCAT(sin_tenant, N' sin tenant, ', incoherentes, N' incoherentes')
    FROM dt
),
f AS (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN esperado = obtenido THEN 1 ELSE 0 END AS ok
    FROM r
)
SELECT z.comprobacion, z.esperado, z.obtenido, z.estado
FROM (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN ok = 1 THEN N'OK' ELSE N'FALLA' END AS estado
    FROM f
    UNION ALL
    SELECT 2147483647, N'RESUMEN', N'0 FALLA', CONCAT(SUM(ok), N' OK, ', COUNT(*) - SUM(ok), N' FALLA'),
           CASE WHEN SUM(ok) = COUNT(*) THEN N'OK' ELSE N'FALLA' END
    FROM f
) z
ORDER BY z.orden;
