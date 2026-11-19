-- ============================================================
-- Script   : 045_auditoria_seguridad.sql
-- Proposito: Paso F1.8a del plan de la Fase 1 (auditoria de seguridad y de plataforma; ADR-030; decisiones D70 a D76):
--              * Crea dbo.auditoria_seguridad (TABLA DE TENANT: id_tenant obligatorio, clave foranea a saas.tenants y politica RLS
--                con las mismas funciones que las demas tablas de tenant): lo que ocurre DENTRO de un tenant (el sitio que elige una
--                persona, y desde F1.8b los cambios de administracion y el soporte del operador).
--              * Crea saas.auditoria_plataforma (plano de control, SIN RLS, ADR-013): lo de las cuentas (entradas, fallos, bloqueos,
--                segundo factor, claves, invitaciones aceptadas) y las acciones del operador. Su id_tenant es el tenant sobre el que se
--                actuo y puede faltar: no filtra nada.
--              * Las dos son INMUTABLES: un disparador INSTEAD OF UPDATE, DELETE rechaza cualquier modificacion o borrado, igual que
--                dbo.TR_bitacora_cambios_inmutable. No llevan clave foranea a cuentas ni a empresas a proposito: la auditoria nunca debe
--                bloquear ni ser afectada por cambios en otras tablas.
--              * Agrega dos columnas NULAS a dbo.bitacora_cambios: id_cuenta (quien hizo el cambio) e id_correlacion (con que
--                peticion). Las filas anteriores quedan sin ellas.
--            No cambia lo que ve ni lo que puede hacer ningun usuario: la aplicacion publicada ignora las tablas y las columnas nuevas.
--            Solo AGREGA: no borra ni modifica ninguna fila ni ningun objeto existente (salvo ampliar seg.pol_tenant y dbo.bitacora_cambios).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-18
-- BD       : eBD_SPD
-- Requiere : 025 a 037 aplicados (saas.tenants, seg.fn_tenant_actual, seg.pol_tenant, dbo.bitacora_cambios con id_tenant y su disparador).
--            038 a 044 pueden estar aplicados o no: la politica no se cuenta entera, solo se comprueban los predicados de la tabla nueva.
-- Rollback : ver la seccion ROLLBACK al final (se detiene si alguna de las dos tablas de auditoria tiene filas o si la bitacora ya
--            tiene datos en las columnas nuevas: una auditoria no se descarta sin una decision expresa).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos (saas.tenants, seg.fn_tenant_actual, seg.pol_tenant, dbo.bitacora_cambios con id_tenant y su
--           disparador de inmutabilidad);
--        b) los objetos nuevos existen A MEDIAS (algunas de las 2 tablas, sus 2 disparadores, las 2 columnas de la bitacora y los 5
--           predicados RLS si y otros no) o una tabla existe con columnas distintas de las esperadas;
--        c) la tabla nueva de tenant esta en otra politica o tiene un numero de predicados distinto de 0 o 5.
--   2. CAMBIO (una sola transaccion): las 2 tablas con sus restricciones e indices, los 2 disparadores, los 5 predicados RLS y las 2
--      columnas de la bitacora. Solo crea lo que falta.
--   3. POSTCHECK dentro de la misma transaccion: columnas exactas, restricciones CHECK y claves foraneas confiables, disparadores
--      INSTEAD OF habilitados, 5 predicados (1 filtro y 4 bloqueos, todos de seg.fn_tenant_actual) en la tabla de tenant y ninguno en la
--      de plataforma, las 2 columnas nuevas nulables en la bitacora y la bitacora con las mismas filas que antes. Si algo no cuadra,
--      se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * Funciona con la politica encendida o apagada y la deja como estaba (ALTER SECURITY POLICY ... ADD no cambia el estado).
--   * ASCII puro.
--   * Limite conocido: TRUNCATE TABLE no dispara el disparador; solo quien tiene permiso de modificar el esquema puede hacerlo
--     (igual que en dbo.bitacora_cambios). En el hosting nuevo la aplicacion entra con un usuario sin ese permiso (ADR-016).
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
IF OBJECT_ID(N'tempdb..#precheck_045') IS NOT NULL DROP TABLE #precheck_045;
IF OBJECT_ID(N'tempdb..#columnas_045') IS NOT NULL DROP TABLE #columnas_045;

