-- ============================================================
-- Script   : 020_tasas_cambio_v2.sql
-- Proposito: Estructura v2 de las tasas de cambio para el job automatico de eGestion360-Web, que
--            reemplaza a la app WinForms APICambioAHNL (descargaba el Excel del BCH y guardaba el
--            dolar en otra base):
--            * dbo.tasas_cambio pasa a la estructura v2: tasa oficial (id_empresa NULL) o propia de
--              una empresa, tipo COMPRA / VENTA / REFERENCIA, fecha de vigencia, fuente, tasa derivada
--              (es_derivada) e historico versionado (estado, version, id_tasa_anterior).
--              Convencion: 1 moneda_origen = tasa moneda_destino (ej. 1 USD = 26.8925 HNL).
--            * dbo.tasas_cambio_ejecuciones: bitacora del job, una fila por ejecucion o intento.
--            * dbo.tasas_cambio_ejecuciones_detalle: resultado por moneda y tipo de cada ejecucion.
--            * Retira dbo.tipos_cambio (F0_Catalogos_Transversales.sql), que nunca tuvo pantalla ni
--              servicio, si existe y esta vacia.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-04
-- BD       : eBD_SPD
-- Requiere : dbo.empresas; dbo.monedas con codigo_iso CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS
--            (script 017 aplicado); SQL Server 2016 o superior (DROP ... IF EXISTS).
--            dbo.tasas_cambio (estructura vieja de KPI_02) y dbo.tipos_cambio deben estar VACIAS si existen.
-- Rollback : archivo aparte 020_tasas_cambio_v2_reversa.sql (solo corre si las tablas nuevas estan vacias).
-- ============================================================
-- Modelo C# : Models/Catalogos/TasaCambio.cs, Models/Catalogos/TasaCambioEjecucion.cs (incluye
--             TasaCambioEjecucionDetalle), Models/Catalogos/TasasCambioCatalogo.cs (valores de los CHECK)
--             y Data/ApplicationDbContext.cs (indice unico filtrado, IX_tce_job_fecha y claves foraneas).
-- Documento : 1 - Documetacion/TASAS_CAMBIO.md
--
-- CLASIFICACION DE LOS CAMBIOS (para /alerta-bd)
--   [VERDE / AGREGA]
--     + dbo.tasas_cambio_ejecuciones (bitacora del job) con el indice IX_tce_job_fecha.
--     + dbo.tasas_cambio_ejecuciones_detalle con el indice IX_tced_ejecucion.
--     + dbo.tasas_cambio v2 con UX_tasas_cambio_vigente (unico filtrado) e IX_tasas_cambio_consulta.
--   [AMARILLO / MODIFICA]
--     ~ dbo.tasas_cambio se recrea con la estructura v2 (DROP + CREATE, solo si esta vacia):
--       id_empresa pasa a NULL (NULL = tasa oficial, comun a todas las empresas); fecha -> fecha_vigencia;
--       fuente NVARCHAR(100) NULL -> VARCHAR(20) NOT NULL; columnas nuevas tipo_tasa, fecha_hora_obtencion,
--       referencia_fuente, es_derivada, estado, version, id_tasa_anterior e id_ejecucion.
--       Las claves foraneas a empresas y a monedas (las del 017) se recrean con el MISMO nombre.
--   [ROJO / ELIMINA]
--     - dbo.tasas_cambio con la estructura vieja (KPI_02): sus 3 claves foraneas, 2 indices
--       (UX_tasas_cambio_empresa_fecha_par, IX_tasas_cambio_par_fecha), 2 CHECK y 2 valores por defecto.
--       Solo si esta vacia.
--     - dbo.tipos_cambio (F0) con su clave foranea y su indice. Solo si existe y esta vacia.
--   [AZUL / IMPACTO]
--     * Datos: ninguno. El PRECHECK aborta, sin cambiar nada, si alguna tabla que se elimina tiene filas.
--     * Aplicacion publicada: no lee estas tablas (BchTasaCambioService escribia la estructura vieja pero
--       nunca se registro en Program.cs; la entidad TipoCambio no tenia pantalla). Se puede aplicar antes
--       de publicar la version con el job; esa version necesita este script ANTES de activar
--       TasasCambio__Habilitado=true.
--     * Claves foraneas hacia dbo.monedas: las mismas dos de tasas_cambio, con el mismo nombre.
--     * Bloqueo: breve, sobre tablas vacias. Sin datos semilla.
--
-- Seguridad de ejecucion (igual que 017 y 019):
--   * Ejecutar TODO el archivo en una sola sesion. El PRECHECK deja la marca #precheck_020 que exige el
--     bloque de cambio; si el PRECHECK falla, el bloque de cambio no hace nada.
--   * Idempotente: una segunda ejecucion no cambia nada (lo informa y vuelve a verificar la estructura).
--   * El cambio corre en UNA transaccion (XACT_ABORT + TRY/CATCH) con el POSTCHECK adentro: si la
--     estructura final no coincide con la esperada (columna por columna, claves e indices), se revierte todo.
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
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
-- La marca se crea primero y vacia: si cualquier validacion aborta, queda sin la fila ok = 1.
IF OBJECT_ID(N'tempdb..#precheck_020') IS NOT NULL DROP TABLE #precheck_020;
CREATE TABLE #precheck_020 (
    ok                   BIT         NOT NULL,
    estado_tasas_cambio  VARCHAR(10) NOT NULL,   -- NO_EXISTE, V1 (KPI_02) o V2
    tipos_cambio_existe  BIT         NOT NULL
);

