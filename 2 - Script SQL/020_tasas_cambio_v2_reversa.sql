-- ============================================================
-- Script   : 020_tasas_cambio_v2_reversa.sql
-- Proposito: Reversa del script 020_tasas_cambio_v2.sql. Elimina las tres tablas de la v2
--            (dbo.tasas_cambio_ejecuciones_detalle, dbo.tasas_cambio y dbo.tasas_cambio_ejecuciones) y
--            recrea dbo.tasas_cambio con la estructura vieja de KPI_02_Referencias.sql mas las dos claves
--            foraneas hacia dbo.monedas que agrego el 017. Opcional: recrea dbo.tipos_cambio (F0).
--            SOLO corre si las tablas de la v2 estan VACIAS; si alguna tiene filas, aborta sin cambiar nada.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-04
-- BD       : eBD_SPD
-- Requiere : dbo.empresas; dbo.monedas con codigo_iso CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS (017);
--            SQL Server 2016 o superior.
-- Rollback : volver a ejecutar 020_tasas_cambio_v2.sql.
-- ============================================================
-- CLASIFICACION DE LOS CAMBIOS (para /alerta-bd)
--   [ROJO / ELIMINA]
--     - dbo.tasas_cambio_ejecuciones_detalle, dbo.tasas_cambio (v2) y dbo.tasas_cambio_ejecuciones, con sus
--       claves, CHECK, defaults e indices. Solo si estan vacias.
--   [VERDE / AGREGA]
--     + dbo.tasas_cambio con la estructura de KPI_02 (id_empresa NOT NULL, fecha, fuente NVARCHAR(100) NULL),
--       PK_tasas_cambio, FK_tasas_cambio_empresa, CK_tasas_cambio_tasa, CK_tasas_cambio_monedas,
--       UX_tasas_cambio_empresa_fecha_par, IX_tasas_cambio_par_fecha y, del 017,
--       FK_tasas_cambio_moneda_origen y FK_tasas_cambio_moneda_destino.
--     + (opcional, @recrear_tipos_cambio = 1) dbo.tipos_cambio con la estructura de F0_Catalogos_Transversales.sql.
--   [AMARILLO / MODIFICA]
--     ~ Ninguna columna se altera: las tablas se eliminan y se recrean.
--   [AZUL / IMPACTO]
--     * Datos: ninguno (las tablas que se eliminan deben estar vacias).
--     * Aplicacion: la version con el job de tasas de cambio deja de funcionar (sus entidades apuntan a la v2).
--       ANTES de revertir: TasasCambio__Habilitado=false en el servidor y la variable de GitHub
--       TASAS_CAMBIO_DESPERTAR distinta de true; despues, publicar una version sin el job.
--     * Si hubo datos y aun asi se quiere revertir: exportarlos y vaciarlas a mano, con su propia aprobacion.
--       Este script NUNCA borra filas.
--
-- dbo.tipos_cambio: el 020 la elimina solo si existia vacia (su salida lo dice: "dbo.tipos_cambio eliminada").
--   Si fue asi y se quiere dejarla como estaba, poner @recrear_tipos_cambio = 1 en el PRECHECK de abajo.
--
-- Seguridad de ejecucion: igual que el 020 (una sola sesion, marca #precheck_020r, una transaccion con
-- el POSTCHECK adentro, idempotente). Requiere aprobacion /alerta-bd.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_020r') IS NOT NULL DROP TABLE #precheck_020r;
CREATE TABLE #precheck_020r (
    ok                    BIT         NOT NULL,
    accion                VARCHAR(10) NOT NULL,   -- REVERTIR o NADA
    recrear_tipos_cambio  BIT         NOT NULL
);

-- >>> Parametro: 1 = recrear dbo.tipos_cambio (solo si el 020 la elimino). <<<
DECLARE @recrear_tipos_cambio BIT = 0;

IF DB_NAME() <> N'eBD_SPD'
    THROW 50020, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 13
    THROW 50020, N'Se requiere SQL Server 2016 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.empresas', N'U') IS NULL OR OBJECT_ID(N'dbo.monedas', N'U') IS NULL
    THROW 50020, N'Faltan dbo.empresas o dbo.monedas. Abortar.', 1;

IF NOT EXISTS (SELECT 1
               FROM sys.columns c
               JOIN sys.types ty ON ty.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso'
                 AND ty.name = N'char' AND c.max_length = 3
                 AND c.collation_name = N'SQL_Latin1_General_CP1_CI_AS')
    THROW 50020, N'dbo.monedas.codigo_iso no es CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS: no se podrian recrear las FK del 017. Abortar.', 1;

