-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 1: Catálogos Maestros
-- -----------------------------------------------------------------------------
-- Tablas creadas (en orden):
--   1. tipos_vehiculo
--   2. rutas
--   3. personas
--   4. talleres
--   5. categorias_repuesto
--   6. vehiculos           (depende de tipos_vehiculo y rutas)
--
-- Convenciones:
--   - snake_case en tablas y columnas.
--   - Multi-tenancy: todas las tablas llevan id_empresa FK -> dbo.empresas.
--   - Soft delete: columnas eliminado / fecha_eliminado (no borrado físico).
--   - Auditoría: creado_por / fecha_creacion / modificado_por / fecha_modificacion.
--   - Concurrencia optimista: token_concurrencia (ROWVERSION).
--   - Unicidad por tenant aplicada vía índice único filtrado (WHERE eliminado = 0)
--     para permitir reusar códigos/placas después de un soft delete.
--
-- Script idempotente (IF OBJECT_ID ... IS NULL). Ejecutar una sola vez.
-- Prerequisito: la tabla dbo.empresas debe existir.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. tipos_vehiculo
--    Catálogo de tipos (bus, camión, pick-up, auto, motocicleta, etc.).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.tipos_vehiculo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tipos_vehiculo (
        id_tipo_vehiculo    INT           IDENTITY(1,1) NOT NULL,
        id_empresa          INT           NOT NULL,
        codigo              VARCHAR(20)   NOT NULL,
        nombre              NVARCHAR(100) NOT NULL,
        descripcion         NVARCHAR(500) NULL,
        activo              BIT           NOT NULL CONSTRAINT DF_tipos_vehiculo_activo          DEFAULT (1),
        eliminado           BIT           NOT NULL CONSTRAINT DF_tipos_vehiculo_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)  NULL,
        creado_por          NVARCHAR(100) NOT NULL,
        fecha_creacion      DATETIME2(3)  NOT NULL CONSTRAINT DF_tipos_vehiculo_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100) NULL,
        fecha_modificacion  DATETIME2(3)  NULL,
        token_concurrencia  ROWVERSION    NOT NULL,
        CONSTRAINT PK_tipos_vehiculo PRIMARY KEY CLUSTERED (id_tipo_vehiculo),
        CONSTRAINT FK_tipos_vehiculo_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
    );

    CREATE UNIQUE INDEX UX_tipos_vehiculo_empresa_codigo
        ON dbo.tipos_vehiculo (id_empresa, codigo)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 2. rutas
--    Rutas o zonas de operación (opcional por vehículo).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.rutas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.rutas (
        id_ruta             INT           IDENTITY(1,1) NOT NULL,
        id_empresa          INT           NOT NULL,
        codigo              VARCHAR(20)   NOT NULL,
        nombre              NVARCHAR(150) NOT NULL,
        descripcion         NVARCHAR(500) NULL,
        distancia_km        DECIMAL(10,2) NULL,
        activo              BIT           NOT NULL CONSTRAINT DF_rutas_activo          DEFAULT (1),
        eliminado           BIT           NOT NULL CONSTRAINT DF_rutas_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)  NULL,
        creado_por          NVARCHAR(100) NOT NULL,
        fecha_creacion      DATETIME2(3)  NOT NULL CONSTRAINT DF_rutas_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100) NULL,
        fecha_modificacion  DATETIME2(3)  NULL,
        token_concurrencia  ROWVERSION    NOT NULL,
        CONSTRAINT PK_rutas PRIMARY KEY CLUSTERED (id_ruta),
        CONSTRAINT FK_rutas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
    );

    CREATE UNIQUE INDEX UX_rutas_empresa_codigo
        ON dbo.rutas (id_empresa, codigo)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 3. personas
