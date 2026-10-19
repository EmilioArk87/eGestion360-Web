-- ============================================================
-- Script   : 040_planes_derechos_estado_tenant.sql
-- Proposito: Paso F1.7 del plan de la Fase 1 (modelo de planes y derechos de uso de ADR-010 y ADR-013; decisiones D52 a D56):
--              * Crea en el esquema saas (plano de control, SIN RLS, ADR-013) cinco tablas: planes, plan_versiones, derechos,
--                plan_version_derechos y tenant_planes. El codigo de la aplicacion NUNCA nombrara un plan: solo preguntara por
--                derechos (modulo.*, caracteristica.* o limite.*) del plan vigente de su tenant.
--              * Agrega saas.tenants.modo_suspension ('lectura' por defecto, o 'bloqueo'; D53: suspender un tenant lo deja en
--                solo lectura salvo que se elija el bloqueo para ese tenant) y la restriccion CK_tenants_interno_activo: el
--                tenant interno (la propia plataforma, ADR-006) no puede suspenderse ni cancelarse.
--              * Siembra los derechos (uno por cada modulo de dbo.modulos, LEIDO de la tabla al correr el script, mas
--                limite.usuarios y limite.empresas: D54, por ahora solo se aplican esos dos limites), los tres planes iniciales
--                con su version 1 (vigente desde 2026-10-15) y asigna un plan a cada tenant, buscandolo por su codigo:
--                    SIP -> interno   DEMO -> demostracion   TRANSGAR -> flota   IISE -> flota
--            LOS TOPES Y LA LISTA DE MODULOS DE LOS PLANES SON PROVISIONALES: ningun nombre ni valor se escribe en el codigo
--            de la aplicacion y se pueden editar sin publicar nada. Contenido sembrado:
--                interno       todos los modulos de dbo.modulos; limite.usuarios y limite.empresas sin tope (NULL)
--                demostracion  modulo.flota y modulo.catalogos; limite.usuarios 10; limite.empresas 2
--                flota         modulo.flota; limite.usuarios 50; limite.empresas 5
--            Una fila de plan_version_derechos AUSENTE significa derecho NO concedido; en un limite, valor_limite NULL significa
--            sin tope. Por eso cada version lista siempre los dos limites.
--            No toca ninguna fila de dbo ni ninguna tabla de tenant; no cambia lo que ve ninguna pantalla (la aplicacion
--            publicada ignora las tablas y la columna nuevas: EF solo lee y escribe las columnas que mapea).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-15
-- BD       : eBD_SPD
-- Requiere : 025 a 036 aplicados. El 037 (politica RLS encendida) puede estar aplicado o no: el script funciona en los dos
--            estados (ver el PRECHECK). Se puede aplicar antes o despues de publicar la version con F1.7.
-- Rollback : ver la seccion ROLLBACK al final (quita lo que crea este script; se detiene si el operador ya hizo asignaciones).
-- Emergencia: 2 - Script SQL/emergencia_derechos_abrir.sql deja a todos los tenants activos y con el plan interno.
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts 025 a 036 (esquema saas, saas.tenants, id_tenant obligatorio en empresas y empresa_modulos,
--           politica seg.pol_tenant con sus 199 predicados);
--        b) alguno de los 4 tenants del reparto (SIP, DEMO, TRANSGAR, IISE) no existe, o hay otro tenant sin plan vigente al
--           que este script no sabe que plan darle;
--        c) los objetos nuevos existen A MEDIAS (algunas de las 5 tablas, la columna y las 2 restricciones de saas.tenants si y
--           otras no) o alguna tabla existe con columnas distintas de las esperadas;
--        d) un codigo de dbo.modulos no sirve para formar un derecho (solo a-z, 0-9 y guion bajo), o un plan sembrado nombra un
--           modulo que no esta en dbo.modulos;
--        e) NADIE PIERDE ACCESO: para cada tenant al que este script asigna un plan, TODOS los modulos que hoy tienen activos
--           sus empresas en dbo.empresa_modulos (activo = 1) estan incluidos en ese plan, y su numero de usuarios activos y de
--           empresas cabe en los limites del plan. Con la politica RLS ENCENDIDA dbo.empresa_modulos no se ve sin tenant en la
--           sesion, por eso se lee tenant por tenant fijando SESSION_CONTEXT y se deja en NULL al terminar;
--        f) el tenant interno no esta activo (no se podria crear CK_tenants_interno_activo).
--   2. CAMBIO (una sola transaccion): tablas, restricciones, columna, derechos, planes, versiones, derechos de cada version y
--      asignaciones. Solo inserta lo que falta; no pisa nada que el operador ya haya editado.
--   3. POSTCHECK dentro de la misma transaccion: tablas y columnas, restricciones confiables, cada tenant con exactamente un plan
--      vigente, el reparto de arriba, un derecho por modulo, y de nuevo que nadie pierde acceso (leyendo otra vez los modulos).
--      Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los demas bloques exigen; si el
--     PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice. Un tenant que ya tiene un plan vigente no se toca.
--   * ASCII puro: los nombres y descripciones sembrados no llevan tildes.
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_040') IS NOT NULL DROP TABLE #precheck_040;
IF OBJECT_ID(N'tempdb..#plan_040') IS NOT NULL DROP TABLE #plan_040;
IF OBJECT_ID(N'tempdb..#plan_derecho_040') IS NOT NULL DROP TABLE #plan_derecho_040;
IF OBJECT_ID(N'tempdb..#asignacion_040') IS NOT NULL DROP TABLE #asignacion_040;
IF OBJECT_ID(N'tempdb..#columnas_040') IS NOT NULL DROP TABLE #columnas_040;
IF OBJECT_ID(N'tempdb..#uso_040') IS NOT NULL DROP TABLE #uso_040;
IF OBJECT_ID(N'tempdb..#conteo_040') IS NOT NULL DROP TABLE #conteo_040;
IF OBJECT_ID(N'tempdb..#versiones_nuevas_040') IS NOT NULL DROP TABLE #versiones_nuevas_040;

