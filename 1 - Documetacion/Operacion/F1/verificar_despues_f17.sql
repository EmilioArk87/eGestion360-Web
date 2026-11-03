-- ============================================================
-- Archivo  : verificar_despues_f17.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de aplicar el script 040 (F1.7: planes, derechos de uso y
--            estado del tenant). Comprueba:
--              * las 5 tablas nuevas del esquema saas (planes, plan_versiones, derechos, plan_version_derechos, tenant_planes) con
--                sus columnas (nombre, tipo, longitud y nulabilidad), claves primarias, claves unicas e indices, claves foraneas y
--                restricciones CHECK, todas habilitadas y confiables;
--              * saas.tenants: la columna modo_suspension (VARCHAR(10) NOT NULL, 'lectura' por defecto), su CHECK, y la restriccion
--                CK_tenants_interno_activo (el tenant interno no puede suspenderse ni cancelarse);
--              * los derechos sembrados contra dbo.modulos (uno por modulo, mas limite.usuarios y limite.empresas), los 3 planes
--                con su version 1, el reparto de planes (SIP interno, DEMO demostracion, TRANSGAR y IISE flota) y que cada tenant
--                tiene exactamente un plan vigente;
--              * NADIE PIERDE ACCESO: ningun modulo activo de una empresa queda fuera del plan de su tenant, y ningun tenant supera
--                los limites de usuarios y de empresas de su plan;
--              * que no se cambio nada mas: 56 tablas en dbo, 41 columnas id_tenant en dbo, 152 claves foraneas fuera de las tablas
--                nuevas (157 en total con las 5 nuevas), la politica seg.pol_tenant con sus 199 predicados (encendida o apagada: no la
--                toca este script) y ninguna politica sobre tablas de saas (ADR-013).
--            Es el estado de la salida de F1.7: antes de aplicar 040 da FALLA. Las filas de la seccion "valores sembrados" (planes,
--            sus modulos y topes, reparto) comprueban lo que sembro 040: si el operador los edita despues, esas filas dejaran de
--            coincidir y es lo esperado (ajustar el numero que corresponda y anotarlo).
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME_F17.md). No escribe nada.
-- Nota RLS : con la politica ENCENDIDA, una conexion sin tenant (como la de ejecutar_verificacion.ps1) no ve filas de
--            dbo.empresa_modulos. La fila "Modulos activos fuera del plan" lo dice en su texto ("filas visibles de empresa_modulos:
--            N de M"); cuando N < M esa fila no pudo mirar nada y solo vale lo que comprobaron el PRECHECK y el POSTCHECK del script
--            040, que leen tenant por tenant. Los usuarios y las empresas por tenant si se ven siempre (empresas no filtra la lectura;
--            Users no lleva RLS).
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

DECLARE @hay_tablas BIT = CASE WHEN (SELECT COUNT(*) FROM sys.objects
                                     WHERE schema_id = SCHEMA_ID(N'saas') AND type = 'U'
                                       AND name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes')) = 5
                                THEN 1 ELSE 0 END;
DECLARE @n_modulos INT, @lista_modulos NVARCHAR(400);

DECLARE @planes_lista NVARCHAR(400), @versiones_ok INT, @der_por_version NVARCHAR(100),
        @mod_interno NVARCHAR(400), @mod_demo NVARCHAR(400), @mod_flota NVARCHAR(400), @limites NVARCHAR(400),
        @sin_plan NVARCHAR(400), @multi_plan INT, @reparto_ok INT, @reparto_mal NVARCHAR(400), @motivo040 INT,
        @der_modulos INT, @mod_sin_der NVARCHAR(400), @der_sin_mod NVARCHAR(400), @der_limites NVARCHAR(400), @der_total INT,
        @fuera_n INT, @fuera_lista NVARCHAR(400), @sobre_usuarios NVARCHAR(400), @sobre_empresas NVARCHAR(400),
        @em_visibles INT, @em_total BIGINT;

SELECT @n_modulos = COUNT(*), @lista_modulos = LEFT(STRING_AGG(CAST(codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY codigo), 400)
FROM dbo.modulos;

SELECT @em_visibles = COUNT(*) FROM dbo.empresa_modulos;
SELECT @em_total = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.empresa_modulos') AND p.index_id IN (0, 1);

-- Los datos de las tablas nuevas se leen solo si existen las cinco (antes del 040 no existen y las comprobaciones dan FALLA).
IF @hay_tablas = 1
BEGIN
    -- planes sembrados y sus versiones
    SELECT @planes_lista = STRING_AGG(CAST(p.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY p.codigo)
    FROM saas.planes p WHERE p.codigo IN (N'interno', N'demostracion', N'flota');

    SELECT @versiones_ok = COUNT(*)
    FROM saas.planes p
    WHERE p.codigo IN (N'interno', N'demostracion', N'flota')
      AND EXISTS (SELECT 1 FROM saas.plan_versiones v WHERE v.id_plan = p.id_plan AND v.numero = 1 AND v.vigente_desde = CAST('20261015' AS DATE));

    SELECT @der_por_version = CONCAT(COALESCE(SUM(CASE WHEN p.codigo = N'interno' THEN x.n END), 0), N' / ',
                                     COALESCE(SUM(CASE WHEN p.codigo = N'demostracion' THEN x.n END), 0), N' / ',
                                     COALESCE(SUM(CASE WHEN p.codigo = N'flota' THEN x.n END), 0))
    FROM (SELECT v.id_plan, COUNT(*) AS n
          FROM saas.plan_versiones v
          JOIN saas.plan_version_derechos l ON l.id_plan_version = v.id_plan_version
          WHERE v.numero = 1
          GROUP BY v.id_plan) x
    JOIN saas.planes p ON p.id_plan = x.id_plan;

    SELECT @mod_interno = COALESCE(LEFT(STRING_AGG(CAST(REPLACE(d.codigo, 'modulo.', '') AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY d.codigo), 400), N'')
    FROM saas.planes p
    JOIN saas.plan_versiones v ON v.id_plan = p.id_plan AND v.numero = 1
    JOIN saas.plan_version_derechos l ON l.id_plan_version = v.id_plan_version
    JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.tipo = 'modulo'
    WHERE p.codigo = N'interno';

    SELECT @mod_demo = COALESCE(LEFT(STRING_AGG(CAST(REPLACE(d.codigo, 'modulo.', '') AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY d.codigo), 400), N'')
    FROM saas.planes p
    JOIN saas.plan_versiones v ON v.id_plan = p.id_plan AND v.numero = 1
    JOIN saas.plan_version_derechos l ON l.id_plan_version = v.id_plan_version
    JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.tipo = 'modulo'
    WHERE p.codigo = N'demostracion';

    SELECT @mod_flota = COALESCE(LEFT(STRING_AGG(CAST(REPLACE(d.codigo, 'modulo.', '') AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY d.codigo), 400), N'')
    FROM saas.planes p
    JOIN saas.plan_versiones v ON v.id_plan = p.id_plan AND v.numero = 1
    JOIN saas.plan_version_derechos l ON l.id_plan_version = v.id_plan_version
    JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.tipo = 'modulo'
    WHERE p.codigo = N'flota';

    SELECT @limites = LEFT(STRING_AGG(CAST(x.txt AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY x.codigo), 400)
    FROM (SELECT p.codigo,
                 CONCAT(p.codigo, N' ',
                        COALESCE(CAST(MAX(CASE WHEN d.codigo = 'limite.usuarios' THEN l.valor_limite END) AS NVARCHAR(10)), N'NULL'), N'/',
                        COALESCE(CAST(MAX(CASE WHEN d.codigo = 'limite.empresas' THEN l.valor_limite END) AS NVARCHAR(10)), N'NULL')) AS txt
          FROM saas.planes p
          JOIN saas.plan_versiones v ON v.id_plan = p.id_plan AND v.numero = 1
          JOIN saas.plan_version_derechos l ON l.id_plan_version = v.id_plan_version
          JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.tipo = 'limite'
          WHERE p.codigo IN (N'interno', N'demostracion', N'flota')
          GROUP BY p.codigo) x;

    -- derechos contra dbo.modulos
    SELECT @der_modulos = COUNT(*), @der_total = (SELECT COUNT(*) FROM saas.derechos)
    FROM saas.derechos WHERE tipo = 'modulo';

    SELECT @mod_sin_der = LEFT(STRING_AGG(CAST(m.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY m.codigo), 400)
    FROM dbo.modulos m
    WHERE NOT EXISTS (SELECT 1 FROM saas.derechos d WHERE d.tipo = 'modulo' AND d.codigo = 'modulo.' + CAST(m.codigo AS VARCHAR(50)));

    SELECT @der_sin_mod = LEFT(STRING_AGG(CAST(d.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY d.codigo), 400)
    FROM saas.derechos d
    WHERE d.tipo = 'modulo'
      AND NOT EXISTS (SELECT 1 FROM dbo.modulos m WHERE 'modulo.' + CAST(m.codigo AS VARCHAR(50)) = d.codigo);

    SELECT @der_limites = COALESCE(LEFT(STRING_AGG(CAST(d.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY d.codigo), 400), N'')
    FROM saas.derechos d WHERE d.tipo = 'limite';

    -- un plan vigente por tenant y el reparto de 040
    SELECT @sin_plan = LEFT(STRING_AGG(CAST(t.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY t.codigo), 400)
    FROM saas.tenants t
    WHERE NOT EXISTS (SELECT 1 FROM saas.tenant_planes tp WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL);

    SELECT @multi_plan = COUNT(*)
    FROM saas.tenants t
    WHERE (SELECT COUNT(*) FROM saas.tenant_planes tp WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL) > 1;

    SELECT @reparto_ok = SUM(CASE WHEN ok.n > 0 THEN 1 ELSE 0 END),
           @reparto_mal = LEFT(STRING_AGG(CASE WHEN ok.n = 0 THEN CAST(a.codigo_tenant AS NVARCHAR(MAX)) END, N',') WITHIN GROUP (ORDER BY a.codigo_tenant), 400)
    FROM (VALUES (N'SIP', N'interno'), (N'DEMO', N'demostracion'), (N'TRANSGAR', N'flota'), (N'IISE', N'flota')) a (codigo_tenant, codigo_plan)
    CROSS APPLY (SELECT COUNT(*) AS n
                 FROM saas.tenants t
                 JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
                 JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
                 JOIN saas.planes p ON p.id_plan = v.id_plan
                 WHERE t.codigo = a.codigo_tenant AND p.codigo = a.codigo_plan) ok;

    SELECT @motivo040 = COUNT(*) FROM saas.tenant_planes WHERE motivo = N'script:040';

    -- NADIE PIERDE ACCESO: modulos activos fuera del plan vigente de su tenant (solo lo que esta conexion puede ver)
    SELECT @fuera_n = COUNT(*),
           @fuera_lista = LEFT(STRING_AGG(CAST(CONCAT(f.id_tenant, N':', f.codigo) AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY f.id_tenant, f.codigo), 400)
    FROM (SELECT em.id_tenant, m.codigo
          FROM dbo.empresa_modulos em
          JOIN dbo.modulos m ON m.id_modulo = em.id_modulo
          WHERE em.activo = 1
          GROUP BY em.id_tenant, m.codigo) f
    WHERE NOT EXISTS (SELECT 1
                      FROM saas.tenant_planes tp
                      JOIN saas.plan_version_derechos l ON l.id_plan_version = tp.id_plan_version
                      JOIN saas.derechos d ON d.id_derecho = l.id_derecho
                      WHERE tp.id_tenant = f.id_tenant AND tp.vigente_hasta IS NULL
                        AND d.codigo = 'modulo.' + CAST(f.codigo AS VARCHAR(50)));

    -- limites: usuarios activos y empresas por tenant contra el plan vigente
    SELECT @sobre_usuarios = LEFT(STRING_AGG(CAST(CONCAT(c.codigo, N' (', c.usuarios, N' > ', c.tope, N')') AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY c.codigo), 400)
    FROM (SELECT t.codigo, l.valor_limite AS tope,
                 (SELECT COUNT(*) FROM dbo.Users u JOIN dbo.empresas e ON e.id_empresa = u.EmpresaId
                  WHERE e.id_tenant = t.id_tenant AND e.eliminado = 0 AND u.IsActive = 1) AS usuarios
          FROM saas.tenants t
          JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
          JOIN saas.plan_version_derechos l ON l.id_plan_version = tp.id_plan_version AND l.valor_limite IS NOT NULL
          JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.codigo = 'limite.usuarios') c
    WHERE c.usuarios > c.tope;

    SELECT @sobre_empresas = LEFT(STRING_AGG(CAST(CONCAT(c.codigo, N' (', c.empresas, N' > ', c.tope, N')') AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY c.codigo), 400)
    FROM (SELECT t.codigo, l.valor_limite AS tope,
                 (SELECT COUNT(*) FROM dbo.empresas e WHERE e.id_tenant = t.id_tenant AND e.eliminado = 0) AS empresas
          FROM saas.tenants t
          JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
          JOIN saas.plan_version_derechos l ON l.id_plan_version = tp.id_plan_version AND l.valor_limite IS NOT NULL
          JOIN saas.derechos d ON d.id_derecho = l.id_derecho AND d.codigo = 'limite.empresas') c
    WHERE c.empresas > c.tope;
END;

WITH
-- Columnas esperadas de las 5 tablas nuevas: tabla, columna y definicion 'tipo|bytes|nulo' (nvarchar(100) = 200 bytes).
col_base AS (
    SELECT v.tabla, v.columna, v.def
    FROM (VALUES
        (N'planes', N'id_plan', N'int|4|0'), (N'planes', N'codigo', N'varchar|30|0'), (N'planes', N'nombre', N'nvarchar|200|0'),
        (N'planes', N'descripcion', N'nvarchar|600|1'), (N'planes', N'activo', N'bit|1|0'),
        (N'plan_versiones', N'id_plan_version', N'int|4|0'), (N'plan_versiones', N'id_plan', N'int|4|0'),
        (N'plan_versiones', N'numero', N'int|4|0'), (N'plan_versiones', N'vigente_desde', N'date|3|0'),
        (N'plan_versiones', N'vigente_hasta', N'date|3|1'),
        (N'derechos', N'id_derecho', N'int|4|0'), (N'derechos', N'codigo', N'varchar|60|0'), (N'derechos', N'tipo', N'varchar|15|0'),
        (N'derechos', N'descripcion', N'nvarchar|400|0'),
        (N'plan_version_derechos', N'id_plan_version', N'int|4|0'), (N'plan_version_derechos', N'id_derecho', N'int|4|0'),
        (N'plan_version_derechos', N'tipo_derecho', N'varchar|15|0'), (N'plan_version_derechos', N'valor_limite', N'int|4|1'),
        (N'tenant_planes', N'id_tenant_plan', N'int|4|0'), (N'tenant_planes', N'id_tenant', N'int|4|0'),
        (N'tenant_planes', N'id_plan_version', N'int|4|0'), (N'tenant_planes', N'vigente_desde', N'datetime2|8|0'),
        (N'tenant_planes', N'vigente_hasta', N'datetime2|8|1'), (N'tenant_planes', N'motivo', N'nvarchar|600|1')
    ) v (tabla, columna, def)
),
col_esp AS (
    SELECT tabla, columna, def FROM col_base
    UNION ALL
    SELECT t.tabla, a.columna, a.def
    FROM (VALUES (N'planes'), (N'plan_versiones'), (N'derechos'), (N'plan_version_derechos'), (N'tenant_planes')) t (tabla)
    CROSS JOIN (VALUES (N'creado_por', N'nvarchar|200|0'), (N'fecha_creacion', N'datetime2|8|0'),
                       (N'modificado_por', N'nvarchar|200|1'), (N'fecha_modificacion', N'datetime2|8|1')) a (columna, def)
),
col_real AS (
    SELECT o.name AS tabla, c.name AS columna, CONCAT(TYPE_NAME(c.user_type_id), N'|', c.max_length, N'|', c.is_nullable) AS def
    FROM sys.objects o
    JOIN sys.columns c ON c.object_id = o.object_id
    WHERE o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
      AND o.name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes')
),
col_dif AS (
    SELECT COALESCE(e.tabla, r.tabla) AS tabla, COALESCE(e.columna, r.columna) AS columna
    FROM col_esp e
    FULL OUTER JOIN col_real r ON r.tabla = e.tabla AND r.columna = e.columna
    WHERE e.def IS NULL OR r.def IS NULL OR e.def <> r.def
),
-- Claves primarias, claves unicas e indices: tabla, nombre, unico, columnas y filtro.
idx_esp AS (
    SELECT v.tabla, v.nombre, v.unico, v.cols, v.filtro
    FROM (VALUES
        (N'planes', N'PK_planes', 1, N'id_plan', N''),
        (N'planes', N'UX_planes_codigo', 1, N'codigo', N''),
        (N'plan_versiones', N'PK_plan_versiones', 1, N'id_plan_version', N''),
        (N'plan_versiones', N'UX_plan_versiones_plan_numero', 1, N'id_plan,numero', N''),
        (N'derechos', N'PK_derechos', 1, N'id_derecho', N''),
        (N'derechos', N'UX_derechos_codigo', 1, N'codigo', N''),
        (N'derechos', N'UX_derechos_id_tipo', 1, N'id_derecho,tipo', N''),
        (N'plan_version_derechos', N'PK_plan_version_derechos', 1, N'id_plan_version,id_derecho', N''),
        (N'plan_version_derechos', N'IX_plan_version_derechos_derecho', 0, N'id_derecho', N''),
        (N'tenant_planes', N'PK_tenant_planes', 1, N'id_tenant_plan', N''),
        (N'tenant_planes', N'UX_tenant_planes_vigente', 1, N'id_tenant', N'([vigente_hasta] IS NULL)'),
        (N'tenant_planes', N'IX_tenant_planes_tenant', 0, N'id_tenant,vigente_desde', N''),
        (N'tenant_planes', N'IX_tenant_planes_plan_version', 0, N'id_plan_version', N'')
    ) v (tabla, nombre, unico, cols, filtro)
),
idx_real AS (
    SELECT o.name AS tabla, i.name AS nombre, CAST(i.is_unique AS INT) AS unico, COALESCE(i.filter_definition, N'') AS filtro,
           STRING_AGG(CAST(c.name AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS cols
    FROM sys.indexes i
    JOIN sys.objects o ON o.object_id = i.object_id AND o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE o.name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes') AND i.type > 0
    GROUP BY o.name, i.name, i.is_unique, i.filter_definition
),
-- Claves foraneas propias: nombre, hijo, padre, columnas de los dos lados y regla de borrado (0 sin accion, 1 cascada).
fk_esp AS (
    SELECT v.nombre, v.hijo, v.padre, v.cols_hijo, v.cols_padre, v.borrado
    FROM (VALUES
        (N'FK_plan_versiones_planes', N'plan_versiones', N'saas.planes', N'id_plan', N'id_plan', 0),
        (N'FK_plan_version_derechos_plan_versiones', N'plan_version_derechos', N'saas.plan_versiones', N'id_plan_version', N'id_plan_version', 1),
        (N'FK_plan_version_derechos_derechos', N'plan_version_derechos', N'saas.derechos', N'id_derecho,tipo_derecho', N'id_derecho,tipo', 0),
        (N'FK_tenant_planes_tenants', N'tenant_planes', N'saas.tenants', N'id_tenant', N'id_tenant', 0),
        (N'FK_tenant_planes_plan_versiones', N'tenant_planes', N'saas.plan_versiones', N'id_plan_version', N'id_plan_version', 0)
    ) v (nombre, hijo, padre, cols_hijo, cols_padre, borrado)
),
fk_real AS (
    SELECT f.name AS nombre, OBJECT_NAME(f.parent_object_id) AS hijo,
           CONCAT(OBJECT_SCHEMA_NAME(f.referenced_object_id), N'.', OBJECT_NAME(f.referenced_object_id)) AS padre,
           (SELECT STRING_AGG(CAST(COL_NAME(k.parent_object_id, k.parent_column_id) AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY k.constraint_column_id)
            FROM sys.foreign_key_columns k WHERE k.constraint_object_id = f.object_id) AS cols_hijo,
           (SELECT STRING_AGG(CAST(COL_NAME(k.referenced_object_id, k.referenced_column_id) AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY k.constraint_column_id)
            FROM sys.foreign_key_columns k WHERE k.constraint_object_id = f.object_id) AS cols_padre,
           f.delete_referential_action AS borrado, f.is_disabled, f.is_not_trusted
    FROM sys.foreign_keys f
    WHERE f.parent_object_id IN (SELECT o.object_id FROM sys.objects o
                                 WHERE o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
                                   AND o.name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes'))
),
-- Restricciones CHECK: las 7 de las tablas nuevas y las 2 de saas.tenants.
ck_esp AS (
    SELECT v.nombre
    FROM (VALUES (N'CK_planes_codigo'), (N'CK_plan_versiones_numero'), (N'CK_plan_versiones_vigencia'), (N'CK_derechos_tipo'),
                 (N'CK_derechos_codigo'), (N'CK_plan_version_derechos_valor'), (N'CK_tenant_planes_vigencia'),
                 (N'CK_tenants_modo_suspension'), (N'CK_tenants_interno_activo')) v (nombre)
),
ck_real AS (
    SELECT k.name AS nombre, k.is_disabled, k.is_not_trusted, k.definition
    FROM sys.check_constraints k
    WHERE k.schema_id = SCHEMA_ID(N'saas')
),
c AS (
    SELECT (SELECT COUNT(*) FROM sys.schemas WHERE name = N'saas') AS esq_saas,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'saas') AND type = 'U') AS tablas_saas,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'saas') AND type = 'U'
              AND name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes')) AS tablas_nuevas,
           (SELECT COUNT(*) FROM col_esp) AS col_n,
           (SELECT COUNT(*) FROM col_dif) AS col_dif_n,
           (SELECT LEFT(STRING_AGG(CAST(CONCAT(tabla, N'.', columna) AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY tabla, columna), 300) FROM col_dif) AS col_dif_lista,
           (SELECT COUNT(*) FROM sys.columns c JOIN sys.objects o ON o.object_id = c.object_id AND o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
            WHERE c.is_identity = 1 AND o.name IN (N'planes', N'plan_versiones', N'derechos', N'tenant_planes')) AS identidades,
           (SELECT COUNT(*) FROM idx_esp e WHERE EXISTS (SELECT 1 FROM idx_real r
                    WHERE r.tabla = e.tabla AND r.nombre = e.nombre AND r.unico = e.unico AND r.cols = e.cols AND r.filtro = e.filtro)) AS idx_ok,
           (SELECT COUNT(*) FROM idx_real r WHERE NOT EXISTS (SELECT 1 FROM idx_esp e WHERE e.tabla = r.tabla AND e.nombre = r.nombre)) AS idx_extra,
           (SELECT LEFT(STRING_AGG(CAST(e.nombre AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY e.nombre), 300) FROM idx_esp e
            WHERE NOT EXISTS (SELECT 1 FROM idx_real r WHERE r.tabla = e.tabla AND r.nombre = e.nombre AND r.unico = e.unico AND r.cols = e.cols AND r.filtro = e.filtro)) AS idx_mal,
           (SELECT COUNT(*) FROM fk_esp e WHERE EXISTS (SELECT 1 FROM fk_real r
                    WHERE r.nombre = e.nombre AND r.hijo = e.hijo AND r.padre = e.padre AND r.cols_hijo = e.cols_hijo
                      AND r.cols_padre = e.cols_padre AND r.borrado = e.borrado)) AS fk_ok,
           (SELECT LEFT(STRING_AGG(CAST(e.nombre AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY e.nombre), 300) FROM fk_esp e
            WHERE NOT EXISTS (SELECT 1 FROM fk_real r WHERE r.nombre = e.nombre AND r.hijo = e.hijo AND r.padre = e.padre
                              AND r.cols_hijo = e.cols_hijo AND r.cols_padre = e.cols_padre AND r.borrado = e.borrado)) AS fk_mal,
           (SELECT COUNT(*) FROM fk_real) AS fk_propias,
           (SELECT COUNT(*) FROM fk_real WHERE is_disabled = 1 OR is_not_trusted = 1) AS fk_no_confiables,
           (SELECT COUNT(*) FROM ck_esp e WHERE EXISTS (SELECT 1 FROM ck_real r WHERE r.nombre = e.nombre)) AS ck_ok,
           (SELECT LEFT(STRING_AGG(CAST(e.nombre AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY e.nombre), 300) FROM ck_esp e
            WHERE NOT EXISTS (SELECT 1 FROM ck_real r WHERE r.nombre = e.nombre)) AS ck_mal,
           (SELECT COUNT(*) FROM ck_real r WHERE r.nombre IN (SELECT nombre FROM ck_esp) AND (r.is_disabled = 1 OR r.is_not_trusted = 1)) AS ck_no_confiables,
           -- saas.tenants
           (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'saas.tenants')) AS tenants_columnas,
           (SELECT CONCAT(TYPE_NAME(user_type_id), N'|', max_length, N'|', is_nullable) FROM sys.columns
            WHERE object_id = OBJECT_ID(N'saas.tenants') AND name = N'modo_suspension') AS modo_def,
           (SELECT d.definition FROM sys.default_constraints d
            WHERE d.name = N'DF_tenants_modo_suspension' AND d.parent_object_id = OBJECT_ID(N'saas.tenants')) AS modo_default,
           (SELECT COUNT(*) FROM ck_real WHERE nombre = N'CK_tenants_modo_suspension'
              AND definition LIKE N'%lectura%' AND definition LIKE N'%bloqueo%') AS ck_modo_def,
           (SELECT COUNT(*) FROM ck_real WHERE nombre = N'CK_tenants_interno_activo'
              AND definition LIKE N'%interno%' AND definition LIKE N'%activo%') AS ck_interno_def,
           (SELECT COUNT(*) FROM saas.tenants WHERE tipo = N'interno' AND estado <> N'activo') AS internos_no_activos,
           (SELECT COUNT(*) FROM sys.indexes WHERE name = N'UX_tenants_interno' AND object_id = OBJECT_ID(N'saas.tenants') AND is_unique = 1) AS ux_interno,
           -- que no se cambio nada mas
           (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) AS tablas_dbo,
           (SELECT CASE WHEN OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NOT NULL THEN 1 ELSE 0 END) AS con038,
           (SELECT CASE WHEN OBJECT_ID(N'dbo.rol_permisos', N'U') IS NOT NULL THEN 1 ELSE 0 END) AS con041,
           (SELECT COUNT(*) FROM sys.columns c INNER JOIN sys.tables tb ON tb.object_id = c.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
            WHERE c.name = N'id_tenant') AS cols_dbo,
           (SELECT COUNT(*) FROM sys.foreign_keys) AS fks,
           (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'saas.tenants')) AS fks_tenants,
           (SELECT COUNT(*) FROM sys.foreign_keys f
            JOIN sys.objects hijo ON hijo.object_id = f.parent_object_id AND hijo.schema_id = SCHEMA_ID(N'saas')
            JOIN sys.objects padre ON padre.object_id = f.referenced_object_id AND padre.schema_id <> SCHEMA_ID(N'saas')) AS fks_saas_a_fuera,
           (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_existe,
           (SELECT MAX(CAST(is_enabled AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_encendida,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS predicados,
           (SELECT COUNT(*) FROM sys.security_predicates p
            JOIN sys.objects o ON o.object_id = p.target_object_id AND o.schema_id = SCHEMA_ID(N'saas')) AS predicados_en_saas,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg') AND parent_object_id = 0) AS obj_seg
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM c
    CROSS APPLY (VALUES
        -- 1xx: las tablas nuevas y sus restricciones
        (110, N'Esquema saas existe',                                              N'1', CAST(c.esq_saas AS NVARCHAR(400))),
        (120, N'Tablas del esquema saas (tenants y las 5 nuevas; +8 si ya esta el 038; +3 si ya esta el 041)', CAST(6 + 8 * c.con038 + 3 * c.con041 AS NVARCHAR(400)), CAST(c.tablas_saas AS NVARCHAR(400))),
        (130, N'Tablas nuevas: planes, plan_versiones, derechos, plan_version_derechos, tenant_planes', N'5 de 5', CONCAT(c.tablas_nuevas, N' de 5')),
        (140, N'Columnas de las tablas nuevas (nombre, tipo, longitud y nulabilidad): diferencias', N'0',
              CASE WHEN c.col_dif_n = 0 THEN N'0' ELSE CONCAT(c.col_dif_n, N' (', c.col_dif_lista, N')') END),
        (150, N'Columnas IDENTITY (planes, plan_versiones, derechos, tenant_planes)', N'4', CAST(c.identidades AS NVARCHAR(400))),
        (160, N'Claves primarias, claves unicas e indices esperados (13)',          N'13 de 13',
              CONCAT(c.idx_ok, N' de 13', CASE WHEN c.idx_ok < 13 THEN CONCAT(N' (fallan: ', c.idx_mal, N')') ELSE N'' END)),
        (170, N'Indices no previstos en las tablas nuevas',                         N'0', CAST(c.idx_extra AS NVARCHAR(400))),
        (180, N'Claves foraneas propias (nombre, tablas, columnas y regla de borrado)', N'5 de 5',
              CONCAT(c.fk_ok, N' de 5', CASE WHEN c.fk_ok < 5 THEN CONCAT(N' (fallan: ', c.fk_mal, N')') ELSE N'' END)),
        (190, N'Restricciones CHECK (7 en las tablas nuevas y 2 en saas.tenants)',  N'9 de 9',
              CONCAT(c.ck_ok, N' de 9', CASE WHEN c.ck_ok < 9 THEN CONCAT(N' (faltan: ', c.ck_mal, N')') ELSE N'' END)),
        (195, N'Claves foraneas y CHECK deshabilitadas o no confiables',            N'0', CAST(c.fk_no_confiables + c.ck_no_confiables AS NVARCHAR(400))),
        -- 2xx: saas.tenants
        (210, N'Columnas de saas.tenants (las 13 de 025 y modo_suspension)',        N'14', CAST(c.tenants_columnas AS NVARCHAR(400))),
        (220, N'saas.tenants.modo_suspension es VARCHAR(10) NOT NULL',              N'varchar|10|0', COALESCE(c.modo_def, N'no existe')),
        (230, N'Valor por defecto de modo_suspension (D53: solo lectura)',          N'(''lectura'')', COALESCE(c.modo_default, N'no existe')),
        (240, N'CK_tenants_modo_suspension admite solo ''lectura'' y ''bloqueo''',  N'1', CAST(c.ck_modo_def AS NVARCHAR(400))),
        (250, N'CK_tenants_interno_activo: el tenant interno no se suspende ni se cancela', N'1', CAST(c.ck_interno_def AS NVARCHAR(400))),
        (260, N'Tenants internos que no estan activos',                             N'0', CAST(c.internos_no_activos AS NVARCHAR(400))),
        (270, N'Indice unico UX_tenants_interno (como mucho un tenant interno)',    N'1', CAST(c.ux_interno AS NVARCHAR(400))),
        -- 3xx: derechos contra dbo.modulos
        (310, N'Derechos de tipo modulo (uno por cada fila de dbo.modulos)',        CAST(@n_modulos AS NVARCHAR(400)), COALESCE(CAST(@der_modulos AS NVARCHAR(400)), N'no existe')),
        (320, N'Modulos de dbo.modulos sin su derecho modulo.<codigo>',             N'0', COALESCE(@mod_sin_der, CASE WHEN @hay_tablas = 1 THEN N'0' ELSE N'no existe' END)),
        (330, N'Derechos de tipo modulo cuyo modulo ya no esta en dbo.modulos',     N'0', COALESCE(@der_sin_mod, CASE WHEN @hay_tablas = 1 THEN N'0' ELSE N'no existe' END)),
        (340, N'Derechos de tipo limite (D54: solo estos dos)',                     N'limite.empresas,limite.usuarios', COALESCE(@der_limites, N'no existe')),
        (350, N'Derechos en total (modulos y los 2 limites)',                       CAST(@n_modulos + 2 AS NVARCHAR(400)), COALESCE(CAST(@der_total AS NVARCHAR(400)), N'no existe')),
        -- 4xx: valores sembrados por 040 (provisionales: si el operador los edita, estas filas dejan de coincidir)
        (410, N'Planes sembrados (valores sembrados): interno, demostracion y flota', N'demostracion,flota,interno', COALESCE(@planes_lista, N'no existe')),
        (420, N'Planes con version 1 vigente desde 2026-10-15 (valores sembrados)', N'3 de 3', CONCAT(COALESCE(@versiones_ok, 0), N' de 3')),
        (430, N'Derechos por version 1: interno / demostracion / flota (valores sembrados)', CONCAT(@n_modulos + 2, N' / 4 / 3'), COALESCE(@der_por_version, N'no existe')),
        (440, N'Modulos del plan interno = todos los de dbo.modulos',               COALESCE(@lista_modulos, N''), COALESCE(@mod_interno, N'no existe')),
        (450, N'Modulos del plan demostracion (valores sembrados)',                 N'catalogos,flota', COALESCE(@mod_demo, N'no existe')),
        (460, N'Modulos del plan flota (valores sembrados)',                        N'flota', COALESCE(@mod_flota, N'no existe')),
        (470, N'Limites usuarios/empresas por plan, provisionales (valores sembrados)', N'demostracion 10/2, flota 50/5, interno NULL/NULL', COALESCE(@limites, N'no existe')),
        -- 5xx: un plan vigente por tenant y el reparto de 040
        (510, N'Tenants sin plan vigente',                                          N'0', COALESCE(@sin_plan, CASE WHEN @hay_tablas = 1 THEN N'0' ELSE N'no existe' END)),
        (520, N'Tenants con mas de un plan vigente',                                N'0', COALESCE(CAST(@multi_plan AS NVARCHAR(400)), N'no existe')),
        (530, N'Reparto de 040: SIP interno, DEMO demostracion, TRANSGAR y IISE flota (valores sembrados)', N'4 de 4',
              CASE WHEN @reparto_ok IS NULL THEN N'no existe'
                   ELSE CONCAT(@reparto_ok, N' de 4', CASE WHEN @reparto_ok < 4 THEN CONCAT(N' (fallan: ', @reparto_mal, N')') ELSE N'' END) END),
        (540, N'Asignaciones con motivo script:040 (vigentes o cerradas)',          N'4', COALESCE(CAST(@motivo040 AS NVARCHAR(400)), N'no existe')),
        -- 6xx: nadie pierde acceso
        (610, CONCAT(N'Modulos activos de empresas fuera del plan de su tenant (filas visibles de empresa_modulos: ', @em_visibles, N' de ', @em_total,
                     CASE WHEN @em_visibles < @em_total THEN N'; NO VERIFICABLE desde esta conexion sin tenant, ver el PRECHECK de 040' ELSE N'' END, N')'),
              N'0', CASE WHEN @fuera_n IS NULL THEN N'no existe' WHEN @fuera_n = 0 THEN N'0' ELSE CONCAT(@fuera_n, N' (', @fuera_lista, N')') END),
        (620, N'Tenants con mas usuarios activos que limite.usuarios de su plan',   N'0', COALESCE(@sobre_usuarios, CASE WHEN @hay_tablas = 1 THEN N'0' ELSE N'no existe' END)),
        (630, N'Tenants con mas empresas que limite.empresas de su plan',           N'0', COALESCE(@sobre_empresas, CASE WHEN @hay_tablas = 1 THEN N'0' ELSE N'no existe' END)),
        -- 7xx: que no se cambio nada mas
        -- Las cifras de F1.5 (script 038) se suman solo si ya esta aplicado: 040 y 038/039 pueden aplicarse en cualquier orden.
        (710, N'Tablas en el esquema dbo (sin cambios; +1 si ya esta el 038; +1 si ya esta el 041)', CAST(56 + c.con038 + c.con041 AS NVARCHAR(400)), CAST(c.tablas_dbo AS NVARCHAR(400))),
        (720, N'Columnas id_tenant en el esquema dbo (sin cambios; +1 si ya esta el 038; +1 si ya esta el 041)', CAST(41 + c.con038 + c.con041 AS NVARCHAR(400)), CAST(c.cols_dbo AS NVARCHAR(400))),
        (730, N'Claves foraneas fuera de las 5 tablas nuevas (sin cambios; +13 si ya esta el 038; +5 si ya esta el 041)', CAST(152 + 13 * c.con038 + 5 * c.con041 AS NVARCHAR(400)), CAST(c.fks - c.fk_propias AS NVARCHAR(400))),
        (740, N'Claves foraneas de las 5 tablas nuevas',                            N'5', CAST(c.fk_propias AS NVARCHAR(400))),
        (750, N'Claves foraneas en total (152 + 5; +13 si ya esta el 038; +5 si ya esta el 041)', CAST(157 + 13 * c.con038 + 5 * c.con041 AS NVARCHAR(400)), CAST(c.fks AS NVARCHAR(400))),
        (760, N'Claves foraneas hacia saas.tenants (41 de F1.2 + tenant_planes; +3 si ya esta el 038; +1 si ya esta el 041)', CAST(42 + 3 * c.con038 + c.con041 AS NVARCHAR(400)), CAST(c.fks_tenants AS NVARCHAR(400))),
        (770, N'Claves foraneas de tablas de saas hacia tablas fuera de saas (ADR-006; 1 si ya esta el 038: membresias -> personas)', CAST(c.con038 AS NVARCHAR(400)), CAST(c.fks_saas_a_fuera AS NVARCHAR(400))),
        (780, CONCAT(N'Politica seg.pol_tenant intacta con 199 predicados, 204 si ya esta el 038, 209 si tambien el 041 (', CASE WHEN c.pol_encendida = 1 THEN N'encendida' WHEN c.pol_encendida = 0 THEN N'apagada' ELSE N'no existe' END, N')'),
              CAST(199 + 5 * c.con038 + 5 * c.con041 AS NVARCHAR(400)), CAST(c.predicados AS NVARCHAR(400))),
        (790, N'Predicados de RLS sobre tablas de saas (el plano de control no lleva RLS, ADR-013)', N'0', CAST(c.predicados_en_saas AS NVARCHAR(400))),
        (795, N'Objetos propios del esquema seg (3 funciones y la politica)',       N'4', CAST(c.obj_seg AS NVARCHAR(400)))
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
