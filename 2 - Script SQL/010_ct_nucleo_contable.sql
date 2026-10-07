-- ============================================================
-- Script   : 010_ct_nucleo_contable.sql
-- Proposito: Nucleo del modulo contable (cuentas, ejercicios, periodos, centros de costo, asientos, movimientos)
-- Autor    : eGestion360-Web
-- Fecha    : 2026-08-06 (revisado 2026-10-06, antes de aplicarse por primera vez)
-- BD       : eBD_SPD
-- Requiere : dbo.empresas (multitenant), dbo.domain_events (F0_Outbox.sql) y dbo.monedas con codigo_iso CHAR(3) (017)
-- Reversa  : 010_ct_nucleo_contable_reversa.sql (solo con las 6 tablas vacias)
-- ============================================================
-- Convenciones: snake_case + prefijo de modulo ct_ (ver 1 - Documetacion/ESTANDARES_ERP.md).
--
-- Clasificacion de los cambios:
--   [VERDE / AGREGA]  6 tablas nuevas: ct_cuentas, ct_ejercicios, ct_periodos, ct_centros_costo, ct_asientos,
--                     ct_asiento_movimientos, con sus indices, claves y restricciones.
--   [AMARILLO / MODIFICA] nada.   [ROJO / ELIMINA] nada.
--   [IMPACTO] ninguno sobre datos existentes: solo se crean tablas vacias.
--
-- Revision del 2026-10-06 (el script nunca se habia ejecutado):
--   1. Aislamiento entre empresas garantizado por la BD: las relaciones internas del modulo son claves foraneas
--      compuestas (id, id_empresa) -> (id, id_empresa), como FK_clientes_vinculo (014). Un movimiento no puede usar
--      la cuenta, el asiento ni el centro de costo de otra empresa; un asiento, el periodo de otra; un periodo, el
--      ejercicio de otra; una cuenta, el padre de otra. Cada tabla referida tiene su UNIQUE (id, id_empresa).
--   2. Una sola transaccion con XACT_ABORT y TRY/CATCH: o se crean las 6 tablas completas o no queda nada.
--   3. POSTCHECK completo dentro de la transaccion (columnas, PK/UQ/FK con sus columnas, CHECK, DEFAULT, indices);
--      si algo no coincide se revierte todo. Las comparaciones contra el catalogo usan COLLATE DATABASE_DEFAULT
--      (sys.objects.type tiene intercalacion fija, ver 020).
--   4. ct_cuentas.moneda pasa a CHAR(3) con la intercalacion de dbo.monedas y FK a monedas(codigo_iso) (017).
--   6. CHECK fecha_inicio <= fecha_fin en ejercicios y periodos.
--   7. token_concurrencia (rowversion) en ct_cuentas, ct_ejercicios y ct_periodos (ct_asientos ya lo tenia).
--   (5 es de Entity Framework: el detalle no se borra en cascada con el asiento.)
-- Idempotente: si las 6 tablas ya existen, no cambia nada y el POSTCHECK verifica su estructura.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- requerido por los indices filtrados (WHERE ... IS NOT NULL)
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (no cambia nada; si algo falla, aborta)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_010') IS NOT NULL DROP TABLE #precheck_010;
CREATE TABLE #precheck_010 (ok BIT NOT NULL, tablas_existentes INT NOT NULL);

IF DB_NAME() <> N'eBD_SPD'
    THROW 50010, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 13
    THROW 50010, N'Se requiere SQL Server 2016 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.empresas', N'U') IS NULL
    THROW 50010, N'Falta dbo.empresas. Ejecute la estructura base / multitenant antes. Abortar.', 1;

IF OBJECT_ID(N'dbo.domain_events', N'U') IS NULL
    THROW 50010, N'Falta dbo.domain_events. Ejecute F0_Outbox.sql antes. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso'
                 AND t.name = N'char' AND c.max_length = 3 AND c.collation_name = N'SQL_Latin1_General_CP1_CI_AS')
    THROW 50010, N'dbo.monedas.codigo_iso no es CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS. Ejecute antes el script 017. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.monedas WHERE codigo_iso = 'HNL')
    THROW 50010, N'Falta la moneda HNL en dbo.monedas (es el valor por defecto de ct_cuentas.moneda). Abortar.', 1;

-- Los nombres del script, si existen, deben ser tablas
IF EXISTS (SELECT 1 FROM sys.objects
           WHERE schema_id = SCHEMA_ID(N'dbo') AND type <> 'U'
             AND name IN (N'ct_cuentas', N'ct_ejercicios', N'ct_periodos', N'ct_centros_costo', N'ct_asientos', N'ct_asiento_movimientos'))
    THROW 50010, N'Hay un objeto que no es tabla con el nombre de una de las tablas ct_*. Revise a mano. Abortar.', 1;

-- O no existe ninguna de las 6 (instalacion nueva) o existen las 6 (segunda ejecucion: el POSTCHECK verifica).
DECLARE @existentes INT = (SELECT COUNT(*) FROM sys.tables
                           WHERE schema_id = SCHEMA_ID(N'dbo')
                             AND name IN (N'ct_cuentas', N'ct_ejercicios', N'ct_periodos', N'ct_centros_costo', N'ct_asientos', N'ct_asiento_movimientos'));
