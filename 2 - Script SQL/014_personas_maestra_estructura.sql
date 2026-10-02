-- ============================================================
-- Script   : 014_personas_maestra_estructura.sql
-- Proposito: Convierte dbo.personas en la tabla maestra de personas (una fila por persona real,
--            compartida por varias empresas) y crea la estructura de vinculos por empresa y roles:
--            persona_documentos, persona_empresa, empleados, bitacora_cambios (inmutable) y los
--            catalogos de departamentos, municipios, tipos de documento y tipos de licencia.
--            Vincula dbo.clientes con las personas (clientes personas naturales).
--            SOLO ESTRUCTURA: no migra datos (eso es 016) ni carga catalogos (eso es 015).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-30
-- BD       : eBD_SPD
-- Requiere : dbo.empresas, dbo.personas, dbo.clientes, dbo.catalogo_paises; SQL Server 2017 o superior
-- Rollback : ver la seccion ROLLBACK al final (solo valido mientras las estructuras nuevas no tengan datos)
-- ============================================================
-- Plan      : artifact "Mejora de datos de personas" (version 4). Decisiones D1 a D10.
-- Convenciones: snake_case en espanol y plural; auditoria y token_concurrencia segun
--            1 - Documetacion/ESTANDARES_ERP.md. Los catalogos globales llevan el prefijo catalogo_
--            (igual que catalogo_paises) y no llevan id_empresa.
--
-- Diseno (resumen):
--   personas             Maestra: nombre, datos personales, contacto y licencia. Sin dependencia de empresa.
--   persona_documentos   Documentos de identidad. Indice unico global = una persona una sola vez.
--   persona_empresa      Vinculo persona-empresa con el rol (empleado, cliente, proveedor, usuario...).
--   empleados            Rol de empleado: codigo_interno (D6), cargo (D7), tarifa. Apunta al vinculo.
--   clientes             Rol de cliente (ya existe): se enlaza al vinculo cuando es persona natural.
--   bitacora_cambios     Historial por campo. Solo se agrega: un disparador impide UPDATE y DELETE.
--
-- Compatibilidad: es la fase EXPANDIR. No se elimina ninguna columna ni dato de dbo.personas, y la
-- aplicacion actual sigue funcionando sin cambios. Las columnas viejas (id_empresa, documento,
-- tipo_documento, cargo, tarifa_diaria, moneda_tarifa, fecha_ingreso, fecha_baja) se retiran en el
-- script 017, cuando el codigo y los consumidores ya no las lean.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los
--     demas bloques exigen; si el PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: cada bloque comprueba si su objeto ya existe.
--   * Los bloques que modifican tablas existentes corren dentro de una transaccion con TRY/CATCH:
--     si algo falla, se revierte ese bloque completo.
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
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF DB_NAME() <> N'eBD_SPD'
    THROW 50014, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 14
    THROW 50014, N'Se requiere SQL Server 2017 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.empresas', N'U') IS NULL
    THROW 50014, N'Falta dbo.empresas. Abortar.', 1;

IF OBJECT_ID(N'dbo.personas', N'U') IS NULL
    THROW 50014, N'Falta dbo.personas. Ejecute KPI_01_Catalogos.sql antes. Abortar.', 1;

IF OBJECT_ID(N'dbo.clientes', N'U') IS NULL
    THROW 50014, N'Falta dbo.clientes. Ejecute F0_Catalogos_Transversales.sql antes. Abortar.', 1;

IF OBJECT_ID(N'dbo.catalogo_paises', N'U') IS NULL
    THROW 50014, N'Falta dbo.catalogo_paises. Ejecute Estructura BD.sql antes. Abortar.', 1;

IF COL_LENGTH(N'dbo.personas', N'id_persona') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'id_empresa') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'documento') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'tipo_documento') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'cargo') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'email') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'telefono') IS NULL
    THROW 50014, N'dbo.personas no tiene la estructura esperada. Abortar.', 1;

IF OBJECT_ID(N'tempdb..#precheck_014') IS NOT NULL DROP TABLE #precheck_014;
CREATE TABLE #precheck_014 (ok BIT NOT NULL);
INSERT INTO #precheck_014 (ok) VALUES (1);

-- Estado previo (informativo)
SELECT
    (SELECT COUNT(*) FROM dbo.personas)                                                   AS personas_filas,
    (SELECT COUNT(*) FROM dbo.clientes)                                                   AS clientes_filas,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints
                      WHERE name = N'CK_personas_cargo'
                        AND parent_object_id = OBJECT_ID(N'dbo.personas'))
         THEN 'existe' ELSE 'no existe' END                                               AS ck_personas_cargo,
    (SELECT COUNT(*) FROM sys.tables
      WHERE name IN (N'persona_documentos', N'persona_empresa', N'empleados', N'bitacora_cambios',
                     N'catalogo_departamentos', N'catalogo_municipios',
                     N'catalogo_tipos_documento', N'catalogo_tipos_licencia'))            AS tablas_nuevas_ya_existentes;
GO