IF DB_NAME() <> N'eBD_SPD'
    THROW 50020, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 13
    THROW 50020, N'Se requiere SQL Server 2016 o superior (DROP ... IF EXISTS). Abortar.', 1;

IF OBJECT_ID(N'dbo.empresas', N'U') IS NULL OR OBJECT_ID(N'dbo.monedas', N'U') IS NULL
    THROW 50020, N'Faltan dbo.empresas o dbo.monedas. Abortar.', 1;

-- Una clave foranea exige el mismo tipo y la misma intercalacion que la columna referenciada.
IF NOT EXISTS (SELECT 1
               FROM sys.columns c
               JOIN sys.types ty ON ty.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso'
                 AND ty.name = N'char' AND c.max_length = 3
                 AND c.collation_name = N'SQL_Latin1_General_CP1_CI_AS')
    THROW 50020, N'dbo.monedas.codigo_iso no es CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS. Ejecute antes el script 017. Abortar.', 1;

-- Los nombres que usa el script, si existen, deben ser tablas (no vistas, sinonimos u otros objetos).
IF (OBJECT_ID(N'dbo.tasas_cambio') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tasas_cambio_ejecuciones') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle') IS NOT NULL AND OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL)
   OR (OBJECT_ID(N'dbo.tipos_cambio') IS NOT NULL AND OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NULL)
    THROW 50020, N'Hay un objeto que no es tabla con el nombre de una de las tablas del script. Revise a mano. Abortar.', 1;

DECLARE @estado VARCHAR(10), @tipos BIT = 0, @n INT, @msg NVARCHAR(800);

-- 1) Estado de dbo.tasas_cambio
IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL
    SET @estado = 'NO_EXISTE';
ELSE IF COL_LENGTH(N'dbo.tasas_cambio', N'fecha') IS NOT NULL
        AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NULL
        AND COL_LENGTH(N'dbo.tasas_cambio', N'tipo_tasa') IS NULL
    SET @estado = 'V1';
ELSE IF COL_LENGTH(N'dbo.tasas_cambio', N'fecha') IS NULL
        AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NOT NULL
        AND COL_LENGTH(N'dbo.tasas_cambio', N'tipo_tasa') IS NOT NULL
    SET @estado = 'V2';
ELSE
BEGIN
    THROW 50020, N'dbo.tasas_cambio no tiene ni la estructura vieja (KPI_02) ni la v2. Revise a mano. Abortar.', 1;
END;

-- 2) Estructura vieja: debe estar vacia y sin dependencias que este script no conoce.
--    Con la v2 no se valida nada de esto: no se toca (sus filas son del job y son validas).
IF @estado = 'V1'
BEGIN
    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio;', N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'dbo.tasas_cambio (estructura vieja de KPI_02) tiene ', @n, N' fila(s). Este script no migra datos ',
                          N'a ciegas: revise a mano de donde salieron y decida si se descartan o se pasan a la estructura v2 ',
                          N'(fuente MANUAL o LEGADO_WINFORMS). No se cambio nada. Abortar.');
        THROW 50020, @msg, 1;
    END;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE referenced_object_id = OBJECT_ID(N'dbo.tasas_cambio')
                 AND parent_object_id <> OBJECT_ID(N'dbo.tasas_cambio'))
        THROW 50020, N'Otra tabla tiene una clave foranea hacia dbo.tasas_cambio (ver sys.foreign_keys). Revise a mano. Abortar.', 1;

    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID(N'dbo.tasas_cambio'))
        THROW 50020, N'dbo.tasas_cambio tiene un disparador (trigger) que este script no conoce. Revise a mano. Abortar.', 1;

    -- (Los indices filtrados y las restricciones CHECK de la propia tabla tambien aparecen como dependencias:
    --  no cuentan. Solo importan objetos ajenos a la tabla: vistas, procedimientos, funciones, otras tablas.)
    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
               JOIN sys.objects o ON o.object_id = d.referencing_id
               WHERE d.referenced_entity_name = N'tasas_cambio'
                 AND ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
                 AND d.referencing_id <> OBJECT_ID(N'dbo.tasas_cambio')
                 AND o.parent_object_id <> OBJECT_ID(N'dbo.tasas_cambio'))
        THROW 50020, N'Una vista, procedimiento o funcion usa dbo.tasas_cambio (ver sys.sql_expression_dependencies). Revise a mano. Abortar.', 1;
END;

-- 3) dbo.tipos_cambio (F0): solo se elimina si esta vacia y nada depende de ella.
IF OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NOT NULL
BEGIN
    SET @tipos = 1;

    EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tipos_cambio;', N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'dbo.tipos_cambio tiene ', @n, N' fila(s). Este script no borra datos: revise a mano si se descartan ',
                          N'o se pasan a dbo.tasas_cambio. No se cambio nada. Abortar.');
        THROW 50020, @msg, 1;
    END;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE referenced_object_id = OBJECT_ID(N'dbo.tipos_cambio')
                 AND parent_object_id <> OBJECT_ID(N'dbo.tipos_cambio'))
        THROW 50020, N'Otra tabla tiene una clave foranea hacia dbo.tipos_cambio (ver sys.foreign_keys). Revise a mano. Abortar.', 1;

    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID(N'dbo.tipos_cambio'))
        THROW 50020, N'dbo.tipos_cambio tiene un disparador (trigger) que este script no conoce. Revise a mano. Abortar.', 1;

    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
               JOIN sys.objects o ON o.object_id = d.referencing_id
               WHERE d.referenced_entity_name = N'tipos_cambio'
                 AND ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
                 AND d.referencing_id <> OBJECT_ID(N'dbo.tipos_cambio')
                 AND o.parent_object_id <> OBJECT_ID(N'dbo.tipos_cambio'))
        THROW 50020, N'Una vista, procedimiento o funcion usa dbo.tipos_cambio (ver sys.sql_expression_dependencies). Revise a mano. Abortar.', 1;