IF @existentes NOT IN (0, 6)
BEGIN
    DECLARE @msg NVARCHAR(400) = CONCAT(N'Existen ', @existentes, N' de las 6 tablas ct_*: es una instalacion a medias. ',
                                        N'Revise a mano (o use la reversa) antes de reintentar. No se cambio nada. Abortar.');
    THROW 50010, @msg, 1;
END;

INSERT INTO #precheck_010 (ok, tablas_existentes) VALUES (1, @existentes);

SELECT @existentes AS tablas_ct_existentes,
       CASE @existentes WHEN 0 THEN N'Se crean las 6 tablas del nucleo contable'
                        ELSE N'Ya existen: no se cambia nada; el POSTCHECK verifica su estructura' END AS accion;
PRINT N'PRECHECK 010 superado.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK dentro de la transaccion
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_010') IS NULL
    THROW 50010, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM #precheck_010 WHERE ok = 1)
    THROW 50010, N'El PRECHECK no termino bien (ver el error anterior). No se cambio nada.', 1;

DECLARE @cambios INT = 0;
DECLARE @problemas TABLE (problema NVARCHAR(400) NOT NULL);

-- Estructura esperada (la usa el POSTCHECK)
DECLARE @columnas TABLE (tabla SYSNAME NOT NULL, columna SYSNAME NOT NULL, tipo VARCHAR(30) NOT NULL, nulable BIT NOT NULL, identidad BIT NOT NULL);
INSERT INTO @columnas (tabla, columna, tipo, nulable, identidad) VALUES
    (N'ct_cuentas', N'id_cuenta',          'int',            0, 1),
    (N'ct_cuentas', N'id_empresa',         'int',            0, 0),
    (N'ct_cuentas', N'codigo',             'nvarchar(30)',   0, 0),
    (N'ct_cuentas', N'nombre',             'nvarchar(200)',  0, 0),
    (N'ct_cuentas', N'id_cuenta_padre',    'int',            1, 0),
    (N'ct_cuentas', N'nivel',              'int',            0, 0),
    (N'ct_cuentas', N'naturaleza',         'nvarchar(10)',   0, 0),
    (N'ct_cuentas', N'tipo',               'nvarchar(20)',   0, 0),
    (N'ct_cuentas', N'es_movimiento',      'bit',            0, 0),
    (N'ct_cuentas', N'moneda',             'char(3)',        0, 0),
    (N'ct_cuentas', N'activo',             'bit',            0, 0),
    (N'ct_cuentas', N'eliminado',          'bit',            0, 0),
    (N'ct_cuentas', N'fecha_eliminado',    'datetime2(7)',   1, 0),
    (N'ct_cuentas', N'creado_por',         'nvarchar(100)',  0, 0),
    (N'ct_cuentas', N'fecha_creacion',     'datetime2(7)',   0, 0),
    (N'ct_cuentas', N'modificado_por',     'nvarchar(100)',  1, 0),
    (N'ct_cuentas', N'fecha_modificacion', 'datetime2(7)',   1, 0),
    (N'ct_cuentas', N'token_concurrencia', 'timestamp',      0, 0),

    (N'ct_ejercicios', N'id_ejercicio',       'int',           0, 1),
    (N'ct_ejercicios', N'id_empresa',         'int',           0, 0),
    (N'ct_ejercicios', N'anio',               'int',           0, 0),
    (N'ct_ejercicios', N'fecha_inicio',       'date',          0, 0),
    (N'ct_ejercicios', N'fecha_fin',          'date',          0, 0),
    (N'ct_ejercicios', N'estado',             'nvarchar(20)',  0, 0),
    (N'ct_ejercicios', N'creado_por',         'nvarchar(100)', 0, 0),
    (N'ct_ejercicios', N'fecha_creacion',     'datetime2(7)',  0, 0),
    (N'ct_ejercicios', N'modificado_por',     'nvarchar(100)', 1, 0),
    (N'ct_ejercicios', N'fecha_modificacion', 'datetime2(7)',  1, 0),
    (N'ct_ejercicios', N'token_concurrencia', 'timestamp',     0, 0),

    (N'ct_periodos', N'id_periodo',         'int',           0, 1),
    (N'ct_periodos', N'id_ejercicio',       'int',           0, 0),
    (N'ct_periodos', N'id_empresa',         'int',           0, 0),
    (N'ct_periodos', N'numero',             'int',           0, 0),
    (N'ct_periodos', N'fecha_inicio',       'date',          0, 0),
    (N'ct_periodos', N'fecha_fin',          'date',          0, 0),
    (N'ct_periodos', N'estado',             'nvarchar(20)',  0, 0),
    (N'ct_periodos', N'creado_por',         'nvarchar(100)', 0, 0),
    (N'ct_periodos', N'fecha_creacion',     'datetime2(7)',  0, 0),
    (N'ct_periodos', N'modificado_por',     'nvarchar(100)', 1, 0),
    (N'ct_periodos', N'fecha_modificacion', 'datetime2(7)',  1, 0),
    (N'ct_periodos', N'token_concurrencia', 'timestamp',     0, 0),

    (N'ct_centros_costo', N'id_centro_costo',    'int',           0, 1),
    (N'ct_centros_costo', N'id_empresa',         'int',           0, 0),
    (N'ct_centros_costo', N'codigo',             'nvarchar(30)',  0, 0),
    (N'ct_centros_costo', N'nombre',             'nvarchar(200)', 0, 0),
    (N'ct_centros_costo', N'activo',             'bit',           0, 0),
    (N'ct_centros_costo', N'eliminado',          'bit',           0, 0),
    (N'ct_centros_costo', N'fecha_eliminado',    'datetime2(7)',  1, 0),
    (N'ct_centros_costo', N'creado_por',         'nvarchar(100)', 0, 0),
    (N'ct_centros_costo', N'fecha_creacion',     'datetime2(7)',  0, 0),
    (N'ct_centros_costo', N'modificado_por',     'nvarchar(100)', 1, 0),
    (N'ct_centros_costo', N'fecha_modificacion', 'datetime2(7)',  1, 0),

    (N'ct_asientos', N'id_asiento',         'int',           0, 1),
    (N'ct_asientos', N'id_empresa',         'int',           0, 0),
    (N'ct_asientos', N'id_periodo',         'int',           0, 0),
    (N'ct_asientos', N'numero',             'int',           1, 0),
    (N'ct_asientos', N'fecha',              'date',          0, 0),
    (N'ct_asientos', N'tipo_asiento',       'nvarchar(20)',  0, 0),
    (N'ct_asientos', N'concepto',           'nvarchar(500)', 0, 0),
    (N'ct_asientos', N'origen',             'nvarchar(20)',  0, 0),
    (N'ct_asientos', N'id_evento_origen',   'bigint',        1, 0),
    (N'ct_asientos', N'total_debito',       'decimal(18,2)', 0, 0),
    (N'ct_asientos', N'total_credito',      'decimal(18,2)', 0, 0),
    (N'ct_asientos', N'estado',             'nvarchar(20)',  0, 0),
    (N'ct_asientos', N'motivo_anulacion',   'nvarchar(500)', 1, 0),
    (N'ct_asientos', N'fecha_anulacion',    'datetime2(7)',  1, 0),
    (N'ct_asientos', N'eliminado',          'bit',           0, 0),
    (N'ct_asientos', N'fecha_eliminado',    'datetime2(7)',  1, 0),
    (N'ct_asientos', N'creado_por',         'nvarchar(100)', 0, 0),
    (N'ct_asientos', N'fecha_creacion',     'datetime2(7)',  0, 0),
    (N'ct_asientos', N'modificado_por',     'nvarchar(100)', 1, 0),
    (N'ct_asientos', N'fecha_modificacion', 'datetime2(7)',  1, 0),
    (N'ct_asientos', N'token_concurrencia', 'timestamp',     0, 0),

    (N'ct_asiento_movimientos', N'id_movimiento',   'int',           0, 1),
    (N'ct_asiento_movimientos', N'id_asiento',      'int',           0, 0),
    (N'ct_asiento_movimientos', N'id_empresa',      'int',           0, 0),
    (N'ct_asiento_movimientos', N'numero_linea',    'int',           0, 0),
    (N'ct_asiento_movimientos', N'id_cuenta',       'int',           0, 0),
    (N'ct_asiento_movimientos', N'id_centro_costo', 'int',           1, 0),
    (N'ct_asiento_movimientos', N'descripcion',     'nvarchar(300)', 1, 0),
    (N'ct_asiento_movimientos', N'debito',          'decimal(18,2)', 0, 0),
    (N'ct_asiento_movimientos', N'credito',         'decimal(18,2)', 0, 0);

