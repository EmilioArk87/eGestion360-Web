-- ============================================================
-- Script   : 010_ct_nucleo_contable.sql
-- Proposito: Nucleo del modulo contable (cuentas, ejercicios, periodos, centros de costo, asientos, movimientos)
-- Autor    : eGestion360-Web
-- Fecha    : 2026-08-06
-- BD       : eBD_SPD
-- Requiere : dbo.empresas (multitenant) y dbo.domain_events (F0_Outbox.sql)
-- Rollback : Ver seccion ROLLBACK al final (DROP de las 6 tablas ct_* en orden inverso)
-- ============================================================
-- Convenciones: snake_case + prefijo de modulo ct_ (ver 1 - Documetacion/ESTANDARES_ERP.md).
-- Idempotente: cada tabla se crea solo si no existe. Aditivo (no destructivo).
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
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'empresas')
BEGIN
    RAISERROR('Falta dbo.empresas. Ejecute la estructura base / multitenant antes. Abortar.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'domain_events')
BEGIN
    RAISERROR('Falta dbo.domain_events. Ejecute F0_Outbox.sql antes. Abortar.', 16, 1);
    RETURN;
END;

-- Informativo: cuantas tablas ct_* existen ya (0 en instalacion limpia)
SELECT COUNT(*) AS ct_tablas_existentes
FROM sys.tables
WHERE name LIKE 'ct[_]%';

-- ------------------------------------------------------------
-- CAMBIO
-- ------------------------------------------------------------

-- [VERDE / AGREGA] ct_cuentas — Plan de cuentas jerarquico
IF OBJECT_ID('dbo.ct_cuentas', 'U') IS NULL
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
        moneda              NVARCHAR(3)    NOT NULL CONSTRAINT DF_ctcta_mon  DEFAULT ('HNL'),
        activo              BIT            NOT NULL CONSTRAINT DF_ctcta_act   DEFAULT (1),
        eliminado           BIT            NOT NULL CONSTRAINT DF_ctcta_elim  DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctcta_fc   DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        CONSTRAINT PK_ct_cuentas         PRIMARY KEY CLUSTERED (id_cuenta),
        CONSTRAINT FK_ct_cuentas_empresa FOREIGN KEY (id_empresa)      REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_cuentas_padre   FOREIGN KEY (id_cuenta_padre) REFERENCES dbo.ct_cuentas(id_cuenta),
        CONSTRAINT CK_ct_cuentas_nat     CHECK (naturaleza IN ('deudora','acreedora')),
        CONSTRAINT CK_ct_cuentas_tipo    CHECK (tipo IN ('activo','pasivo','patrimonio','ingreso','gasto','orden'))
    );
    CREATE UNIQUE INDEX UX_ct_cuentas_empresa_codigo ON dbo.ct_cuentas(id_empresa, codigo);
    CREATE INDEX        IX_ct_cuentas_empresa_padre  ON dbo.ct_cuentas(id_empresa, id_cuenta_padre);
END;

-- [VERDE / AGREGA] ct_ejercicios — Ejercicios fiscales
IF OBJECT_ID('dbo.ct_ejercicios', 'U') IS NULL
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
        CONSTRAINT PK_ct_ejercicios         PRIMARY KEY CLUSTERED (id_ejercicio),
        CONSTRAINT FK_ct_ejercicios_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_ct_ejercicios_estado  CHECK (estado IN ('abierto','cerrado'))
    );
    CREATE UNIQUE INDEX UX_ct_ejercicios_empresa_anio ON dbo.ct_ejercicios(id_empresa, anio);
END;

-- [VERDE / AGREGA] ct_periodos — Periodos contables (por ejercicio)
IF OBJECT_ID('dbo.ct_periodos', 'U') IS NULL
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
        CONSTRAINT PK_ct_periodos           PRIMARY KEY CLUSTERED (id_periodo),
        CONSTRAINT FK_ct_periodos_ejercicio FOREIGN KEY (id_ejercicio) REFERENCES dbo.ct_ejercicios(id_ejercicio),
        CONSTRAINT FK_ct_periodos_empresa   FOREIGN KEY (id_empresa)   REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_ct_periodos_estado    CHECK (estado IN ('abierto','cerrado')),
        CONSTRAINT CK_ct_periodos_numero    CHECK (numero BETWEEN 1 AND 13)
    );
    CREATE UNIQUE INDEX UX_ct_periodos_ejercicio_numero ON dbo.ct_periodos(id_ejercicio, numero);
END;

-- [VERDE / AGREGA] ct_centros_costo — Centros de costo (opcional por linea de asiento)
IF OBJECT_ID('dbo.ct_centros_costo', 'U') IS NULL
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
        CONSTRAINT PK_ct_centros_costo         PRIMARY KEY CLUSTERED (id_centro_costo),
        CONSTRAINT FK_ct_centros_costo_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );
    CREATE UNIQUE INDEX UX_ct_centros_costo_empresa_codigo ON dbo.ct_centros_costo(id_empresa, codigo);