--    Personal asignado a vehículos (conductores, cobradores, mecánicos, etc.).
--    Un registro único por persona; el rol se lleva en 'cargo'.
--    La tarifa diaria base se usa para poblar salarios_diarios.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.personas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.personas (
        id_persona          INT           IDENTITY(1,1) NOT NULL,
        id_empresa          INT           NOT NULL,
        documento           VARCHAR(30)   NOT NULL,
        tipo_documento      VARCHAR(20)   NOT NULL CONSTRAINT DF_personas_tipo_documento DEFAULT ('DNI'),
        nombres             NVARCHAR(100) NOT NULL,
        apellidos           NVARCHAR(100) NOT NULL,
        cargo               VARCHAR(30)   NOT NULL,   -- CONDUCTOR | COBRADOR | MECANICO | SUPERVISOR | OTRO
        tarifa_diaria       DECIMAL(18,2) NULL,
        moneda_tarifa       CHAR(3)       NULL,
        telefono            VARCHAR(30)   NULL,
        email               VARCHAR(150)  NULL,
        fecha_ingreso       DATE          NULL,
        fecha_baja          DATE          NULL,
        activo              BIT           NOT NULL CONSTRAINT DF_personas_activo          DEFAULT (1),
        eliminado           BIT           NOT NULL CONSTRAINT DF_personas_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)  NULL,
        creado_por          NVARCHAR(100) NOT NULL,
        fecha_creacion      DATETIME2(3)  NOT NULL CONSTRAINT DF_personas_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100) NULL,
        fecha_modificacion  DATETIME2(3)  NULL,
        token_concurrencia  ROWVERSION    NOT NULL,
        CONSTRAINT PK_personas PRIMARY KEY CLUSTERED (id_persona),
        CONSTRAINT FK_personas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa),
        CONSTRAINT CK_personas_cargo   CHECK (cargo IN ('CONDUCTOR','COBRADOR','MECANICO','SUPERVISOR','OTRO'))
    );

    CREATE UNIQUE INDEX UX_personas_empresa_documento
        ON dbo.personas (id_empresa, documento)
        WHERE eliminado = 0;

    CREATE INDEX IX_personas_empresa_cargo
        ON dbo.personas (id_empresa, cargo)
        WHERE eliminado = 0 AND activo = 1;
END;
GO

-- -----------------------------------------------------------------------------
-- 4. talleres
--    Talleres y proveedores de mantenimiento.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.talleres', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.talleres (
        id_taller           INT           IDENTITY(1,1) NOT NULL,
        id_empresa          INT           NOT NULL,
        codigo              VARCHAR(20)   NOT NULL,
        nombre              NVARCHAR(150) NOT NULL,
        rtn                 VARCHAR(30)   NULL,
        direccion           NVARCHAR(250) NULL,
        telefono            VARCHAR(30)   NULL,
        email               VARCHAR(150)  NULL,
        contacto            NVARCHAR(100) NULL,
        activo              BIT           NOT NULL CONSTRAINT DF_talleres_activo          DEFAULT (1),
        eliminado           BIT           NOT NULL CONSTRAINT DF_talleres_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)  NULL,
        creado_por          NVARCHAR(100) NOT NULL,
        fecha_creacion      DATETIME2(3)  NOT NULL CONSTRAINT DF_talleres_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100) NULL,
        fecha_modificacion  DATETIME2(3)  NULL,
        token_concurrencia  ROWVERSION    NOT NULL,
        CONSTRAINT PK_talleres PRIMARY KEY CLUSTERED (id_taller),
        CONSTRAINT FK_talleres_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
    );

    CREATE UNIQUE INDEX UX_talleres_empresa_codigo
        ON dbo.talleres (id_empresa, codigo)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 5. categorias_repuesto
