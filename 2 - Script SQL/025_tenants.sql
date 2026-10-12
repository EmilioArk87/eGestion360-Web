-- ============================================================
-- Script   : 025_tenants.sql
-- Proposito: Paso F1.1 del plan de la Fase 1 (plan de arquitectura SaaS multi-tenant, ADR-019):
--              * Crea los esquemas saas (plano de control) y seg (seguridad y RLS, vacio por ahora), ADR-013.
--              * Crea saas.tenants: la cuenta comercial, que es quien paga y la frontera de aislamiento (ADR-002).
--              * Da de alta los 4 tenants iniciales (decision D18):
--                  SIP      (interno, activo)      <- empresa 1  SIP Tecnologia
--                  DEMO     (demo, activo)         <- empresa 2  Demo
--                  TRANSGAR (cliente, activo)      <- empresa 4  Transgar
--                  IISE     (cliente, suspendido)  <- empresa 3  inactiva y sin datos de operacion
--              * Agrega dbo.empresas.id_tenant (NULLABLE por ahora, ADR-001: expandir -> migrar -> contraer),
--                con su clave foranea a saas.tenants y un indice, y la llena con el tenant de cada empresa.
--            No toca ninguna otra tabla ni ninguna fila de datos de negocio. El resto de las tablas de tenant
--            recibe id_tenant en los scripts 026 a 030 (F1.2).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-11
-- BD       : eBD_SPD
-- Requiere : 023 aplicado. La version de la aplicacion publicada NO necesita cambiar antes: ignora la columna
--            nueva (EF solo lee y escribe las columnas que mapea) y las empresas que cree quedan con
--            id_tenant NULL hasta que se publique la version con F1.1. Aplicar este script justo antes de
--            publicar esa version.
-- Rollback : ver la seccion ROLLBACK al final. Solo vale mientras nada dependa de saas.tenants ni de
--            empresas.id_tenant (es decir, antes de los scripts 026 en adelante).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta si:
--        a) alguna de las 4 empresas esperadas no existe con su id y su codigo, o esta eliminada;
--        b) existe otra empresa sin tenant asignado en D18 (hay que decidir su tenant antes de seguir);
--        c) empresas.id_tenant ya existe con un tipo distinto de INT, o una empresa esperada ya tiene
--           id_tenant apuntando a un tenant distinto del de D18;
--        d) saas.tenants ya existe con columnas distintas a las esperadas.
--   2. CAMBIO (una sola transaccion): esquemas, tabla, tenants, columna, clave foranea, indice y llenado.
--   3. POSTCHECK dentro de la misma transaccion: si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los demas
--     bloques exigen; si el PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice. No sobrescribe tenants existentes.
--   * Los nombres de los tenants salen de las propias empresas (nombre comercial o razon social): el script
--     no lleva caracteres fuera de ASCII.
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
IF OBJECT_ID(N'tempdb..#precheck_025') IS NOT NULL DROP TABLE #precheck_025;
IF OBJECT_ID(N'tempdb..#tenants_025') IS NOT NULL DROP TABLE #tenants_025;

CREATE TABLE #tenants_025 (
    codigo          VARCHAR(30)    NOT NULL PRIMARY KEY,
    tipo            VARCHAR(10)    NOT NULL,
    estado          VARCHAR(12)    NOT NULL,
    motivo          NVARCHAR(300)  NULL,
    id_empresa      INT            NOT NULL,
    codigo_empresa  NVARCHAR(50)   NOT NULL
);

