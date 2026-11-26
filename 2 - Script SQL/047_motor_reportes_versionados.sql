-- ============================================================
-- Script   : 047_motor_reportes_versionados.sql
-- Proposito: Fase 1 del plan del motor de reportes (PLAN_MOTOR_REPORTES_BD.md; ADR-031, decisiones RP1 a RP9):
--              * Crea 4 tablas con prefijo rep_ en el esquema dbo (ADR-013):
--                  - dbo.rep_reportes          (MIXTA: id_tenant nulo = reporte global de la plataforma; con valor = del tenant)
--                  - dbo.rep_reporte_versiones (MIXTA: cada version de la plantilla; inmutable una vez publicada)
--                  - dbo.rep_reporte_bitacora  (MIXTA: quien hizo que sobre un reporte o una version; INMUTABLE)
--                  - dbo.rep_ejecuciones       (de TENANT: cada generacion o impresion con la version exacta; INMUTABLE)
--              * Las 3 mixtas entran en seg.pol_tenant con seg.fn_mixta_lectura / seg.fn_mixta_escritura (como dbo.tasas_cambio):
--                un tenant ve las globales y las suyas y solo escribe las suyas; las globales solo con ambito = 'plataforma'.
--                La de tenant entra con seg.fn_tenant_actual. 5 predicados por tabla (1 filtro y 4 bloqueos), 20 en total.
--              * hash_sha256 es una columna CALCULADA y PERSISTIDA (RP6): SHA-256 de contenido_plantilla + N'|' +
--                ISNULL(configuracion_json, N'') en UTF-16LE, en hexadecimal mayuscula. Nadie puede guardar una huella falsa.
--              * 5 disparadores:
--                  - TR_rep_reportes_reglas          : no cambian id_tenant ni codigo; la version activa es una version PUBLICADA del
--                                                       mismo reporte (RP7).
--                  - TR_rep_reporte_versiones_reglas : misma id_tenant que su reporte; nace borrador o publicada; no cambian
--                                                       id_reporte, id_tenant ni numero_version; publicada o archivada solo pasa de
--                                                       publicada a archivada y nada mas cambia; solo se borra un borrador; la version
--                                                       activa no se archiva (RP5, RP7).
--                  - TR_rep_reporte_bitacora_inmutable, TR_rep_ejecuciones_inmutable : INSTEAD OF UPDATE, DELETE (RP8), como 045.
--                  - TR_rep_ejecuciones_version      : la ejecucion usa una version no borrador, del reporte y numero que dice, global
--                                                       o del mismo tenant.
--              * Siembra el reporte GLOBAL psi_constancia_licencia con su version 1 PUBLICADA y ACTIVA, con las mismas medidas, CSS y
--                redaccion que hoy tiene Pages/Psicologia/Constancias/Imprimir.cshtml, y 3 filas en la bitacora.
--            No cambia lo que ve ni lo que puede hacer ningun usuario: la aplicacion publicada ignora las tablas nuevas y la constancia se
--            sigue imprimiendo con la plantilla Razor hasta que se conecte el motor.
--            Solo AGREGA: no borra ni modifica ninguna fila ni ningun objeto existente (salvo ampliar seg.pol_tenant).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-22
-- BD       : eBD_SPD
-- Requiere : 025 a 037 aplicados (saas.tenants, seg.fn_tenant_actual, seg.fn_mixta_lectura, seg.fn_mixta_escritura, seg.pol_tenant).
--            038 a 046 pueden estar aplicados o no: la politica no se cuenta entera, solo los predicados de las tablas nuevas.
--            El 046 (permisos de auditoria, F1.8c) y el 048 (menu configurable) son independientes de este.
-- Rollback : ver la seccion ROLLBACK al final (se detiene si hay filas que no sean la semilla: ejecuciones, otras versiones, reportes
--            de tenant o bitacora ajena a la semilla).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos (saas.tenants, las 3 funciones de seg y la politica seg.pol_tenant);
--        b) los objetos nuevos existen A MEDIAS (de las 4 tablas, los 5 disparadores y los 4 juegos de 5 predicados, unos si y otros
--           no) o una tabla existe con columnas distintas de las esperadas;
--        c) alguna tabla nueva tiene un numero de predicados distinto de 0 o 5, o esta en otra politica.
--   2. CAMBIO (una sola transaccion): las 4 tablas con sus restricciones e indices, la clave foranea de la version activa, los 5
--      disparadores, los 20 predicados RLS y la semilla. Solo crea lo que falta.
--   3. POSTCHECK dentro de la misma transaccion: columnas exactas, hash calculado y persistido, restricciones CHECK y claves foraneas
--      confiables, disparadores habilitados, 5 predicados por tabla con las funciones correctas, y la semilla con su version activa
--      publicada y su huella correcta. Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * Funciona con la politica encendida o apagada y la deja como estaba (ALTER SECURITY POLICY ... ADD no cambia el estado).
--   * La semilla global se escribe con SESSION_CONTEXT ambito = 'plataforma' (lo exige seg.fn_mixta_escritura con la politica
--     encendida); el valor anterior se restituye al terminar, tambien si falla.
--   * ASCII puro: los acentos de la plantilla van como entidades HTML (&Oacute;) y los del JSON como \u00fa.
--   * Limite conocido: TRUNCATE TABLE no dispara los disparadores; solo quien puede modificar el esquema puede hacerlo (ADR-016).
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
--   * NO ENSAYADO todavia en una copia local: ensayar antes de pedir la aprobacion para produccion.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_047') IS NOT NULL DROP TABLE #precheck_047;
IF OBJECT_ID(N'tempdb..#columnas_047') IS NOT NULL DROP TABLE #columnas_047;
IF OBJECT_ID(N'tempdb..#tablas_047') IS NOT NULL DROP TABLE #tablas_047;

CREATE TABLE #tablas_047 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    mixta    BIT NOT NULL
);
INSERT INTO #tablas_047 (tabla, mixta) VALUES
    (N'dbo.rep_reportes', 1), (N'dbo.rep_reporte_versiones', 1), (N'dbo.rep_reporte_bitacora', 1), (N'dbo.rep_ejecuciones', 0);