END;

-- 4) Avisos (no abortan)
IF @estado <> 'V2'
   AND (OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NOT NULL
        OR OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NOT NULL)
    PRINT N'Aviso: la bitacora (tasas_cambio_ejecuciones o su detalle) ya existe sin la tasas_cambio v2. No se recrea; el POSTCHECK verificara su estructura.';

SELECT @n = COUNT(*) FROM dbo.monedas WHERE codigo_iso IN ('HNL', 'USD', 'EUR') AND activo = 1;
IF @n < 3
    PRINT N'Aviso: HNL, USD o EUR no estan activas en dbo.monedas. El script sigue, pero el job no podra guardar esas tasas.';

INSERT INTO #precheck_020 (ok, estado_tasas_cambio, tipos_cambio_existe) VALUES (1, @estado, @tipos);

-- Plan de lo que hara el bloque de cambio (SELECT para verlo tambien en DBeaver)
SELECT @estado AS estado_tasas_cambio,
       CASE @estado WHEN 'V2' THEN N'Ya tiene la estructura v2: no se toca'
                    WHEN 'V1' THEN N'Estructura vieja vacia: se elimina y se crea la v2'
                    ELSE N'No existe: se crea la v2' END AS accion_tasas_cambio,
       CASE WHEN OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL THEN N'Se crea' ELSE N'Ya existe' END AS tasas_cambio_ejecuciones,
       CASE WHEN OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL THEN N'Se crea' ELSE N'Ya existe' END AS tasas_cambio_ejecuciones_detalle,
       CASE WHEN @tipos = 1 THEN N'Existe vacia: se elimina' ELSE N'No existe: nada que hacer' END AS tipos_cambio,
       CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS NVARCHAR(128)) AS intercalacion_bd;
PRINT N'PRECHECK 020 superado.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK dentro de la transaccion
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_020') IS NULL
    THROW 50020, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM #precheck_020 WHERE ok = 1)
    THROW 50020, N'El PRECHECK no termino bien (ver el error anterior). No se cambio nada.', 1;

DECLARE @n INT, @cambios INT = 0;
DECLARE @problemas TABLE (problema NVARCHAR(400) NOT NULL);

-- Estructura esperada: debe coincidir con los modelos C# (ver la cabecera). tipo con la sintaxis de
-- SQL Server; ROWVERSION aparece en el catalogo como timestamp.
DECLARE @columnas TABLE (tabla SYSNAME NOT NULL, columna SYSNAME NOT NULL, tipo VARCHAR(30) NOT NULL,
                         nulable BIT NOT NULL, identidad BIT NOT NULL);