-- Los tres planes iniciales (provisionales).
CREATE TABLE #plan_040 (
    orden        INT            NOT NULL,
    codigo       VARCHAR(30)    COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    nombre       NVARCHAR(100)  COLLATE DATABASE_DEFAULT NOT NULL,
    descripcion  NVARCHAR(300)  COLLATE DATABASE_DEFAULT NOT NULL
);
INSERT INTO #plan_040 (orden, codigo, nombre, descripcion) VALUES
    (1, 'interno',      N'Interno',      N'Plan de la propia plataforma (tenant interno): todos los modulos y sin topes. Contenido provisional.'),
    (2, 'demostracion', N'Demostracion', N'Plan de los tenants de demostracion: flota y catalogos con topes bajos. Contenido provisional.'),
    (3, 'flota',        N'Flota',        N'Plan del primer cliente: modulo de flota con topes de usuarios y empresas. Contenido provisional.');

-- Derechos de cada plan: modulos (valor_limite NULL) y los dos limites (NULL = sin tope).
CREATE TABLE #plan_derecho_040 (
    codigo_plan     VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL,
    codigo_derecho  VARCHAR(60) COLLATE DATABASE_DEFAULT NOT NULL,
    valor_limite    INT         NULL,
    PRIMARY KEY (codigo_plan, codigo_derecho)
);
-- interno: todos los modulos que haya en dbo.modulos (no se escriben a mano).
INSERT INTO #plan_derecho_040 (codigo_plan, codigo_derecho, valor_limite)
SELECT 'interno', 'modulo.' + CAST(m.codigo AS VARCHAR(50)), NULL
FROM dbo.modulos m;
INSERT INTO #plan_derecho_040 (codigo_plan, codigo_derecho, valor_limite) VALUES
    ('demostracion', 'modulo.flota',      NULL),
    ('demostracion', 'modulo.catalogos',  NULL),
    ('flota',        'modulo.flota',      NULL),
    ('interno',      'limite.usuarios',   NULL),
    ('interno',      'limite.empresas',   NULL),
    ('demostracion', 'limite.usuarios',   10),
    ('demostracion', 'limite.empresas',   2),
    ('flota',        'limite.usuarios',   50),
    ('flota',        'limite.empresas',   5);

-- Reparto inicial: cada tenant se busca por su codigo, nunca por su id. asignar = 0 si ya tiene un plan vigente.
CREATE TABLE #asignacion_040 (
    codigo_tenant  VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    codigo_plan    VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL,
    asignar        BIT         NOT NULL DEFAULT 1
);
INSERT INTO #asignacion_040 (codigo_tenant, codigo_plan) VALUES
    ('SIP', 'interno'), ('DEMO', 'demostracion'), ('TRANSGAR', 'flota'), ('IISE', 'flota');

-- Columnas esperadas de cada tabla nueva (las cuatro de auditoria, como saas.tenants, van en todas).
CREATE TABLE #columnas_040 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    columna  SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, columna)
);
INSERT INTO #columnas_040 (tabla, columna) VALUES
    (N'planes', N'id_plan'), (N'planes', N'codigo'), (N'planes', N'nombre'), (N'planes', N'descripcion'), (N'planes', N'activo'),
    (N'plan_versiones', N'id_plan_version'), (N'plan_versiones', N'id_plan'), (N'plan_versiones', N'numero'),
    (N'plan_versiones', N'vigente_desde'), (N'plan_versiones', N'vigente_hasta'),
    (N'derechos', N'id_derecho'), (N'derechos', N'codigo'), (N'derechos', N'tipo'), (N'derechos', N'descripcion'),
    (N'plan_version_derechos', N'id_plan_version'), (N'plan_version_derechos', N'id_derecho'),
    (N'plan_version_derechos', N'tipo_derecho'), (N'plan_version_derechos', N'valor_limite'),
    (N'tenant_planes', N'id_tenant_plan'), (N'tenant_planes', N'id_tenant'), (N'tenant_planes', N'id_plan_version'),
    (N'tenant_planes', N'vigente_desde'), (N'tenant_planes', N'vigente_hasta'), (N'tenant_planes', N'motivo');
INSERT INTO #columnas_040 (tabla, columna)
SELECT t.tabla, a.columna
FROM (VALUES (N'planes'), (N'plan_versiones'), (N'derechos'), (N'plan_version_derechos'), (N'tenant_planes')) t (tabla)
CROSS JOIN (VALUES (N'creado_por'), (N'fecha_creacion'), (N'modificado_por'), (N'fecha_modificacion')) a (columna);

-- Lo que usa hoy cada tenant: modulos activos en sus empresas, y cuantas empresas y usuarios tiene.
CREATE TABLE #uso_040 (
    id_tenant      INT          NOT NULL,
    codigo_modulo  NVARCHAR(50) COLLATE DATABASE_DEFAULT NOT NULL,
    empresas       INT          NOT NULL,
    PRIMARY KEY (id_tenant, codigo_modulo)
);
CREATE TABLE #conteo_040 (
    id_tenant  INT NOT NULL PRIMARY KEY,
    empresas   INT NOT NULL,
    usuarios   INT NOT NULL
);

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;
DECLARE @t INT;

-- a) scripts 025 a 036 aplicados (el 037 es opcional)
IF SCHEMA_ID(N'saas') IS NULL SET @errores += N' Falta el esquema saas (script 025).';
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresas') AND name = N'id_tenant' AND is_nullable = 0)
    SET @errores += N' dbo.empresas no tiene id_tenant obligatorio (scripts 025 y 031).';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_modulos') AND name = N'id_tenant' AND is_nullable = 0)
    SET @errores += N' dbo.empresa_modulos no tiene id_tenant obligatorio (scripts 026 y 031).';
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';
ELSE
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    -- 199 predicados (036) o 204 si el script 038 ya agrego dbo.accesos_empresa: 038/039 y 040 se pueden aplicar en cualquier orden.
    IF @n <> CASE WHEN OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NOT NULL THEN 204 ELSE 199 END
        SET @errores += CONCAT(N' La politica seg.pol_tenant tiene ', @n, N' predicados y se esperaban ',
                               CASE WHEN OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NOT NULL THEN 204 ELSE 199 END, N'.');