-- Columnas esperadas de cada tabla nueva (las dos tablas tienen las mismas 15; la columna nueva de la bitacora no se lista aqui porque
-- esa tabla ya existe con otras columnas).
CREATE TABLE #columnas_045 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    columna  SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, columna)
);
INSERT INTO #columnas_045 (tabla, columna)
SELECT t.tabla, c.columna
FROM (VALUES (N'dbo.auditoria_seguridad'), (N'saas.auditoria_plataforma')) t (tabla)
CROSS JOIN (VALUES (N'id_evento'), (N'fecha_hora'), (N'tipo'), (N'resultado'), (N'id_cuenta'), (N'usuario'), (N'es_operador'), (N'id_tenant'),
                   (N'id_empresa'), (N'objeto_tipo'), (N'objeto_id'), (N'detalle'), (N'ip'), (N'agente'), (N'id_correlacion')) c (columna);

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @predSeg INT, @predOtra INT;

-- a) scripts previos
IF SCHEMA_ID(N'saas') IS NULL SET @errores += N' Falta el esquema saas (script 025).';
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
IF OBJECT_ID(N'dbo.bitacora_cambios', N'U') IS NULL SET @errores += N' Falta la tabla dbo.bitacora_cambios (script 014).';
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_tenant')
        SET @errores += N' dbo.bitacora_cambios no tiene id_tenant (scripts 026 a 035).';
    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_bitacora_cambios_inmutable' AND parent_id = OBJECT_ID(N'dbo.bitacora_cambios')
                   AND is_instead_of_trigger = 1 AND is_disabled = 0)
        SET @errores += N' Falta el disparador TR_bitacora_cambios_inmutable de dbo.bitacora_cambios (script 014).';
END
IF OBJECT_ID(N'seg.fn_tenant_actual') IS NULL SET @errores += N' Falta la funcion seg.fn_tenant_actual (script 036).';
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';

-- c) la tabla de tenant en la politica: 0 predicados (primera vez) o 5 (ya aplicado); y en ninguna otra politica
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
BEGIN
    SELECT @predSeg = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad');
    IF @predSeg NOT IN (0, 5)
        SET @errores += CONCAT(N' La politica tiene ', @predSeg, N' predicados sobre dbo.auditoria_seguridad y se esperaban 0 o 5.');

    SELECT @predOtra = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'saas.auditoria_plataforma');
    IF @predOtra > 0
        SET @errores += N' saas.auditoria_plataforma esta en la politica seg.pol_tenant y es del plano de control, sin RLS (ADR-013).';
END

SELECT @errores += N' ' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND p.target_object_id IN (OBJECT_ID(N'dbo.auditoria_seguridad'), OBJECT_ID(N'saas.auditoria_plataforma'));

-- b) estado de los objetos nuevos: ninguno (primera vez) o todos (ya aplicado); a medias, no
DECLARE @tablasExisten INT = (SELECT COUNT(*) FROM (VALUES (N'dbo.auditoria_seguridad'), (N'saas.auditoria_plataforma')) v (t)
                              WHERE OBJECT_ID(v.t, N'U') IS NOT NULL);
DECLARE @disparadoresExisten INT = (SELECT COUNT(*) FROM sys.triggers
                                    WHERE name IN (N'TR_auditoria_seguridad_inmutable', N'TR_auditoria_plataforma_inmutable'));
DECLARE @columnasBitacora INT = (SELECT COUNT(*) FROM sys.columns
                                 WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name IN (N'id_cuenta', N'id_correlacion'));
DECLARE @objetosExisten INT = @tablasExisten + @disparadoresExisten + @columnasBitacora + CASE WHEN COALESCE(@predSeg, 0) = 5 THEN 1 ELSE 0 END;

IF @objetosExisten NOT IN (0, 7)
    SET @errores += CONCAT(N' Los objetos de este script existen a medias (', @objetosExisten, N' de 7: ', @tablasExisten, N' de 2 tablas, ',
                           @disparadoresExisten, N' de 2 disparadores, ', @columnasBitacora, N' de 2 columnas de la bitacora y ',
                           CASE WHEN COALESCE(@predSeg, 0) = 5 THEN N'con' ELSE N'sin' END, N' predicados RLS). Revisar a mano antes de seguir.');

