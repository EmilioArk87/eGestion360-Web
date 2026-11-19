-- ============================================================
-- Archivo  : verificar_despues_045.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de aplicar el script 045 (F1.8a: auditoria de seguridad y de
--            plataforma). Comprueba:
--              * las 2 tablas nuevas (dbo.auditoria_seguridad y saas.auditoria_plataforma) con sus columnas, restricciones CHECK,
--                clave foranea a saas.tenants e indices, todo habilitado y confiable;
--              * que las dos son inmutables: un disparador INSTEAD OF UPDATE, DELETE habilitado en cada una;
--              * dbo.auditoria_seguridad: sus 5 predicados en seg.pol_tenant (1 filtro y 4 bloqueos, todos con seg.fn_tenant_actual), que
--                una conexion SIN tenant ve 0 filas aunque la tabla tenga filas reales (prueba concluyente de la RLS, solo si ya hay filas) y
--                que saas.auditoria_plataforma NO esta en la politica (plano de control);
--              * dbo.bitacora_cambios: las dos columnas nuevas (id_cuenta e id_correlacion), nulables.
--            Antes de aplicar 045 da FALLA. Esta verificacion no escribe nada: que cada evento deje su fila lo comprueban las pruebas de la
--            aplicacion y, en produccion, la tabla misma al usarse (la comprobacion diaria informa cuantas filas hay).
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME_F18.md). No escribe nada.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

DECLARE @hay_tablas BIT = CASE WHEN OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NOT NULL AND OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NOT NULL
                               THEN 1 ELSE 0 END;

DECLARE @filas_seg BIGINT, @filas_plat BIGINT, @filas_seg_visibles INT,
        @pred INT, @pred_filtro INT, @pred_fn INT, @pred_plat INT,
        @cols_bitacora INT, @cols_bitacora_nulables INT;

IF @hay_tablas = 1
BEGIN
    SELECT @filas_seg = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND p.index_id IN (0, 1);
    SELECT @filas_plat = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'saas.auditoria_plataforma') AND p.index_id IN (0, 1);
    SELECT @filas_seg_visibles = COUNT(*) FROM dbo.auditoria_seguridad;

    SELECT @pred = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad');
    SELECT @pred_filtro = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND predicate_type_desc = N'FILTER';
    SELECT @pred_fn = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND predicate_definition LIKE N'%fn_tenant_actual%';
    SELECT @pred_plat = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'saas.auditoria_plataforma');
END;

SELECT @cols_bitacora = COUNT(*), @cols_bitacora_nulables = SUM(CASE WHEN is_nullable = 1 THEN 1 ELSE 0 END)
FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name IN (N'id_cuenta', N'id_correlacion');