-- ------------------------------------------------------------
-- CAMBIO 1/11: [VERDE / AGREGA] catalogos globales (vacios; 015 los carga)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.catalogo_departamentos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalogo_departamentos (
        id_departamento  INT IDENTITY(1,1) NOT NULL,
        pais_iso         CHAR(2) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        codigo           CHAR(2)       NOT NULL,
        nombre           NVARCHAR(100) NOT NULL,
        activo           BIT           NOT NULL CONSTRAINT DF_cat_depto_activo DEFAULT (1),
        fecha_creacion   DATETIME2(0)  NOT NULL CONSTRAINT DF_cat_depto_fc DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_catalogo_departamentos PRIMARY KEY CLUSTERED (id_departamento),
        CONSTRAINT FK_catalogo_departamentos_pais FOREIGN KEY (pais_iso) REFERENCES dbo.catalogo_paises (pais_iso)
    );
    CREATE UNIQUE INDEX UX_catalogo_departamentos_codigo
        ON dbo.catalogo_departamentos (pais_iso, codigo);
    PRINT N'  + dbo.catalogo_departamentos';
END;

IF OBJECT_ID(N'dbo.catalogo_municipios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalogo_municipios (
        id_municipio     INT IDENTITY(1,1) NOT NULL,
        id_departamento  INT           NOT NULL,
        codigo           CHAR(4)       NOT NULL,
        nombre           NVARCHAR(100) NOT NULL,
        activo           BIT           NOT NULL CONSTRAINT DF_cat_muni_activo DEFAULT (1),
        fecha_creacion   DATETIME2(0)  NOT NULL CONSTRAINT DF_cat_muni_fc DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_catalogo_municipios PRIMARY KEY CLUSTERED (id_municipio),
        CONSTRAINT FK_catalogo_municipios_departamento FOREIGN KEY (id_departamento)
            REFERENCES dbo.catalogo_departamentos (id_departamento)
    );
    CREATE UNIQUE INDEX UX_catalogo_municipios_codigo
        ON dbo.catalogo_municipios (id_departamento, codigo);
    PRINT N'  + dbo.catalogo_municipios';
END;

IF OBJECT_ID(N'dbo.catalogo_tipos_documento', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalogo_tipos_documento (
        codigo           VARCHAR(20)   NOT NULL,
        nombre           NVARCHAR(100) NOT NULL,
        pais_iso         CHAR(2) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        patron           NVARCHAR(200) NULL,     -- expresion regular .NET que usa el servicio de validacion
        largo_min        TINYINT       NULL,
        largo_max        TINYINT       NULL,
        es_identidad     BIT           NOT NULL CONSTRAINT DF_cat_tdoc_identidad DEFAULT (1),
        activo           BIT           NOT NULL CONSTRAINT DF_cat_tdoc_activo DEFAULT (1),
        fecha_creacion   DATETIME2(0)  NOT NULL CONSTRAINT DF_cat_tdoc_fc DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_catalogo_tipos_documento PRIMARY KEY CLUSTERED (codigo),
        CONSTRAINT FK_catalogo_tipos_documento_pais FOREIGN KEY (pais_iso) REFERENCES dbo.catalogo_paises (pais_iso),
        CONSTRAINT CK_catalogo_tipos_documento_largos
            CHECK (largo_min IS NULL OR largo_max IS NULL OR largo_min <= largo_max)
    );
    PRINT N'  + dbo.catalogo_tipos_documento';
END;

IF OBJECT_ID(N'dbo.catalogo_tipos_licencia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalogo_tipos_licencia (
        codigo           VARCHAR(10)   NOT NULL,
        nombre           NVARCHAR(100) NOT NULL,
        activo           BIT           NOT NULL CONSTRAINT DF_cat_tlic_activo DEFAULT (1),
        fecha_creacion   DATETIME2(0)  NOT NULL CONSTRAINT DF_cat_tlic_fc DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_catalogo_tipos_licencia PRIMARY KEY CLUSTERED (codigo)
    );
    PRINT N'  + dbo.catalogo_tipos_licencia';
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 2/11: [AMARILLO / MODIFICA] dbo.personas: relajar columnas que pasan al vinculo
--   * Se quita CK_personas_cargo (decision D7: el cargo se valida contra dbo.cargos de la empresa).
--   * id_empresa, documento, tipo_documento y cargo pasan a admitir NULL: una persona maestra no
--     pertenece a una empresa y puede no tener documento de identidad todavia.
--   * El indice unico (id_empresa, documento) se recrea con filtro "documento IS NOT NULL" para que
--     admita varias filas sin documento. No se pierde ningun dato.
--   Atomico: todo el bloque corre en una transaccion.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Quitar los objetos que dependen de las columnas a modificar
    IF EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_personas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_empresa;

    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_personas_empresa_documento' AND object_id = OBJECT_ID(N'dbo.personas'))
        DROP INDEX UX_personas_empresa_documento ON dbo.personas;

    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_personas_empresa_cargo' AND object_id = OBJECT_ID(N'dbo.personas'))
        DROP INDEX IX_personas_empresa_cargo ON dbo.personas;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_personas_cargo' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas DROP CONSTRAINT CK_personas_cargo;

    IF EXISTS (SELECT 1 FROM sys.default_constraints
               WHERE name = N'DF_personas_tipo_documento' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas DROP CONSTRAINT DF_personas_tipo_documento;

    -- 2) Permitir NULL (los tipos y largos no cambian)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas')
               AND name = N'id_empresa' AND is_nullable = 0)
        ALTER TABLE dbo.personas ALTER COLUMN id_empresa INT NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas')
               AND name = N'documento' AND is_nullable = 0)
        ALTER TABLE dbo.personas ALTER COLUMN documento VARCHAR(30) NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas')
               AND name = N'tipo_documento' AND is_nullable = 0)
        ALTER TABLE dbo.personas ALTER COLUMN tipo_documento VARCHAR(20) NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas')
               AND name = N'cargo' AND is_nullable = 0)
        ALTER TABLE dbo.personas ALTER COLUMN cargo VARCHAR(30) NULL;

    -- 3) Recrear la clave foranea y los indices
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE name = N'FK_personas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_empresa
            FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UX_personas_empresa_documento' AND object_id = OBJECT_ID(N'dbo.personas'))
        CREATE UNIQUE INDEX UX_personas_empresa_documento
            ON dbo.personas (id_empresa, documento)
            WHERE eliminado = 0 AND documento IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_personas_empresa_cargo' AND object_id = OBJECT_ID(N'dbo.personas'))
        CREATE INDEX IX_personas_empresa_cargo
            ON dbo.personas (id_empresa, cargo)
            WHERE eliminado = 0 AND activo = 1;

    COMMIT TRANSACTION;
    PRINT N'  ~ dbo.personas: columnas legacy relajadas, CK_personas_cargo retirado';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 3/11: [VERDE / AGREGA] dbo.personas: columnas de la persona maestra