--    Categorías para clasificar gastos de repuestos y llantas.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.categorias_repuesto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.categorias_repuesto (
        id_categoria_repuesto INT           IDENTITY(1,1) NOT NULL,
        id_empresa            INT           NOT NULL,
        nombre                NVARCHAR(100) NOT NULL,
        descripcion           NVARCHAR(500) NULL,
        activo                BIT           NOT NULL CONSTRAINT DF_categorias_repuesto_activo          DEFAULT (1),
        eliminado             BIT           NOT NULL CONSTRAINT DF_categorias_repuesto_eliminado       DEFAULT (0),
        fecha_eliminado       DATETIME2(3)  NULL,
        creado_por            NVARCHAR(100) NOT NULL,
        fecha_creacion        DATETIME2(3)  NOT NULL CONSTRAINT DF_categorias_repuesto_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por        NVARCHAR(100) NULL,
        fecha_modificacion    DATETIME2(3)  NULL,
        token_concurrencia    ROWVERSION    NOT NULL,
        CONSTRAINT PK_categorias_repuesto PRIMARY KEY CLUSTERED (id_categoria_repuesto),
        CONSTRAINT FK_categorias_repuesto_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
    );

    CREATE UNIQUE INDEX UX_categorias_repuesto_empresa_nombre
        ON dbo.categorias_repuesto (id_empresa, nombre)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 6. vehiculos
--    Unidades de la flota (cualquier tipo: bus, camión, pick-up, auto, etc.).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.vehiculos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.vehiculos (
        id_vehiculo         INT           IDENTITY(1,1) NOT NULL,
        id_empresa          INT           NOT NULL,
        id_tipo_vehiculo    INT           NOT NULL,
        id_ruta             INT           NULL,
        placa               VARCHAR(20)   NOT NULL,
        numero_interno      VARCHAR(20)   NULL,
        marca               NVARCHAR(50)  NULL,
        modelo              NVARCHAR(50)  NULL,
        anio                SMALLINT      NULL,
        vin                 VARCHAR(30)   NULL,
        color               NVARCHAR(30)  NULL,
        capacidad           INT           NULL,            -- asientos o carga (según tipo)
        tipo_combustible    VARCHAR(30)   NOT NULL CONSTRAINT DF_vehiculos_tipo_combustible DEFAULT ('DIESEL'),
        km_inicial          DECIMAL(12,2) NOT NULL CONSTRAINT DF_vehiculos_km_inicial       DEFAULT (0),
        fecha_alta          DATE          NULL,
        fecha_baja          DATE          NULL,
        activo              BIT           NOT NULL CONSTRAINT DF_vehiculos_activo           DEFAULT (1),
        eliminado           BIT           NOT NULL CONSTRAINT DF_vehiculos_eliminado        DEFAULT (0),
        fecha_eliminado     DATETIME2(3)  NULL,
        creado_por          NVARCHAR(100) NOT NULL,
        fecha_creacion      DATETIME2(3)  NOT NULL CONSTRAINT DF_vehiculos_fecha_creacion   DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100) NULL,
        fecha_modificacion  DATETIME2(3)  NULL,
        token_concurrencia  ROWVERSION    NOT NULL,
        CONSTRAINT PK_vehiculos PRIMARY KEY CLUSTERED (id_vehiculo),
        CONSTRAINT FK_vehiculos_empresa          FOREIGN KEY (id_empresa)       REFERENCES dbo.empresas (id_empresa),
        CONSTRAINT FK_vehiculos_tipo             FOREIGN KEY (id_tipo_vehiculo) REFERENCES dbo.tipos_vehiculo (id_tipo_vehiculo),
        CONSTRAINT FK_vehiculos_ruta             FOREIGN KEY (id_ruta)          REFERENCES dbo.rutas (id_ruta),
        CONSTRAINT CK_vehiculos_km_inicial       CHECK (km_inicial >= 0),
        CONSTRAINT CK_vehiculos_anio             CHECK (anio IS NULL OR anio BETWEEN 1950 AND 2100)
    );

    CREATE UNIQUE INDEX UX_vehiculos_empresa_placa
        ON dbo.vehiculos (id_empresa, placa)
        WHERE eliminado = 0;

    CREATE INDEX IX_vehiculos_empresa_tipo
        ON dbo.vehiculos (id_empresa, id_tipo_vehiculo)
        WHERE eliminado = 0;

    CREATE INDEX IX_vehiculos_empresa_ruta
        ON dbo.vehiculos (id_empresa, id_ruta)
        WHERE eliminado = 0 AND id_ruta IS NOT NULL;
END;
GO