-- las tablas que ya existan tienen exactamente las columnas esperadas
SELECT @errores += N' La tabla ' + x.tabla + N' no tiene la columna ' + x.columna + N'.'
FROM #columnas_045 x
WHERE OBJECT_ID(x.tabla, N'U') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);

SELECT @errores += N' La tabla ' + SCHEMA_NAME(o.schema_id) + N'.' + o.name + N' tiene una columna no prevista: ' + c.name + N'.'
FROM sys.objects o
JOIN sys.columns c ON c.object_id = o.object_id
WHERE o.type = 'U'
  AND (SCHEMA_NAME(o.schema_id) + N'.' + o.name) IN (SELECT DISTINCT tabla FROM #columnas_045)
  AND NOT EXISTS (SELECT 1 FROM #columnas_045 x WHERE x.tabla = (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AND x.columna = c.name);

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 045 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50045, @mensaje, 1;
END

CREATE TABLE #precheck_045 (ok BIT NOT NULL);
INSERT INTO #precheck_045 (ok) VALUES (1);

PRINT CONCAT(N'PRECHECK 045 correcto. Objetos nuevos ya existentes: ', @objetosExisten, N' de 7.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_045') IS NULL
    THROW 50045, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @porCrear INT = (CASE WHEN OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NULL THEN 1 ELSE 0 END)
                      + (CASE WHEN OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NULL THEN 1 ELSE 0 END);
DECLARE @predicadosNuevos INT = 0;
DECLARE @columnasNuevas INT = 0;
DECLARE @malas INT;
DECLARE @filasBitacoraAntes BIGINT = (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND p.index_id IN (0, 1));
DECLARE @filasBitacoraDespues BIGINT;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Tabla de tenant: lo que ocurre dentro de un tenant (RLS, inmutable)
    IF OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.auditoria_seguridad (
            id_evento       BIGINT         IDENTITY(1,1) NOT NULL,
            fecha_hora      DATETIME2(3)   NOT NULL CONSTRAINT DF_auditoria_seguridad_fecha_hora DEFAULT SYSUTCDATETIME(),
            tipo            VARCHAR(60)    NOT NULL,
            resultado       VARCHAR(10)    NOT NULL,
            id_cuenta       INT            NULL,
            usuario         NVARCHAR(100)  NULL,
            es_operador     BIT            NOT NULL CONSTRAINT DF_auditoria_seguridad_es_operador DEFAULT 0,
            id_tenant       INT            NOT NULL,
            id_empresa      INT            NULL,
            objeto_tipo     VARCHAR(40)    NULL,
            objeto_id       NVARCHAR(60)   NULL,
            detalle         NVARCHAR(2000) NULL,
            ip              VARCHAR(45)    NULL,
            agente          NVARCHAR(200)  NULL,
            id_correlacion  VARCHAR(64)    NOT NULL,
            CONSTRAINT PK_auditoria_seguridad PRIMARY KEY CLUSTERED (id_evento),
            CONSTRAINT CK_auditoria_seguridad_resultado CHECK (resultado IN ('ok', 'fallo', 'denegado')),
            -- El tipo es grupo.accion y solo lleva a-z, 0-9, _ y punto.
            CONSTRAINT CK_auditoria_seguridad_tipo CHECK (LEN(tipo) > 2 AND tipo LIKE '%.%' AND tipo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_.]%'),
            CONSTRAINT CK_auditoria_seguridad_detalle CHECK (detalle IS NULL OR ISJSON(detalle) = 1),
            CONSTRAINT FK_auditoria_seguridad_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant)
        );
        CREATE INDEX IX_auditoria_seguridad_tenant_fecha ON dbo.auditoria_seguridad (id_tenant, fecha_hora DESC, id_evento DESC);
        CREATE INDEX IX_auditoria_seguridad_tenant_tipo ON dbo.auditoria_seguridad (id_tenant, tipo, fecha_hora DESC);
        CREATE INDEX IX_auditoria_seguridad_tenant_cuenta ON dbo.auditoria_seguridad (id_tenant, id_cuenta, fecha_hora DESC);
        CREATE INDEX IX_auditoria_seguridad_correlacion ON dbo.auditoria_seguridad (id_correlacion);
    END

    -- 2. Tabla de plataforma: lo de las cuentas y del operador (esquema saas, sin RLS, inmutable)
    IF OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.auditoria_plataforma (
            id_evento       BIGINT         IDENTITY(1,1) NOT NULL,
            fecha_hora      DATETIME2(3)   NOT NULL CONSTRAINT DF_auditoria_plataforma_fecha_hora DEFAULT SYSUTCDATETIME(),
            tipo            VARCHAR(60)    NOT NULL,
            resultado       VARCHAR(10)    NOT NULL,
            id_cuenta       INT            NULL,
            usuario         NVARCHAR(100)  NULL,
            es_operador     BIT            NOT NULL CONSTRAINT DF_auditoria_plataforma_es_operador DEFAULT 0,
            id_tenant       INT            NULL,
            id_empresa      INT            NULL,
            objeto_tipo     VARCHAR(40)    NULL,
            objeto_id       NVARCHAR(60)   NULL,
            detalle         NVARCHAR(2000) NULL,
            ip              VARCHAR(45)    NULL,
            agente          NVARCHAR(200)  NULL,
            id_correlacion  VARCHAR(64)    NOT NULL,
            CONSTRAINT PK_auditoria_plataforma PRIMARY KEY CLUSTERED (id_evento),
            CONSTRAINT CK_auditoria_plataforma_resultado CHECK (resultado IN ('ok', 'fallo', 'denegado')),
            CONSTRAINT CK_auditoria_plataforma_tipo CHECK (LEN(tipo) > 2 AND tipo LIKE '%.%' AND tipo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_.]%'),
            CONSTRAINT CK_auditoria_plataforma_detalle CHECK (detalle IS NULL OR ISJSON(detalle) = 1),
            -- Para saber de que tenant es la fila, no para filtrarla: el plano de control no lleva RLS.
            CONSTRAINT FK_auditoria_plataforma_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant)
        );
        CREATE INDEX IX_auditoria_plataforma_fecha ON saas.auditoria_plataforma (fecha_hora DESC, id_evento DESC);
        CREATE INDEX IX_auditoria_plataforma_tipo ON saas.auditoria_plataforma (tipo, fecha_hora DESC);
        CREATE INDEX IX_auditoria_plataforma_cuenta ON saas.auditoria_plataforma (id_cuenta, fecha_hora DESC);
        CREATE INDEX IX_auditoria_plataforma_tenant ON saas.auditoria_plataforma (id_tenant, fecha_hora DESC);
        CREATE INDEX IX_auditoria_plataforma_correlacion ON saas.auditoria_plataforma (id_correlacion);
    END

    -- 3. Inmutables: ninguna fila se modifica ni se borra (como dbo.TR_bitacora_cambios_inmutable)
    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_auditoria_seguridad_inmutable' AND parent_id = OBJECT_ID(N'dbo.auditoria_seguridad'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_auditoria_seguridad_inmutable