--   Se agregan una por una y solo las que faltan. Todas admiten NULL salvo estado_identidad,
--   que parte en 'pendiente' para las filas existentes (el script 016 marca 'verificada' a las
--   que tienen documento de identidad).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @cols TABLE (n INT IDENTITY(1,1) PRIMARY KEY, nombre SYSNAME NOT NULL, definicion NVARCHAR(300) NOT NULL);
INSERT INTO @cols (nombre, definicion) VALUES
    (N'primer_nombre',                  N'NVARCHAR(50) NULL'),
    (N'segundo_nombre',                 N'NVARCHAR(50) NULL'),
    (N'primer_apellido',                N'NVARCHAR(50) NULL'),
    (N'segundo_apellido',               N'NVARCHAR(50) NULL'),
    (N'nombre_normalizado',             N'NVARCHAR(200) NULL'),
    (N'sexo',                           N'CHAR(1) NULL'),
    (N'estado_civil',                   N'VARCHAR(15) NULL'),
    (N'fecha_nacimiento',               N'DATE NULL'),
    (N'pais_nacionalidad',              N'CHAR(2) COLLATE SQL_Latin1_General_CP1_CI_AS NULL'),
    (N'tipo_sangre',                    N'VARCHAR(3) NULL'),
    (N'id_municipio_nacimiento',        N'INT NULL'),
    (N'id_municipio_residencia',        N'INT NULL'),
    (N'direccion_residencia',           N'NVARCHAR(300) NULL'),
    (N'telefono_secundario',            N'VARCHAR(30) NULL'),
    (N'contacto_emergencia_nombre',     N'NVARCHAR(150) NULL'),
    (N'contacto_emergencia_telefono',   N'VARCHAR(30) NULL'),
    (N'contacto_emergencia_parentesco', N'VARCHAR(30) NULL'),
    (N'licencia_tipo',                  N'VARCHAR(10) NULL'),
    (N'licencia_numero',                N'VARCHAR(30) NULL'),
    (N'licencia_vencimiento',           N'DATE NULL'),
    (N'estado_identidad',               N'VARCHAR(15) NOT NULL CONSTRAINT DF_personas_estado_identidad DEFAULT (''pendiente'')'),
    (N'id_persona_principal',           N'INT NULL');

DECLARE @i INT = 1, @max INT = (SELECT MAX(n) FROM @cols), @nom SYSNAME, @def NVARCHAR(300), @sql NVARCHAR(600);

BEGIN TRY
    BEGIN TRANSACTION;

    WHILE @i <= @max
    BEGIN
        SELECT @nom = nombre, @def = definicion FROM @cols WHERE n = @i;

        IF COL_LENGTH(N'dbo.personas', @nom) IS NULL
        BEGIN
            SET @sql = N'ALTER TABLE dbo.personas ADD ' + QUOTENAME(@nom) + N' ' + @def + N';';
            EXEC sys.sp_executesql @sql;
            PRINT N'  + dbo.personas.' + @nom;
        END;

        SET @i += 1;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 4/11: [VERDE / AGREGA] dbo.personas: restricciones, claves foraneas e indices nuevos
