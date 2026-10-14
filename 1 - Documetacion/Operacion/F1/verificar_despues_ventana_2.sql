-- ============================================================
-- Archivo  : verificar_despues_ventana_2.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de la ventana 2 (scripts 031 a 036): tablas contraidas
--            y politica de seguridad creada pero APAGADA. Comprueba:
--              * id_tenant NOT NULL en las 39 tablas que no son mixtas y nulable en las 2 mixtas (tasas_cambio y sen_boletines);
--              * las 18 claves unicas (id_tenant, ...) y la clave unica de identidad de persona_documentos con id_tenant primero;
--              * las 78 claves foraneas compuestas FKT_ (con id_tenant en primer lugar, confiables y habilitadas), que las 4
--                relaciones de borrado "poner en nulo" pasaron a "sin accion" y que se conservan las 6 de borrado en cascada;
--              * ninguna clave foranea simple remanente entre tablas de tenant (las 92 simples de hoy son las 78 compuestas,
--                porque 14 estaban repetidas) y las 41 claves a saas.tenants intactas: 125 + 41 + 78 - 92 = 152 en total;
--              * la politica seg.pol_tenant con sus 199 predicados (39 de filtro y 160 de bloqueo sobre 40 tablas), las 3
--                funciones seg.fn_*, SCHEMABINDING, domain_events fuera de la politica y is_enabled = 0 (APAGADA).
--            Es el estado EXACTO de esta ventana: despues de 037 da FALLA en "politica apagada", y antes de 031 da FALLA en casi todo.
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME.md). No escribe nada.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

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
           SUM(CASE WHEN tipo <> N'M' THEN obligatoria ELSE 0 END) AS obl_ok,
           SUM(CASE WHEN tipo <> N'M' THEN 1 ELSE 0 END) AS obl_n,
           LEFT(STRING_AGG(CASE WHEN tipo <> N'M' AND obligatoria = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS obl_mal,
           SUM(CASE WHEN tipo = N'M' THEN nulable ELSE 0 END) AS nul_ok,
           SUM(CASE WHEN tipo = N'M' THEN 1 ELSE 0 END) AS nul_n,
           LEFT(STRING_AGG(CASE WHEN tipo = N'M' AND nulable = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY n), 400) AS nul_mal
    FROM est
),
ux AS (
    SELECT SUM(x.existe) AS ok, COUNT(*) AS n,
           LEFT(STRING_AGG(CASE WHEN x.existe = 0 THEN CAST(x.nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY x.nombre), 400) AS mal
    FROM (
        SELECT v.tabla, v.nombre,
               CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i
                                 JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
                                 JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                                 WHERE i.name = v.nombre AND i.object_id = OBJECT_ID(N'dbo.' + v.tabla)
                                   AND i.is_unique = 1 AND i.is_primary_key = 0 AND c.name = N'id_tenant') THEN 1 ELSE 0 END AS existe
        FROM (VALUES
            (N'empresas', N'UX_empresas_tenant_id_empresa'),
            (N'empresa_roles', N'UX_empresa_roles_tenant_id_rol'),
            (N'personas', N'UX_personas_tenant_id_persona'),
            (N'persona_empresa', N'UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo'),
            (N'clientes', N'UX_clientes_tenant_id_cliente'),
            (N'vehiculos', N'UX_vehiculos_tenant_id_vehiculo'),
            (N'rutas', N'UX_rutas_tenant_id_ruta'),
            (N'categorias_repuesto', N'UX_categorias_repuesto_tenant_id_categoria_repuesto'),
            (N'talleres', N'UX_talleres_tenant_id_taller'),
            (N'tipos_vehiculo', N'UX_tipos_vehiculo_tenant_id_tipo_vehiculo'),
            (N'condiciones_pago', N'UX_condiciones_pago_tenant_id_condicion_pago'),
            (N'facturas', N'UX_facturas_tenant_id_factura'),
            (N'impuestos', N'UX_impuestos_tenant_id_impuesto'),
            (N'productos_servicios', N'UX_productos_servicios_tenant_id_producto'),
            (N'formas_pago', N'UX_formas_pago_tenant_id_forma_pago'),
            (N'pagos', N'UX_pagos_tenant_id_pago'),
            (N'sen_boletines', N'UX_sen_boletines_tenant_id_sen_boletin'),
            (N'tasas_cambio', N'UX_tasas_cambio_tenant_id_tasa_cambio')
        ) v (tabla, nombre)
    ) x
),
fkt AS (
    SELECT f.object_id, f.name, f.is_disabled, f.is_not_trusted, f.delete_referential_action,
           CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns k
                             WHERE k.constraint_object_id = f.object_id AND k.constraint_column_id = 1
                               AND COL_NAME(k.parent_object_id, k.parent_column_id) = N'id_tenant'
                               AND COL_NAME(k.referenced_object_id, k.referenced_column_id) = N'id_tenant') THEN 1 ELSE 0 END AS id_tenant_primero
    FROM sys.foreign_keys f
    WHERE f.name LIKE N'FKT[_]%' AND f.schema_id = SCHEMA_ID(N'dbo')
),
simples AS (
    SELECT f.name
    FROM sys.foreign_keys f
    JOIN sys.tables hijo  ON hijo.object_id  = f.parent_object_id     AND hijo.schema_id  = SCHEMA_ID(N'dbo')
    JOIN sys.tables padre ON padre.object_id = f.referenced_object_id  AND padre.schema_id = SCHEMA_ID(N'dbo')
    WHERE hijo.name IN (SELECT nombre FROM t41) AND padre.name IN (SELECT nombre FROM t41) AND f.name NOT LIKE N'FKT[_]%'
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
           (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'saas.tenants')) AS fks_tenants,
           (SELECT COUNT(*) FROM sys.indexes i
              INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              INNER JOIN sys.tables tb ON tb.object_id = i.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
            WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND i.has_filter = 0 AND c.name = N'id_tenant') AS claves_unicas,
           (SELECT COUNT(*) FROM sys.indexes i
              INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = N'UX_persona_documentos_identidad' AND i.object_id = OBJECT_ID(N'dbo.persona_documentos')
              AND i.is_unique = 1 AND c.name = N'id_tenant') AS identidad_con_tenant,
           (SELECT COUNT(*) FROM fkt) AS fkt,
           (SELECT COUNT(*) FROM fkt WHERE id_tenant_primero = 1) AS fkt_tenant_primero,
           (SELECT COUNT(*) FROM fkt WHERE is_disabled = 1 OR is_not_trusted = 1) AS fkt_no_confiables,
           (SELECT COUNT(*) FROM fkt WHERE delete_referential_action IN (2, 3)) AS fkt_poner_nulo,
           (SELECT COUNT(*) FROM fkt WHERE delete_referential_action = 1) AS fkt_cascada,
           (SELECT COUNT(*) FROM simples) AS n_simples,
           (SELECT LEFT(STRING_AGG(CAST(name AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY name), 400) FROM simples) AS lista_simples,
           (SELECT COUNT(*) FROM sys.security_policies) AS politicas,
           (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_existe,
           (SELECT MAX(CAST(is_enabled AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_encendida,
           (SELECT MAX(CAST(is_schema_bound AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_schemabinding,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS predicados,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND predicate_type_desc = N'FILTER') AS pred_filtro,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND predicate_type_desc = N'BLOCK') AS pred_bloqueo,
           (SELECT COUNT(DISTINCT target_object_id) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS pred_tablas,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')
                                                         AND target_object_id = OBJECT_ID(N'dbo.domain_events')) AS pred_domain_events,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg') AND parent_object_id = 0
                                              AND name IN (N'fn_tenant_actual', N'fn_mixta_lectura', N'fn_mixta_escritura')) AS funciones_seg
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM c
    CROSS JOIN ag
    CROSS JOIN ux
    CROSS APPLY (VALUES
        (110, N'Esquema saas existe',                                             N'1', CAST(c.esq_saas AS NVARCHAR(400))),
        (120, N'Esquema seg existe',                                              N'1', CAST(c.esq_seg AS NVARCHAR(400))),
        (130, N'Objetos propios del esquema saas (solo la tabla tenants)',        N'1', CAST(c.obj_saas AS NVARCHAR(400))),
        (140, N'Objetos propios del esquema seg (3 funciones y la politica)',     N'4', CAST(c.obj_seg AS NVARCHAR(400))),
        (150, N'Tablas en el esquema dbo (sin cambios)',                          N'56', CAST(c.tablas_dbo AS NVARCHAR(400))),
        (160, N'Columnas id_tenant en el esquema dbo (solo las 41 tablas)',       N'41', CAST(c.cols_dbo AS NVARCHAR(400))),
        (200, N'Columna id_tenant en las 41 tablas', N'41 de 41', CONCAT(ag.col_ok, N' de ', ag.col_n, CASE WHEN ag.col_ok < ag.col_n THEN CONCAT(N' (fallan: ', ag.col_mal, N')') ELSE N'' END)),
        (210, N'Clave foranea FK_<tabla>_tenants hacia saas.tenants en las 41 tablas', N'41 de 41', CONCAT(ag.fk_ok, N' de ', ag.fk_n, CASE WHEN ag.fk_ok < ag.fk_n THEN CONCAT(N' (fallan: ', ag.fk_mal, N')') ELSE N'' END)),
        (220, N'Indice IX_<tabla>_tenant (primera columna id_tenant) en las 41 tablas', N'41 de 41', CONCAT(ag.ix_ok, N' de ', ag.ix_n, CASE WHEN ag.ix_ok < ag.ix_n THEN CONCAT(N' (fallan: ', ag.ix_mal, N')') ELSE N'' END)),
        (230, N'id_tenant NOT NULL en las 39 tablas que no son mixtas', N'39 de 39', CONCAT(ag.obl_ok, N' de ', ag.obl_n, CASE WHEN ag.obl_ok < ag.obl_n THEN CONCAT(N' (fallan: ', ag.obl_mal, N')') ELSE N'' END)),
        (240, N'id_tenant nulable en las 2 mixtas (tasas_cambio y sen_boletines)', N'2 de 2', CONCAT(ag.nul_ok, N' de ', ag.nul_n, CASE WHEN ag.nul_ok < ag.nul_n THEN CONCAT(N' (fallan: ', ag.nul_mal, N')') ELSE N'' END)),
        (310, N'Claves unicas con id_tenant primero (sin filtro; las 18 de 031 a 035)', N'18', CAST(c.claves_unicas AS NVARCHAR(400))),
        (320, N'Clave unica de identidad de persona_documentos con id_tenant primero',   N'1',  CAST(c.identidad_con_tenant AS NVARCHAR(400))),
        (330, N'Claves unicas (id_tenant, ...) por nombre',  N'18 de 18', CONCAT(ux.ok, N' de ', ux.n, CASE WHEN ux.ok < ux.n THEN CONCAT(N' (faltan: ', ux.mal, N')') ELSE N'' END)),
        (400, N'Claves foraneas compuestas FKT_',                                  N'78', CAST(c.fkt AS NVARCHAR(400))),
        (410, N'FKT_ con id_tenant como primera columna en los dos lados',         N'78', CAST(c.fkt_tenant_primero AS NVARCHAR(400))),
        (420, N'FKT_ deshabilitadas o no confiables',                              N'0',  CAST(c.fkt_no_confiables AS NVARCHAR(400))),
        (430, N'FKT_ con borrado "poner en nulo" (las 4 pasaron a "sin accion")',  N'0',  CAST(c.fkt_poner_nulo AS NVARCHAR(400))),
        (440, N'FKT_ con borrado en cascada (se conservan las 6)',                 N'6',  CAST(c.fkt_cascada AS NVARCHAR(400))),
        (450, N'Claves foraneas simples remanentes entre tablas de tenant',        N'0',  CASE WHEN c.n_simples = 0 THEN N'0' ELSE CONCAT(c.n_simples, N' (', c.lista_simples, N')') END),
        (460, N'Claves foraneas hacia saas.tenants (FK_<tabla>_tenants)',          N'41', CAST(c.fks_tenants AS NVARCHAR(400))),
        (470, N'Claves foraneas en total (125 + 41 + 78 - 92)',                    N'152', CAST(c.fks AS NVARCHAR(400))),
        (600, N'Politicas de seguridad en la base',                                N'1',  CAST(c.politicas AS NVARCHAR(400))),
        (610, N'Politica seg.pol_tenant existe',                                   N'1',  CAST(c.pol_existe AS NVARCHAR(400))),
        (620, N'Politica APAGADA (is_enabled = 0; la enciende el script 037)',     N'0',  COALESCE(CAST(c.pol_encendida AS NVARCHAR(400)), N'no existe')),
        (630, N'Politica con SCHEMABINDING',                                       N'1',  COALESCE(CAST(c.pol_schemabinding AS NVARCHAR(400)), N'no existe')),
        (640, N'Predicados de la politica',                                        N'199', CAST(c.predicados AS NVARCHAR(400))),
        (650, N'Predicados de filtro (37 tablas de tenant y 2 mixtas)',            N'39', CAST(c.pred_filtro AS NVARCHAR(400))),
        (660, N'Predicados de bloqueo (4 por cada una de las 40 tablas)',          N'160', CAST(c.pred_bloqueo AS NVARCHAR(400))),
        (670, N'Tablas cubiertas por la politica (todas menos domain_events)',     N'40', CAST(c.pred_tablas AS NVARCHAR(400))),
        (680, N'Predicados sobre domain_events (queda fuera de la politica)',      N'0',  CAST(c.pred_domain_events AS NVARCHAR(400))),
        (690, N'Funciones seg.fn_tenant_actual, fn_mixta_lectura y fn_mixta_escritura', N'3', CAST(c.funciones_seg AS NVARCHAR(400)))
    ) v (orden, comprobacion, esperado, obtenido)
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