WITH
-- Columnas esperadas: tabla, columna y definicion 'tipo|bytes|nulo' (nvarchar(100) = 200 bytes). Las dos tablas son iguales salvo id_tenant.
col_esp AS (
    SELECT t.tabla, c.columna, CASE WHEN c.columna = N'id_tenant' THEN t.def_tenant ELSE c.def END AS def
    FROM (VALUES (N'dbo.auditoria_seguridad', N'int|4|0'), (N'saas.auditoria_plataforma', N'int|4|1')) t (tabla, def_tenant)
    CROSS JOIN (VALUES
        (N'id_evento', N'bigint|8|0'), (N'fecha_hora', N'datetime2|7|0'), (N'tipo', N'varchar|60|0'), (N'resultado', N'varchar|10|0'),
        (N'id_cuenta', N'int|4|1'), (N'usuario', N'nvarchar|200|1'), (N'es_operador', N'bit|1|0'), (N'id_tenant', N''),
        (N'id_empresa', N'int|4|1'), (N'objeto_tipo', N'varchar|40|1'), (N'objeto_id', N'nvarchar|120|1'), (N'detalle', N'nvarchar|4000|1'),
        (N'ip', N'varchar|45|1'), (N'agente', N'nvarchar|400|1'), (N'id_correlacion', N'varchar|64|0')
    ) c (columna, def)
),
col_real AS (
    SELECT (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AS tabla, c.name AS columna, CONCAT(TYPE_NAME(c.user_type_id), N'|', c.max_length, N'|', c.is_nullable) AS def
    FROM sys.objects o
    JOIN sys.columns c ON c.object_id = o.object_id
    WHERE o.type = 'U' AND o.name IN (N'auditoria_seguridad', N'auditoria_plataforma') AND SCHEMA_NAME(o.schema_id) IN (N'saas', N'dbo')
),
col_dif AS (
    SELECT COALESCE(e.tabla, r.tabla) AS tabla, COALESCE(e.columna, r.columna) AS columna
    FROM col_esp e
    FULL OUTER JOIN col_real r ON r.tabla = e.tabla AND r.columna = e.columna
    WHERE e.def IS NULL OR r.def IS NULL OR e.def <> r.def
),
chk AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'CK_auditoria_seguridad_resultado'), (N'CK_auditoria_seguridad_tipo'), (N'CK_auditoria_seguridad_detalle'),
                 (N'CK_auditoria_plataforma_resultado'), (N'CK_auditoria_plataforma_tipo'), (N'CK_auditoria_plataforma_detalle')) v (nombre)
),
fkn AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'FK_auditoria_seguridad_tenants'), (N'FK_auditoria_plataforma_tenants')) v (nombre)
),
idx AS (
    SELECT v.indice, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.indice AND i.object_id = OBJECT_ID(v.tabla)) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_fecha'), (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_tipo'),
                 (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_cuenta'), (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_correlacion'),
                 (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_fecha'), (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_tipo'),
                 (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_cuenta'), (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_tenant'),
                 (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_correlacion')) v (tabla, indice)
),
trg AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.triggers t WHERE t.name = v.nombre AND t.parent_id = OBJECT_ID(v.tabla) AND t.is_instead_of_trigger = 1 AND t.is_disabled = 0) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'TR_auditoria_seguridad_inmutable', N'dbo.auditoria_seguridad'), (N'TR_auditoria_plataforma_inmutable', N'saas.auditoria_plataforma')) v (nombre, tabla)
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM (VALUES
        -- 1xx: las tablas nuevas y sus restricciones
        (110, N'Tablas nuevas: dbo.auditoria_seguridad y saas.auditoria_plataforma', N'1', CAST(@hay_tablas AS NVARCHAR(400))),
        (120, N'Columnas de las 2 tablas nuevas (nombre, tipo, longitud y nulabilidad): diferencias', N'0',
              CASE WHEN @hay_tablas = 1 THEN CAST((SELECT COUNT(*) FROM col_dif) AS NVARCHAR(400)) ELSE N'no existen' END),
        (130, N'Restricciones CHECK de las 2 tablas (3 por tabla), habilitadas y confiables', N'6 de 6', CONCAT((SELECT SUM(ok) FROM chk), N' de ', (SELECT COUNT(*) FROM chk))),
        (140, N'Claves foraneas hacia saas.tenants (1 por tabla), habilitadas y confiables', N'2 de 2', CONCAT((SELECT SUM(ok) FROM fkn), N' de ', (SELECT COUNT(*) FROM fkn))),
        (150, N'Indices de consulta (4 en la de tenant y 5 en la de plataforma)', N'9 de 9', CONCAT((SELECT SUM(ok) FROM idx), N' de ', (SELECT COUNT(*) FROM idx))),
        -- 2xx: inmutables
        (210, N'Disparadores INSTEAD OF UPDATE, DELETE habilitados (uno por tabla)', N'2 de 2', CONCAT((SELECT SUM(ok) FROM trg), N' de ', (SELECT COUNT(*) FROM trg))),
        -- 3xx: RLS
        (310, N'Predicados de dbo.auditoria_seguridad en seg.pol_tenant (1 filtro y 4 bloqueos)', N'5', COALESCE(CAST(@pred AS NVARCHAR(400)), N'no existe')),
        (320, N'Predicados de filtro de dbo.auditoria_seguridad', N'1', COALESCE(CAST(@pred_filtro AS NVARCHAR(400)), N'no existe')),
        (330, N'Predicados de dbo.auditoria_seguridad que usan seg.fn_tenant_actual', N'5', COALESCE(CAST(@pred_fn AS NVARCHAR(400)), N'no existe')),
        (340, N'saas.auditoria_plataforma fuera de la politica (plano de control, sin RLS)', N'0', COALESCE(CAST(@pred_plat AS NVARCHAR(400)), N'no existe')),
        (350, N'RLS: dbo.auditoria_seguridad devuelve 0 filas a una conexion sin tenant', N'0', COALESCE(CAST(@filas_seg_visibles AS NVARCHAR(400)), N'no existe')),
        (360, N'Cobertura: si la tabla de tenant tiene filas reales, la politica las oculta (prueba concluyente)', N'1',
              CASE WHEN @hay_tablas = 0 THEN N'0' WHEN COALESCE(@filas_seg, 0) = 0 THEN N'1' WHEN @filas_seg_visibles = 0 THEN N'1' ELSE N'0' END),
        -- 4xx: la bitacora de cambios
        (410, N'dbo.bitacora_cambios: columnas nuevas id_cuenta e id_correlacion', N'2', CAST(COALESCE(@cols_bitacora, 0) AS NVARCHAR(400))),
        (420, N'dbo.bitacora_cambios: las dos columnas nuevas son nulables', N'2', CAST(COALESCE(@cols_bitacora_nulables, 0) AS NVARCHAR(400)))
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

-- Informacion (no es una comprobacion): cuantas filas de auditoria hay y de que tipos (conteo real, sin pasar por la RLS)
IF @hay_tablas = 1
    SELECT N'filas reales' AS dato, CONCAT(N'dbo.auditoria_seguridad: ', COALESCE(@filas_seg, 0), N'; saas.auditoria_plataforma: ', COALESCE(@filas_plat, 0)) AS valor;