--   Las filas existentes tienen NULL en todas las columnas nuevas, asi que ninguna restriccion
--   puede rechazarlas (el email actual esta vacio en las 88 filas; se verifico en F0).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_sexo' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_sexo
            CHECK (sexo IS NULL OR sexo IN ('M', 'F'));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_estado_civil' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_estado_civil
            CHECK (estado_civil IS NULL OR estado_civil IN ('soltero', 'casado', 'union_libre', 'divorciado', 'viudo'));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_tipo_sangre' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_tipo_sangre
            CHECK (tipo_sangre IS NULL OR tipo_sangre IN ('A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_estado_identidad' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_estado_identidad
            CHECK (estado_identidad IN ('verificada', 'pendiente'));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_fecha_nacimiento' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_fecha_nacimiento
            CHECK (fecha_nacimiento IS NULL OR fecha_nacimiento >= '19000101');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_emergencia' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_emergencia
            CHECK (   (contacto_emergencia_nombre IS NULL     AND contacto_emergencia_telefono IS NULL     AND contacto_emergencia_parentesco IS NULL)
                   OR (contacto_emergencia_nombre IS NOT NULL AND contacto_emergencia_telefono IS NOT NULL AND contacto_emergencia_parentesco IS NOT NULL));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_email' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_email
            CHECK (email IS NULL OR email LIKE '%_@_%._%');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT CK_personas_principal
            CHECK (id_persona_principal IS NULL OR id_persona_principal <> id_persona);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_pais_nacionalidad' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_pais_nacionalidad
            FOREIGN KEY (pais_nacionalidad) REFERENCES dbo.catalogo_paises (pais_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_municipio_nacimiento' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_municipio_nacimiento
            FOREIGN KEY (id_municipio_nacimiento) REFERENCES dbo.catalogo_municipios (id_municipio);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_municipio_residencia' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_municipio_residencia
            FOREIGN KEY (id_municipio_residencia) REFERENCES dbo.catalogo_municipios (id_municipio);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_licencia_tipo' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_licencia_tipo
            FOREIGN KEY (licencia_tipo) REFERENCES dbo.catalogo_tipos_licencia (codigo);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_principal
            FOREIGN KEY (id_persona_principal) REFERENCES dbo.personas (id_persona);

    -- Deteccion de parecidos al dar de alta (nombre normalizado + fecha de nacimiento)
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_nombre_normalizado' AND object_id = OBJECT_ID(N'dbo.personas'))
        CREATE INDEX IX_personas_nombre_normalizado
            ON dbo.personas (nombre_normalizado, fecha_nacimiento)
            WHERE eliminado = 0 AND nombre_normalizado IS NOT NULL;

    -- Fusion de duplicados: localizar las fichas sobrantes de una persona principal
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_principal' AND object_id = OBJECT_ID(N'dbo.personas'))
        CREATE INDEX IX_personas_principal
            ON dbo.personas (id_persona_principal)
            WHERE id_persona_principal IS NOT NULL;

    COMMIT TRANSACTION;
    PRINT N'  ~ dbo.personas: restricciones, claves foraneas e indices nuevos';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 5/11: [VERDE / AGREGA] dbo.persona_documentos
--   Documentos de identidad de la persona. El indice unico UX_persona_documentos_identidad
--   garantiza que un documento (tipo + pais + numero normalizado) pertenece a una sola persona,
--   sin importar la empresa. El numero de empleado NO va aqui (vive en dbo.empleados).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.persona_documentos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.persona_documentos (
        id_persona_documento INT IDENTITY(1,1) NOT NULL,
        id_persona           INT           NOT NULL,
        tipo_documento       VARCHAR(20)   NOT NULL,
        pais_emisor          CHAR(2) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
                             CONSTRAINT DF_pdoc_pais DEFAULT ('HN'),
        numero               NVARCHAR(30)  NOT NULL,
        numero_normalizado   AS (UPPER(REPLACE(REPLACE(REPLACE(numero, N'-', N''), N' ', N''), N'.', N''))) PERSISTED,
        es_principal         BIT           NOT NULL CONSTRAINT DF_pdoc_principal DEFAULT (0),
        fecha_vencimiento    DATE          NULL,
        activo               BIT           NOT NULL CONSTRAINT DF_pdoc_activo DEFAULT (1),
        eliminado            BIT           NOT NULL CONSTRAINT DF_pdoc_elim DEFAULT (0),
        fecha_eliminado      DATETIME2(3)  NULL,
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2(3)  NOT NULL CONSTRAINT DF_pdoc_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por       NVARCHAR(100) NULL,
        fecha_modificacion   DATETIME2(3)  NULL,
        token_concurrencia   ROWVERSION    NOT NULL,
        CONSTRAINT PK_persona_documentos PRIMARY KEY CLUSTERED (id_persona_documento),
        CONSTRAINT FK_persona_documentos_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona),
        CONSTRAINT FK_persona_documentos_tipo    FOREIGN KEY (tipo_documento) REFERENCES dbo.catalogo_tipos_documento (codigo),
        CONSTRAINT FK_persona_documentos_pais    FOREIGN KEY (pais_emisor) REFERENCES dbo.catalogo_paises (pais_iso),
        CONSTRAINT CK_persona_documentos_numero  CHECK (LEN(LTRIM(RTRIM(numero))) > 0)
    );

    -- Unicidad global del documento de identidad
    CREATE UNIQUE INDEX UX_persona_documentos_identidad
        ON dbo.persona_documentos (tipo_documento, pais_emisor, numero_normalizado)
        WHERE eliminado = 0;

    -- Un solo documento principal por persona
    CREATE UNIQUE INDEX UX_persona_documentos_principal
        ON dbo.persona_documentos (id_persona)
        WHERE es_principal = 1 AND eliminado = 0;

    CREATE INDEX IX_persona_documentos_persona
        ON dbo.persona_documentos (id_persona);

    PRINT N'  + dbo.persona_documentos';
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 6/11: [VERDE / AGREGA] dbo.persona_empresa (registro de vinculos y roles)
--   Una fila por cada rol que una persona tiene en una empresa. Para un empleado, fecha_inicio y
--   fecha_fin son el ingreso y la baja; para un cliente, el alta y la baja comercial.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.persona_empresa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.persona_empresa (
        id_persona_empresa INT IDENTITY(1,1) NOT NULL,
        id_empresa         INT           NOT NULL,
        id_persona         INT           NOT NULL,
        tipo_vinculo       VARCHAR(20)   NOT NULL,
        fecha_inicio       DATE          NULL,
        fecha_fin          DATE          NULL,
        motivo_fin         NVARCHAR(200) NULL,
        activo             BIT           NOT NULL CONSTRAINT DF_pemp_activo DEFAULT (1),
        eliminado          BIT           NOT NULL CONSTRAINT DF_pemp_elim DEFAULT (0),
        fecha_eliminado    DATETIME2(3)  NULL,
        creado_por         NVARCHAR(100) NOT NULL,
        fecha_creacion     DATETIME2(3)  NOT NULL CONSTRAINT DF_pemp_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por     NVARCHAR(100) NULL,
        fecha_modificacion DATETIME2(3)  NULL,
        token_concurrencia ROWVERSION    NOT NULL,
        CONSTRAINT PK_persona_empresa PRIMARY KEY CLUSTERED (id_persona_empresa),
        CONSTRAINT FK_persona_empresa_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
        CONSTRAINT FK_persona_empresa_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona),
        CONSTRAINT CK_persona_empresa_tipo
            CHECK (tipo_vinculo IN ('empleado', 'cliente', 'proveedor', 'usuario', 'contacto', 'otro')),
        CONSTRAINT CK_persona_empresa_fechas
            CHECK (fecha_inicio IS NULL OR fecha_fin IS NULL OR fecha_fin >= fecha_inicio)
    );

    -- Destino de las claves foraneas compuestas de las tablas de rol (empleados, clientes)
    CREATE UNIQUE INDEX UX_persona_empresa_rol
        ON dbo.persona_empresa (id_persona_empresa, id_empresa, tipo_vinculo);

    -- Un solo vinculo vigente de cada rol por persona y empresa (los pasados se conservan)
    CREATE UNIQUE INDEX UX_persona_empresa_vigente
        ON dbo.persona_empresa (id_empresa, id_persona, tipo_vinculo)
        WHERE eliminado = 0 AND fecha_fin IS NULL;

    -- Listar los clientes o los empleados vigentes de una empresa
    CREATE INDEX IX_persona_empresa_empresa_tipo
        ON dbo.persona_empresa (id_empresa, tipo_vinculo)
        WHERE eliminado = 0 AND fecha_fin IS NULL;

    CREATE INDEX IX_persona_empresa_persona
        ON dbo.persona_empresa (id_persona);

    PRINT N'  + dbo.persona_empresa';
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 7/11: [VERDE / AGREGA] dbo.empleados (rol de empleado)
--   La clave foranea compuesta (id_persona_empresa, id_empresa, tipo_vinculo) obliga a que cada
--   fila cuelgue de un vinculo de la misma empresa y de tipo 'empleado'.
--   codigo_interno = decision D6. cargo = decision D7 (sin CHECK: lo valida el servicio contra dbo.cargos).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.empleados', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.empleados (
        id_empleado        INT IDENTITY(1,1) NOT NULL,
        id_persona_empresa INT           NOT NULL,
        id_empresa         INT           NOT NULL,
        tipo_vinculo       VARCHAR(20)   NOT NULL CONSTRAINT DF_empleados_tipo DEFAULT ('empleado'),
        codigo_interno     VARCHAR(30)   NULL,
        cargo              VARCHAR(30)   NULL,
        tarifa_diaria      DECIMAL(18,2) NULL,
        moneda_tarifa      CHAR(3)       NULL,
        activo             BIT           NOT NULL CONSTRAINT DF_empleados_activo DEFAULT (1),
        eliminado          BIT           NOT NULL CONSTRAINT DF_empleados_elim DEFAULT (0),
        fecha_eliminado    DATETIME2(3)  NULL,
        creado_por         NVARCHAR(100) NOT NULL,
        fecha_creacion     DATETIME2(3)  NOT NULL CONSTRAINT DF_empleados_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por     NVARCHAR(100) NULL,
        fecha_modificacion DATETIME2(3)  NULL,
        token_concurrencia ROWVERSION    NOT NULL,
        CONSTRAINT PK_empleados PRIMARY KEY CLUSTERED (id_empleado),
        CONSTRAINT FK_empleados_vinculo FOREIGN KEY (id_persona_empresa, id_empresa, tipo_vinculo)
            REFERENCES dbo.persona_empresa (id_persona_empresa, id_empresa, tipo_vinculo),
        CONSTRAINT CK_empleados_tipo   CHECK (tipo_vinculo = 'empleado'),
        CONSTRAINT CK_empleados_tarifa CHECK (tarifa_diaria IS NULL OR tarifa_diaria >= 0)
    );

    -- Un vinculo tiene como mucho una ficha de empleado
    CREATE UNIQUE INDEX UX_empleados_vinculo
        ON dbo.empleados (id_persona_empresa)
        WHERE eliminado = 0;

    -- Numero de empleado unico por empresa entre los empleados activos
    CREATE UNIQUE INDEX UX_empleados_codigo
        ON dbo.empleados (id_empresa, codigo_interno)
        WHERE codigo_interno IS NOT NULL AND eliminado = 0 AND activo = 1;

    CREATE INDEX IX_empleados_empresa_cargo
        ON dbo.empleados (id_empresa, cargo)
        WHERE eliminado = 0 AND activo = 1;

    PRINT N'  + dbo.empleados';
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 8/11: [VERDE / AGREGA] dbo.clientes: columnas para enlazar con la persona
--   dbo.clientes esta vacia (0 filas, verificado en F0). Los clientes juridicos siguen sin persona:
--   id_persona_empresa queda en NULL y la clave foranea compuesta no se evalua.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.clientes', N'id_persona_empresa') IS NULL
    BEGIN
        ALTER TABLE dbo.clientes ADD id_persona_empresa INT NULL;
        PRINT N'  + dbo.clientes.id_persona_empresa';
    END;

    IF COL_LENGTH(N'dbo.clientes', N'tipo_vinculo') IS NULL
    BEGIN
        ALTER TABLE dbo.clientes ADD tipo_vinculo VARCHAR(20) NOT NULL
            CONSTRAINT DF_cli_tvinculo DEFAULT ('cliente');
        PRINT N'  + dbo.clientes.tipo_vinculo';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 9/11: [VERDE / AGREGA] dbo.clientes: restriccion, clave foranea compuesta e indice
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_clientes_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
        ALTER TABLE dbo.clientes ADD CONSTRAINT CK_clientes_tipo_vinculo
            CHECK (tipo_vinculo = 'cliente');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
        ALTER TABLE dbo.clientes ADD CONSTRAINT FK_clientes_vinculo
            FOREIGN KEY (id_persona_empresa, id_empresa, tipo_vinculo)
            REFERENCES dbo.persona_empresa (id_persona_empresa, id_empresa, tipo_vinculo);

    -- Un vinculo de cliente corresponde a un solo registro de cliente
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_clientes_vinculo' AND object_id = OBJECT_ID(N'dbo.clientes'))
        CREATE UNIQUE INDEX UX_clientes_vinculo
            ON dbo.clientes (id_persona_empresa)
            WHERE id_persona_empresa IS NOT NULL AND eliminado = 0;

    COMMIT TRANSACTION;
    PRINT N'  ~ dbo.clientes: enlace con persona_empresa';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 10/11: [VERDE / AGREGA] dbo.bitacora_cambios