CREATE TABLE #columnas_047 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    columna  SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, columna)
);
INSERT INTO #columnas_047 (tabla, columna)
SELECT N'dbo.rep_reportes', c FROM (VALUES (N'id_reporte'), (N'id_tenant'), (N'codigo'), (N'nombre'), (N'descripcion'), (N'modulo'),
    (N'fuente_datos'), (N'tipo_salida'), (N'id_version_activa'), (N'activo'), (N'creado_por'), (N'fecha_creacion')) v (c)
UNION ALL
SELECT N'dbo.rep_reporte_versiones', c FROM (VALUES (N'id_version'), (N'id_reporte'), (N'id_tenant'), (N'numero_version'), (N'estado'),
    (N'contenido_plantilla'), (N'configuracion_json'), (N'hash_sha256'), (N'motivo_cambio'), (N'id_version_origen'), (N'id_cuenta'),
    (N'creado_por'), (N'fecha_creacion'), (N'fecha_publicacion')) v (c)
UNION ALL
SELECT N'dbo.rep_reporte_bitacora', c FROM (VALUES (N'id_bitacora'), (N'id_tenant'), (N'id_reporte'), (N'codigo_reporte'), (N'accion'),
    (N'id_version_anterior'), (N'id_version_nueva'), (N'observaciones'), (N'detalle_json'), (N'id_cuenta'), (N'usuario'),
    (N'id_correlacion'), (N'fecha_hora')) v (c)
UNION ALL
SELECT N'dbo.rep_ejecuciones', c FROM (VALUES (N'id_ejecucion'), (N'id_tenant'), (N'id_empresa'), (N'id_reporte'), (N'id_version'),
    (N'codigo_reporte'), (N'numero_version'), (N'entidad_origen'), (N'id_registro_origen'), (N'parametros_json'), (N'tipo_salida'),
    (N'id_cuenta'), (N'usuario'), (N'id_correlacion'), (N'fecha_hora')) v (c);

DECLARE @errores NVARCHAR(MAX) = N'';

-- a) scripts previos
IF SCHEMA_ID(N'saas') IS NULL SET @errores += N' Falta el esquema saas (script 025).';
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
IF OBJECT_ID(N'seg.fn_tenant_actual', N'IF') IS NULL SET @errores += N' Falta la funcion seg.fn_tenant_actual (script 036).';
IF OBJECT_ID(N'seg.fn_mixta_lectura', N'IF') IS NULL SET @errores += N' Falta la funcion seg.fn_mixta_lectura (script 036).';
IF OBJECT_ID(N'seg.fn_mixta_escritura', N'IF') IS NULL SET @errores += N' Falta la funcion seg.fn_mixta_escritura (script 036).';
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';