END
IF OBJECT_ID(N'dbo.modulos', N'U') IS NULL SET @errores += N' Falta la tabla dbo.modulos.';
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL SET @errores += N' Falta la tabla dbo.Users.';

-- c) estado de los objetos nuevos: ninguno (primera vez) o todos (ya aplicado); a medias, no
DECLARE @tablasExisten INT = (SELECT COUNT(*) FROM (VALUES (N'planes'), (N'plan_versiones'), (N'derechos'),
                                                           (N'plan_version_derechos'), (N'tenant_planes')) v (t)
                              WHERE OBJECT_ID(N'saas.' + v.t, N'U') IS NOT NULL);
DECLARE @tieneModo BIT = CASE WHEN COL_LENGTH(N'saas.tenants', N'modo_suspension') IS NULL THEN 0 ELSE 1 END;
DECLARE @tieneCkModo BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_modo_suspension'
                                             AND parent_object_id = OBJECT_ID(N'saas.tenants')) THEN 1 ELSE 0 END;
DECLARE @tieneCkInterno BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_interno_activo'
                                                AND parent_object_id = OBJECT_ID(N'saas.tenants')) THEN 1 ELSE 0 END;
DECLARE @objetosExisten INT = @tablasExisten + @tieneModo + @tieneCkModo + @tieneCkInterno;

IF @objetosExisten NOT IN (0, 8)
    SET @errores += CONCAT(N' Los objetos de este script existen a medias (', @objetosExisten, N' de 8): tablas ', @tablasExisten,
                           N' de 5; columna modo_suspension: ', CASE WHEN @tieneModo = 1 THEN N'si' ELSE N'no' END,
                           N'; CK_tenants_modo_suspension: ', CASE WHEN @tieneCkModo = 1 THEN N'si' ELSE N'no' END,
                           N'; CK_tenants_interno_activo: ', CASE WHEN @tieneCkInterno = 1 THEN N'si' ELSE N'no' END,
                           N'. Revisar a mano antes de seguir.');

-- c) las tablas que ya existan tienen exactamente las columnas esperadas
SELECT @errores += N' La tabla saas.' + x.tabla + N' no tiene la columna ' + x.columna + N'.'
FROM #columnas_040 x
WHERE OBJECT_ID(N'saas.' + x.tabla, N'U') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'saas.' + x.tabla) AND c.name = x.columna);