--   Una fila por campo modificado. Sin claves foraneas a proposito: la bitacora nunca debe
--   bloquear ni ser afectada por cambios en otras tablas.
--   id_transaccion agrupa las filas de un mismo guardado. id_persona da el historial completo
--   de una persona aunque el registro modificado sea un documento, un vinculo o un rol.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.bitacora_cambios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bitacora_cambios (
        id_bitacora     BIGINT IDENTITY(1,1) NOT NULL,
        id_transaccion  UNIQUEIDENTIFIER NOT NULL,
        id_empresa      INT              NULL,
        id_persona      INT              NULL,
        entidad         VARCHAR(60)      NOT NULL,
        id_registro     BIGINT           NOT NULL,
        operacion       VARCHAR(10)      NOT NULL,
        campo           VARCHAR(100)     NULL,
        valor_anterior  NVARCHAR(2000)   NULL,
        valor_nuevo     NVARCHAR(2000)   NULL,
        fecha_hora      DATETIME2(3)     NOT NULL CONSTRAINT DF_bitacora_fh DEFAULT (SYSUTCDATETIME()),
        usuario         NVARCHAR(100)    NOT NULL,
        origen          VARCHAR(50)      NOT NULL CONSTRAINT DF_bitacora_origen DEFAULT ('app'),
        CONSTRAINT PK_bitacora_cambios PRIMARY KEY CLUSTERED (id_bitacora),
        CONSTRAINT CK_bitacora_cambios_operacion CHECK (operacion IN ('INSERT', 'UPDATE', 'DELETE'))
    );

    CREATE INDEX IX_bitacora_cambios_entidad_registro
        ON dbo.bitacora_cambios (entidad, id_registro, fecha_hora);

    CREATE INDEX IX_bitacora_cambios_persona
        ON dbo.bitacora_cambios (id_persona, fecha_hora)
        WHERE id_persona IS NOT NULL;

    CREATE INDEX IX_bitacora_cambios_empresa
        ON dbo.bitacora_cambios (id_empresa, fecha_hora);

    CREATE INDEX IX_bitacora_cambios_transaccion
        ON dbo.bitacora_cambios (id_transaccion);

    PRINT N'  + dbo.bitacora_cambios';
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 11/11: [VERDE / AGREGA] disparador de inmutabilidad de la bitacora
--   INSTEAD OF UPDATE, DELETE: rechaza cualquier modificacion o borrado. Se crea con SQL dinamico
--   porque CREATE TRIGGER debe ser la primera sentencia de su lote.
--   Nota: TRUNCATE TABLE no dispara triggers; el permiso para truncar debe quedar restringido.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_bitacora_cambios_inmutable
ON dbo.bitacora_cambios
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50015, N''La bitacora de cambios es de solo lectura: no se permite modificar ni borrar filas.'', 1;
END;';
PRINT N'  + dbo.TR_bitacora_cambios_inmutable';
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
--   Falla con THROW si falta cualquier objeto o queda algun resto.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_014') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @faltan TABLE (problema NVARCHAR(300) NOT NULL);