-- Restricciones por nombre: PK, UQ, F (con tabla destino y columnas en orden), C, D
DECLARE @restricciones TABLE (tabla SYSNAME NOT NULL, nombre SYSNAME NOT NULL, tipo CHAR(2) NOT NULL,
                              tabla_destino SYSNAME NULL, columnas NVARCHAR(200) NULL, columnas_destino NVARCHAR(200) NULL);
INSERT INTO @restricciones (tabla, nombre, tipo, tabla_destino, columnas, columnas_destino) VALUES
    (N'ct_cuentas', N'PK_ct_cuentas',                 'PK', NULL, NULL, NULL),
    (N'ct_cuentas', N'UQ_ct_cuentas_cuenta_empresa',  'UQ', NULL, NULL, NULL),
    (N'ct_cuentas', N'FK_ct_cuentas_empresa',         'F',  N'empresas',   N'id_empresa',                 N'id_empresa'),
    (N'ct_cuentas', N'FK_ct_cuentas_moneda',          'F',  N'monedas',    N'moneda',                     N'codigo_iso'),
    (N'ct_cuentas', N'FK_ct_cuentas_padre',           'F',  N'ct_cuentas', N'id_cuenta_padre,id_empresa', N'id_cuenta,id_empresa'),
    (N'ct_cuentas', N'CK_ct_cuentas_nat',             'C',  NULL, NULL, NULL),
    (N'ct_cuentas', N'CK_ct_cuentas_tipo',            'C',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_nivel',                'D',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_mov',                  'D',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_mon',                  'D',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_act',                  'D',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_elim',                 'D',  NULL, NULL, NULL),
    (N'ct_cuentas', N'DF_ctcta_fc',                   'D',  NULL, NULL, NULL),

    (N'ct_ejercicios', N'PK_ct_ejercicios',                  'PK', NULL, NULL, NULL),
    (N'ct_ejercicios', N'UQ_ct_ejercicios_ejercicio_empresa', 'UQ', NULL, NULL, NULL),
    (N'ct_ejercicios', N'FK_ct_ejercicios_empresa',          'F',  N'empresas', N'id_empresa', N'id_empresa'),
    (N'ct_ejercicios', N'CK_ct_ejercicios_estado',           'C',  NULL, NULL, NULL),
    (N'ct_ejercicios', N'CK_ct_ejercicios_fechas',           'C',  NULL, NULL, NULL),
    (N'ct_ejercicios', N'DF_ctejer_est',                     'D',  NULL, NULL, NULL),
    (N'ct_ejercicios', N'DF_ctejer_fc',                      'D',  NULL, NULL, NULL),

    (N'ct_periodos', N'PK_ct_periodos',                'PK', NULL, NULL, NULL),
    (N'ct_periodos', N'UQ_ct_periodos_periodo_empresa', 'UQ', NULL, NULL, NULL),
    (N'ct_periodos', N'FK_ct_periodos_ejercicio',      'F',  N'ct_ejercicios', N'id_ejercicio,id_empresa', N'id_ejercicio,id_empresa'),
    (N'ct_periodos', N'FK_ct_periodos_empresa',        'F',  N'empresas',      N'id_empresa',              N'id_empresa'),
    (N'ct_periodos', N'CK_ct_periodos_estado',         'C',  NULL, NULL, NULL),
    (N'ct_periodos', N'CK_ct_periodos_numero',         'C',  NULL, NULL, NULL),
    (N'ct_periodos', N'CK_ct_periodos_fechas',         'C',  NULL, NULL, NULL),
    (N'ct_periodos', N'DF_ctper_est',                  'D',  NULL, NULL, NULL),
    (N'ct_periodos', N'DF_ctper_fc',                   'D',  NULL, NULL, NULL),

    (N'ct_centros_costo', N'PK_ct_centros_costo',               'PK', NULL, NULL, NULL),
    (N'ct_centros_costo', N'UQ_ct_centros_costo_centro_empresa', 'UQ', NULL, NULL, NULL),
    (N'ct_centros_costo', N'FK_ct_centros_costo_empresa',       'F',  N'empresas', N'id_empresa', N'id_empresa'),
    (N'ct_centros_costo', N'DF_ctcc_act',                       'D',  NULL, NULL, NULL),
    (N'ct_centros_costo', N'DF_ctcc_elim',                      'D',  NULL, NULL, NULL),
    (N'ct_centros_costo', N'DF_ctcc_fc',                        'D',  NULL, NULL, NULL),

    (N'ct_asientos', N'PK_ct_asientos',                'PK', NULL, NULL, NULL),
    (N'ct_asientos', N'UQ_ct_asientos_asiento_empresa', 'UQ', NULL, NULL, NULL),
    (N'ct_asientos', N'FK_ct_asientos_empresa',        'F',  N'empresas',    N'id_empresa',            N'id_empresa'),
    (N'ct_asientos', N'FK_ct_asientos_periodo',        'F',  N'ct_periodos', N'id_periodo,id_empresa', N'id_periodo,id_empresa'),
    (N'ct_asientos', N'CK_ct_asientos_tipo',           'C',  NULL, NULL, NULL),
    (N'ct_asientos', N'CK_ct_asientos_origen',         'C',  NULL, NULL, NULL),
    (N'ct_asientos', N'CK_ct_asientos_estado',         'C',  NULL, NULL, NULL),
    (N'ct_asientos', N'CK_ct_asientos_cuadre',         'C',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_tipo',                  'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_orig',                  'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_td',                    'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_tc',                    'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_est',                   'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_elim',                  'D',  NULL, NULL, NULL),
    (N'ct_asientos', N'DF_ctas_fc',                    'D',  NULL, NULL, NULL),

    (N'ct_asiento_movimientos', N'PK_ct_asiento_movimientos',    'PK', NULL, NULL, NULL),
    (N'ct_asiento_movimientos', N'FK_ct_mov_asiento',            'F',  N'ct_asientos',      N'id_asiento,id_empresa',      N'id_asiento,id_empresa'),
    (N'ct_asiento_movimientos', N'FK_ct_mov_empresa',            'F',  N'empresas',         N'id_empresa',                 N'id_empresa'),
    (N'ct_asiento_movimientos', N'FK_ct_mov_cuenta',             'F',  N'ct_cuentas',       N'id_cuenta,id_empresa',       N'id_cuenta,id_empresa'),
    (N'ct_asiento_movimientos', N'FK_ct_mov_centro',             'F',  N'ct_centros_costo', N'id_centro_costo,id_empresa', N'id_centro_costo,id_empresa'),
    (N'ct_asiento_movimientos', N'CK_ct_mov_no_negativos',       'C',  NULL, NULL, NULL),
    (N'ct_asiento_movimientos', N'CK_ct_mov_debito_xor_credito', 'C',  NULL, NULL, NULL),
    (N'ct_asiento_movimientos', N'DF_ctmov_deb',                 'D',  NULL, NULL, NULL),
    (N'ct_asiento_movimientos', N'DF_ctmov_cre',                 'D',  NULL, NULL, NULL);

-- Indices (los de PK y UQ los cubre @restricciones): unicidad, filtro y columnas clave en orden
DECLARE @indices TABLE (tabla SYSNAME NOT NULL, indice SYSNAME NOT NULL, unico BIT NOT NULL, filtro NVARCHAR(100) NULL, columnas NVARCHAR(200) NOT NULL);
INSERT INTO @indices (tabla, indice, unico, filtro, columnas) VALUES
    (N'ct_cuentas',             N'UX_ct_cuentas_empresa_codigo',          1, NULL,               N'id_empresa,codigo'),
    (N'ct_cuentas',             N'IX_ct_cuentas_empresa_padre',           0, NULL,               N'id_empresa,id_cuenta_padre'),
    (N'ct_ejercicios',          N'UX_ct_ejercicios_empresa_anio',         1, NULL,               N'id_empresa,anio'),
    (N'ct_periodos',            N'UX_ct_periodos_ejercicio_numero',       1, NULL,               N'id_ejercicio,numero'),
    (N'ct_centros_costo',       N'UX_ct_centros_costo_empresa_codigo',    1, NULL,               N'id_empresa,codigo'),
    (N'ct_asientos',            N'UX_ct_asientos_empresa_evento',         1, N'id_evento_origen', N'id_empresa,id_evento_origen'),
    (N'ct_asientos',            N'UX_ct_asientos_empresa_numero',         1, N'numero',          N'id_empresa,numero'),
    (N'ct_asientos',            N'IX_ct_asientos_empresa_fecha',          0, NULL,               N'id_empresa,fecha'),
    (N'ct_asientos',            N'IX_ct_asientos_empresa_periodo_estado', 0, NULL,               N'id_empresa,id_periodo,estado'),
    (N'ct_asiento_movimientos', N'UX_ct_mov_asiento_linea',               1, NULL,               N'id_asiento,numero_linea'),
    (N'ct_asiento_movimientos', N'IX_ct_mov_asiento',                     0, NULL,               N'id_asiento'),
    (N'ct_asiento_movimientos', N'IX_ct_mov_empresa_cuenta',              0, NULL,               N'id_empresa,id_cuenta');

BEGIN TRY
    BEGIN TRANSACTION;

    -- [VERDE / AGREGA] ct_cuentas — Plan de cuentas jerarquico
    IF OBJECT_ID(N'dbo.ct_cuentas', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_cuentas (
            id_cuenta           INT IDENTITY(1,1) NOT NULL,
            id_empresa          INT            NOT NULL,
            codigo              NVARCHAR(30)   NOT NULL,
            nombre              NVARCHAR(200)  NOT NULL,
            id_cuenta_padre     INT            NULL,
            nivel               INT            NOT NULL CONSTRAINT DF_ctcta_nivel DEFAULT (1),
            naturaleza          NVARCHAR(10)   NOT NULL,   -- deudora / acreedora
            tipo                NVARCHAR(20)   NOT NULL,   -- activo/pasivo/patrimonio/ingreso/gasto/orden
            es_movimiento       BIT            NOT NULL CONSTRAINT DF_ctcta_mov  DEFAULT (1),  -- 1 = acepta asientos
            moneda              CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT DF_ctcta_mon DEFAULT ('HNL'),
            activo              BIT            NOT NULL CONSTRAINT DF_ctcta_act   DEFAULT (1),
            eliminado           BIT            NOT NULL CONSTRAINT DF_ctcta_elim  DEFAULT (0),
            fecha_eliminado     DATETIME2      NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctcta_fc   DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2      NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_ct_cuentas                PRIMARY KEY CLUSTERED (id_cuenta),
            CONSTRAINT UQ_ct_cuentas_cuenta_empresa UNIQUE (id_cuenta, id_empresa),
            CONSTRAINT FK_ct_cuentas_empresa        FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT FK_ct_cuentas_moneda         FOREIGN KEY (moneda)     REFERENCES dbo.monedas (codigo_iso),
            CONSTRAINT CK_ct_cuentas_nat            CHECK (naturaleza IN ('deudora','acreedora')),
            CONSTRAINT CK_ct_cuentas_tipo           CHECK (tipo IN ('activo','pasivo','patrimonio','ingreso','gasto','orden'))
        );
        -- El padre debe ser de la misma empresa (si id_cuenta_padre es NULL la FK no se evalua)
        ALTER TABLE dbo.ct_cuentas ADD CONSTRAINT FK_ct_cuentas_padre
            FOREIGN KEY (id_cuenta_padre, id_empresa) REFERENCES dbo.ct_cuentas (id_cuenta, id_empresa);
        CREATE UNIQUE INDEX UX_ct_cuentas_empresa_codigo ON dbo.ct_cuentas (id_empresa, codigo);
        CREATE INDEX        IX_ct_cuentas_empresa_padre  ON dbo.ct_cuentas (id_empresa, id_cuenta_padre);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_cuentas';
    END;

    -- [VERDE / AGREGA] ct_ejercicios — Ejercicios fiscales
    IF OBJECT_ID(N'dbo.ct_ejercicios', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_ejercicios (
            id_ejercicio        INT IDENTITY(1,1) NOT NULL,
            id_empresa          INT            NOT NULL,
            anio                INT            NOT NULL,
            fecha_inicio        DATE           NOT NULL,
            fecha_fin           DATE           NOT NULL,
            estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctejer_est DEFAULT ('abierto'),
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctejer_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2      NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_ct_ejercicios                   PRIMARY KEY CLUSTERED (id_ejercicio),
            CONSTRAINT UQ_ct_ejercicios_ejercicio_empresa UNIQUE (id_ejercicio, id_empresa),
            CONSTRAINT FK_ct_ejercicios_empresa           FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT CK_ct_ejercicios_estado            CHECK (estado IN ('abierto','cerrado')),
            CONSTRAINT CK_ct_ejercicios_fechas            CHECK (fecha_inicio <= fecha_fin)
        );
        CREATE UNIQUE INDEX UX_ct_ejercicios_empresa_anio ON dbo.ct_ejercicios (id_empresa, anio);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_ejercicios';
    END;

    -- [VERDE / AGREGA] ct_periodos — Periodos contables (por ejercicio, de la misma empresa)
    IF OBJECT_ID(N'dbo.ct_periodos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_periodos (
            id_periodo          INT IDENTITY(1,1) NOT NULL,
            id_ejercicio        INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            numero              INT            NOT NULL,   -- 1..12 (13 = ajustes/cierre)
            fecha_inicio        DATE           NOT NULL,
            fecha_fin           DATE           NOT NULL,
            estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctper_est DEFAULT ('abierto'),
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctper_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2      NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_ct_periodos                 PRIMARY KEY CLUSTERED (id_periodo),
            CONSTRAINT UQ_ct_periodos_periodo_empresa UNIQUE (id_periodo, id_empresa),
            CONSTRAINT FK_ct_periodos_ejercicio       FOREIGN KEY (id_ejercicio, id_empresa) REFERENCES dbo.ct_ejercicios (id_ejercicio, id_empresa),
            CONSTRAINT FK_ct_periodos_empresa         FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT CK_ct_periodos_estado          CHECK (estado IN ('abierto','cerrado')),
            CONSTRAINT CK_ct_periodos_numero          CHECK (numero BETWEEN 1 AND 13),
            CONSTRAINT CK_ct_periodos_fechas          CHECK (fecha_inicio <= fecha_fin)
        );
        CREATE UNIQUE INDEX UX_ct_periodos_ejercicio_numero ON dbo.ct_periodos (id_ejercicio, numero);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_periodos';
    END;

    -- [VERDE / AGREGA] ct_centros_costo — Centros de costo (opcional por linea de asiento)
    IF OBJECT_ID(N'dbo.ct_centros_costo', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_centros_costo (
            id_centro_costo     INT IDENTITY(1,1) NOT NULL,
            id_empresa          INT            NOT NULL,
            codigo              NVARCHAR(30)   NOT NULL,
            nombre              NVARCHAR(200)  NOT NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_ctcc_act  DEFAULT (1),
            eliminado           BIT            NOT NULL CONSTRAINT DF_ctcc_elim DEFAULT (0),
            fecha_eliminado     DATETIME2      NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctcc_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2      NULL,
            CONSTRAINT PK_ct_centros_costo                PRIMARY KEY CLUSTERED (id_centro_costo),
            CONSTRAINT UQ_ct_centros_costo_centro_empresa UNIQUE (id_centro_costo, id_empresa),
            CONSTRAINT FK_ct_centros_costo_empresa        FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
        );
        CREATE UNIQUE INDEX UX_ct_centros_costo_empresa_codigo ON dbo.ct_centros_costo (id_empresa, codigo);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_centros_costo';
    END;

    -- [VERDE / AGREGA] ct_asientos — Cabecera del asiento (partida doble)
    IF OBJECT_ID(N'dbo.ct_asientos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_asientos (
            id_asiento          INT IDENTITY(1,1) NOT NULL,
            id_empresa          INT            NOT NULL,
            id_periodo          INT            NOT NULL,
            numero              INT            NULL,        -- correlativo asignado al mayorizar
            fecha               DATE           NOT NULL,
            tipo_asiento        NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_tipo DEFAULT ('diario'),
            concepto            NVARCHAR(500)  NOT NULL,
            origen              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_orig DEFAULT ('manual'),
            id_evento_origen    BIGINT         NULL,        -- domain_events.id_evento (idempotencia outbox)
            total_debito        DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctas_td   DEFAULT (0),
            total_credito       DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctas_tc   DEFAULT (0),
            estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_est  DEFAULT ('borrador'),
            motivo_anulacion    NVARCHAR(500)  NULL,
            fecha_anulacion     DATETIME2      NULL,
            eliminado           BIT            NOT NULL CONSTRAINT DF_ctas_elim DEFAULT (0),
            fecha_eliminado     DATETIME2      NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctas_fc   DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2      NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_ct_asientos                 PRIMARY KEY CLUSTERED (id_asiento),
            CONSTRAINT UQ_ct_asientos_asiento_empresa UNIQUE (id_asiento, id_empresa),
            CONSTRAINT FK_ct_asientos_empresa         FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT FK_ct_asientos_periodo         FOREIGN KEY (id_periodo, id_empresa) REFERENCES dbo.ct_periodos (id_periodo, id_empresa),
            CONSTRAINT CK_ct_asientos_tipo            CHECK (tipo_asiento IN ('apertura','diario','ajuste','cierre')),
            CONSTRAINT CK_ct_asientos_origen          CHECK (origen IN ('manual','automatico')),
            CONSTRAINT CK_ct_asientos_estado          CHECK (estado IN ('borrador','mayorizado','anulado')),
            -- Partida doble: la cabecera se persiste cuadrada. Si se decide permitir borradores
            -- descuadrados, relajar este CHECK y validar el cuadre en el Service al mayorizar.
            CONSTRAINT CK_ct_asientos_cuadre          CHECK (total_debito = total_credito)
        );
        -- Idempotencia: un evento del outbox genera a lo sumo un asiento por empresa
        CREATE UNIQUE INDEX UX_ct_asientos_empresa_evento
            ON dbo.ct_asientos (id_empresa, id_evento_origen) WHERE id_evento_origen IS NOT NULL;
        -- Correlativo unico de asientos ya mayorizados
        CREATE UNIQUE INDEX UX_ct_asientos_empresa_numero
            ON dbo.ct_asientos (id_empresa, numero) WHERE numero IS NOT NULL;
        CREATE INDEX IX_ct_asientos_empresa_fecha          ON dbo.ct_asientos (id_empresa, fecha);
        CREATE INDEX IX_ct_asientos_empresa_periodo_estado ON dbo.ct_asientos (id_empresa, id_periodo, estado);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_asientos';
    END;

    -- [VERDE / AGREGA] ct_asiento_movimientos — Detalle del asiento (debito / credito), todo de la misma empresa
    IF OBJECT_ID(N'dbo.ct_asiento_movimientos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ct_asiento_movimientos (
            id_movimiento       INT IDENTITY(1,1) NOT NULL,
            id_asiento          INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            numero_linea        INT            NOT NULL,
            id_cuenta           INT            NOT NULL,
            id_centro_costo     INT            NULL,
            descripcion         NVARCHAR(300)  NULL,
            debito              DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctmov_deb DEFAULT (0),
            credito             DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctmov_cre DEFAULT (0),
            CONSTRAINT PK_ct_asiento_movimientos    PRIMARY KEY CLUSTERED (id_movimiento),
            CONSTRAINT FK_ct_mov_asiento            FOREIGN KEY (id_asiento, id_empresa)      REFERENCES dbo.ct_asientos (id_asiento, id_empresa),
            CONSTRAINT FK_ct_mov_empresa            FOREIGN KEY (id_empresa)                  REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT FK_ct_mov_cuenta             FOREIGN KEY (id_cuenta, id_empresa)       REFERENCES dbo.ct_cuentas (id_cuenta, id_empresa),
            -- Si id_centro_costo es NULL la FK no se evalua
            CONSTRAINT FK_ct_mov_centro             FOREIGN KEY (id_centro_costo, id_empresa) REFERENCES dbo.ct_centros_costo (id_centro_costo, id_empresa),
            CONSTRAINT CK_ct_mov_no_negativos       CHECK (debito >= 0 AND credito >= 0),
            -- Exactamente uno de los dos importes debe ser > 0
            CONSTRAINT CK_ct_mov_debito_xor_credito CHECK ((debito > 0 AND credito = 0) OR (credito > 0 AND debito = 0))
        );
        CREATE UNIQUE INDEX UX_ct_mov_asiento_linea  ON dbo.ct_asiento_movimientos (id_asiento, numero_linea);
        CREATE INDEX        IX_ct_mov_asiento        ON dbo.ct_asiento_movimientos (id_asiento);
        CREATE INDEX        IX_ct_mov_empresa_cuenta ON dbo.ct_asiento_movimientos (id_empresa, id_cuenta);
        SET @cambios += 1;
        PRINT N'  + dbo.ct_asiento_movimientos';
    END;

    -- --------------------------------------------------------
    -- POSTCHECK (dentro de la transaccion: si algo no cuadra, se revierte todo)
    -- Las comparaciones contra el catalogo llevan COLLATE DATABASE_DEFAULT.
    -- --------------------------------------------------------

    -- a) Columnas: nombre, tipo, longitud, nulabilidad e identidad
    WITH reales AS (
        SELECT o.name COLLATE DATABASE_DEFAULT AS tabla, c.name COLLATE DATABASE_DEFAULT AS columna,
               CAST(CASE
                   WHEN ty.name IN ('char', 'varchar', 'binary', 'varbinary')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length AS VARCHAR(10)) END + ')'
                   WHEN ty.name IN ('nchar', 'nvarchar')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length / 2 AS VARCHAR(10)) END + ')'
                   WHEN ty.name IN ('decimal', 'numeric')
                       THEN ty.name + '(' + CAST(c.precision AS VARCHAR(3)) + ',' + CAST(c.scale AS VARCHAR(3)) + ')'
                   WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
                       THEN ty.name + '(' + CAST(c.scale AS VARCHAR(3)) + ')'
                   ELSE ty.name END AS VARCHAR(30)) COLLATE DATABASE_DEFAULT AS tipo,
               c.is_nullable AS nulable, c.is_identity AS identidad
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE o.object_id IN (OBJECT_ID(N'dbo.ct_cuentas'), OBJECT_ID(N'dbo.ct_ejercicios'), OBJECT_ID(N'dbo.ct_periodos'),
                              OBJECT_ID(N'dbo.ct_centros_costo'), OBJECT_ID(N'dbo.ct_asientos'), OBJECT_ID(N'dbo.ct_asiento_movimientos'))
    )
    INSERT INTO @problemas (problema)
    SELECT CASE
               WHEN r.columna IS NULL THEN CONCAT(N'Falta la columna ', e.tabla, N'.', e.columna)
               WHEN e.columna IS NULL THEN CONCAT(N'Columna no esperada ', r.tabla, N'.', r.columna)
               ELSE CONCAT(N'Columna ', e.tabla, N'.', e.columna, N': es ', r.tipo,
                           CASE WHEN r.nulable = 1 THEN N' NULL' ELSE N' NOT NULL' END,
                           CASE WHEN r.identidad = 1 THEN N' IDENTITY' ELSE N'' END,
                           N'; se esperaba ', e.tipo,
                           CASE WHEN e.nulable = 1 THEN N' NULL' ELSE N' NOT NULL' END,
                           CASE WHEN e.identidad = 1 THEN N' IDENTITY' ELSE N'' END)
           END
    FROM @columnas e
    FULL OUTER JOIN reales r ON r.tabla = e.tabla AND r.columna = e.columna
    WHERE r.columna IS NULL OR e.columna IS NULL
       OR r.tipo <> e.tipo OR r.nulable <> e.nulable OR r.identidad <> e.identidad;

    -- b) ct_cuentas.moneda con la intercalacion de dbo.monedas.codigo_iso (la FK la exige)
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.ct_cuentas') AND name = N'moneda'
                     AND collation_name = N'SQL_Latin1_General_CP1_CI_AS')
        INSERT INTO @problemas (problema) VALUES (N'ct_cuentas.moneda no tiene la intercalacion SQL_Latin1_General_CP1_CI_AS');

    -- c) Restricciones por nombre y tipo
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta o no coincide la restriccion ', x.nombre, N' en ', x.tabla)
    FROM @restricciones x
    WHERE NOT EXISTS (SELECT 1 FROM sys.objects o
                      WHERE o.name COLLATE DATABASE_DEFAULT = x.nombre
                        AND o.type COLLATE DATABASE_DEFAULT = x.tipo
                        AND o.parent_object_id = OBJECT_ID(N'dbo.' + x.tabla));

    -- d) Claves foraneas: tabla destino y columnas (de origen y destino) en orden
    WITH fks AS (
        SELECT fk.name COLLATE DATABASE_DEFAULT AS nombre,
               OBJECT_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT AS tabla_destino,
               STRING_AGG(cp.name, ',') WITHIN GROUP (ORDER BY fc.constraint_column_id) COLLATE DATABASE_DEFAULT AS columnas,
               STRING_AGG(cr.name, ',') WITHIN GROUP (ORDER BY fc.constraint_column_id) COLLATE DATABASE_DEFAULT AS columnas_destino
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = fk.object_id
        JOIN sys.columns cp ON cp.object_id = fc.parent_object_id AND cp.column_id = fc.parent_column_id
        JOIN sys.columns cr ON cr.object_id = fc.referenced_object_id AND cr.column_id = fc.referenced_column_id
        GROUP BY fk.name, fk.referenced_object_id
    )
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'La clave foranea ', x.nombre, N' no apunta a ', x.tabla_destino, N'(', x.columnas_destino,
                  N') desde (', x.columnas, N')')
    FROM @restricciones x
    WHERE x.tipo = 'F'
      AND NOT EXISTS (SELECT 1 FROM fks f
                      WHERE f.nombre = x.nombre AND f.tabla_destino = x.tabla_destino
                        AND f.columnas = x.columnas AND f.columnas_destino = x.columnas_destino);

    -- e) Indices: existencia, unicidad, filtro y columnas clave en orden
    WITH idx AS (
        SELECT OBJECT_NAME(i.object_id) COLLATE DATABASE_DEFAULT AS tabla, i.name COLLATE DATABASE_DEFAULT AS indice,
               i.is_unique AS unico, i.has_filter AS filtrado,
               CAST(i.filter_definition AS NVARCHAR(400)) COLLATE DATABASE_DEFAULT AS filtro,
               STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) COLLATE DATABASE_DEFAULT AS columnas
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id IN (OBJECT_ID(N'dbo.ct_cuentas'), OBJECT_ID(N'dbo.ct_ejercicios'), OBJECT_ID(N'dbo.ct_periodos'),
                              OBJECT_ID(N'dbo.ct_centros_costo'), OBJECT_ID(N'dbo.ct_asientos'), OBJECT_ID(N'dbo.ct_asiento_movimientos'))
        GROUP BY i.object_id, i.name, i.is_unique, i.has_filter, i.filter_definition
    )
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta el indice ', x.indice, N' en ', x.tabla, N' o no coincide su unicidad, filtro o columnas')
    FROM @indices x
    WHERE NOT EXISTS (SELECT 1 FROM idx i
                      WHERE i.tabla = x.tabla AND i.indice = x.indice AND i.unico = x.unico AND i.columnas = x.columnas
                        AND ((x.filtro IS NULL AND i.filtrado = 0)
                             OR (x.filtro IS NOT NULL AND i.filtrado = 1 AND i.filtro LIKE N'%' + x.filtro + N'%IS NOT NULL%')));

    IF EXISTS (SELECT 1 FROM @problemas)
    BEGIN
        SELECT problema FROM @problemas;
        THROW 50010, N'POSTCHECK 010 fallo (ver la lista de problemas). Se revierte todo.', 1;
    END;

    COMMIT TRANSACTION;

    IF @cambios = 0
        PRINT N'No habia nada que cambiar: el nucleo contable ya estaba creado. POSTCHECK correcto.';
    ELSE
        PRINT CONCAT(N'Listo: ', @cambios, N' tabla(s) creada(s). POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- Resultado (solo lectura)
-- ------------------------------------------------------------
SELECT t.name AS tabla,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS columnas
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name IN (N'ct_cuentas', N'ct_ejercicios', N'ct_periodos', N'ct_centros_costo', N'ct_asientos', N'ct_asiento_movimientos')
ORDER BY t.name;

IF OBJECT_ID(N'tempdb..#precheck_010') IS NOT NULL DROP TABLE #precheck_010;
GO