INSERT INTO #tenants_025 (codigo, tipo, estado, motivo, id_empresa, codigo_empresa) VALUES
    ('SIP',      'interno', 'activo',     NULL, 1, N'00000000000000000001'),
    ('DEMO',     'demo',    'activo',     NULL, 2, N'0000000000000000002'),
    ('IISE',     'cliente', 'suspendido', N'Empresa inactiva y sin datos de operacion al pasar a tenants (D18, 2026-10-11).', 3, N'0000000000000000003'),
    ('TRANSGAR', 'cliente', 'activo',     NULL, 4, N'TRANSGAR');

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @tieneColumna BIT = CASE WHEN COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NULL THEN 0 ELSE 1 END;
DECLARE @existeTabla BIT = CASE WHEN OBJECT_ID(N'saas.tenants', N'U') IS NULL THEN 0 ELSE 1 END;

-- a) las 4 empresas esperadas existen con su codigo y no estan eliminadas
SELECT @errores += N' La empresa ' + CAST(x.id_empresa AS NVARCHAR(10)) + N' (codigo ' + x.codigo_empresa
                 + N') no existe con ese codigo o esta eliminada.'
FROM #tenants_025 x
WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e
                  WHERE e.id_empresa = x.id_empresa AND RTRIM(e.codigo) = x.codigo_empresa AND e.eliminado = 0);

-- b) ninguna otra empresa queda sin tenant
IF @tieneColumna = 0
BEGIN
    SELECT @errores += N' La empresa ' + CAST(e.id_empresa AS NVARCHAR(10)) + N' no tiene tenant asignado en D18.'
    FROM dbo.empresas e
    WHERE e.id_empresa NOT IN (SELECT id_empresa FROM #tenants_025);
END
ELSE
BEGIN
    EXEC sys.sp_executesql
        N'SELECT @e += N'' La empresa '' + CAST(e.id_empresa AS NVARCHAR(10)) + N'' no tiene tenant asignado.''
          FROM dbo.empresas e
          WHERE e.id_tenant IS NULL AND e.id_empresa NOT IN (SELECT id_empresa FROM #tenants_025);',
        N'@e NVARCHAR(MAX) OUTPUT', @e = @errores OUTPUT;

    -- c) tipo de la columna y empresas esperadas ya asignadas a otro tenant
    SELECT @errores += N' empresas.id_tenant ya existe con un tipo distinto de INT.'
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.empresas') AND name = N'id_tenant' AND system_type_id <> 56;

    IF @existeTabla = 1
    BEGIN
        EXEC sys.sp_executesql
            N'SELECT @e += N'' La empresa '' + CAST(e.id_empresa AS NVARCHAR(10)) + N'' ya apunta a otro tenant distinto del de D18.''
              FROM #tenants_025 x
              JOIN dbo.empresas e ON e.id_empresa = x.id_empresa
              JOIN saas.tenants t ON t.codigo = x.codigo
              WHERE e.id_tenant IS NOT NULL AND e.id_tenant <> t.id_tenant;',
            N'@e NVARCHAR(MAX) OUTPUT', @e = @errores OUTPUT;
    END
END

-- d) si saas.tenants ya existe, debe tener exactamente las columnas esperadas
IF @existeTabla = 1
BEGIN
    DECLARE @esperadas TABLE (nombre SYSNAME NOT NULL PRIMARY KEY);
    INSERT INTO @esperadas (nombre) VALUES
        (N'id_tenant'), (N'uid_publico'), (N'codigo'), (N'nombre'), (N'tipo'), (N'estado'), (N'fecha_estado'),
        (N'motivo_estado'), (N'creado_por'), (N'fecha_creacion'), (N'modificado_por'), (N'fecha_modificacion'),
        (N'token_concurrencia');

    SELECT @errores += N' saas.tenants no tiene la columna ' + e.nombre + N'.'
    FROM @esperadas e
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'saas.tenants') AND c.name = e.nombre);

    SELECT @errores += N' saas.tenants tiene una columna no prevista: ' + c.name + N'.'
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'saas.tenants') AND c.name NOT IN (SELECT nombre FROM @esperadas);
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 025 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50025, @mensaje, 1;
END

CREATE TABLE #precheck_025 (ok BIT NOT NULL);
INSERT INTO #precheck_025 (ok) VALUES (1);