-- c) predicados: 0 (primera vez) o 5 (ya aplicado) por tabla, y en ninguna otra politica
DECLARE @tablasCon5 INT = 0;
SELECT @errores += CONCAT(N' La politica tiene ', n.preds, N' predicados sobre ', n.tabla, N' y se esperaban 0 o 5.')
FROM (SELECT t.tabla, (SELECT COUNT(*) FROM sys.security_predicates p
                       WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(t.tabla)) AS preds
      FROM #tablas_047 t) n
WHERE n.preds NOT IN (0, 5);

SELECT @tablasCon5 = COUNT(*) FROM #tablas_047 t
WHERE (SELECT COUNT(*) FROM sys.security_predicates p
       WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(t.tabla)) = 5;

SELECT @errores += N' ' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND p.target_object_id IN (SELECT OBJECT_ID(tabla) FROM #tablas_047);

-- b) estado de los objetos nuevos: ninguno (primera vez) o todos (ya aplicado); a medias, no
DECLARE @tablasExisten INT = (SELECT COUNT(*) FROM #tablas_047 WHERE OBJECT_ID(tabla, N'U') IS NOT NULL);
DECLARE @disparadoresExisten INT = (SELECT COUNT(*) FROM sys.triggers
    WHERE name IN (N'TR_rep_reportes_reglas', N'TR_rep_reporte_versiones_reglas', N'TR_rep_reporte_bitacora_inmutable',
                   N'TR_rep_ejecuciones_inmutable', N'TR_rep_ejecuciones_version'));
DECLARE @objetosExisten INT = @tablasExisten + @disparadoresExisten + @tablasCon5;

IF @objetosExisten NOT IN (0, 13)
    SET @errores += CONCAT(N' Los objetos de este script existen a medias (', @objetosExisten, N' de 13: ', @tablasExisten, N' de 4 tablas, ',
                           @disparadoresExisten, N' de 5 disparadores y ', @tablasCon5, N' de 4 tablas con sus 5 predicados RLS). Revisar a mano antes de seguir.');

SELECT @errores += N' La tabla ' + x.tabla + N' no tiene la columna ' + x.columna + N'.'
FROM #columnas_047 x
WHERE OBJECT_ID(x.tabla, N'U') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);

SELECT @errores += N' La tabla ' + OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id) + N' tiene una columna no prevista: ' + c.name + N'.'
FROM sys.columns c
WHERE c.object_id IN (SELECT OBJECT_ID(tabla) FROM #tablas_047)
  AND NOT EXISTS (SELECT 1 FROM #columnas_047 x WHERE x.tabla = (OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id)) AND x.columna = c.name);

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 047 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50047, @mensaje, 1;
END

CREATE TABLE #precheck_047 (ok BIT NOT NULL);
INSERT INTO #precheck_047 (ok) VALUES (1);

PRINT CONCAT(N'PRECHECK 047 correcto. Objetos nuevos ya existentes: ', @objetosExisten, N' de 13.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_047') IS NULL
    THROW 50047, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @porCrear INT = (SELECT COUNT(*) FROM #tablas_047 WHERE OBJECT_ID(tabla, N'U') IS NULL);
DECLARE @predicadosNuevos INT = 0;
DECLARE @sembrado BIT = 0;
DECLARE @malas INT;
DECLARE @ambitoOriginal SQL_VARIANT = SESSION_CONTEXT(N'ambito');
DECLARE @idReporte INT, @idVersion INT;
DECLARE @plantilla NVARCHAR(MAX), @configuracion NVARCHAR(MAX);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. El reporte (global o del tenant)
    IF OBJECT_ID(N'dbo.rep_reportes', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rep_reportes (
            id_reporte          INT            IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NULL,
            codigo              VARCHAR(60)    NOT NULL,
            nombre              NVARCHAR(150)  NOT NULL,
            descripcion         NVARCHAR(500)  NULL,
            modulo              VARCHAR(40)    NOT NULL,
            fuente_datos        VARCHAR(80)    NOT NULL,
            tipo_salida         VARCHAR(20)    NOT NULL,
            id_version_activa   INT            NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_rep_reportes_activo DEFAULT 1,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_rep_reportes_fecha_creacion DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_rep_reportes PRIMARY KEY CLUSTERED (id_reporte),
            CONSTRAINT CK_rep_reportes_tipo_salida CHECK (tipo_salida IN ('impresion_html', 'excel', 'pdf')),
            -- codigo y modulo: a-z, 0-9 y _ ; fuente_datos ademas admite punto (esquema.fuente, p. ej. psi.constancia)
            CONSTRAINT CK_rep_reportes_codigo CHECK (LEN(codigo) >= 3 AND codigo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_]%'),
            CONSTRAINT CK_rep_reportes_modulo CHECK (LEN(modulo) >= 2 AND modulo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_]%'),
            CONSTRAINT CK_rep_reportes_fuente_datos CHECK (LEN(fuente_datos) >= 3 AND fuente_datos COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_.]%'),
            CONSTRAINT FK_rep_reportes_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant)
        );
        CREATE UNIQUE INDEX UX_rep_reportes_global ON dbo.rep_reportes (codigo) WHERE id_tenant IS NULL;
        CREATE UNIQUE INDEX UX_rep_reportes_tenant ON dbo.rep_reportes (id_tenant, codigo) WHERE id_tenant IS NOT NULL;
        CREATE INDEX IX_rep_reportes_modulo ON dbo.rep_reportes (modulo, codigo);
    END

    -- 2. Las versiones (historico completo; inmutables una vez publicadas)
    IF OBJECT_ID(N'dbo.rep_reporte_versiones', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rep_reporte_versiones (
            id_version          INT            IDENTITY(1,1) NOT NULL,
            id_reporte          INT            NOT NULL,
            id_tenant           INT            NULL,
            numero_version      INT            NOT NULL,
            estado              VARCHAR(15)    NOT NULL,
            contenido_plantilla NVARCHAR(MAX)  NOT NULL,
            configuracion_json  NVARCHAR(MAX)  NULL,
            -- RP6: huella calculada por la BD (UTF-16LE, hexadecimal mayuscula, sin 0x)
            hash_sha256         AS CONVERT(CHAR(64), HASHBYTES('SHA2_256', contenido_plantilla + N'|' + ISNULL(configuracion_json, N'')), 2) PERSISTED NOT NULL,
            motivo_cambio       NVARCHAR(500)  NULL,
            id_version_origen   INT            NULL,
            id_cuenta           INT            NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_rep_reporte_versiones_fecha_creacion DEFAULT SYSUTCDATETIME(),
            fecha_publicacion   DATETIME2(3)   NULL,
            CONSTRAINT PK_rep_reporte_versiones PRIMARY KEY CLUSTERED (id_version),
            CONSTRAINT UX_rep_reporte_versiones_numero UNIQUE (id_reporte, numero_version),
            CONSTRAINT CK_rep_reporte_versiones_estado CHECK (estado IN ('borrador', 'publicada', 'archivada')),
            CONSTRAINT CK_rep_reporte_versiones_numero CHECK (numero_version > 0),
            CONSTRAINT CK_rep_reporte_versiones_publicacion CHECK ((estado = 'borrador' AND fecha_publicacion IS NULL)
                                                                OR (estado <> 'borrador' AND fecha_publicacion IS NOT NULL)),
            CONSTRAINT CK_rep_reporte_versiones_config_json CHECK (configuracion_json IS NULL OR ISJSON(configuracion_json) = 1),
            CONSTRAINT FK_rep_reporte_versiones_reportes FOREIGN KEY (id_reporte) REFERENCES dbo.rep_reportes (id_reporte),
            CONSTRAINT FK_rep_reporte_versiones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FK_rep_reporte_versiones_origen FOREIGN KEY (id_version_origen) REFERENCES dbo.rep_reporte_versiones (id_version)
        );
        -- Un solo borrador abierto por reporte
        CREATE UNIQUE INDEX UX_rep_reporte_versiones_un_borrador ON dbo.rep_reporte_versiones (id_reporte) WHERE estado = 'borrador';
        CREATE INDEX IX_rep_reporte_versiones_tenant ON dbo.rep_reporte_versiones (id_tenant, id_reporte);
    END

    -- 2b. La version activa del reporte (FK diferida: las dos tablas se apuntan entre si)
    IF OBJECT_ID(N'FK_rep_reportes_version_activa', N'F') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.rep_reportes ADD CONSTRAINT FK_rep_reportes_version_activa
    FOREIGN KEY (id_version_activa) REFERENCES dbo.rep_reporte_versiones (id_version);';

    -- 3. Bitacora de reportes (inmutable; sin claves foraneas a proposito, como la auditoria del 045)
    IF OBJECT_ID(N'dbo.rep_reporte_bitacora', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rep_reporte_bitacora (
            id_bitacora         BIGINT         IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NULL,
            id_reporte          INT            NOT NULL,
            codigo_reporte      VARCHAR(60)    NOT NULL,
            accion              VARCHAR(30)    NOT NULL,
            id_version_anterior INT            NULL,
            id_version_nueva    INT            NULL,
            observaciones       NVARCHAR(1000) NULL,
            detalle_json        NVARCHAR(MAX)  NULL,
            id_cuenta           INT            NULL,
            usuario             NVARCHAR(100)  NOT NULL,
            id_correlacion      VARCHAR(64)    NULL,
            fecha_hora          DATETIME2(3)   NOT NULL CONSTRAINT DF_rep_reporte_bitacora_fecha_hora DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_rep_reporte_bitacora PRIMARY KEY CLUSTERED (id_bitacora),
            CONSTRAINT CK_rep_reporte_bitacora_accion CHECK (accion IN ('CREAR_REPORTE', 'CREAR_BORRADOR', 'EDITAR_BORRADOR', 'DESCARTAR_BORRADOR',
                                                                       'PUBLICAR', 'ARCHIVAR', 'CLONAR_GLOBAL', 'REVERTIR', 'ACTIVAR', 'DESACTIVAR')),
            CONSTRAINT CK_rep_reporte_bitacora_detalle_json CHECK (detalle_json IS NULL OR ISJSON(detalle_json) = 1)
        );
        CREATE INDEX IX_rep_reporte_bitacora_reporte ON dbo.rep_reporte_bitacora (id_reporte, fecha_hora DESC, id_bitacora DESC);
        CREATE INDEX IX_rep_reporte_bitacora_tenant ON dbo.rep_reporte_bitacora (id_tenant, fecha_hora DESC);
        CREATE INDEX IX_rep_reporte_bitacora_correlacion ON dbo.rep_reporte_bitacora (id_correlacion);
    END

    -- 4. Ejecuciones (de tenant; inmutables)
    IF OBJECT_ID(N'dbo.rep_ejecuciones', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rep_ejecuciones (
            id_ejecucion        BIGINT         IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            id_reporte          INT            NOT NULL,
            id_version          INT            NOT NULL,
            codigo_reporte      VARCHAR(60)    NOT NULL,
            numero_version      INT            NOT NULL,
            entidad_origen      VARCHAR(60)    NULL,
            id_registro_origen  BIGINT         NULL,
            parametros_json     NVARCHAR(MAX)  NULL,
            tipo_salida         VARCHAR(20)    NOT NULL,
            id_cuenta           INT            NULL,
            usuario             NVARCHAR(100)  NOT NULL,
            id_correlacion      VARCHAR(64)    NULL,
            fecha_hora          DATETIME2(3)   NOT NULL CONSTRAINT DF_rep_ejecuciones_fecha_hora DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_rep_ejecuciones PRIMARY KEY CLUSTERED (id_ejecucion),
            CONSTRAINT CK_rep_ejecuciones_tipo_salida CHECK (tipo_salida IN ('impresion_html', 'excel', 'pdf')),
            CONSTRAINT CK_rep_ejecuciones_parametros_json CHECK (parametros_json IS NULL OR ISJSON(parametros_json) = 1),
            CONSTRAINT FK_rep_ejecuciones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FK_rep_ejecuciones_reportes FOREIGN KEY (id_reporte) REFERENCES dbo.rep_reportes (id_reporte),
            CONSTRAINT FK_rep_ejecuciones_versiones FOREIGN KEY (id_version) REFERENCES dbo.rep_reporte_versiones (id_version)
        );
        CREATE INDEX IX_rep_ejecuciones_tenant_fecha ON dbo.rep_ejecuciones (id_tenant, fecha_hora DESC, id_ejecucion DESC);
        CREATE INDEX IX_rep_ejecuciones_origen ON dbo.rep_ejecuciones (id_tenant, entidad_origen, id_registro_origen);
        CREATE INDEX IX_rep_ejecuciones_version ON dbo.rep_ejecuciones (id_version);
    END

    -- 5. Disparadores
    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_rep_reportes_reglas' AND parent_id = OBJECT_ID(N'dbo.rep_reportes'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_rep_reportes_reglas
ON dbo.rep_reportes
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id_reporte = i.id_reporte
               WHERE ISNULL(i.id_tenant, -1) <> ISNULL(d.id_tenant, -1) OR i.codigo <> d.codigo)
        THROW 50047, N''Un reporte no cambia de tenant ni de codigo: para personalizarlo se clona.'', 1;

    -- RP7: la version activa es una version PUBLICADA del mismo reporte
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.id_version_activa IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.rep_reporte_versiones v
                                 WHERE v.id_version = i.id_version_activa AND v.id_reporte = i.id_reporte AND v.estado = ''publicada''))
        THROW 50047, N''La version activa de un reporte debe ser una version publicada de ese mismo reporte.'', 1;
END;';

    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_rep_reporte_versiones_reglas' AND parent_id = OBJECT_ID(N'dbo.rep_reporte_versiones'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_rep_reporte_versiones_reglas
ON dbo.rep_reporte_versiones
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    -- Solo se borra un borrador
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.estado <> ''borrador''
               AND NOT EXISTS (SELECT 1 FROM inserted i WHERE i.id_version = d.id_version))
        THROW 50047, N''Una version publicada o archivada no se borra.'', 1;

    -- Al insertar nace borrador o publicada
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.estado NOT IN (''borrador'', ''publicada'')
               AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.id_version = i.id_version))
        THROW 50047, N''Una version nueva nace como borrador o publicada.'', 1;

    -- Misma id_tenant que su reporte
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE NOT EXISTS (SELECT 1 FROM dbo.rep_reportes r
                                 WHERE r.id_reporte = i.id_reporte AND ISNULL(r.id_tenant, -1) = ISNULL(i.id_tenant, -1)))
        THROW 50047, N''La version debe tener el mismo tenant que su reporte (global con global, tenant con tenant).'', 1;

    -- No cambian el reporte, el tenant ni el numero
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id_version = i.id_version
               WHERE i.id_reporte <> d.id_reporte OR ISNULL(i.id_tenant, -1) <> ISNULL(d.id_tenant, -1) OR i.numero_version <> d.numero_version)
        THROW 50047, N''Una version no cambia de reporte, de tenant ni de numero.'', 1;

    -- RP5: publicada o archivada solo pasa de publicada a archivada, y nada mas cambia
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id_version = i.id_version
               WHERE d.estado IN (''publicada'', ''archivada'')
                 AND (NOT (i.estado = d.estado OR (d.estado = ''publicada'' AND i.estado = ''archivada''))
                      OR EXISTS (SELECT i.hash_sha256, i.motivo_cambio, i.id_version_origen, i.id_cuenta, i.creado_por, i.fecha_creacion, i.fecha_publicacion
                                 EXCEPT
                                 SELECT d.hash_sha256, d.motivo_cambio, d.id_version_origen, d.id_cuenta, d.creado_por, d.fecha_creacion, d.fecha_publicacion)))
        THROW 50047, N''Una version publicada es inmutable: solo puede pasar a archivada.'', 1;

    -- RP7: la version activa de un reporte no se archiva
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.rep_reportes r ON r.id_version_activa = i.id_version WHERE i.estado <> ''publicada'')
        THROW 50047, N''No se puede archivar la version activa de un reporte: active otra antes.'', 1;