SELECT @errores += N' La tabla saas.' + o.name + N' tiene una columna no prevista: ' + c.name + N'.'
FROM sys.objects o
JOIN sys.columns c ON c.object_id = o.object_id
WHERE o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
  AND o.name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes')
  AND NOT EXISTS (SELECT 1 FROM #columnas_040 x WHERE x.tabla = o.name AND x.columna = c.name);

-- Las comprobaciones de datos solo corren si lo anterior (scripts previos, objetos completos, columnas) esta en orden.
IF LEN(@errores) = 0
BEGIN
    -- saas.tenants: las 13 columnas de la 025 (y modo_suspension si ya se agrego)
    DECLARE @colTenants TABLE (nombre SYSNAME NOT NULL PRIMARY KEY);
    INSERT INTO @colTenants (nombre) VALUES
        (N'id_tenant'), (N'uid_publico'), (N'codigo'), (N'nombre'), (N'tipo'), (N'estado'), (N'fecha_estado'),
        (N'motivo_estado'), (N'creado_por'), (N'fecha_creacion'), (N'modificado_por'), (N'fecha_modificacion'),
        (N'token_concurrencia');
    SELECT @errores += N' saas.tenants no tiene la columna ' + e.nombre + N'.'
    FROM @colTenants e
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'saas.tenants') AND c.name = e.nombre);
    SELECT @errores += N' saas.tenants tiene una columna no prevista: ' + c.name + N'.'
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'saas.tenants') AND c.name <> N'modo_suspension' AND c.name NOT IN (SELECT nombre FROM @colTenants);

    -- b) los tenants del reparto existen
    SELECT @errores += N' El tenant ' + a.codigo_tenant + N' (plan ' + a.codigo_plan + N') no existe en saas.tenants.'
    FROM #asignacion_040 a
    WHERE NOT EXISTS (SELECT 1 FROM saas.tenants t WHERE t.codigo = a.codigo_tenant);

    -- f) el tenant interno esta activo (la restriccion CK_tenants_interno_activo no se podria crear si no)
    SELECT @errores += N' El tenant interno ' + t.codigo + N' esta ' + t.estado + N': debe estar activo.'
    FROM saas.tenants t
    WHERE t.tipo = 'interno' AND t.estado <> 'activo';

    -- Quien ya tiene un plan vigente no se toca (asignar = 0) y los tenants fuera del reparto necesitan uno ya.
    IF @tablasExisten = 5
    BEGIN
        EXEC sys.sp_executesql
            N'UPDATE a SET a.asignar = 0
              FROM #asignacion_040 a
              JOIN saas.tenants t ON t.codigo = a.codigo_tenant
              JOIN saas.tenant_planes p ON p.id_tenant = t.id_tenant AND p.vigente_hasta IS NULL;';
        EXEC sys.sp_executesql
            N'SELECT @e += N'' El tenant '' + t.codigo + N'' no esta en el reparto de este script y no tiene un plan vigente: asignarle uno a mano.''
              FROM saas.tenants t
              WHERE t.codigo NOT IN (SELECT codigo_tenant FROM #asignacion_040)
                AND NOT EXISTS (SELECT 1 FROM saas.tenant_planes p WHERE p.id_tenant = t.id_tenant AND p.vigente_hasta IS NULL);',
            N'@e NVARCHAR(MAX) OUTPUT', @e = @errores OUTPUT;
    END
    ELSE
    BEGIN
        SELECT @errores += N' El tenant ' + t.codigo + N' no esta en el reparto de este script (SIP, DEMO, TRANSGAR, IISE): decidir su plan antes de seguir.'
        FROM saas.tenants t
        WHERE t.codigo NOT IN (SELECT codigo_tenant FROM #asignacion_040);
    END

    -- d) codigos de modulo utilizables como derecho, y modulos que los planes sembrados nombran
    SELECT @errores += N' El codigo de modulo ' + m.codigo + N' no sirve para formar un derecho (solo a-z, 0-9 y guion bajo, hasta 50 caracteres).'
    FROM dbo.modulos m
    WHERE m.codigo COLLATE Latin1_General_100_BIN2 LIKE N'%[^a-z0-9_]%' OR LEN(m.codigo) = 0;

    SELECT @errores += N' El plan ' + d.codigo_plan + N' nombra el modulo ' + d.codigo_derecho + N', que no esta en dbo.modulos.'
    FROM #plan_derecho_040 d
    WHERE d.codigo_derecho LIKE 'modulo.%'
      AND NOT EXISTS (SELECT 1 FROM dbo.modulos m WHERE 'modulo.' + CAST(m.codigo AS VARCHAR(50)) = d.codigo_derecho);

    -- e) NADIE PIERDE ACCESO. Los modulos activos se leen tenant por tenant: con la politica RLS ENCENDIDA, dbo.empresa_modulos
    --    no devuelve filas a una sesion sin tenant, y la comprobacion pasaria sin mirar nada. El contexto se deja en NULL al terminar.
    IF LEN(@errores) = 0
    BEGIN
        BEGIN TRY
            DECLARE cur_tenants CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants ORDER BY id_tenant;
            OPEN cur_tenants;
            FETCH NEXT FROM cur_tenants INTO @t;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
                EXEC sys.sp_executesql
                    N'INSERT INTO #uso_040 (id_tenant, codigo_modulo, empresas)
                      SELECT em.id_tenant, m.codigo, COUNT(*)
                      FROM dbo.empresa_modulos em
                      JOIN dbo.modulos m ON m.id_modulo = em.id_modulo
                      WHERE em.activo = 1 AND em.id_tenant = @t
                      GROUP BY em.id_tenant, m.codigo;',
                    N'@t INT', @t = @t;
                FETCH NEXT FROM cur_tenants INTO @t;
            END
            CLOSE cur_tenants;
            DEALLOCATE cur_tenants;
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        END TRY
        BEGIN CATCH
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
            THROW;
        END CATCH

        -- Cuantas empresas y usuarios activos tiene cada tenant (dbo.empresas se lee abierta; dbo.Users no lleva RLS).
        EXEC sys.sp_executesql
            N'INSERT INTO #conteo_040 (id_tenant, empresas, usuarios)
              SELECT t.id_tenant,
                     (SELECT COUNT(*) FROM dbo.empresas e WHERE e.id_tenant = t.id_tenant AND e.eliminado = 0),
                     (SELECT COUNT(*) FROM dbo.Users u JOIN dbo.empresas e ON e.id_empresa = u.EmpresaId
                      WHERE e.id_tenant = t.id_tenant AND e.eliminado = 0 AND u.IsActive = 1)
              FROM saas.tenants t;';

        SELECT @errores += N' El tenant ' + a.codigo_tenant + N' tiene activo el modulo ' + u.codigo_modulo + N' que el plan '
                         + a.codigo_plan + N' no incluye: perderia el acceso.'
        FROM #asignacion_040 a
        JOIN saas.tenants t ON t.codigo = a.codigo_tenant
        JOIN #uso_040 u ON u.id_tenant = t.id_tenant
        WHERE a.asignar = 1
          AND NOT EXISTS (SELECT 1 FROM #plan_derecho_040 d
                          WHERE d.codigo_plan = a.codigo_plan AND d.codigo_derecho = 'modulo.' + CAST(u.codigo_modulo AS VARCHAR(50)));

        SELECT @errores += N' El tenant ' + a.codigo_tenant + N' tiene ' + CAST(c.usuarios AS NVARCHAR(10)) + N' usuarios activos y el plan '
                         + a.codigo_plan + N' admite solo ' + CAST(d.valor_limite AS NVARCHAR(10)) + N'.'
        FROM #asignacion_040 a
        JOIN saas.tenants t ON t.codigo = a.codigo_tenant
        JOIN #conteo_040 c ON c.id_tenant = t.id_tenant
        JOIN #plan_derecho_040 d ON d.codigo_plan = a.codigo_plan AND d.codigo_derecho = 'limite.usuarios'
        WHERE a.asignar = 1 AND d.valor_limite IS NOT NULL AND c.usuarios > d.valor_limite;

        SELECT @errores += N' El tenant ' + a.codigo_tenant + N' tiene ' + CAST(c.empresas AS NVARCHAR(10)) + N' empresas y el plan '
                         + a.codigo_plan + N' admite solo ' + CAST(d.valor_limite AS NVARCHAR(10)) + N'.'
        FROM #asignacion_040 a
        JOIN saas.tenants t ON t.codigo = a.codigo_tenant
        JOIN #conteo_040 c ON c.id_tenant = t.id_tenant
        JOIN #plan_derecho_040 d ON d.codigo_plan = a.codigo_plan AND d.codigo_derecho = 'limite.empresas'
        WHERE a.asignar = 1 AND d.valor_limite IS NOT NULL AND c.empresas > d.valor_limite;
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 040 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50040, @mensaje, 1;
END

CREATE TABLE #precheck_040 (ok BIT NOT NULL);
INSERT INTO #precheck_040 (ok) VALUES (1);