-- Tablas nuevas
INSERT INTO @faltan (problema)
SELECT N'Falta la tabla ' + t
FROM (VALUES (N'dbo.catalogo_departamentos'), (N'dbo.catalogo_municipios'), (N'dbo.catalogo_tipos_documento'),
             (N'dbo.catalogo_tipos_licencia'), (N'dbo.persona_documentos'), (N'dbo.persona_empresa'),
             (N'dbo.empleados'), (N'dbo.bitacora_cambios')) v (t)
WHERE OBJECT_ID(t, N'U') IS NULL;

-- Columnas nuevas de personas
INSERT INTO @faltan (problema)
SELECT N'Falta la columna dbo.personas.' + c
FROM (VALUES (N'primer_nombre'), (N'segundo_nombre'), (N'primer_apellido'), (N'segundo_apellido'),
             (N'nombre_normalizado'), (N'sexo'), (N'estado_civil'), (N'fecha_nacimiento'),
             (N'pais_nacionalidad'), (N'tipo_sangre'), (N'id_municipio_nacimiento'),
             (N'id_municipio_residencia'), (N'direccion_residencia'), (N'telefono_secundario'),
             (N'contacto_emergencia_nombre'), (N'contacto_emergencia_telefono'),
             (N'contacto_emergencia_parentesco'), (N'licencia_tipo'), (N'licencia_numero'),
             (N'licencia_vencimiento'), (N'estado_identidad'), (N'id_persona_principal')) v (c)