END;';

    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_rep_reporte_bitacora_inmutable' AND parent_id = OBJECT_ID(N'dbo.rep_reporte_bitacora'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_rep_reporte_bitacora_inmutable
ON dbo.rep_reporte_bitacora
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50047, N''La bitacora de reportes es de solo lectura: no se permite modificar ni borrar filas.'', 1;
END;';

    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_rep_ejecuciones_inmutable' AND parent_id = OBJECT_ID(N'dbo.rep_ejecuciones'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_rep_ejecuciones_inmutable
ON dbo.rep_ejecuciones
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50047, N''El historial de ejecuciones de reportes es de solo lectura: no se permite modificar ni borrar filas.'', 1;
END;';

    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_rep_ejecuciones_version' AND parent_id = OBJECT_ID(N'dbo.rep_ejecuciones'))
        EXEC sys.sp_executesql N'
CREATE TRIGGER dbo.TR_rep_ejecuciones_version
ON dbo.rep_ejecuciones
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE NOT EXISTS (SELECT 1 FROM dbo.rep_reporte_versiones v
                                 WHERE v.id_version = i.id_version AND v.id_reporte = i.id_reporte AND v.numero_version = i.numero_version
                                   AND v.estado <> ''borrador'' AND (v.id_tenant IS NULL OR v.id_tenant = i.id_tenant)))
        THROW 50047, N''La ejecucion debe usar una version publicada del reporte y numero indicados, global o del mismo tenant.'', 1;