DECLARE @aAsignar INT = (SELECT COUNT(*) FROM #asignacion_040 WHERE asignar = 1);
DECLARE @enReparto INT = (SELECT COUNT(*) FROM #asignacion_040);
PRINT CONCAT(N'PRECHECK 040 correcto. Objetos nuevos ya existentes: ', @objetosExisten, N' de 8; tenants a los que se asignara plan: ',
             @aAsignar, N' de ', @enReparto, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_040') IS NULL
    THROW 50040, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @porCrear INT = (SELECT (CASE WHEN OBJECT_ID(N'saas.planes', N'U') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN OBJECT_ID(N'saas.plan_versiones', N'U') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN OBJECT_ID(N'saas.derechos', N'U') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN OBJECT_ID(N'saas.tenant_planes', N'U') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN COL_LENGTH(N'saas.tenants', N'modo_suspension') IS NULL THEN 1 ELSE 0 END)
                              + (CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_modo_suspension'
                                                   AND parent_object_id = OBJECT_ID(N'saas.tenants')) THEN 0 ELSE 1 END)
                              + (CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_interno_activo'
                                                   AND parent_object_id = OBJECT_ID(N'saas.tenants')) THEN 0 ELSE 1 END));
DECLARE @derechosNuevos INT = 0, @planesNuevos INT = 0, @versionesNuevas INT = 0, @enlacesNuevos INT = 0, @asignaciones INT = 0;
DECLARE @malas INT;
DECLARE @t INT;
DECLARE @detalle NVARCHAR(1000);

CREATE TABLE #versiones_nuevas_040 (id_plan_version INT NOT NULL PRIMARY KEY, id_plan INT NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Tablas del plano de control (esquema saas, sin RLS)
    IF OBJECT_ID(N'saas.planes', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.planes (
            id_plan             INT           IDENTITY(1,1) NOT NULL,
            codigo              VARCHAR(30)   NOT NULL,
            nombre              NVARCHAR(100) NOT NULL,
            descripcion         NVARCHAR(300) NULL,
            activo              BIT           NOT NULL CONSTRAINT DF_planes_activo DEFAULT 1,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_planes_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_planes PRIMARY KEY CLUSTERED (id_plan),
            CONSTRAINT UX_planes_codigo UNIQUE (codigo),
            CONSTRAINT CK_planes_codigo CHECK (LEN(codigo) > 0 AND codigo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_]%')
        );
    END

    IF OBJECT_ID(N'saas.plan_versiones', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.plan_versiones (
            id_plan_version     INT       IDENTITY(1,1) NOT NULL,
            id_plan             INT       NOT NULL,
            numero              INT       NOT NULL,
            vigente_desde       DATE      NOT NULL,
            vigente_hasta       DATE      NULL,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2 NOT NULL CONSTRAINT DF_plan_versiones_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2 NULL,
            CONSTRAINT PK_plan_versiones PRIMARY KEY CLUSTERED (id_plan_version),
            CONSTRAINT FK_plan_versiones_planes FOREIGN KEY (id_plan) REFERENCES saas.planes (id_plan),
            CONSTRAINT UX_plan_versiones_plan_numero UNIQUE (id_plan, numero),
            CONSTRAINT CK_plan_versiones_numero CHECK (numero > 0),
            CONSTRAINT CK_plan_versiones_vigencia CHECK (vigente_hasta IS NULL OR vigente_hasta >= vigente_desde)
        );
    END

    IF OBJECT_ID(N'saas.derechos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.derechos (
            id_derecho          INT           IDENTITY(1,1) NOT NULL,
            codigo              VARCHAR(60)   NOT NULL,
            tipo                VARCHAR(15)   NOT NULL,
            descripcion         NVARCHAR(200) NOT NULL,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_derechos_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_derechos PRIMARY KEY CLUSTERED (id_derecho),
            CONSTRAINT UX_derechos_codigo UNIQUE (codigo),
            -- Clave de apoyo para que plan_version_derechos compruebe el tipo con una clave foranea compuesta (sin disparadores).
            CONSTRAINT UX_derechos_id_tipo UNIQUE (id_derecho, tipo),
            CONSTRAINT CK_derechos_tipo CHECK (tipo IN ('modulo', 'caracteristica', 'limite')),
            -- El codigo empieza por su tipo (modulo.flota, limite.usuarios, caracteristica.xxx) y solo lleva a-z, 0-9, _ y punto.
            CONSTRAINT CK_derechos_codigo CHECK (codigo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_.]%'
                                                 AND codigo COLLATE Latin1_General_100_BIN2 LIKE tipo + '.[a-z0-9_]%')
        );
    END

    IF OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.plan_version_derechos (
            id_plan_version     INT           NOT NULL,
            id_derecho          INT           NOT NULL,
            tipo_derecho        VARCHAR(15)   NOT NULL,
            valor_limite        INT           NULL,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_plan_version_derechos_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_plan_version_derechos PRIMARY KEY CLUSTERED (id_plan_version, id_derecho),
            CONSTRAINT FK_plan_version_derechos_plan_versiones FOREIGN KEY (id_plan_version)
                REFERENCES saas.plan_versiones (id_plan_version) ON DELETE CASCADE,
            -- (id_derecho, tipo_derecho) apunta a (id_derecho, tipo) de derechos: tipo_derecho no puede mentir sobre el derecho.
            CONSTRAINT FK_plan_version_derechos_derechos FOREIGN KEY (id_derecho, tipo_derecho)
                REFERENCES saas.derechos (id_derecho, tipo),
            -- valor_limite solo existe en los derechos de tipo limite (NULL = sin tope) y no es negativo.
            CONSTRAINT CK_plan_version_derechos_valor CHECK (valor_limite IS NULL OR (tipo_derecho = 'limite' AND valor_limite >= 0))
        );
        CREATE INDEX IX_plan_version_derechos_derecho ON saas.plan_version_derechos (id_derecho);
    END

    IF OBJECT_ID(N'saas.tenant_planes', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.tenant_planes (
            id_tenant_plan      INT           IDENTITY(1,1) NOT NULL,
            id_tenant           INT           NOT NULL,
            id_plan_version     INT           NOT NULL,
            vigente_desde       DATETIME2     NOT NULL CONSTRAINT DF_tenant_planes_vigente_desde DEFAULT SYSUTCDATETIME(),
            vigente_hasta       DATETIME2     NULL,
            motivo              NVARCHAR(300) NULL,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_tenant_planes_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_tenant_planes PRIMARY KEY CLUSTERED (id_tenant_plan),
            CONSTRAINT FK_tenant_planes_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FK_tenant_planes_plan_versiones FOREIGN KEY (id_plan_version) REFERENCES saas.plan_versiones (id_plan_version),
            CONSTRAINT CK_tenant_planes_vigencia CHECK (vigente_hasta IS NULL OR vigente_hasta >= vigente_desde)
        );
        -- Un solo plan vigente (sin fecha de fin) por tenant a la vez; el historial queda en las filas cerradas.
        CREATE UNIQUE INDEX UX_tenant_planes_vigente ON saas.tenant_planes (id_tenant) WHERE vigente_hasta IS NULL;
        CREATE INDEX IX_tenant_planes_tenant ON saas.tenant_planes (id_tenant, vigente_desde);
        CREATE INDEX IX_tenant_planes_plan_version ON saas.tenant_planes (id_plan_version);
    END

    -- 2. Estado del tenant: modo de suspension y el tenant interno siempre activo
    -- (Dinamico: la columna se agrega en este mismo lote y la restriccion que la usa no compila antes de que exista.)
    IF COL_LENGTH(N'saas.tenants', N'modo_suspension') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE saas.tenants ADD modo_suspension VARCHAR(10) NOT NULL
                                 CONSTRAINT DF_tenants_modo_suspension DEFAULT ''lectura'';';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_modo_suspension' AND parent_object_id = OBJECT_ID(N'saas.tenants'))
        EXEC sys.sp_executesql N'ALTER TABLE saas.tenants ADD CONSTRAINT CK_tenants_modo_suspension
                                 CHECK (modo_suspension IN (''lectura'', ''bloqueo''));';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_interno_activo' AND parent_object_id = OBJECT_ID(N'saas.tenants'))
        EXEC sys.sp_executesql N'ALTER TABLE saas.tenants ADD CONSTRAINT CK_tenants_interno_activo
                                 CHECK (tipo <> ''interno'' OR estado = ''activo'');';

    -- 3. Datos iniciales: solo se inserta lo que falta. No se toca nada que el operador ya haya editado.
    INSERT INTO saas.derechos (codigo, tipo, descripcion, creado_por)
    SELECT x.codigo, x.tipo, x.descripcion, N'script:040'
    FROM (SELECT 'modulo.' + CAST(m.codigo AS VARCHAR(50)) AS codigo, 'modulo' AS tipo, N'Acceso al modulo ' + m.codigo AS descripcion
          FROM dbo.modulos m
          UNION ALL SELECT 'limite.usuarios', 'limite', N'Maximo de usuarios activos del tenant (sin valor: sin tope)'
          UNION ALL SELECT 'limite.empresas', 'limite', N'Maximo de empresas del tenant (sin valor: sin tope)') x
    WHERE NOT EXISTS (SELECT 1 FROM saas.derechos d WHERE d.codigo = x.codigo);
    SET @derechosNuevos = @@ROWCOUNT;

    INSERT INTO saas.planes (codigo, nombre, descripcion, creado_por)
    SELECT p.codigo, p.nombre, p.descripcion, N'script:040'
    FROM #plan_040 p
    WHERE NOT EXISTS (SELECT 1 FROM saas.planes x WHERE x.codigo = p.codigo)
    ORDER BY p.orden;   -- ids 1, 2 y 3 en el orden interno, demostracion, flota (el codigo nunca los usa)
    SET @planesNuevos = @@ROWCOUNT;

    -- Version 1 de los planes que no tienen ninguna (vigente desde 2026-10-15).
    INSERT INTO saas.plan_versiones (id_plan, numero, vigente_desde, creado_por)
    OUTPUT inserted.id_plan_version, inserted.id_plan INTO #versiones_nuevas_040 (id_plan_version, id_plan)
    SELECT p.id_plan, 1, CAST('20261015' AS DATE), N'script:040'
    FROM saas.planes p
    WHERE p.codigo IN (SELECT codigo FROM #plan_040)
      AND NOT EXISTS (SELECT 1 FROM saas.plan_versiones v WHERE v.id_plan = p.id_plan);
    SET @versionesNuevas = @@ROWCOUNT;

    -- Derechos de cada version recien creada (las versiones que ya existian no se tocan).
    INSERT INTO saas.plan_version_derechos (id_plan_version, id_derecho, tipo_derecho, valor_limite, creado_por)
    SELECT vn.id_plan_version, d.id_derecho, d.tipo, pd.valor_limite, N'script:040'
    FROM #versiones_nuevas_040 vn
    JOIN saas.planes p ON p.id_plan = vn.id_plan
    JOIN #plan_derecho_040 pd ON pd.codigo_plan = p.codigo
    JOIN saas.derechos d ON d.codigo = pd.codigo_derecho;
    SET @enlacesNuevos = @@ROWCOUNT;

    -- Asignacion: a cada tenant del reparto sin plan vigente, la version abierta mas reciente de su plan.
    INSERT INTO saas.tenant_planes (id_tenant, id_plan_version, vigente_desde, motivo, creado_por)
    SELECT t.id_tenant,
           (SELECT TOP (1) v.id_plan_version FROM saas.plan_versiones v
            WHERE v.id_plan = p.id_plan AND v.vigente_hasta IS NULL ORDER BY v.numero DESC),
           SYSUTCDATETIME(), N'script:040', N'script:040'
    FROM #asignacion_040 a
    JOIN saas.tenants t ON t.codigo = a.codigo_tenant
    JOIN saas.planes p ON p.codigo = a.codigo_plan
    WHERE NOT EXISTS (SELECT 1 FROM saas.tenant_planes tp WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL);
    SET @asignaciones = @@ROWCOUNT;

    -- 4. POSTCHECK: si algo no cuadra, se revierte todo.
    -- 4a. tablas con exactamente sus columnas
    SELECT @malas = COUNT(*)
    FROM #columnas_040 x
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'saas.' + x.tabla) AND c.name = x.columna);
    IF @malas > 0
        THROW 50040, N'POSTCHECK: falta alguna columna en las tablas nuevas de saas. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM sys.columns c
    JOIN sys.objects o ON o.object_id = c.object_id AND o.schema_id = SCHEMA_ID(N'saas') AND o.type = 'U'
    WHERE o.name IN (N'planes', N'plan_versiones', N'derechos', N'plan_version_derechos', N'tenant_planes')
      AND NOT EXISTS (SELECT 1 FROM #columnas_040 x WHERE x.tabla = o.name AND x.columna = c.name);
    IF @malas > 0
        THROW 50040, N'POSTCHECK: alguna tabla nueva tiene columnas no previstas. Se revierte.', 1;

    -- 4b. restricciones y claves existen y son confiables (no estan deshabilitadas ni sin validar)
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'CK_planes_codigo'), (N'CK_plan_versiones_numero'), (N'CK_plan_versiones_vigencia'), (N'CK_derechos_tipo'),
                 (N'CK_derechos_codigo'), (N'CK_plan_version_derechos_valor'), (N'CK_tenant_planes_vigencia'),
                 (N'CK_tenants_modo_suspension'), (N'CK_tenants_interno_activo')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50040, N'POSTCHECK: falta alguna restriccion CHECK o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM (VALUES (N'FK_plan_versiones_planes'), (N'FK_plan_version_derechos_plan_versiones'), (N'FK_plan_version_derechos_derechos'),
                 (N'FK_tenant_planes_tenants'), (N'FK_tenant_planes_plan_versiones')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50040, N'POSTCHECK: falta alguna clave foranea o no es confiable. Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tenant_planes_vigente' AND object_id = OBJECT_ID(N'saas.tenant_planes')
                   AND is_unique = 1 AND has_filter = 1)
        THROW 50040, N'POSTCHECK: falta el indice unico filtrado UX_tenant_planes_vigente (un plan vigente por tenant). Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'saas.tenants') AND name = N'modo_suspension'
                   AND system_type_id = 167 AND max_length = 10 AND is_nullable = 0)
        THROW 50040, N'POSTCHECK: saas.tenants.modo_suspension no quedo como VARCHAR(10) NOT NULL. Se revierte.', 1;

    -- 4c. cada tenant tiene exactamente un plan vigente; los del reparto de este script, el que corresponde
    SELECT @malas = COUNT(*)
    FROM saas.tenants t
    WHERE (SELECT COUNT(*) FROM saas.tenant_planes tp WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL) <> 1;
    IF @malas > 0
        THROW 50040, N'POSTCHECK: algun tenant no tiene exactamente un plan vigente. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM #asignacion_040 a
    JOIN saas.tenants t ON t.codigo = a.codigo_tenant
    WHERE a.asignar = 1
      AND NOT EXISTS (SELECT 1 FROM saas.tenant_planes tp
                      JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
                      JOIN saas.planes p ON p.id_plan = v.id_plan
                      WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL AND p.codigo = a.codigo_plan AND tp.motivo = N'script:040');
    IF @malas > 0
        THROW 50040, N'POSTCHECK: algun tenant del reparto no quedo con el plan que le corresponde. Se revierte.', 1;

    -- 4d. un derecho por cada modulo, los dos limites, y cada version nueva con todos los derechos de su plan
    SELECT @malas = COUNT(*)
    FROM dbo.modulos m
    WHERE NOT EXISTS (SELECT 1 FROM saas.derechos d WHERE d.tipo = 'modulo' AND d.codigo = 'modulo.' + CAST(m.codigo AS VARCHAR(50)));
    IF @malas > 0
        THROW 50040, N'POSTCHECK: algun modulo de dbo.modulos no tiene su derecho. Se revierte.', 1;

    SELECT @malas = 2 - COUNT(*) FROM saas.derechos WHERE tipo = 'limite' AND codigo IN ('limite.usuarios', 'limite.empresas');
    IF @malas > 0
        THROW 50040, N'POSTCHECK: faltan limite.usuarios o limite.empresas. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM #versiones_nuevas_040 vn
    JOIN saas.planes p ON p.id_plan = vn.id_plan
    WHERE (SELECT COUNT(*) FROM saas.plan_version_derechos l WHERE l.id_plan_version = vn.id_plan_version)
          <> (SELECT COUNT(*) FROM #plan_derecho_040 pd WHERE pd.codigo_plan = p.codigo);
    IF @malas > 0
        THROW 50040, N'POSTCHECK: alguna version de plan nueva no quedo con todos sus derechos. Se revierte.', 1;

    -- 4e. NADIE PIERDE ACCESO, de nuevo y con los datos de ahora (otra vez tenant por tenant, ver el PRECHECK)
    DELETE FROM #uso_040;
    BEGIN TRY
        DECLARE cur_tenants CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants ORDER BY id_tenant;
        OPEN cur_tenants;
        FETCH NEXT FROM cur_tenants INTO @t;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
            INSERT INTO #uso_040 (id_tenant, codigo_modulo, empresas)
            SELECT em.id_tenant, m.codigo, COUNT(*)
            FROM dbo.empresa_modulos em
            JOIN dbo.modulos m ON m.id_modulo = em.id_modulo
            WHERE em.activo = 1 AND em.id_tenant = @t
            GROUP BY em.id_tenant, m.codigo;
            FETCH NEXT FROM cur_tenants INTO @t;
        END
        CLOSE cur_tenants;
        DEALLOCATE cur_tenants;
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    END TRY
    BEGIN CATCH
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        THROW;
    END CATCH

    SET @detalle = N'';
    SELECT @detalle += N' ' + t.codigo + N' -> ' + u.codigo_modulo + N';'
    FROM #asignacion_040 a
    JOIN saas.tenants t ON t.codigo = a.codigo_tenant
    JOIN #uso_040 u ON u.id_tenant = t.id_tenant
    WHERE a.asignar = 1
      AND NOT EXISTS (SELECT 1
                      FROM saas.tenant_planes tp
                      JOIN saas.plan_version_derechos l ON l.id_plan_version = tp.id_plan_version
                      JOIN saas.derechos d ON d.id_derecho = l.id_derecho
                      WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
                        AND d.codigo = 'modulo.' + CAST(u.codigo_modulo AS VARCHAR(50)));
    IF LEN(@detalle) > 0
    BEGIN
        SET @detalle = LEFT(N'POSTCHECK: perderian el acceso a un modulo activo:' + @detalle + N' Se revierte.', 1000);
        THROW 50040, @detalle, 1;
    END

    SET @detalle = N'';
    SELECT @detalle += N' ' + t.codigo + N' (' + d.codigo + N');'
    FROM #asignacion_040 a
    JOIN saas.tenants t ON t.codigo = a.codigo_tenant
    JOIN #conteo_040 c ON c.id_tenant = t.id_tenant
    JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
    JOIN saas.plan_version_derechos l ON l.id_plan_version = tp.id_plan_version AND l.valor_limite IS NOT NULL
    JOIN saas.derechos d ON d.id_derecho = l.id_derecho
    WHERE a.asignar = 1
      AND ((d.codigo = 'limite.usuarios' AND c.usuarios > l.valor_limite) OR (d.codigo = 'limite.empresas' AND c.empresas > l.valor_limite));
    IF LEN(@detalle) > 0
    BEGIN
        SET @detalle = LEFT(N'POSTCHECK: algun tenant ya supera un limite de su plan:' + @detalle + N' Se revierte.', 1000);
        THROW 50040, @detalle, 1;
    END

    COMMIT TRANSACTION;

    IF @porCrear = 0 AND @derechosNuevos = 0 AND @planesNuevos = 0 AND @versionesNuevas = 0 AND @enlacesNuevos = 0 AND @asignaciones = 0
        PRINT N'Nada que hacer: tablas, columna, restricciones, derechos, planes y asignaciones ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 040 aplicado: ', @porCrear, N' objeto(s) creados; ', @derechosNuevos, N' derecho(s), ', @planesNuevos, N' plan(es), ',
                     @versionesNuevas, N' version(es), ', @enlacesNuevos, N' derecho(s) de version y ', @asignaciones, N' asignacion(es) de plan. ',
                     N'Si algo falla: 2 - Script SQL/emergencia_derechos_abrir.sql.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    THROW;
END CATCH;
GO

-- Resultado: el plan vigente de cada tenant y el contenido de cada plan
SELECT t.id_tenant, t.codigo, t.tipo, t.estado, t.modo_suspension, p.codigo AS codigo_plan, v.numero AS numero_version, tp.vigente_desde
FROM saas.tenants t
LEFT JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
LEFT JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
LEFT JOIN saas.planes p ON p.id_plan = v.id_plan
ORDER BY t.id_tenant;

SELECT p.codigo AS codigo_plan, v.numero AS numero_version, v.vigente_desde,
       (SELECT STRING_AGG(REPLACE(d.codigo, 'modulo.', ''), ', ') WITHIN GROUP (ORDER BY d.codigo)
        FROM saas.plan_version_derechos l JOIN saas.derechos d ON d.id_derecho = l.id_derecho
        WHERE l.id_plan_version = v.id_plan_version AND d.tipo = 'modulo') AS modulos,
       (SELECT l.valor_limite FROM saas.plan_version_derechos l JOIN saas.derechos d ON d.id_derecho = l.id_derecho
        WHERE l.id_plan_version = v.id_plan_version AND d.codigo = 'limite.usuarios') AS limite_usuarios,
       (SELECT l.valor_limite FROM saas.plan_version_derechos l JOIN saas.derechos d ON d.id_derecho = l.id_derecho
        WHERE l.id_plan_version = v.id_plan_version AND d.codigo = 'limite.empresas') AS limite_empresas
FROM saas.planes p
JOIN saas.plan_versiones v ON v.id_plan = p.id_plan
ORDER BY p.id_plan, v.numero;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita las cinco tablas nuevas de saas, la columna modo_suspension y las dos restricciones de saas.tenants. Se detiene si
--   hay asignaciones de plan que NO son de este script (las hizo el operador o la emergencia): se perderian. No toca nada de dbo.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'saas.tenant_planes', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM saas.tenant_planes WHERE motivo IS NULL OR motivo <> N'script:040')
        THROW 50040, N'ROLLBACK 040 detenido: hay asignaciones de plan que no son de este script y se perderian. Revisar primero.', 1;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_interno_activo' AND parent_object_id = OBJECT_ID(N'saas.tenants'))
        ALTER TABLE saas.tenants DROP CONSTRAINT CK_tenants_interno_activo;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tenants_modo_suspension' AND parent_object_id = OBJECT_ID(N'saas.tenants'))
        ALTER TABLE saas.tenants DROP CONSTRAINT CK_tenants_modo_suspension;
    IF COL_LENGTH(N'saas.tenants', N'modo_suspension') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tenants_modo_suspension' AND parent_object_id = OBJECT_ID(N'saas.tenants'))
            ALTER TABLE saas.tenants DROP CONSTRAINT DF_tenants_modo_suspension;
        ALTER TABLE saas.tenants DROP COLUMN modo_suspension;
    END

    IF OBJECT_ID(N'saas.tenant_planes', N'U') IS NOT NULL DROP TABLE saas.tenant_planes;
    IF OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NOT NULL DROP TABLE saas.plan_version_derechos;
    IF OBJECT_ID(N'saas.derechos', N'U') IS NOT NULL DROP TABLE saas.derechos;
    IF OBJECT_ID(N'saas.plan_versiones', N'U') IS NOT NULL DROP TABLE saas.plan_versiones;
    IF OBJECT_ID(N'saas.planes', N'U') IS NOT NULL DROP TABLE saas.planes;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