ON dbo.auditoria_seguridad
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50045, N''La auditoria de seguridad es de solo lectura: no se permite modificar ni borrar filas.'', 1;
END;';

    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_auditoria_plataforma_inmutable' AND parent_id = OBJECT_ID(N'saas.auditoria_plataforma'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER saas.TR_auditoria_plataforma_inmutable
ON saas.auditoria_plataforma
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50045, N''La auditoria de la plataforma es de solo lectura: no se permite modificar ni borrar filas.'', 1;
END;';

    -- 4. Politica: la tabla de tenant entra con las mismas funciones que las demas (1 filtro y 4 bloqueos).
    --    ALTER SECURITY POLICY ... ADD no cambia el estado (encendida o apagada) de la politica.
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.auditoria_seguridad,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.auditoria_seguridad AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.auditoria_seguridad AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.auditoria_seguridad BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.auditoria_seguridad BEFORE DELETE;';
        SET @predicadosNuevos = 5;
    END

    -- 5. Quien y con que peticion: dos columnas nulas en la bitacora de cambios (las filas anteriores quedan sin ellas).
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_cuenta')
    BEGIN
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios ADD id_cuenta INT NULL;';
        SET @columnasNuevas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_correlacion')
    BEGIN
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios ADD id_correlacion VARCHAR(64) NULL;';
        SET @columnasNuevas += 1;
    END

    -- 6. POSTCHECK (dentro de la transaccion: si algo no cuadra, se revierte todo)
    -- 6a. columnas exactas de las dos tablas nuevas
    SELECT @malas = COUNT(*)
    FROM #columnas_045 x
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);
    IF @malas > 0 THROW 50045, N'POSTCHECK: a alguna tabla nueva le falta una columna. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM sys.columns c
    WHERE c.object_id IN (OBJECT_ID(N'dbo.auditoria_seguridad'), OBJECT_ID(N'saas.auditoria_plataforma'))
      AND NOT EXISTS (SELECT 1 FROM #columnas_045 x WHERE x.tabla = (OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id)) AND x.columna = c.name);
    IF @malas > 0 THROW 50045, N'POSTCHECK: alguna tabla nueva tiene una columna no prevista. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND name = N'id_tenant' AND is_nullable = 1;
    IF @malas > 0 THROW 50045, N'POSTCHECK: dbo.auditoria_seguridad.id_tenant debe ser obligatorio. Se revierte.', 1;

    -- 6b. restricciones habilitadas y confiables: 3 CHECK y 1 clave foranea por tabla
    SELECT @malas = COUNT(*) FROM (VALUES (N'CK_auditoria_seguridad_resultado'), (N'CK_auditoria_seguridad_tipo'), (N'CK_auditoria_seguridad_detalle'),
                                          (N'CK_auditoria_plataforma_resultado'), (N'CK_auditoria_plataforma_tipo'), (N'CK_auditoria_plataforma_detalle')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0 THROW 50045, N'POSTCHECK: alguna restriccion CHECK de las tablas de auditoria falta o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM (VALUES (N'FK_auditoria_seguridad_tenants'), (N'FK_auditoria_plataforma_tenants')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0 THROW 50045, N'POSTCHECK: alguna clave foranea de las tablas de auditoria falta o no es confiable. Se revierte.', 1;

    -- 6c. inmutables: un disparador INSTEAD OF habilitado en cada tabla
    SELECT @malas = COUNT(*) FROM (VALUES (N'TR_auditoria_seguridad_inmutable', N'dbo.auditoria_seguridad'),
                                          (N'TR_auditoria_plataforma_inmutable', N'saas.auditoria_plataforma')) v (nombre, tabla)
    WHERE NOT EXISTS (SELECT 1 FROM sys.triggers t WHERE t.name = v.nombre AND t.parent_id = OBJECT_ID(v.tabla) AND t.is_instead_of_trigger = 1 AND t.is_disabled = 0);
    IF @malas > 0 THROW 50045, N'POSTCHECK: falta un disparador INSTEAD OF habilitado en alguna tabla de auditoria. Se revierte.', 1;

    -- 6d. politica: 5 predicados (1 filtro y 4 bloqueos, todos de seg.fn_tenant_actual) en la tabla de tenant y ninguno en la de plataforma
    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad');
    IF @malas <> 5 THROW 50045, N'POSTCHECK: dbo.auditoria_seguridad debe tener 5 predicados en la politica. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND predicate_type_desc = N'FILTER';
    IF @malas <> 1 THROW 50045, N'POSTCHECK: dbo.auditoria_seguridad debe tener un solo filtro. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad')
      AND predicate_definition NOT LIKE N'%fn_tenant_actual%';
    IF @malas > 0 THROW 50045, N'POSTCHECK: algun predicado de dbo.auditoria_seguridad no usa seg.fn_tenant_actual. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'saas.auditoria_plataforma');
    IF @malas > 0 THROW 50045, N'POSTCHECK: saas.auditoria_plataforma no debe estar en la politica (plano de control, sin RLS). Se revierte.', 1;

    -- 6e. la bitacora: dos columnas nuevas nulables y las mismas filas que antes
    SELECT @malas = COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name IN (N'id_cuenta', N'id_correlacion') AND is_nullable = 1;
    IF @malas <> 2 THROW 50045, N'POSTCHECK: dbo.bitacora_cambios debe tener id_cuenta e id_correlacion, ambas nulables. Se revierte.', 1;

    SELECT @filasBitacoraDespues = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND p.index_id IN (0, 1);
    IF COALESCE(@filasBitacoraAntes, 0) <> COALESCE(@filasBitacoraDespues, 0)
        THROW 50045, N'POSTCHECK: dbo.bitacora_cambios cambio de numero de filas. Se revierte.', 1;

    -- 6f. indices de consulta de las dos tablas (4 + 5)
    SELECT @malas = COUNT(*) FROM (VALUES (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_fecha'), (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_tipo'),
                                          (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_tenant_cuenta'), (N'dbo.auditoria_seguridad', N'IX_auditoria_seguridad_correlacion'),
                                          (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_fecha'), (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_tipo'),
                                          (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_cuenta'), (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_tenant'),
                                          (N'saas.auditoria_plataforma', N'IX_auditoria_plataforma_correlacion')) v (tabla, indice)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.indice AND i.object_id = OBJECT_ID(v.tabla));
    IF @malas > 0 THROW 50045, N'POSTCHECK: falta algun indice de las tablas de auditoria. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @porCrear = 0 AND @predicadosNuevos = 0 AND @columnasNuevas = 0
        PRINT N'Nada que hacer: las tablas de auditoria, sus disparadores, los predicados RLS y las columnas de la bitacora ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 045 aplicado: ', @porCrear, N' tabla(s) de auditoria creadas, ', @predicadosNuevos, N' predicado(s) RLS y ', @columnasNuevas,
                     N' columna(s) nuevas en dbo.bitacora_cambios. Ninguna fila existente cambio.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: las dos tablas (vacias al aplicar), sus predicados y las columnas nuevas de la bitacora
SELECT N'dbo.auditoria_seguridad' AS tabla, SUM(p.rows) AS filas_reales FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND p.index_id IN (0, 1)
UNION ALL
SELECT N'saas.auditoria_plataforma', SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'saas.auditoria_plataforma') AND p.index_id IN (0, 1);