IF (OBJECT_ID(N'dbo.tasas_cambio') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tasas_cambio_ejecuciones') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tipos_cambio') IS NOT NULL AND OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NULL)
    THROW 50020, N'Hay un objeto que no es tabla con el nombre de una de las tablas del script. Revise a mano. Abortar.', 1;

DECLARE @v2 BIT = 0, @v1 BIT = 0, @n INT, @msg NVARCHAR(800), @accion VARCHAR(10);

IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NOT NULL AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha') IS NULL
        SET @v2 = 1;
    ELSE IF COL_LENGTH(N'dbo.tasas_cambio', N'fecha') IS NOT NULL AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NULL
        SET @v1 = 1;
    ELSE
    BEGIN
        THROW 50020, N'dbo.tasas_cambio no tiene ni la estructura v2 ni la vieja (KPI_02). Revise a mano. Abortar.', 1;
    END;
END;

-- Ya revertido (o el 020 nunca se aplico): tasas_cambio vieja y sin tablas de la v2
IF @v1 = 1
   AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL
   AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL
    SET @accion = 'NADA';
ELSE
    SET @accion = 'REVERTIR';

IF @accion = 'REVERTIR'
BEGIN
    -- 1) Las tablas de la v2 deben estar vacias
    DECLARE @filas_detalle INT = 0, @filas_tasas INT = 0, @filas_ejecuciones INT = 0;

    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NOT NULL
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio_ejecuciones_detalle;', N'@n INT OUTPUT', @n = @filas_detalle OUTPUT;
    IF @v2 = 1
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio;', N'@n INT OUTPUT', @n = @filas_tasas OUTPUT;
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NOT NULL
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio_ejecuciones;', N'@n INT OUTPUT', @n = @filas_ejecuciones OUTPUT;

    IF @filas_detalle + @filas_tasas + @filas_ejecuciones > 0
    BEGIN
        SET @msg = CONCAT(N'Las tablas de la v2 tienen datos (tasas_cambio: ', @filas_tasas, N', tasas_cambio_ejecuciones: ',
                          @filas_ejecuciones, N', tasas_cambio_ejecuciones_detalle: ', @filas_detalle, N'). ',
                          N'La reversa solo corre con las tablas vacias: exporte los datos y decida a mano. No se cambio nada. Abortar.');
        THROW 50020, @msg, 1;
    END;

    -- 2) Dependencias que este script no conoce (claves foraneas desde otras tablas, vistas, procedimientos, disparadores)
    --    (ISNULL(..., 0) en los NOT IN: una tabla que no existe daria NULL y anularia la comparacion.)
    IF EXISTS (SELECT 1 FROM sys.foreign_keys fk
               WHERE fk.referenced_object_id IN (OBJECT_ID(N'dbo.tasas_cambio'), OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'),
                                                 OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'))
                 AND fk.parent_object_id NOT IN (ISNULL(OBJECT_ID(N'dbo.tasas_cambio'), 0),
                                                 ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'), 0),
                                                 ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'), 0)))
        THROW 50020, N'Otra tabla tiene una clave foranea hacia las tablas de la v2 (ver sys.foreign_keys). Revise a mano. Abortar.', 1;

    IF EXISTS (SELECT 1 FROM sys.triggers
               WHERE parent_id IN (OBJECT_ID(N'dbo.tasas_cambio'), OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'),
                                   OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle')))
        THROW 50020, N'Las tablas de la v2 tienen disparadores (triggers) que este script no conoce. Revise a mano. Abortar.', 1;

    -- Las restricciones CHECK y los indices filtrados de las propias tablas tambien aparecen como dependencias:
    -- no cuentan. Solo importan objetos ajenos a las tres tablas.
    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
               JOIN sys.objects o ON o.object_id = d.referencing_id
               WHERE d.referenced_entity_name IN (N'tasas_cambio', N'tasas_cambio_ejecuciones', N'tasas_cambio_ejecuciones_detalle')
                 AND ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
                 AND d.referencing_id NOT IN (ISNULL(OBJECT_ID(N'dbo.tasas_cambio'), 0),
                                              ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'), 0),
                                              ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'), 0))
                 AND o.parent_object_id NOT IN (ISNULL(OBJECT_ID(N'dbo.tasas_cambio'), 0),
                                                ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'), 0),
                                                ISNULL(OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'), 0)))
        THROW 50020, N'Una vista, procedimiento o funcion usa las tablas de la v2 (ver sys.sql_expression_dependencies). Revise a mano. Abortar.', 1;
END;

IF @recrear_tipos_cambio = 1 AND OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NOT NULL
    PRINT N'Aviso: dbo.tipos_cambio ya existe; no se recrea.';

INSERT INTO #precheck_020r (ok, accion, recrear_tipos_cambio) VALUES (1, @accion, @recrear_tipos_cambio);

SELECT @accion AS accion,
       CASE WHEN @v2 = 1 THEN N'v2 vacia: se elimina y se recrea la vieja (KPI_02)'
            WHEN @v1 = 1 THEN N'Ya tiene la estructura vieja (KPI_02)'
            ELSE N'No existe: se crea con la estructura vieja (KPI_02)' END AS tasas_cambio,
       CASE WHEN OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL THEN N'No existe' ELSE N'Se elimina (vacia)' END AS tasas_cambio_ejecuciones,
       CASE WHEN OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL THEN N'No existe' ELSE N'Se elimina (vacia)' END AS tasas_cambio_ejecuciones_detalle,
       CASE WHEN @recrear_tipos_cambio = 0 THEN N'No se toca'
            WHEN OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NULL THEN N'Se recrea (F0)'
            ELSE N'Ya existe: no se toca' END AS tipos_cambio;
PRINT N'PRECHECK de la reversa 020 superado.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK dentro de la transaccion
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_020r') IS NULL
    THROW 50020, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM #precheck_020r WHERE ok = 1)
    THROW 50020, N'El PRECHECK no termino bien (ver el error anterior). No se cambio nada.', 1;

DECLARE @recrear_tipos_cambio BIT = (SELECT recrear_tipos_cambio FROM #precheck_020r WHERE ok = 1);
DECLARE @n INT, @cambios INT = 0;
DECLARE @problemas TABLE (problema NVARCHAR(400) NOT NULL);

-- Estructura esperada de dbo.tasas_cambio vieja (KPI_02_Referencias.sql / Estructura BD.sql)
DECLARE @columnas TABLE (columna SYSNAME NOT NULL, tipo VARCHAR(30) NOT NULL, nulable BIT NOT NULL, identidad BIT NOT NULL);
INSERT INTO @columnas (columna, tipo, nulable, identidad) VALUES
    (N'id_tasa_cambio',     'int',           0, 1),
    (N'id_empresa',         'int',           0, 0),
    (N'fecha',              'date',          0, 0),
    (N'moneda_origen',      'char(3)',       0, 0),
    (N'moneda_destino',     'char(3)',       0, 0),
    (N'tasa',               'decimal(18,8)', 0, 0),
    (N'fuente',             'nvarchar(100)', 1, 0),
    (N'eliminado',          'bit',           0, 0),
    (N'fecha_eliminado',    'datetime2(3)',  1, 0),
    (N'creado_por',         'nvarchar(100)', 0, 0),
    (N'fecha_creacion',     'datetime2(3)',  0, 0),
    (N'modificado_por',     'nvarchar(100)', 1, 0),
    (N'fecha_modificacion', 'datetime2(3)',  1, 0),
    (N'token_concurrencia', 'timestamp',     0, 0);

DECLARE @restricciones TABLE (nombre SYSNAME NOT NULL, tipo CHAR(2) NOT NULL, tabla_destino SYSNAME NULL);
INSERT INTO @restricciones (nombre, tipo, tabla_destino) VALUES
    (N'PK_tasas_cambio',                'PK', NULL),
    (N'FK_tasas_cambio_empresa',        'F',  N'empresas'),
    (N'FK_tasas_cambio_moneda_origen',  'F',  N'monedas'),
    (N'FK_tasas_cambio_moneda_destino', 'F',  N'monedas'),
    (N'CK_tasas_cambio_tasa',           'C',  NULL),
    (N'CK_tasas_cambio_monedas',        'C',  NULL),
    (N'DF_tasas_cambio_eliminado',      'D',  NULL),
    (N'DF_tasas_cambio_fecha_creacion', 'D',  NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- --------------------------------------------------------
    -- CAMBIO 1/4: [ROJO / ELIMINA] dbo.tasas_cambio_ejecuciones_detalle (vacia)
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio_ejecuciones_detalle WITH (TABLOCKX, HOLDLOCK);',
                               N'@n INT OUTPUT', @n = @n OUTPUT;
        IF @n > 0
            THROW 50020, N'dbo.tasas_cambio_ejecuciones_detalle recibio filas despues del PRECHECK. Se revierte.', 1;
        DROP TABLE dbo.tasas_cambio_ejecuciones_detalle;
        SET @cambios += 1;
        PRINT N'  - dbo.tasas_cambio_ejecuciones_detalle eliminada';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 2/4: [ROJO / ELIMINA] dbo.tasas_cambio v2 (vacia) y dbo.tasas_cambio_ejecuciones (vacia)
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio WITH (TABLOCKX, HOLDLOCK);',
                               N'@n INT OUTPUT', @n = @n OUTPUT;
        IF @n > 0
            THROW 50020, N'dbo.tasas_cambio (v2) recibio filas despues del PRECHECK. Se revierte.', 1;
        DROP TABLE dbo.tasas_cambio;
        SET @cambios += 1;
        PRINT N'  - dbo.tasas_cambio (v2) eliminada';
    END;

    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio_ejecuciones WITH (TABLOCKX, HOLDLOCK);',
                               N'@n INT OUTPUT', @n = @n OUTPUT;
        IF @n > 0
            THROW 50020, N'dbo.tasas_cambio_ejecuciones recibio filas despues del PRECHECK. Se revierte.', 1;
        DROP TABLE dbo.tasas_cambio_ejecuciones;
        SET @cambios += 1;
        PRINT N'  - dbo.tasas_cambio_ejecuciones eliminada';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 3/4: [VERDE / AGREGA] dbo.tasas_cambio con la estructura de KPI_02 + FK del 017
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tasas_cambio (
            id_tasa_cambio      INT            IDENTITY(1,1) NOT NULL,
            id_empresa          INT            NOT NULL,
            fecha               DATE           NOT NULL,
            moneda_origen       CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            moneda_destino      CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            tasa                DECIMAL(18,8)  NOT NULL,
            fuente              NVARCHAR(100)  NULL,
            eliminado           BIT            NOT NULL CONSTRAINT DF_tasas_cambio_eliminado       DEFAULT (0),
            fecha_eliminado     DATETIME2(3)   NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_tasas_cambio_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_tasas_cambio          PRIMARY KEY CLUSTERED (id_tasa_cambio),
            CONSTRAINT FK_tasas_cambio_empresa  FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT CK_tasas_cambio_tasa     CHECK (tasa > 0),
            CONSTRAINT CK_tasas_cambio_monedas  CHECK (moneda_origen <> moneda_destino)
        );

        -- Por sp_executesql: al compilarse el lote, dbo.tasas_cambio todavia es la v2 y no tiene la columna fecha.
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_tasas_cambio_empresa_fecha_par
            ON dbo.tasas_cambio (id_empresa, fecha, moneda_origen, moneda_destino)
            WHERE eliminado = 0;';

        EXEC sys.sp_executesql N'CREATE INDEX IX_tasas_cambio_par_fecha
            ON dbo.tasas_cambio (id_empresa, moneda_origen, moneda_destino, fecha DESC)
            WHERE eliminado = 0;';

        -- Claves foraneas del 017 (mismo nombre)
        ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_moneda_origen
            FOREIGN KEY (moneda_origen) REFERENCES dbo.monedas (codigo_iso);
        ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_moneda_destino
            FOREIGN KEY (moneda_destino) REFERENCES dbo.monedas (codigo_iso);

        SET @cambios += 1;
        PRINT N'  + dbo.tasas_cambio con la estructura vieja (KPI_02) y las FK del 017';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 4/4: [VERDE / AGREGA] (opcional) dbo.tipos_cambio con la estructura de F0_Catalogos_Transversales.sql
    -- --------------------------------------------------------
    IF @recrear_tipos_cambio = 1 AND OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tipos_cambio (
            id_tipo_cambio       INT IDENTITY(1,1) NOT NULL,
            id_empresa           INT NOT NULL,
            moneda_origen        NVARCHAR(3)   NOT NULL,
            moneda_destino       NVARCHAR(3)   NOT NULL,
            fecha                DATETIME2     NOT NULL,
            tasa                 DECIMAL(18,8) NOT NULL,
            fuente               NVARCHAR(20)  NOT NULL CONSTRAINT DF_tc_fuente DEFAULT ('manual'),
            creado_por           NVARCHAR(100) NOT NULL,
            fecha_creacion       DATETIME2     NOT NULL,
            CONSTRAINT PK_tipos_cambio         PRIMARY KEY CLUSTERED (id_tipo_cambio),
            CONSTRAINT FK_tipos_cambio_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
        );

        CREATE UNIQUE INDEX UX_tc_empresa_par_fecha ON dbo.tipos_cambio (id_empresa, moneda_origen, moneda_destino, fecha);

        SET @cambios += 1;
        PRINT N'  + dbo.tipos_cambio (estructura F0)';
    END;

    -- --------------------------------------------------------
    -- POSTCHECK (dentro de la transaccion: si algo no cuadra, se revierte todo)
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NOT NULL
        INSERT INTO @problemas (problema) VALUES (N'dbo.tasas_cambio_ejecuciones sigue existiendo');
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NOT NULL
        INSERT INTO @problemas (problema) VALUES (N'dbo.tasas_cambio_ejecuciones_detalle sigue existiendo');
    IF @recrear_tipos_cambio = 1 AND OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NULL
        INSERT INTO @problemas (problema) VALUES (N'No se recreo dbo.tipos_cambio');

    WITH reales AS (
        SELECT c.name AS columna,
               CAST(CASE
                   WHEN ty.name IN ('char', 'varchar', 'binary', 'varbinary')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length AS VARCHAR(10)) END + ')'
                   WHEN ty.name IN ('nchar', 'nvarchar')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length / 2 AS VARCHAR(10)) END + ')'
                   WHEN ty.name IN ('decimal', 'numeric')
                       THEN ty.name + '(' + CAST(c.precision AS VARCHAR(3)) + ',' + CAST(c.scale AS VARCHAR(3)) + ')'
                   WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
                       THEN ty.name + '(' + CAST(c.scale AS VARCHAR(3)) + ')'
                   ELSE ty.name END AS VARCHAR(30)) AS tipo,
               c.is_nullable AS nulable, c.is_identity AS identidad
        FROM sys.columns c
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.tasas_cambio')
    )
    INSERT INTO @problemas (problema)
    SELECT CASE
               WHEN r.columna IS NULL THEN CONCAT(N'Falta la columna tasas_cambio.', e.columna)
               WHEN e.columna IS NULL THEN CONCAT(N'Columna no esperada tasas_cambio.', r.columna)
               ELSE CONCAT(N'Columna tasas_cambio.', e.columna, N': es ', r.tipo,
                           CASE WHEN r.nulable = 1 THEN N' NULL' ELSE N' NOT NULL' END,
                           N'; se esperaba ', e.tipo, CASE WHEN e.nulable = 1 THEN N' NULL' ELSE N' NOT NULL' END)
           END
    FROM @columnas e
    FULL OUTER JOIN reales r ON r.columna = e.columna
    WHERE r.columna IS NULL OR e.columna IS NULL
       OR r.tipo <> e.tipo OR r.nulable <> e.nulable OR r.identidad <> e.identidad;

    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta o no coincide la restriccion ', x.nombre)
    FROM @restricciones x
    WHERE NOT EXISTS (SELECT 1 FROM sys.objects o
                      WHERE o.name = x.nombre AND o.type COLLATE DATABASE_DEFAULT = x.tipo AND o.parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
       OR (x.tipo = 'F' AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk
                                        WHERE fk.name = x.nombre
                                          AND fk.parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')
                                          AND fk.referenced_object_id = OBJECT_ID(N'dbo.' + x.tabla_destino)));

    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta el indice ', v.indice, N' o no coincide su unicidad o su filtro')
    FROM (VALUES (N'UX_tasas_cambio_empresa_fecha_par', 1), (N'IX_tasas_cambio_par_fecha', 0)) v (indice, unico)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i
                      WHERE i.object_id = OBJECT_ID(N'dbo.tasas_cambio') AND i.name = v.indice
                        AND i.is_unique = v.unico AND i.has_filter = 1);

    IF EXISTS (SELECT 1 FROM @problemas)
    BEGIN
        SELECT problema FROM @problemas;
        THROW 50020, N'POSTCHECK de la reversa 020 fallo (ver la lista de problemas). Se revierte todo.', 1;
    END;

    COMMIT TRANSACTION;

    IF @cambios = 0
        PRINT N'No habia nada que revertir: dbo.tasas_cambio ya tiene la estructura vieja y no existen las tablas de la v2. POSTCHECK correcto.';
    ELSE
        PRINT CONCAT(N'Reversa 020 lista: ', @cambios, N' cambio(s). POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado (solo lectura)
IF OBJECT_ID(N'tempdb..#precheck_020r') IS NOT NULL DROP TABLE #precheck_020r;

SELECT t.name AS tabla,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS columnas,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) AS filas
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name IN (N'tasas_cambio', N'tasas_cambio_ejecuciones', N'tasas_cambio_ejecuciones_detalle', N'tipos_cambio')
ORDER BY t.name;
GO