WHERE COL_LENGTH(N'dbo.personas', c) IS NULL;

-- Columnas nuevas de clientes
INSERT INTO @faltan (problema)
SELECT N'Falta la columna dbo.clientes.' + c
FROM (VALUES (N'id_persona_empresa'), (N'tipo_vinculo')) v (c)
WHERE COL_LENGTH(N'dbo.clientes', c) IS NULL;

-- Columnas legacy de personas que deben admitir NULL
INSERT INTO @faltan (problema)
SELECT N'dbo.personas.' + name + N' sigue siendo NOT NULL'
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.personas')
  AND name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo')
  AND is_nullable = 0;

-- El CHECK de cargo debe haberse retirado
INSERT INTO @faltan (problema)
SELECT N'CK_personas_cargo sigue existiendo'
WHERE EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_personas_cargo');

-- Indices esperados
INSERT INTO @faltan (problema)
SELECT N'Falta el indice ' + n
FROM (VALUES (N'UX_personas_empresa_documento'), (N'IX_personas_empresa_cargo'),
             (N'IX_personas_nombre_normalizado'), (N'IX_personas_principal'),
             (N'UX_persona_documentos_identidad'), (N'UX_persona_documentos_principal'),
             (N'UX_persona_empresa_rol'), (N'UX_persona_empresa_vigente'),
             (N'UX_empleados_vinculo'), (N'UX_empleados_codigo'),
             (N'UX_clientes_vinculo'), (N'IX_bitacora_cambios_persona')) v (n)
WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = n);

-- Restricciones y claves foraneas esperadas
INSERT INTO @faltan (problema)
SELECT N'Falta la restriccion ' + n
FROM (VALUES (N'FK_personas_empresa'), (N'FK_personas_pais_nacionalidad'), (N'FK_personas_municipio_nacimiento'),
             (N'FK_personas_municipio_residencia'), (N'FK_personas_licencia_tipo'), (N'FK_personas_principal'),
             (N'CK_personas_sexo'), (N'CK_personas_estado_civil'), (N'CK_personas_tipo_sangre'),
             (N'CK_personas_estado_identidad'), (N'CK_personas_fecha_nacimiento'), (N'CK_personas_emergencia'),
             (N'CK_personas_email'), (N'CK_personas_principal'),
             (N'FK_persona_documentos_persona'), (N'FK_persona_empresa_empresa'), (N'FK_persona_empresa_persona'),
             (N'FK_empleados_vinculo'), (N'CK_empleados_tipo'), (N'FK_clientes_vinculo'), (N'CK_clientes_tipo_vinculo')) v (n)
WHERE NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = n AND type IN ('F', 'C'));

-- Disparador de inmutabilidad
INSERT INTO @faltan (problema)
SELECT N'Falta el disparador TR_bitacora_cambios_inmutable (INSTEAD OF)'
WHERE NOT EXISTS (SELECT 1 FROM sys.triggers
                  WHERE name = N'TR_bitacora_cambios_inmutable' AND is_instead_of_trigger = 1);

IF EXISTS (SELECT 1 FROM @faltan)
BEGIN
    SELECT problema FROM @faltan;
    THROW 50014, N'POSTCHECK 014 fallo: faltan objetos o quedan restos. Revise la lista anterior.', 1;
END;

-- Resumen: las filas existentes no cambian y las estructuras nuevas estan vacias
SELECT N'personas (filas, sin cambio)' AS dato, COUNT(*) AS valor FROM dbo.personas
UNION ALL SELECT N'persona_documentos', COUNT(*) FROM dbo.persona_documentos
UNION ALL SELECT N'persona_empresa',    COUNT(*) FROM dbo.persona_empresa
UNION ALL SELECT N'empleados',          COUNT(*) FROM dbo.empleados
UNION ALL SELECT N'bitacora_cambios',   COUNT(*) FROM dbo.bitacora_cambios
UNION ALL SELECT N'clientes (sin cambio)', COUNT(*) FROM dbo.clientes
UNION ALL SELECT N'personas con estado_identidad = pendiente', COUNT(*) FROM dbo.personas WHERE estado_identidad = 'pendiente';

SELECT name AS columna_legacy, is_nullable AS admite_null
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.personas') AND name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo')
ORDER BY column_id;

IF OBJECT_ID(N'tempdb..#precheck_014') IS NOT NULL DROP TABLE #precheck_014;
PRINT N'Script 014 aplicado y verificado.';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Valido solo mientras dbo.bitacora_cambios, persona_documentos, persona_empresa y empleados no
--   tengan datos, y mientras no existan personas sin id_empresa/documento/cargo (de lo contrario
--   no se puede volver a NOT NULL). Si ya hay datos, restaurar desde el respaldo del script 016.
--   Se deja comentado a proposito. Para usarlo: copiar este bloque, descomentarlo y ejecutarlo.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM dbo.bitacora_cambios) OR EXISTS (SELECT 1 FROM dbo.persona_documentos)
   OR EXISTS (SELECT 1 FROM dbo.persona_empresa) OR EXISTS (SELECT 1 FROM dbo.empleados)
    THROW 50016, N'Hay datos en las tablas nuevas: no se hace rollback automatico.', 1;

