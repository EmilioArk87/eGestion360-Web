-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 3: Transaccional Operativo Diario
-- -----------------------------------------------------------------------------
-- Tablas creadas (en orden):
--   1. odometro_diario      (km inicial/final, km_recorridos computado)
--   2. cargas_combustible   (galones, precio, total computado)
--   3. salarios_diarios     (pago diario por persona asignada a un vehículo)
--
-- Prerequisitos: Grupo 1 (vehiculos, personas) debe existir.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. odometro_diario
--    Lectura diaria de odómetro. km_recorridos es columna calculada PERSISTED.
--    Base para el divisor del KPI L/KM.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.odometro_diario', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.odometro_diario (
        id_odometro_diario  INT            IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        id_vehiculo         INT            NOT NULL,
        fecha               DATE           NOT NULL,
        km_inicial          DECIMAL(12,2)  NOT NULL,
        km_final            DECIMAL(12,2)  NOT NULL,
        km_recorridos       AS (km_final - km_inicial) PERSISTED,
        id_ruta             INT            NULL,
        id_conductor        INT            NULL,
        observaciones       NVARCHAR(500)  NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_odometro_diario_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)   NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_odometro_diario_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2(3)   NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_odometro_diario           PRIMARY KEY CLUSTERED (id_odometro_diario),
        CONSTRAINT FK_odometro_diario_empresa   FOREIGN KEY (id_empresa)   REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_odometro_diario_vehiculo  FOREIGN KEY (id_vehiculo)  REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT FK_odometro_diario_ruta      FOREIGN KEY (id_ruta)      REFERENCES dbo.rutas     (id_ruta),
        CONSTRAINT FK_odometro_diario_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas  (id_persona),
        CONSTRAINT CK_odometro_diario_km        CHECK (km_final >= km_inicial)
    );

    CREATE UNIQUE INDEX UX_odometro_diario_vehiculo_fecha
        ON dbo.odometro_diario (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_odometro_diario_empresa_fecha
        ON dbo.odometro_diario (id_empresa, fecha)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 2. cargas_combustible
--    Registro de compra/carga de combustible. total = galones * precio_unitario
--    (columna calculada PERSISTED).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.cargas_combustible', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.cargas_combustible (
        id_carga_combustible INT            IDENTITY(1,1) NOT NULL,
        id_empresa           INT            NOT NULL,
        id_vehiculo          INT            NOT NULL,
        fecha                DATE           NOT NULL,
        hora                 TIME(0)        NULL,
        no_factura           VARCHAR(50)    NOT NULL,
        proveedor            NVARCHAR(150)  NULL,
        tipo_combustible     VARCHAR(30)    NOT NULL,
        unidad_medida        VARCHAR(10)    NOT NULL CONSTRAINT DF_cargas_combustible_unidad DEFAULT ('GAL'),
        cantidad             DECIMAL(12,3)  NOT NULL,
        precio_unitario      DECIMAL(18,4)  NOT NULL,
        moneda               CHAR(3)        NOT NULL,
        total                AS (CAST(cantidad AS DECIMAL(18,4)) * precio_unitario) PERSISTED,
        km_odometro          DECIMAL(12,2)  NULL,
        id_conductor         INT            NULL,
        observaciones        NVARCHAR(500)  NULL,
        eliminado            BIT            NOT NULL CONSTRAINT DF_cargas_combustible_eliminado       DEFAULT (0),
        fecha_eliminado      DATETIME2(3)   NULL,
        creado_por           NVARCHAR(100)  NOT NULL,
        fecha_creacion       DATETIME2(3)   NOT NULL CONSTRAINT DF_cargas_combustible_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por       NVARCHAR(100)  NULL,
        fecha_modificacion   DATETIME2(3)   NULL,
        token_concurrencia   ROWVERSION     NOT NULL,
        CONSTRAINT PK_cargas_combustible            PRIMARY KEY CLUSTERED (id_carga_combustible),
        CONSTRAINT FK_cargas_combustible_empresa    FOREIGN KEY (id_empresa)   REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_cargas_combustible_vehiculo   FOREIGN KEY (id_vehiculo)  REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT FK_cargas_combustible_conductor  FOREIGN KEY (id_conductor) REFERENCES dbo.personas  (id_persona),
        CONSTRAINT CK_cargas_combustible_cantidad   CHECK (cantidad > 0),
        CONSTRAINT CK_cargas_combustible_precio     CHECK (precio_unitario >= 0),
        CONSTRAINT CK_cargas_combustible_unidad     CHECK (unidad_medida IN ('GAL','LTR','KWH'))
    );

    CREATE UNIQUE INDEX UX_cargas_combustible_empresa_factura
        ON dbo.cargas_combustible (id_empresa, no_factura)
        WHERE eliminado = 0;

    CREATE INDEX IX_cargas_combustible_vehiculo_fecha
        ON dbo.cargas_combustible (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_cargas_combustible_empresa_fecha
        ON dbo.cargas_combustible (id_empresa, fecha)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 3. salarios_diarios
--    Una fila por (vehículo, persona, fecha). Permite varios roles sobre la
--    misma unidad (p.ej. conductor + cobrador) con sus pagos separados.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.salarios_diarios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.salarios_diarios (
        id_salario_diario   INT            IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        id_vehiculo         INT            NOT NULL,
        id_persona          INT            NOT NULL,
        fecha               DATE           NOT NULL,
        cargo               VARCHAR(30)    NOT NULL,       -- snapshot del rol ese día
        monto               DECIMAL(18,2)  NOT NULL,
        moneda              CHAR(3)        NOT NULL,
        observaciones       NVARCHAR(500)  NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_salarios_diarios_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)   NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_salarios_diarios_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2(3)   NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_salarios_diarios           PRIMARY KEY CLUSTERED (id_salario_diario),
        CONSTRAINT FK_salarios_diarios_empresa   FOREIGN KEY (id_empresa)  REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_salarios_diarios_vehiculo  FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT FK_salarios_diarios_persona   FOREIGN KEY (id_persona)  REFERENCES dbo.personas  (id_persona),
        CONSTRAINT CK_salarios_diarios_monto     CHECK (monto >= 0),
        CONSTRAINT CK_salarios_diarios_cargo     CHECK (cargo IN ('CONDUCTOR','COBRADOR','MECANICO','SUPERVISOR','OTRO'))
    );

    CREATE UNIQUE INDEX UX_salarios_diarios_vehiculo_persona_fecha
        ON dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_salarios_diarios_empresa_fecha
        ON dbo.salarios_diarios (id_empresa, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_salarios_diarios_vehiculo_fecha
        ON dbo.salarios_diarios (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;
END;
GO