SELECT COUNT(*) AS predicados_de_auditoria_seguridad FROM sys.security_predicates
WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad');

SELECT name AS columna_nueva_de_la_bitacora, TYPE_NAME(user_type_id) AS tipo, max_length, is_nullable
FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name IN (N'id_cuenta', N'id_correlacion');
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita los 5 predicados de dbo.auditoria_seguridad de la politica, los disparadores y las 2 tablas, y las 2 columnas de la bitacora.
--   Se detiene si alguna de las tablas de auditoria tiene filas (una auditoria no se descarta sin una decision expresa; sys.partitions no
--   pasa por la RLS: cuenta las filas reales) o si la bitacora ya tiene datos en las columnas nuevas.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF (OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NOT NULL
        AND EXISTS (SELECT 1 FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.auditoria_seguridad') AND p.index_id IN (0, 1) AND p.rows > 0))
       OR (OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NOT NULL
        AND EXISTS (SELECT 1 FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'saas.auditoria_plataforma') AND p.index_id IN (0, 1) AND p.rows > 0))
        THROW 50045, N'ROLLBACK 045 detenido: las tablas de auditoria tienen filas y se perderian. Decidirlo expresamente antes de seguir.', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name IN (N'id_cuenta', N'id_correlacion'))
    BEGIN
        -- La bitacora esta bajo RLS: se cuenta tenant por tenant las filas que ya tienen datos en las columnas nuevas.
        DECLARE @t INT, @conDatos INT = 0, @f INT;
        DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants;
        OPEN c; FETCH NEXT FROM c INTO @t;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
            EXEC sys.sp_executesql N'SELECT @f = COUNT(*) FROM dbo.bitacora_cambios WHERE id_cuenta IS NOT NULL OR id_correlacion IS NOT NULL;',
                 N'@f INT OUTPUT', @f = @f OUTPUT;
            SET @conDatos += @f;
            FETCH NEXT FROM c INTO @t;
        END
        CLOSE c; DEALLOCATE c;
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        IF @conDatos > 0
            THROW 50045, N'ROLLBACK 045 detenido: la bitacora ya tiene datos en id_cuenta o id_correlacion y se perderian. Revisar primero.', 1;
    END

    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.auditoria_seguridad'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.auditoria_seguridad,
    DROP BLOCK PREDICATE ON dbo.auditoria_seguridad AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.auditoria_seguridad AFTER UPDATE,
    DROP BLOCK PREDICATE ON dbo.auditoria_seguridad BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.auditoria_seguridad BEFORE DELETE;';

    IF OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NOT NULL DROP TABLE dbo.auditoria_seguridad;      -- se lleva su disparador y sus indices
    IF OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NOT NULL DROP TABLE saas.auditoria_plataforma;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_cuenta')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios DROP COLUMN id_cuenta;';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_correlacion')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios DROP COLUMN id_correlacion;';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    THROW;
END CATCH;
*/