END;

-- [VERDE / AGREGA] ct_asientos — Cabecera del asiento (partida doble)
IF OBJECT_ID('dbo.ct_asientos', 'U') IS NULL
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
        CONSTRAINT PK_ct_asientos         PRIMARY KEY CLUSTERED (id_asiento),
        CONSTRAINT FK_ct_asientos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_asientos_periodo FOREIGN KEY (id_periodo) REFERENCES dbo.ct_periodos(id_periodo),
        CONSTRAINT CK_ct_asientos_tipo    CHECK (tipo_asiento IN ('apertura','diario','ajuste','cierre')),
        CONSTRAINT CK_ct_asientos_origen  CHECK (origen IN ('manual','automatico')),
        CONSTRAINT CK_ct_asientos_estado  CHECK (estado IN ('borrador','mayorizado','anulado')),
        -- Partida doble: la cabecera se persiste cuadrada. Si se decide permitir borradores
        -- descuadrados, relajar este CHECK y validar el cuadre en el Service al mayorizar.
        CONSTRAINT CK_ct_asientos_cuadre  CHECK (total_debito = total_credito)
    );
    -- Idempotencia: un evento del outbox genera a lo sumo un asiento por empresa
    CREATE UNIQUE INDEX UX_ct_asientos_empresa_evento
        ON dbo.ct_asientos(id_empresa, id_evento_origen) WHERE id_evento_origen IS NOT NULL;
    -- Correlativo unico de asientos ya mayorizados
    CREATE UNIQUE INDEX UX_ct_asientos_empresa_numero
        ON dbo.ct_asientos(id_empresa, numero) WHERE numero IS NOT NULL;
    CREATE INDEX IX_ct_asientos_empresa_fecha          ON dbo.ct_asientos(id_empresa, fecha);
    CREATE INDEX IX_ct_asientos_empresa_periodo_estado ON dbo.ct_asientos(id_empresa, id_periodo, estado);
END;

-- [VERDE / AGREGA] ct_asiento_movimientos — Detalle del asiento (debito / credito)
IF OBJECT_ID('dbo.ct_asiento_movimientos', 'U') IS NULL
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
        CONSTRAINT FK_ct_mov_asiento            FOREIGN KEY (id_asiento)      REFERENCES dbo.ct_asientos(id_asiento),
        CONSTRAINT FK_ct_mov_empresa            FOREIGN KEY (id_empresa)      REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_mov_cuenta             FOREIGN KEY (id_cuenta)       REFERENCES dbo.ct_cuentas(id_cuenta),
        CONSTRAINT FK_ct_mov_centro             FOREIGN KEY (id_centro_costo) REFERENCES dbo.ct_centros_costo(id_centro_costo),
        CONSTRAINT CK_ct_mov_no_negativos       CHECK (debito >= 0 AND credito >= 0),
        -- Exactamente uno de los dos importes debe ser > 0
        CONSTRAINT CK_ct_mov_debito_xor_credito CHECK ((debito > 0 AND credito = 0) OR (credito > 0 AND debito = 0))
    );
    CREATE UNIQUE INDEX UX_ct_mov_asiento_linea  ON dbo.ct_asiento_movimientos(id_asiento, numero_linea);
    CREATE INDEX        IX_ct_mov_asiento        ON dbo.ct_asiento_movimientos(id_asiento);
    CREATE INDEX        IX_ct_mov_empresa_cuenta ON dbo.ct_asiento_movimientos(id_empresa, id_cuenta);
END;

-- ------------------------------------------------------------
-- POSTCHECK (verificacion)
-- ------------------------------------------------------------
-- Deben aparecer las 6 tablas del nucleo contable.
SELECT name AS tabla
FROM sys.tables
WHERE name IN ('ct_cuentas','ct_ejercicios','ct_periodos','ct_centros_costo','ct_asientos','ct_asiento_movimientos')
ORDER BY name;

-- ------------------------------------------------------------
-- ROLLBACK (ejecutar solo si hay que deshacer; respetar el orden inverso por dependencias)
-- ------------------------------------------------------------
-- DROP TABLE IF EXISTS dbo.ct_asiento_movimientos;
-- DROP TABLE IF EXISTS dbo.ct_asientos;
-- DROP TABLE IF EXISTS dbo.ct_centros_costo;
-- DROP TABLE IF EXISTS dbo.ct_periodos;
-- DROP TABLE IF EXISTS dbo.ct_ejercicios;
-- DROP TABLE IF EXISTS dbo.ct_cuentas;