INSERT INTO @columnas (tabla, columna, tipo, nulable, identidad) VALUES
    (N'tasas_cambio', N'id_tasa_cambio',       'int',            0, 1),
    (N'tasas_cambio', N'id_empresa',           'int',            1, 0),
    (N'tasas_cambio', N'moneda_origen',        'char(3)',        0, 0),
    (N'tasas_cambio', N'moneda_destino',       'char(3)',        0, 0),
    (N'tasas_cambio', N'tipo_tasa',            'varchar(10)',    0, 0),
    (N'tasas_cambio', N'tasa',                 'decimal(18,8)',  0, 0),
    (N'tasas_cambio', N'fecha_vigencia',       'date',           0, 0),
    (N'tasas_cambio', N'fecha_hora_obtencion', 'datetime2(3)',   0, 0),
    (N'tasas_cambio', N'fuente',               'varchar(20)',    0, 0),
    (N'tasas_cambio', N'referencia_fuente',    'nvarchar(400)',  1, 0),
    (N'tasas_cambio', N'es_derivada',          'bit',            0, 0),
    (N'tasas_cambio', N'estado',               'varchar(12)',    0, 0),
    (N'tasas_cambio', N'version',              'smallint',       0, 0),
    (N'tasas_cambio', N'id_tasa_anterior',     'int',            1, 0),
    (N'tasas_cambio', N'id_ejecucion',         'bigint',         1, 0),
    (N'tasas_cambio', N'eliminado',            'bit',            0, 0),
    (N'tasas_cambio', N'fecha_eliminado',      'datetime2(3)',   1, 0),
    (N'tasas_cambio', N'creado_por',           'nvarchar(100)',  0, 0),
    (N'tasas_cambio', N'fecha_creacion',       'datetime2(3)',   0, 0),
    (N'tasas_cambio', N'modificado_por',       'nvarchar(100)',  1, 0),
    (N'tasas_cambio', N'fecha_modificacion',   'datetime2(3)',   1, 0),
    (N'tasas_cambio', N'token_concurrencia',   'timestamp',      0, 0),
    (N'tasas_cambio_ejecuciones', N'id_ejecucion',        'bigint',         0, 1),
    (N'tasas_cambio_ejecuciones', N'job',                 'varchar(40)',    0, 0),
    (N'tasas_cambio_ejecuciones', N'disparador',          'varchar(12)',    0, 0),
    (N'tasas_cambio_ejecuciones', N'fecha_objetivo',      'date',           0, 0),
    (N'tasas_cambio_ejecuciones', N'intento',             'tinyint',        0, 0),
    (N'tasas_cambio_ejecuciones', N'id_ejecucion_origen', 'bigint',         1, 0),
    (N'tasas_cambio_ejecuciones', N'estado',              'varchar(20)',    0, 0),
    (N'tasas_cambio_ejecuciones', N'fuente',              'varchar(20)',    1, 0),
    (N'tasas_cambio_ejecuciones', N'endpoint',            'nvarchar(400)',  1, 0),
    (N'tasas_cambio_ejecuciones', N'http_status',         'smallint',       1, 0),
    (N'tasas_cambio_ejecuciones', N'leidos',              'int',            0, 0),
    (N'tasas_cambio_ejecuciones', N'insertados',          'int',            0, 0),
    (N'tasas_cambio_ejecuciones', N'reemplazados',        'int',            0, 0),
    (N'tasas_cambio_ejecuciones', N'duplicados',          'int',            0, 0),
    (N'tasas_cambio_ejecuciones', N'invalidos',           'int',            0, 0),
    (N'tasas_cambio_ejecuciones', N'hash_contenido',      'char(64)',       1, 0),
    (N'tasas_cambio_ejecuciones', N'mensaje',             'nvarchar(1000)', 1, 0),
    (N'tasas_cambio_ejecuciones', N'detalle_error',       'nvarchar(max)',  1, 0),
    (N'tasas_cambio_ejecuciones', N'proximo_intento_utc', 'datetime2(3)',   1, 0),
    (N'tasas_cambio_ejecuciones', N'notificado',          'bit',            0, 0),
    (N'tasas_cambio_ejecuciones', N'servidor',            'nvarchar(100)',  0, 0),
    (N'tasas_cambio_ejecuciones', N'version_app',         'varchar(30)',    1, 0),
    (N'tasas_cambio_ejecuciones', N'ejecutado_por',       'nvarchar(100)',  0, 0),
    (N'tasas_cambio_ejecuciones', N'inicio_utc',          'datetime2(3)',   0, 0),
    (N'tasas_cambio_ejecuciones', N'fin_utc',             'datetime2(3)',   1, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'id_detalle',     'bigint',        0, 1),
    (N'tasas_cambio_ejecuciones_detalle', N'id_ejecucion',   'bigint',        0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'moneda_origen',  'char(3)',       0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'moneda_destino', 'char(3)',       0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'tipo_tasa',      'varchar(10)',   0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'fecha_vigencia', 'date',          1, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'valor_leido',    'decimal(18,8)', 1, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'resultado',      'varchar(20)',   0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'motivo',         'nvarchar(500)', 1, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'id_tasa_cambio', 'int',           1, 0);