END;';

    -- 6. Politica: 5 predicados por tabla. ALTER SECURITY POLICY ... ADD no cambia el estado (encendida o apagada).
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reportes'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant) ON dbo.rep_reportes,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reportes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reportes AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reportes BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reportes BEFORE DELETE;';
        SET @predicadosNuevos += 5;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reporte_versiones'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant) ON dbo.rep_reporte_versiones,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_versiones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_versiones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_versiones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_versiones BEFORE DELETE;';
        SET @predicadosNuevos += 5;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reporte_bitacora'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant) ON dbo.rep_reporte_bitacora,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_bitacora AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_bitacora AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_bitacora BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.rep_reporte_bitacora BEFORE DELETE;';
        SET @predicadosNuevos += 5;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_ejecuciones'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rep_ejecuciones,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rep_ejecuciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rep_ejecuciones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rep_ejecuciones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rep_ejecuciones BEFORE DELETE;';
        SET @predicadosNuevos += 5;
    END

    -- 7. Semilla: reporte GLOBAL psi_constancia_licencia v1, publicada y activa (igual que Imprimir.cshtml al 2026-11-22).
    --    Si el reporte global ya existe, no se toca (el POSTCHECK verifica que tenga una version activa publicada).
    IF NOT EXISTS (SELECT 1 FROM dbo.rep_reportes WHERE id_tenant IS NULL AND codigo = 'psi_constancia_licencia')
    BEGIN
        -- Las filas globales solo se escriben con ambito = plataforma (seg.fn_mixta_escritura)
        EXEC sys.sp_set_session_context @key = N'ambito', @value = N'plataforma';

        SET @configuracion = CAST(N'' AS NVARCHAR(MAX)) + N'{
  "tamano_papel": "letter",
  "pide_resultado": true,
  "hoja": { "ancho_mm": 215, "alto_mm": 275, "fuente": "\"Arial\", \"Helvetica\", sans-serif" },
  "fecha": { "base_mm": 35, "dia_desde_mm": 14, "dia_hasta_mm": 25, "mes_desde_mm": 33, "mes_hasta_mm": 68,
             "anio_desde_mm": 80, "anio_hasta_mm": 92, "tamano_pt": 13, "tamano_guia_pt": 10 },
  "cuerpo": { "arriba_mm": 74, "izquierda_mm": 26, "ancho_mm": 160, "renglon_mm": 14, "tamano_pt": 15, "tamano_minimo_junto_pt": 10 },
  "pruebas": { "arriba_mm": 204, "izquierda_mm": 13, "ancho_mm": 190, "sangria_mm": 98, "renglon_mm": 9, "tamano_pt": 12 },
  "textos": {
    "conclusion_apto": "posee personalidad estable, APTO para conducir.",
    "conclusion_no_apto": "no re\u00fane las condiciones psicol\u00f3gicas requeridas, NO APTO para conducir.",
    "dni_vacio": "________________"
  }
}';

        -- Variables que entrega la fuente psi.constancia: cfg, modo_guia, fecha.dia / fecha.mes / fecha.anio_2d,
        -- paciente.tratamiento / paciente.nombre_completo / paciente.dni (puede ser nulo), constancia.resultado, pruebas_texto.
        -- El cuerpo va en una sola linea para no agregar espacios entre sus partes (como hoy en Razor).
        SET @plantilla = CAST(N'' AS NVARCHAR(MAX)) + N'<style>
  :root {
    --hoja-ancho: {{ cfg.hoja.ancho_mm }}mm;
    --hoja-alto: {{ cfg.hoja.alto_mm }}mm;
    --letra: {{ cfg.hoja.fuente }};
    --fecha-base: {{ cfg.fecha.base_mm }}mm;
    --dia-desde: {{ cfg.fecha.dia_desde_mm }}mm;   --dia-hasta: {{ cfg.fecha.dia_hasta_mm }}mm;
    --mes-desde: {{ cfg.fecha.mes_desde_mm }}mm;   --mes-hasta: {{ cfg.fecha.mes_hasta_mm }}mm;
    --anio-desde: {{ cfg.fecha.anio_desde_mm }}mm; --anio-hasta: {{ cfg.fecha.anio_hasta_mm }}mm;
    --cuerpo-arriba: {{ cfg.cuerpo.arriba_mm }}mm;
    --cuerpo-izquierda: {{ cfg.cuerpo.izquierda_mm }}mm;
    --cuerpo-ancho: {{ cfg.cuerpo.ancho_mm }}mm;
    --cuerpo-renglon: {{ cfg.cuerpo.renglon_mm }}mm;
    --pruebas-arriba: {{ cfg.pruebas.arriba_mm }}mm;
    --pruebas-izquierda: {{ cfg.pruebas.izquierda_mm }}mm;
    --pruebas-ancho: {{ cfg.pruebas.ancho_mm }}mm;
    --pruebas-sangria: {{ cfg.pruebas.sangria_mm }}mm;
    --pruebas-renglon: {{ cfg.pruebas.renglon_mm }}mm;
  }
  @page { size: {{ cfg.tamano_papel }}; margin: 0; }
  .hoja { position: relative; width: var(--hoja-ancho); height: var(--hoja-alto); margin: 16px auto; background: #fff;
          box-shadow: 0 0 0 1px #bbb; overflow: hidden; font-family: var(--letra); color: #000; }
  .fecha { position: absolute; top: calc(var(--fecha-base) - 0.81em); font-size: {{ cfg.fecha.tamano_pt }}pt; line-height: 1; white-space: nowrap;
           text-align: center; }
  .cuerpo { position: absolute; top: var(--cuerpo-arriba); left: var(--cuerpo-izquierda); width: var(--cuerpo-ancho);
            font-size: {{ cfg.cuerpo.tamano_pt }}pt; line-height: var(--cuerpo-renglon); }
  .cuerpo .junto { white-space: nowrap; }
  .pruebas { position: absolute; top: var(--pruebas-arriba); left: var(--pruebas-izquierda); width: var(--pruebas-ancho);
             font-size: {{ cfg.pruebas.tamano_pt }}pt; line-height: var(--pruebas-renglon); text-indent: var(--pruebas-sangria); }
  .guia { position: absolute; color: #9aa0a6; font-size: 10pt; font-weight: bold; white-space: nowrap; }
  .renglon { position: absolute; left: 12mm; right: 12mm; border-bottom: 0.3mm dotted #c0c4c8; }
  .guia.fecha { font-size: {{ cfg.fecha.tamano_guia_pt }}pt; }
  .blanco { position: absolute; top: var(--fecha-base); border-top: 0.3mm solid #9aa0a6; }
  @media print {
    html, body { background: #fff; }
    .barra { display: none; }
    .hoja { margin: 0; box-shadow: none; }
  }
</style>
<div class="hoja">
{{~ if modo_guia ~}}
  <div class="guia" style="top:12mm; left:40mm; font-size:14pt;">COLEGIO DE PSIC&Oacute;LOGOS DE HONDURAS</div>
  <div class="blanco" style="left:var(--dia-desde); width:calc(var(--dia-hasta) - var(--dia-desde));"></div>
  <div class="guia fecha" style="left:var(--dia-hasta); width:calc(var(--mes-desde) - var(--dia-hasta));">de</div>
  <div class="blanco" style="left:var(--mes-desde); width:calc(var(--mes-hasta) - var(--mes-desde));"></div>
  <div class="guia fecha" style="left:var(--mes-hasta); width:calc(var(--anio-desde) - var(--mes-hasta));">del 20</div>
  <div class="blanco" style="left:var(--anio-desde); width:calc(var(--anio-hasta) - var(--anio-desde));"></div>
  <div class="guia" style="top:46mm; left:12mm;">CONSTANCIA DE EXAMEN PSICOL&Oacute;GICO PARA SOLICITUD DE LICENCIA DE CONDUCIR</div>
  {{~ for i in 1..4 ~}}
  <div class="renglon" style="top:calc(var(--cuerpo-arriba) + {{ i }} * var(--cuerpo-renglon) - 3mm);"></div>
  {{~ end ~}}
  <div class="guia" style="top:203mm; left:85mm;">VALOR L. 200.00</div>
  <div class="guia" style="top:var(--pruebas-arriba); left:var(--pruebas-izquierda);">PRUEBAS PSICOL&Oacute;GICAS APLICADAS:</div>
  <div class="renglon" style="top:calc(var(--pruebas-arriba) + var(--pruebas-renglon) - 2mm);"></div>
  <div class="renglon" style="top:calc(var(--pruebas-arriba) + 2 * var(--pruebas-renglon) - 2mm);"></div>
{{~ end ~}}
  <div class="fecha" style="left:var(--dia-desde); width:calc(var(--dia-hasta) - var(--dia-desde));">{{ fecha.dia | html.escape }}</div>
  <div class="fecha" style="left:var(--mes-desde); width:calc(var(--mes-hasta) - var(--mes-desde));">{{ fecha.mes | html.escape }}</div>
  <div class="fecha" style="left:var(--anio-desde); width:calc(var(--anio-hasta) - var(--anio-desde));">{{ fecha.anio_2d | html.escape }}</div>
  <div class="cuerpo" data-minimo-pt="{{ cfg.cuerpo.tamano_minimo_junto_pt }}">Por este medio se hace constar que {{ paciente.tratamiento | html.escape }} <span class="junto">{{ paciente.nombre_completo | html.escape }}</span>, con <span class="junto">DNI {{ if paciente.dni }}{{ paciente.dni | html.escape }}{{ else }}{{ cfg.textos.dni_vacio | html.escape }}{{ end }}</span>, {{ if constancia.resultado == "no_apto" }}{{ cfg.textos.conclusion_no_apto | html.escape }}{{ else }}{{ cfg.textos.conclusion_apto | html.escape }}{{ end }}</div>
  <div class="pruebas">{{ pruebas_texto | html.escape }}</div>
</div>
';

        INSERT INTO dbo.rep_reportes (id_tenant, codigo, nombre, descripcion, modulo, fuente_datos, tipo_salida, creado_por)
        VALUES (NULL, 'psi_constancia_licencia', N'Constancia de examen psicologico para licencia de conducir',
                N'Se imprime sobre la hoja membretada del Colegio de Psicologos de Honduras (215 x 275 mm, papel Carta).',
                'psicologia', 'psi.constancia', 'impresion_html', N'script_047');
        SET @idReporte = SCOPE_IDENTITY();

        INSERT INTO dbo.rep_reporte_versiones (id_reporte, id_tenant, numero_version, estado, contenido_plantilla, configuracion_json,
                                               motivo_cambio, creado_por, fecha_publicacion)
        VALUES (@idReporte, NULL, 1, 'publicada', @plantilla, @configuracion,
                N'Version inicial: migrada de Pages/Psicologia/Constancias/Imprimir.cshtml (plantilla licencia_conducir, version 1).',
                N'script_047', SYSUTCDATETIME());
        SET @idVersion = SCOPE_IDENTITY();

        UPDATE dbo.rep_reportes SET id_version_activa = @idVersion WHERE id_reporte = @idReporte;

        INSERT INTO dbo.rep_reporte_bitacora (id_tenant, id_reporte, codigo_reporte, accion, id_version_anterior, id_version_nueva, observaciones, usuario)
        VALUES (NULL, @idReporte, 'psi_constancia_licencia', 'CREAR_REPORTE', NULL, NULL, N'Semilla del script 047.', N'script_047'),
               (NULL, @idReporte, 'psi_constancia_licencia', 'PUBLICAR', NULL, @idVersion, N'Version 1 migrada de Imprimir.cshtml.', N'script_047'),
               (NULL, @idReporte, 'psi_constancia_licencia', 'ACTIVAR', NULL, @idVersion, N'Version 1 activa.', N'script_047');

        EXEC sys.sp_set_session_context @key = N'ambito', @value = @ambitoOriginal;
        SET @sembrado = 1;
    END

    -- 8. POSTCHECK (dentro de la transaccion: si algo no cuadra, se revierte todo)
    -- 8a. columnas exactas
    SELECT @malas = COUNT(*) FROM #columnas_047 x
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);
    IF @malas > 0 THROW 50047, N'POSTCHECK: a alguna tabla nueva le falta una columna. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.columns c
    WHERE c.object_id IN (SELECT OBJECT_ID(tabla) FROM #tablas_047)
      AND NOT EXISTS (SELECT 1 FROM #columnas_047 x WHERE x.tabla = (OBJECT_SCHEMA_NAME(c.object_id) + N'.' + OBJECT_NAME(c.object_id)) AND x.columna = c.name);
    IF @malas > 0 THROW 50047, N'POSTCHECK: alguna tabla nueva tiene una columna no prevista. Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.computed_columns WHERE object_id = OBJECT_ID(N'dbo.rep_reporte_versiones') AND name = N'hash_sha256' AND is_persisted = 1)
        THROW 50047, N'POSTCHECK: hash_sha256 debe ser una columna calculada y persistida. Se revierte.', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.rep_ejecuciones') AND name = N'id_tenant' AND is_nullable = 1)
        THROW 50047, N'POSTCHECK: dbo.rep_ejecuciones.id_tenant debe ser obligatorio. Se revierte.', 1;

    -- 8b. restricciones habilitadas y confiables
    SELECT @malas = COUNT(*) FROM (VALUES (N'CK_rep_reportes_tipo_salida'), (N'CK_rep_reportes_codigo'), (N'CK_rep_reportes_modulo'),
        (N'CK_rep_reportes_fuente_datos'), (N'CK_rep_reporte_versiones_estado'), (N'CK_rep_reporte_versiones_numero'),
        (N'CK_rep_reporte_versiones_publicacion'), (N'CK_rep_reporte_versiones_config_json'), (N'CK_rep_reporte_bitacora_accion'),
        (N'CK_rep_reporte_bitacora_detalle_json'), (N'CK_rep_ejecuciones_tipo_salida'), (N'CK_rep_ejecuciones_parametros_json')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0 THROW 50047, N'POSTCHECK: alguna restriccion CHECK de las tablas rep_ falta o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM (VALUES (N'FK_rep_reportes_tenants'), (N'FK_rep_reportes_version_activa'), (N'FK_rep_reporte_versiones_reportes'),
        (N'FK_rep_reporte_versiones_tenants'), (N'FK_rep_reporte_versiones_origen'), (N'FK_rep_ejecuciones_tenants'),
        (N'FK_rep_ejecuciones_reportes'), (N'FK_rep_ejecuciones_versiones')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0 THROW 50047, N'POSTCHECK: alguna clave foranea de las tablas rep_ falta o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM (VALUES (N'dbo.rep_reportes', N'UX_rep_reportes_global'), (N'dbo.rep_reportes', N'UX_rep_reportes_tenant'),
        (N'dbo.rep_reporte_versiones', N'UX_rep_reporte_versiones_numero'), (N'dbo.rep_reporte_versiones', N'UX_rep_reporte_versiones_un_borrador')) v (tabla, indice)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.indice AND i.object_id = OBJECT_ID(v.tabla) AND i.is_unique = 1);
    IF @malas > 0 THROW 50047, N'POSTCHECK: falta algun indice unico de las tablas rep_. Se revierte.', 1;

    -- 8c. disparadores habilitados (2 INSTEAD OF y 3 AFTER)
    SELECT @malas = COUNT(*) FROM (VALUES (N'TR_rep_reportes_reglas', N'dbo.rep_reportes', 0), (N'TR_rep_reporte_versiones_reglas', N'dbo.rep_reporte_versiones', 0),
        (N'TR_rep_reporte_bitacora_inmutable', N'dbo.rep_reporte_bitacora', 1), (N'TR_rep_ejecuciones_inmutable', N'dbo.rep_ejecuciones', 1),
        (N'TR_rep_ejecuciones_version', N'dbo.rep_ejecuciones', 0)) v (nombre, tabla, instead_of)
    WHERE NOT EXISTS (SELECT 1 FROM sys.triggers t WHERE t.name = v.nombre AND t.parent_id = OBJECT_ID(v.tabla)
                      AND t.is_instead_of_trigger = v.instead_of AND t.is_disabled = 0);
    IF @malas > 0 THROW 50047, N'POSTCHECK: falta algun disparador habilitado de las tablas rep_. Se revierte.', 1;

    -- 8d. politica: 5 predicados por tabla, 1 filtro, y con la funcion que le toca
    SELECT @malas = COUNT(*) FROM #tablas_047 t
    WHERE (SELECT COUNT(*) FROM sys.security_predicates p WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(t.tabla)) <> 5
       OR (SELECT COUNT(*) FROM sys.security_predicates p WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(t.tabla)
           AND p.predicate_type_desc = N'FILTER') <> 1;
    IF @malas > 0 THROW 50047, N'POSTCHECK: cada tabla rep_ debe tener 5 predicados (1 filtro y 4 bloqueos). Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates p JOIN #tablas_047 t ON p.target_object_id = OBJECT_ID(t.tabla)
    WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant')
      AND NOT ((t.mixta = 0 AND p.predicate_definition LIKE N'%fn_tenant_actual%')
            OR (t.mixta = 1 AND p.predicate_type_desc = N'FILTER' AND p.predicate_definition LIKE N'%fn_mixta_lectura%')
            OR (t.mixta = 1 AND p.predicate_type_desc = N'BLOCK' AND p.predicate_definition LIKE N'%fn_mixta_escritura%'));
    IF @malas > 0 THROW 50047, N'POSTCHECK: algun predicado de las tablas rep_ no usa la funcion que le corresponde. Se revierte.', 1;

    -- 8e. la semilla: version activa publicada, numero 1 o mayor, y huella igual a la del contenido
    IF NOT EXISTS (SELECT 1 FROM dbo.rep_reportes r JOIN dbo.rep_reporte_versiones v ON v.id_version = r.id_version_activa
                   WHERE r.id_tenant IS NULL AND r.codigo = 'psi_constancia_licencia' AND v.estado = 'publicada' AND v.id_tenant IS NULL
                     AND v.hash_sha256 = CONVERT(CHAR(64), HASHBYTES('SHA2_256', v.contenido_plantilla + N'|' + ISNULL(v.configuracion_json, N'')), 2)
                     AND ISJSON(v.configuracion_json) = 1)
        THROW 50047, N'POSTCHECK: el reporte global psi_constancia_licencia no tiene una version activa publicada y valida. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @porCrear = 0 AND @predicadosNuevos = 0 AND @sembrado = 0
        PRINT N'Nada que hacer: las tablas rep_, sus disparadores, los predicados RLS y la semilla ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 047 aplicado: ', @porCrear, N' tabla(s) rep_ creadas, ', @predicadosNuevos, N' predicado(s) RLS y ',
                     CASE WHEN @sembrado = 1 THEN N'la semilla psi_constancia_licencia v1 publicada y activa.' ELSE N'la semilla ya existia.' END);
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'ambito', @value = @ambitoOriginal;
    THROW;
END CATCH;
GO

-- Resultado: filas por tabla (la semilla: 1 reporte, 1 version y 3 de bitacora; sin ejecuciones) y la version activa
SELECT t.tabla, SUM(p.rows) AS filas_reales
FROM (VALUES (N'dbo.rep_reportes'), (N'dbo.rep_reporte_versiones'), (N'dbo.rep_reporte_bitacora'), (N'dbo.rep_ejecuciones')) t (tabla)
LEFT JOIN sys.partitions p ON p.object_id = OBJECT_ID(t.tabla) AND p.index_id IN (0, 1)
GROUP BY t.tabla;

SELECT r.codigo, r.id_version_activa, v.numero_version, v.estado, v.hash_sha256, LEN(v.contenido_plantilla) AS largo_plantilla
FROM dbo.rep_reportes r LEFT JOIN dbo.rep_reporte_versiones v ON v.id_version = r.id_version_activa
WHERE r.id_tenant IS NULL;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita los 20 predicados, la FK de la version activa y las 4 tablas (con sus disparadores e indices).
--   Se detiene si hay algo mas que la semilla (sys.partitions no pasa por la RLS: cuenta las filas reales): alguna ejecucion,
--   mas de 1 reporte, mas de 1 version o mas de 3 filas de bitacora. Un historial no se descarta sin una decision expresa.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM (VALUES (N'dbo.rep_ejecuciones', 0), (N'dbo.rep_reportes', 1), (N'dbo.rep_reporte_versiones', 1), (N'dbo.rep_reporte_bitacora', 3)) v (tabla, maximo)
               WHERE OBJECT_ID(v.tabla, N'U') IS NOT NULL
                 AND (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(v.tabla) AND p.index_id IN (0, 1)) > v.maximo)
        THROW 50047, N'ROLLBACK 047 detenido: las tablas rep_ tienen filas ademas de la semilla y se perderian. Decidirlo expresamente antes de seguir.', 1;

    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_ejecuciones'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.rep_ejecuciones, DROP BLOCK PREDICATE ON dbo.rep_ejecuciones AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.rep_ejecuciones AFTER UPDATE, DROP BLOCK PREDICATE ON dbo.rep_ejecuciones BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.rep_ejecuciones BEFORE DELETE;';
    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reporte_bitacora'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.rep_reporte_bitacora, DROP BLOCK PREDICATE ON dbo.rep_reporte_bitacora AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.rep_reporte_bitacora AFTER UPDATE, DROP BLOCK PREDICATE ON dbo.rep_reporte_bitacora BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.rep_reporte_bitacora BEFORE DELETE;';
    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reporte_versiones'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.rep_reporte_versiones, DROP BLOCK PREDICATE ON dbo.rep_reporte_versiones AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.rep_reporte_versiones AFTER UPDATE, DROP BLOCK PREDICATE ON dbo.rep_reporte_versiones BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.rep_reporte_versiones BEFORE DELETE;';
    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rep_reportes'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.rep_reportes, DROP BLOCK PREDICATE ON dbo.rep_reportes AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.rep_reportes AFTER UPDATE, DROP BLOCK PREDICATE ON dbo.rep_reportes BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.rep_reportes BEFORE DELETE;';

    -- Las tablas inmutables y la de versiones se borran con DROP TABLE (no pasa por los disparadores de fila).
    IF OBJECT_ID(N'dbo.rep_ejecuciones', N'U') IS NOT NULL DROP TABLE dbo.rep_ejecuciones;
    IF OBJECT_ID(N'dbo.rep_reporte_bitacora', N'U') IS NOT NULL DROP TABLE dbo.rep_reporte_bitacora;
    IF OBJECT_ID(N'FK_rep_reportes_version_activa', N'F') IS NOT NULL ALTER TABLE dbo.rep_reportes DROP CONSTRAINT FK_rep_reportes_version_activa;
    IF OBJECT_ID(N'dbo.rep_reporte_versiones', N'U') IS NOT NULL DROP TABLE dbo.rep_reporte_versiones;
    IF OBJECT_ID(N'dbo.rep_reportes', N'U') IS NOT NULL DROP TABLE dbo.rep_reportes;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
