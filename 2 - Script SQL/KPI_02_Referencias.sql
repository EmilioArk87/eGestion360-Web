-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 2: Referencias Externas
-- -----------------------------------------------------------------------------
-- Tablas creadas (en orden):
--   1. tasas_cambio        (histórico de tipos de cambio entre monedas ISO)
--   2. precios_combustible (histórico de precios por tipo de combustible)
--
-- Prerequisitos: dbo.empresas (Grupo 1 no es necesario aquí pero suele estar).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. tasas_cambio
--    Una fila por (empresa, fecha, moneda_origen, moneda_destino).
--    Ejemplo: moneda_origen='HNL', moneda_destino='USD', tasa=0.0403
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tasas_cambio (
        id_tasa_cambio      INT            IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        fecha               DATE           NOT NULL,
        moneda_origen       CHAR(3)        NOT NULL,
        moneda_destino      CHAR(3)        NOT NULL,
        tasa                DECIMAL(18,8)  NOT NULL,
        fuente              NVARCHAR(100)  NULL,       -- p.ej. 'BCH', 'Manual', 'API'
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

    CREATE UNIQUE INDEX UX_tasas_cambio_empresa_fecha_par
        ON dbo.tasas_cambio (id_empresa, fecha, moneda_origen, moneda_destino)
        WHERE eliminado = 0;

    CREATE INDEX IX_tasas_cambio_par_fecha
        ON dbo.tasas_cambio (id_empresa, moneda_origen, moneda_destino, fecha DESC)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 2. precios_combustible
--    Precio vigente por tipo de combustible. fecha_vigencia marca el inicio;
--    el precio aplica hasta que exista una fila posterior con la misma
--    combinación (empresa, tipo_combustible).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.precios_combustible', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.precios_combustible (
        id_precio_combustible INT            IDENTITY(1,1) NOT NULL,
        id_empresa            INT            NOT NULL,
        tipo_combustible      VARCHAR(30)    NOT NULL,    -- DIESEL | GASOLINA_REGULAR | GASOLINA_SUPERIOR | GNV | ELECTRICO ...
        unidad_medida         VARCHAR(10)    NOT NULL CONSTRAINT DF_precios_combustible_unidad DEFAULT ('GAL'),
        fecha_vigencia        DATE           NOT NULL,
        precio                DECIMAL(18,4)  NOT NULL,
        moneda                CHAR(3)        NOT NULL,
        fuente                NVARCHAR(100)  NULL,        -- p.ej. 'SEN', 'Manual'
        eliminado             BIT            NOT NULL CONSTRAINT DF_precios_combustible_eliminado       DEFAULT (0),
        fecha_eliminado       DATETIME2(3)   NULL,
        creado_por            NVARCHAR(100)  NOT NULL,
        fecha_creacion        DATETIME2(3)   NOT NULL CONSTRAINT DF_precios_combustible_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por        NVARCHAR(100)  NULL,
        fecha_modificacion    DATETIME2(3)   NULL,
        token_concurrencia    ROWVERSION     NOT NULL,
        CONSTRAINT PK_precios_combustible         PRIMARY KEY CLUSTERED (id_precio_combustible),
        CONSTRAINT FK_precios_combustible_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
        CONSTRAINT CK_precios_combustible_precio  CHECK (precio > 0),
        CONSTRAINT CK_precios_combustible_unidad  CHECK (unidad_medida IN ('GAL','LTR','KWH'))
    );

    CREATE UNIQUE INDEX UX_precios_combustible_empresa_tipo_fecha
        ON dbo.precios_combustible (id_empresa, tipo_combustible, fecha_vigencia)
        WHERE eliminado = 0;

    CREATE INDEX IX_precios_combustible_lookup
        ON dbo.precios_combustible (id_empresa, tipo_combustible, fecha_vigencia DESC)
        WHERE eliminado = 0;
END;
GO