-- Restricciones con nombre: PK = clave primaria, F = clave foranea (con su tabla destino), C = CHECK, D = valor por defecto
DECLARE @restricciones TABLE (tabla SYSNAME NOT NULL, nombre SYSNAME NOT NULL, tipo CHAR(2) NOT NULL, tabla_destino SYSNAME NULL);
INSERT INTO @restricciones (tabla, nombre, tipo, tabla_destino) VALUES
    (N'tasas_cambio', N'PK_tasas_cambio',                 'PK', NULL),
    (N'tasas_cambio', N'FK_tasas_cambio_empresa',         'F',  N'empresas'),
    (N'tasas_cambio', N'FK_tasas_cambio_moneda_origen',   'F',  N'monedas'),
    (N'tasas_cambio', N'FK_tasas_cambio_moneda_destino',  'F',  N'monedas'),
    (N'tasas_cambio', N'FK_tasas_cambio_tasa_anterior',   'F',  N'tasas_cambio'),
    (N'tasas_cambio', N'FK_tasas_cambio_ejecucion',       'F',  N'tasas_cambio_ejecuciones'),
    (N'tasas_cambio', N'CK_tasas_cambio_tasa',            'C',  NULL),
    (N'tasas_cambio', N'CK_tasas_cambio_monedas',         'C',  NULL),
    (N'tasas_cambio', N'CK_tasas_cambio_tipo_tasa',       'C',  NULL),
    (N'tasas_cambio', N'CK_tasas_cambio_estado',          'C',  NULL),
    (N'tasas_cambio', N'DF_tasas_cambio_es_derivada',     'D',  NULL),
    (N'tasas_cambio', N'DF_tasas_cambio_estado',          'D',  NULL),
    (N'tasas_cambio', N'DF_tasas_cambio_version',         'D',  NULL),
    (N'tasas_cambio', N'DF_tasas_cambio_eliminado',       'D',  NULL),
    (N'tasas_cambio', N'DF_tasas_cambio_fecha_creacion',  'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'PK_tasas_cambio_ejecuciones', 'PK', NULL),
    (N'tasas_cambio_ejecuciones', N'FK_tce_ejecucion_origen',     'F',  N'tasas_cambio_ejecuciones'),
    (N'tasas_cambio_ejecuciones', N'CK_tce_disparador',           'C',  NULL),
    (N'tasas_cambio_ejecuciones', N'CK_tce_estado',               'C',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_job',                  'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_intento',              'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_estado',               'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_leidos',               'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_insertados',           'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_reemplazados',         'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_duplicados',           'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_invalidos',            'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_notificado',           'D',  NULL),
    (N'tasas_cambio_ejecuciones', N'DF_tce_inicio_utc',           'D',  NULL),
    (N'tasas_cambio_ejecuciones_detalle', N'PK_tasas_cambio_ejecuciones_detalle', 'PK', NULL),
    (N'tasas_cambio_ejecuciones_detalle', N'FK_tced_ejecucion',                   'F',  N'tasas_cambio_ejecuciones'),
    (N'tasas_cambio_ejecuciones_detalle', N'FK_tced_tasa_cambio',                 'F',  N'tasas_cambio'),
    (N'tasas_cambio_ejecuciones_detalle', N'CK_tced_resultado',                   'C',  NULL);

-- Indices: columnas clave en orden (posicion > 0) y columnas incluidas (posicion = 0)
DECLARE @indices TABLE (tabla SYSNAME NOT NULL, indice SYSNAME NOT NULL, unico BIT NOT NULL, filtrado BIT NOT NULL);
INSERT INTO @indices (tabla, indice, unico, filtrado) VALUES
    (N'tasas_cambio',                     N'UX_tasas_cambio_vigente',  1, 1),
    (N'tasas_cambio',                     N'IX_tasas_cambio_consulta', 0, 1),
    (N'tasas_cambio_ejecuciones',         N'IX_tce_job_fecha',         0, 0),
    (N'tasas_cambio_ejecuciones_detalle', N'IX_tced_ejecucion',        0, 0);

DECLARE @indice_columnas TABLE (indice SYSNAME NOT NULL, columna SYSNAME NOT NULL, posicion INT NOT NULL, descendente BIT NOT NULL);
INSERT INTO @indice_columnas (indice, columna, posicion, descendente) VALUES
    (N'UX_tasas_cambio_vigente',  N'moneda_origen',  1, 0),
    (N'UX_tasas_cambio_vigente',  N'moneda_destino', 2, 0),
    (N'UX_tasas_cambio_vigente',  N'tipo_tasa',      3, 0),
    (N'UX_tasas_cambio_vigente',  N'fecha_vigencia', 4, 0),
    (N'UX_tasas_cambio_vigente',  N'id_empresa',     5, 0),
    (N'IX_tasas_cambio_consulta', N'moneda_origen',  1, 0),
    (N'IX_tasas_cambio_consulta', N'moneda_destino', 2, 0),
    (N'IX_tasas_cambio_consulta', N'tipo_tasa',      3, 0),
    (N'IX_tasas_cambio_consulta', N'fecha_vigencia', 4, 1),
    (N'IX_tasas_cambio_consulta', N'tasa',           0, 0),
    (N'IX_tasas_cambio_consulta', N'id_empresa',     0, 0),
    (N'IX_tce_job_fecha',         N'job',            1, 0),
    (N'IX_tce_job_fecha',         N'fecha_objetivo', 2, 0),
    (N'IX_tce_job_fecha',         N'inicio_utc',     3, 0),
    (N'IX_tced_ejecucion',        N'id_ejecucion',   1, 0);

BEGIN TRY
    BEGIN TRANSACTION;

    -- --------------------------------------------------------
    -- CAMBIO 1/5: [ROJO / ELIMINA] dbo.tipos_cambio (F0), solo si existe y esta vacia
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NOT NULL
    BEGIN
        -- Se vuelve a contar con bloqueo exclusivo hasta el final de la transaccion.
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tipos_cambio WITH (TABLOCKX, HOLDLOCK);',
                               N'@n INT OUTPUT', @n = @n OUTPUT;
        IF @n > 0
            THROW 50020, N'dbo.tipos_cambio recibio filas despues del PRECHECK. Se revierte.', 1;

        DROP TABLE dbo.tipos_cambio;
        SET @cambios += 1;
        PRINT N'  - dbo.tipos_cambio eliminada (estaba vacia)';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 2/5: [ROJO / ELIMINA] dbo.tasas_cambio con la estructura vieja (KPI_02), solo si esta vacia
    --   Primero sus claves foraneas e indices por nombre; el DROP TABLE se lleva la PK, los CHECK y los defaults.
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha') IS NOT NULL
       AND COL_LENGTH(N'dbo.tasas_cambio', N'fecha_vigencia') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @n = COUNT(*) FROM dbo.tasas_cambio WITH (TABLOCKX, HOLDLOCK);',
                               N'@n INT OUTPUT', @n = @n OUTPUT;
        IF @n > 0
            THROW 50020, N'dbo.tasas_cambio (estructura vieja) recibio filas despues del PRECHECK. Se revierte.', 1;

        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_origen' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
            ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_moneda_origen;
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_destino' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
            ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_moneda_destino;
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
            ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_empresa;

        DROP INDEX IF EXISTS UX_tasas_cambio_empresa_fecha_par ON dbo.tasas_cambio;
        DROP INDEX IF EXISTS IX_tasas_cambio_par_fecha ON dbo.tasas_cambio;

        DROP TABLE dbo.tasas_cambio;
        SET @cambios += 1;
        PRINT N'  - dbo.tasas_cambio con la estructura vieja eliminada (estaba vacia): 3 FK, 2 indices, 2 CHECK, 2 defaults';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 3/5: [VERDE / AGREGA] dbo.tasas_cambio_ejecuciones (bitacora del job)
    --   Se crea antes que tasas_cambio porque esta la referencia (id_ejecucion).
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tasas_cambio_ejecuciones (
            id_ejecucion         BIGINT          IDENTITY(1,1) NOT NULL,
            job                  VARCHAR(40)     NOT NULL CONSTRAINT DF_tce_job          DEFAULT ('TASAS_CAMBIO'),
            disparador           VARCHAR(12)     NOT NULL,
            fecha_objetivo       DATE            NOT NULL,      -- fecha de vigencia que se buscaba
            intento              TINYINT         NOT NULL CONSTRAINT DF_tce_intento      DEFAULT (1),
            id_ejecucion_origen  BIGINT          NULL,          -- primera ejecucion de la cadena de reintentos
            estado               VARCHAR(20)     NOT NULL CONSTRAINT DF_tce_estado       DEFAULT ('EN_CURSO'),
            fuente               VARCHAR(20)     NULL,
            endpoint             NVARCHAR(400)   NULL,          -- URL consultada, SIN la clave del API
            http_status          SMALLINT        NULL,
            leidos               INT             NOT NULL CONSTRAINT DF_tce_leidos       DEFAULT (0),
            insertados           INT             NOT NULL CONSTRAINT DF_tce_insertados   DEFAULT (0),
            reemplazados         INT             NOT NULL CONSTRAINT DF_tce_reemplazados DEFAULT (0),
            duplicados           INT             NOT NULL CONSTRAINT DF_tce_duplicados   DEFAULT (0),
            invalidos            INT             NOT NULL CONSTRAINT DF_tce_invalidos    DEFAULT (0),
            hash_contenido       CHAR(64)        NULL,          -- SHA-256 (hex) de la respuesta de la fuente
            mensaje              NVARCHAR(1000)  NULL,
            detalle_error        NVARCHAR(MAX)   NULL,
            proximo_intento_utc  DATETIME2(3)    NULL,
            notificado           BIT             NOT NULL CONSTRAINT DF_tce_notificado   DEFAULT (0),
            servidor             NVARCHAR(100)   NOT NULL,
            version_app          VARCHAR(30)     NULL,
            ejecutado_por        NVARCHAR(100)   NOT NULL,      -- 'job' o el usuario que la disparo a mano
            inicio_utc           DATETIME2(3)    NOT NULL CONSTRAINT DF_tce_inicio_utc   DEFAULT (SYSUTCDATETIME()),
            fin_utc              DATETIME2(3)    NULL,
            CONSTRAINT PK_tasas_cambio_ejecuciones PRIMARY KEY CLUSTERED (id_ejecucion),
            CONSTRAINT FK_tce_ejecucion_origen FOREIGN KEY (id_ejecucion_origen)
                REFERENCES dbo.tasas_cambio_ejecuciones (id_ejecucion),
            CONSTRAINT CK_tce_disparador CHECK (disparador IN ('PROGRAMADO', 'EXTERNO', 'MANUAL', 'REINTENTO')),
            CONSTRAINT CK_tce_estado CHECK (estado IN ('EN_CURSO', 'EXITOSA', 'PARCIAL', 'REINTENTADA', 'FALLIDA',
                                                       'OMITIDA_DUPLICADA', 'OMITIDA_INVALIDA', 'OMITIDA_SIN_DATOS'))
        );
        SET @cambios += 1;
        PRINT N'  + dbo.tasas_cambio_ejecuciones';
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tce_job_fecha' AND object_id = OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'))
    BEGIN
        CREATE INDEX IX_tce_job_fecha ON dbo.tasas_cambio_ejecuciones (job, fecha_objetivo, inicio_utc);
        SET @cambios += 1;
        PRINT N'  + indice IX_tce_job_fecha';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 4/5: [AMARILLO / MODIFICA] dbo.tasas_cambio con la estructura v2
    --   id_empresa NULL = tasa oficial (comun a todas las empresas); con valor = tasa propia de esa empresa.
    --   Las columnas de moneda llevan la intercalacion de dbo.monedas.codigo_iso (la FK la exige).
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tasas_cambio (
            id_tasa_cambio        INT            IDENTITY(1,1) NOT NULL,
            id_empresa            INT            NULL,
            moneda_origen         CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            moneda_destino        CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            tipo_tasa             VARCHAR(10)    NOT NULL,
            tasa                  DECIMAL(18,8)  NOT NULL,
            fecha_vigencia        DATE           NOT NULL,
            fecha_hora_obtencion  DATETIME2(3)   NOT NULL,      -- momento (UTC) en que se leyo de la fuente
            fuente                VARCHAR(20)    NOT NULL,      -- BCH_API, BCH_XLSX, BCE, DERIVADA, MANUAL, LEGADO_WINFORMS
            referencia_fuente     NVARCHAR(400)  NULL,          -- indicador, URL o formula; nunca claves
            es_derivada           BIT            NOT NULL CONSTRAINT DF_tasas_cambio_es_derivada    DEFAULT (0),
            estado                VARCHAR(12)    NOT NULL CONSTRAINT DF_tasas_cambio_estado         DEFAULT ('VIGENTE'),
            version               SMALLINT       NOT NULL CONSTRAINT DF_tasas_cambio_version        DEFAULT (1),
            id_tasa_anterior      INT            NULL,
            id_ejecucion          BIGINT         NULL,          -- nulo si fue captura manual
            eliminado             BIT            NOT NULL CONSTRAINT DF_tasas_cambio_eliminado      DEFAULT (0),
            fecha_eliminado       DATETIME2(3)   NULL,
            creado_por            NVARCHAR(100)  NOT NULL,
            fecha_creacion        DATETIME2(3)   NOT NULL CONSTRAINT DF_tasas_cambio_fecha_creacion DEFAULT (SYSUTCDATETIME()),
            modificado_por        NVARCHAR(100)  NULL,
            fecha_modificacion    DATETIME2(3)   NULL,
            token_concurrencia    ROWVERSION     NOT NULL,
            CONSTRAINT PK_tasas_cambio PRIMARY KEY CLUSTERED (id_tasa_cambio),
            CONSTRAINT FK_tasas_cambio_empresa        FOREIGN KEY (id_empresa)       REFERENCES dbo.empresas (id_empresa),
            CONSTRAINT FK_tasas_cambio_moneda_origen  FOREIGN KEY (moneda_origen)    REFERENCES dbo.monedas (codigo_iso),
            CONSTRAINT FK_tasas_cambio_moneda_destino FOREIGN KEY (moneda_destino)   REFERENCES dbo.monedas (codigo_iso),
            CONSTRAINT FK_tasas_cambio_tasa_anterior  FOREIGN KEY (id_tasa_anterior) REFERENCES dbo.tasas_cambio (id_tasa_cambio),
            CONSTRAINT FK_tasas_cambio_ejecucion      FOREIGN KEY (id_ejecucion)     REFERENCES dbo.tasas_cambio_ejecuciones (id_ejecucion),
            CONSTRAINT CK_tasas_cambio_tasa      CHECK (tasa > 0),
            CONSTRAINT CK_tasas_cambio_monedas   CHECK (moneda_origen <> moneda_destino),
            CONSTRAINT CK_tasas_cambio_tipo_tasa CHECK (tipo_tasa IN ('COMPRA', 'VENTA', 'REFERENCIA')),
            CONSTRAINT CK_tasas_cambio_estado    CHECK (estado IN ('VIGENTE', 'REEMPLAZADA', 'EN_REVISION', 'RECHAZADA'))
        );
        SET @cambios += 1;
        PRINT N'  + dbo.tasas_cambio (estructura v2)';
    END;

    -- Una sola tasa VIGENTE por par, tipo, fecha y empresa. En un indice unico SQL Server trata los NULL
    -- como iguales: una sola tasa oficial (id_empresa NULL) por clave.
    -- Los dos indices van por sp_executesql: cuando se compila este lote, dbo.tasas_cambio todavia tiene
    -- la estructura vieja, sin tipo_tasa, fecha_vigencia ni estado; asi se compilan al ejecutarse.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tasas_cambio_vigente' AND object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_tasas_cambio_vigente
            ON dbo.tasas_cambio (moneda_origen, moneda_destino, tipo_tasa, fecha_vigencia, id_empresa)
            WHERE estado = ''VIGENTE'' AND eliminado = 0;';
        SET @cambios += 1;
        PRINT N'  + indice UX_tasas_cambio_vigente (unico filtrado)';
    END;

    -- Consulta "tasa vigente en una fecha": la ultima fecha_vigencia <= fecha pedida.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tasas_cambio_consulta' AND object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE INDEX IX_tasas_cambio_consulta
            ON dbo.tasas_cambio (moneda_origen, moneda_destino, tipo_tasa, fecha_vigencia DESC)
            INCLUDE (tasa, id_empresa)
            WHERE estado = ''VIGENTE'' AND eliminado = 0;';
        SET @cambios += 1;
        PRINT N'  + indice IX_tasas_cambio_consulta (filtrado)';
    END;

    -- --------------------------------------------------------
    -- CAMBIO 5/5: [VERDE / AGREGA] dbo.tasas_cambio_ejecuciones_detalle
    --   Sin FK a monedas a proposito: registra tambien lo que la fuente devolvio y fue rechazado.
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tasas_cambio_ejecuciones_detalle (
            id_detalle       BIGINT         IDENTITY(1,1) NOT NULL,
            id_ejecucion     BIGINT         NOT NULL,
            moneda_origen    CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            moneda_destino   CHAR(3)        COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            tipo_tasa        VARCHAR(10)    NOT NULL,
            fecha_vigencia   DATE           NULL,
            valor_leido      DECIMAL(18,8)  NULL,
            resultado        VARCHAR(20)    NOT NULL,
            motivo           NVARCHAR(500)  NULL,
            id_tasa_cambio   INT            NULL,
            CONSTRAINT PK_tasas_cambio_ejecuciones_detalle PRIMARY KEY CLUSTERED (id_detalle),
            CONSTRAINT FK_tced_ejecucion   FOREIGN KEY (id_ejecucion)   REFERENCES dbo.tasas_cambio_ejecuciones (id_ejecucion),
            CONSTRAINT FK_tced_tasa_cambio FOREIGN KEY (id_tasa_cambio) REFERENCES dbo.tasas_cambio (id_tasa_cambio),
            CONSTRAINT CK_tced_resultado CHECK (resultado IN ('INSERTADA', 'REEMPLAZO', 'DUPLICADA', 'INVALIDA',
                                                              'EN_REVISION', 'SIN_DATOS', 'ERROR'))
        );
        SET @cambios += 1;
        PRINT N'  + dbo.tasas_cambio_ejecuciones_detalle';
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tced_ejecucion' AND object_id = OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'))
    BEGIN
        CREATE INDEX IX_tced_ejecucion ON dbo.tasas_cambio_ejecuciones_detalle (id_ejecucion);
        SET @cambios += 1;
        PRINT N'  + indice IX_tced_ejecucion';
    END;

    -- --------------------------------------------------------
    -- POSTCHECK (dentro de la transaccion: si algo no cuadra, se revierte todo)
    -- --------------------------------------------------------
    IF OBJECT_ID(N'dbo.tipos_cambio', N'U') IS NOT NULL
        INSERT INTO @problemas (problema) VALUES (N'dbo.tipos_cambio sigue existiendo');

    -- a) Columnas: nombre, tipo, longitud, nulabilidad e identidad, contra la lista esperada
    WITH reales AS (
        SELECT o.name AS tabla, c.name AS columna,
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
        JOIN sys.objects o ON o.object_id = c.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE o.object_id IN (OBJECT_ID(N'dbo.tasas_cambio'), OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'),
                              OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'))
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

    -- b) Las columnas de moneda deben tener la intercalacion de dbo.monedas.codigo_iso
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Columna ', OBJECT_NAME(c.object_id), N'.', c.name, N' tiene intercalacion ', c.collation_name)
    FROM sys.columns c
    WHERE c.object_id IN (OBJECT_ID(N'dbo.tasas_cambio'), OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'))
      AND c.name IN (N'moneda_origen', N'moneda_destino')
      AND ISNULL(c.collation_name, N'') <> N'SQL_Latin1_General_CP1_CI_AS';

    -- c) Claves primarias, foraneas (con su tabla destino), CHECK y valores por defecto, por nombre
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta o no coincide la restriccion ', x.nombre, N' en ', x.tabla)
    FROM @restricciones x
    WHERE NOT EXISTS (SELECT 1 FROM sys.objects o
                      WHERE o.name = x.nombre AND o.type COLLATE DATABASE_DEFAULT = x.tipo
                        AND o.parent_object_id = OBJECT_ID(N'dbo.' + x.tabla))
       OR (x.tipo = 'F' AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk
                                        WHERE fk.name = x.nombre
                                          AND fk.parent_object_id = OBJECT_ID(N'dbo.' + x.tabla)
                                          AND fk.referenced_object_id = OBJECT_ID(N'dbo.' + x.tabla_destino)));

    -- d) Indices: existencia, unicidad y filtro
    INSERT INTO @problemas (problema)
    SELECT CONCAT(N'Falta el indice ', x.indice, N' en ', x.tabla, N' o no coincide su unicidad o su filtro')
    FROM @indices x
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i
                      WHERE i.object_id = OBJECT_ID(N'dbo.' + x.tabla) AND i.name = x.indice
                        AND i.is_unique = x.unico AND i.has_filter = x.filtrado
                        AND (x.filtrado = 0 OR (i.filter_definition LIKE N'%estado%VIGENTE%'
                                                AND i.filter_definition LIKE N'%eliminado%0%')));

    -- e) Indices: columnas clave en orden, sentido y columnas incluidas
    WITH reales AS (
        SELECT i.name AS indice, c.name AS columna,
               CASE WHEN ic.is_included_column = 1 THEN 0 ELSE ic.key_ordinal END AS posicion,
               ic.is_descending_key AS descendente
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.name IN (SELECT indice FROM @indices)
          AND i.object_id IN (OBJECT_ID(N'dbo.tasas_cambio'), OBJECT_ID(N'dbo.tasas_cambio_ejecuciones'),
                              OBJECT_ID(N'dbo.tasas_cambio_ejecuciones_detalle'))
    )
    INSERT INTO @problemas (problema)
    SELECT DISTINCT CONCAT(N'Las columnas del indice ', ISNULL(e.indice, r.indice), N' no coinciden con las esperadas')
    FROM @indice_columnas e
    FULL OUTER JOIN reales r ON r.indice = e.indice AND r.columna = e.columna
    WHERE r.columna IS NULL OR e.columna IS NULL
       OR r.posicion <> e.posicion OR r.descendente <> e.descendente;

    IF EXISTS (SELECT 1 FROM @problemas)
    BEGIN
        SELECT problema FROM @problemas;
        THROW 50020, N'POSTCHECK 020 fallo (ver la lista de problemas). Se revierte todo.', 1;
    END;

    COMMIT TRANSACTION;

    IF @cambios = 0
        PRINT N'No habia nada que cambiar: la estructura v2 ya estaba aplicada. POSTCHECK correcto.';
    ELSE
        PRINT CONCAT(N'Listo: ', @cambios, N' cambio(s) aplicados. POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- Resultado (solo lectura): tablas del modulo, columnas y filas
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_020') IS NOT NULL DROP TABLE #precheck_020;

SELECT t.name AS tabla,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS columnas,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) AS filas
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name IN (N'tasas_cambio', N'tasas_cambio_ejecuciones', N'tasas_cambio_ejecuciones_detalle', N'tipos_cambio')
ORDER BY t.name;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Esta en el archivo aparte 020_tasas_cambio_v2_reversa.sql. Solo corre si las tres tablas nuevas
--   estan vacias: las elimina y recrea dbo.tasas_cambio con la estructura de KPI_02 y las claves
--   foraneas del 017. Si el 020 elimino dbo.tipos_cambio, la reversa puede recrearla (ver su cabecera).
-- ------------------------------------------------------------