PRINT CONCAT(N'PRECHECK 025 correcto. Columna empresas.id_tenant: ', CASE WHEN @tieneColumna = 1 THEN N'ya existe' ELSE N'por crear' END,
             N'; tabla saas.tenants: ', CASE WHEN @existeTabla = 1 THEN N'ya existe' ELSE N'por crear' END, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_025') IS NULL
    THROW 50025, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @hacia INT = (SELECT (CASE WHEN SCHEMA_ID(N'saas') IS NULL THEN 1 ELSE 0 END)
                           + (CASE WHEN SCHEMA_ID(N'seg') IS NULL THEN 1 ELSE 0 END)
                           + (CASE WHEN OBJECT_ID(N'saas.tenants', N'U') IS NULL THEN 1 ELSE 0 END)
                           + (CASE WHEN COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NULL THEN 1 ELSE 0 END)
                           + (CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_tenants') THEN 0 ELSE 1 END)
                           + (CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas')) THEN 0 ELSE 1 END));
DECLARE @tenantsNuevos INT = 0, @empresasLlenadas INT = 0;
DECLARE @malas INT;

BEGIN TRY
    BEGIN TRANSACTION;

    IF SCHEMA_ID(N'saas') IS NULL EXEC sys.sp_executesql N'CREATE SCHEMA saas AUTHORIZATION dbo;';
    IF SCHEMA_ID(N'seg') IS NULL EXEC sys.sp_executesql N'CREATE SCHEMA seg AUTHORIZATION dbo;';

    IF OBJECT_ID(N'saas.tenants', N'U') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE TABLE saas.tenants (
                id_tenant           INT              IDENTITY(1,1) NOT NULL,
                uid_publico         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tenants_uid_publico DEFAULT NEWSEQUENTIALID(),
                codigo              VARCHAR(30)      NOT NULL,
                nombre              NVARCHAR(200)    NOT NULL,
                tipo                VARCHAR(10)      NOT NULL,
                estado              VARCHAR(12)      NOT NULL,
                fecha_estado        DATETIME2        NOT NULL CONSTRAINT DF_tenants_fecha_estado DEFAULT SYSUTCDATETIME(),
                motivo_estado       NVARCHAR(300)    NULL,
                creado_por          NVARCHAR(100)    NOT NULL,
                fecha_creacion      DATETIME2        NOT NULL CONSTRAINT DF_tenants_fecha_creacion DEFAULT SYSUTCDATETIME(),
                modificado_por      NVARCHAR(100)    NULL,
                fecha_modificacion  DATETIME2        NULL,
                token_concurrencia  ROWVERSION       NOT NULL,
                CONSTRAINT PK_tenants PRIMARY KEY CLUSTERED (id_tenant),
                CONSTRAINT UX_tenants_codigo UNIQUE (codigo),
                CONSTRAINT UX_tenants_uid_publico UNIQUE (uid_publico),
                CONSTRAINT CK_tenants_tipo CHECK (tipo IN (''interno'', ''cliente'', ''demo'')),
                CONSTRAINT CK_tenants_estado CHECK (estado IN (''activo'', ''suspendido'', ''cancelado''))
            );';
        -- Como mucho un tenant interno: el de la propia plataforma (ADR-006).
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_tenants_interno ON saas.tenants (tipo) WHERE tipo = ''interno'';';
    END

    -- Tenants iniciales: no se toca ninguno que ya exista (el operador pudo cambiarlo).
    EXEC sys.sp_executesql N'
        INSERT INTO saas.tenants (codigo, nombre, tipo, estado, motivo_estado, creado_por)
        SELECT x.codigo,
               COALESCE(NULLIF(LTRIM(RTRIM(e.nombre_comercial)), N''''), e.razon_social),
               x.tipo, x.estado, x.motivo, N''script:025''
        FROM #tenants_025 x
        JOIN dbo.empresas e ON e.id_empresa = x.id_empresa
        WHERE NOT EXISTS (SELECT 1 FROM saas.tenants t WHERE t.codigo = x.codigo)
        ORDER BY x.id_empresa;   -- los ids de los tenants siguen el orden de las empresas: SIP=1, DEMO=2, IISE=3, TRANSGAR=4
        SET @n = @@ROWCOUNT;',
        N'@n INT OUTPUT', @n = @tenantsNuevos OUTPUT;

    IF COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresas ADD id_tenant INT NULL;';

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_tenants')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresas ADD CONSTRAINT FK_empresas_tenants
                                 FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresas_tenant ON dbo.empresas (id_tenant);';

    -- Llenado: cada empresa esperada recibe el tenant de D18 (solo si aun no tiene uno).
    EXEC sys.sp_executesql N'
        UPDATE e SET e.id_tenant = t.id_tenant
        FROM dbo.empresas e
        JOIN #tenants_025 x ON x.id_empresa = e.id_empresa
        JOIN saas.tenants t ON t.codigo = x.codigo
        WHERE e.id_tenant IS NULL;
        SET @n = @@ROWCOUNT;',
        N'@n INT OUTPUT', @n = @empresasLlenadas OUTPUT;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'
        SELECT @m = COUNT(*)
        FROM #tenants_025 x
        LEFT JOIN saas.tenants t ON t.codigo = x.codigo
        LEFT JOIN dbo.empresas e ON e.id_empresa = x.id_empresa
        WHERE t.id_tenant IS NULL OR e.id_tenant IS NULL OR e.id_tenant <> t.id_tenant;',
        N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
        THROW 50025, N'POSTCHECK: alguna empresa esperada no quedo con el tenant de D18. Se revierte.', 1;

    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM saas.tenants WHERE tipo = ''interno'';', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas <> 1
        THROW 50025, N'POSTCHECK: tiene que haber exactamente un tenant interno. Se revierte.', 1;

    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
        THROW 50025, N'POSTCHECK: quedaron empresas con id_tenant nulo. Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_tenants')
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas'))
        THROW 50025, N'POSTCHECK: falta la clave foranea o el indice de empresas.id_tenant. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @tenantsNuevos = 0 AND @empresasLlenadas = 0
        PRINT N'Nada que hacer: esquemas, tabla, tenants y empresas.id_tenant ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 025 aplicado: ', @hacia, N' objeto(s) creados; ', @tenantsNuevos, N' tenant(s) nuevos; ',
                     @empresasLlenadas, N' empresa(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: tenants y la empresa que cuelga de cada uno
SELECT t.id_tenant, t.codigo, t.nombre, t.tipo, t.estado,
       (SELECT STRING_AGG(CAST(e.id_empresa AS NVARCHAR(10)), N', ') FROM dbo.empresas e WHERE e.id_tenant = t.id_tenant) AS empresas
FROM saas.tenants t
ORDER BY t.id_tenant;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Solo vale mientras nada dependa de saas.tenants ni de empresas.id_tenant (antes del script 026).
--   Quita la clave foranea, el indice y la columna de empresas, la tabla saas.tenants y los esquemas saas y
--   seg si quedaron vacios. No hay datos de negocio que se pierdan: los tenants se pueden recrear.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_tenants')
        ALTER TABLE dbo.empresas DROP CONSTRAINT FK_empresas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas'))
        DROP INDEX IX_empresas_tenant ON dbo.empresas;
    IF COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NOT NULL
        ALTER TABLE dbo.empresas DROP COLUMN id_tenant;
    IF OBJECT_ID(N'saas.tenants', N'U') IS NOT NULL
        DROP TABLE saas.tenants;
    IF SCHEMA_ID(N'saas') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'saas'))
        EXEC sys.sp_executesql N'DROP SCHEMA saas;';
    IF SCHEMA_ID(N'seg') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg'))
        EXEC sys.sp_executesql N'DROP SCHEMA seg;';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