-- 1) Disparador y bitacora
DROP TRIGGER IF EXISTS dbo.TR_bitacora_cambios_inmutable;
DROP TABLE IF EXISTS dbo.bitacora_cambios;

-- 2) clientes
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_vinculo')
    ALTER TABLE dbo.clientes DROP CONSTRAINT FK_clientes_vinculo;
DROP INDEX IF EXISTS UX_clientes_vinculo ON dbo.clientes;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_clientes_tipo_vinculo')
    ALTER TABLE dbo.clientes DROP CONSTRAINT CK_clientes_tipo_vinculo;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_cli_tvinculo')
    ALTER TABLE dbo.clientes DROP CONSTRAINT DF_cli_tvinculo;
IF COL_LENGTH(N'dbo.clientes', N'id_persona_empresa') IS NOT NULL
    ALTER TABLE dbo.clientes DROP COLUMN id_persona_empresa;
IF COL_LENGTH(N'dbo.clientes', N'tipo_vinculo') IS NOT NULL
    ALTER TABLE dbo.clientes DROP COLUMN tipo_vinculo;

-- 3) Tablas de rol y vinculo (orden inverso de dependencias)
DROP TABLE IF EXISTS dbo.empleados;
DROP TABLE IF EXISTS dbo.persona_empresa;
DROP TABLE IF EXISTS dbo.persona_documentos;

-- 4) personas: quitar restricciones, indices y columnas nuevas
DROP INDEX IF EXISTS IX_personas_nombre_normalizado ON dbo.personas;
DROP INDEX IF EXISTS IX_personas_principal ON dbo.personas;

DECLARE @drop NVARCHAR(MAX) = N'';
SELECT @drop += N'ALTER TABLE dbo.personas DROP CONSTRAINT ' + QUOTENAME(name) + N';'
FROM sys.objects
WHERE parent_object_id = OBJECT_ID(N'dbo.personas')
  AND name IN (N'CK_personas_sexo', N'CK_personas_estado_civil', N'CK_personas_tipo_sangre',
               N'CK_personas_estado_identidad', N'CK_personas_fecha_nacimiento', N'CK_personas_emergencia',
               N'CK_personas_email', N'CK_personas_principal', N'FK_personas_pais_nacionalidad',
               N'FK_personas_municipio_nacimiento', N'FK_personas_municipio_residencia',
               N'FK_personas_licencia_tipo', N'FK_personas_principal', N'DF_personas_estado_identidad');
EXEC sys.sp_executesql @drop;

SET @drop = N'';
SELECT @drop += N'ALTER TABLE dbo.personas DROP COLUMN ' + QUOTENAME(name) + N';'
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.personas')
  AND name IN (N'primer_nombre', N'segundo_nombre', N'primer_apellido', N'segundo_apellido', N'nombre_normalizado',
               N'sexo', N'estado_civil', N'fecha_nacimiento', N'pais_nacionalidad', N'tipo_sangre',
               N'id_municipio_nacimiento', N'id_municipio_residencia', N'direccion_residencia',
               N'telefono_secundario', N'contacto_emergencia_nombre', N'contacto_emergencia_telefono',
               N'contacto_emergencia_parentesco', N'licencia_tipo', N'licencia_numero',
               N'licencia_vencimiento', N'estado_identidad', N'id_persona_principal');
EXEC sys.sp_executesql @drop;

-- 5) personas: restaurar el estado original de las columnas legacy
--    (falla si hay filas con NULL en id_empresa, documento, tipo_documento o cargo)
DROP INDEX IF EXISTS UX_personas_empresa_documento ON dbo.personas;
DROP INDEX IF EXISTS IX_personas_empresa_cargo ON dbo.personas;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_empresa')
    ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_empresa;

ALTER TABLE dbo.personas ALTER COLUMN id_empresa INT NOT NULL;
ALTER TABLE dbo.personas ALTER COLUMN documento VARCHAR(30) NOT NULL;
ALTER TABLE dbo.personas ALTER COLUMN tipo_documento VARCHAR(20) NOT NULL;
ALTER TABLE dbo.personas ALTER COLUMN cargo VARCHAR(30) NOT NULL;

ALTER TABLE dbo.personas ADD CONSTRAINT DF_personas_tipo_documento DEFAULT ('DNI') FOR tipo_documento;
ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
ALTER TABLE dbo.personas WITH NOCHECK ADD CONSTRAINT CK_personas_cargo
    CHECK (cargo IN ('CONDUCTOR', 'COBRADOR', 'MECANICO', 'SUPERVISOR', 'OTRO'));
CREATE UNIQUE INDEX UX_personas_empresa_documento ON dbo.personas (id_empresa, documento) WHERE eliminado = 0;
CREATE INDEX IX_personas_empresa_cargo ON dbo.personas (id_empresa, cargo) WHERE eliminado = 0 AND activo = 1;

-- 6) Catalogos
DROP TABLE IF EXISTS dbo.catalogo_municipios;
DROP TABLE IF EXISTS dbo.catalogo_departamentos;
DROP TABLE IF EXISTS dbo.catalogo_tipos_documento;
DROP TABLE IF EXISTS dbo.catalogo_tipos_licencia;

COMMIT TRANSACTION;
*/
