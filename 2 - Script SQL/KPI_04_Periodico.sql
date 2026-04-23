-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 4: Transaccional Periódico
-- -----------------------------------------------------------------------------
-- Tablas creadas (en orden):
--   1. gastos_repuestos       (compra de repuestos y llantas)
--   2. ordenes_mantenimiento  (mano de obra y servicios en talleres)
--   3. polizas_seguros        (pólizas por vehículo, con costo diario computado)
--
-- Prerequisitos: Grupo 1 (vehiculos, talleres, categorias_repuesto).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. gastos_repuestos
--    Una fila por ítem / línea de factura de repuesto o llanta.
--    subtotal = cantidad * precio_unitario (PERSISTED).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.gastos_repuestos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.gastos_repuestos (
        id_gasto_repuesto     INT            IDENTITY(1,1) NOT NULL,
        id_empresa            INT            NOT NULL,
        id_vehiculo           INT            NOT NULL,
        id_categoria_repuesto INT            NOT NULL,
        fecha                 DATE           NOT NULL,
        no_factura            VARCHAR(50)    NULL,
        proveedor             NVARCHAR(150)  NULL,
        descripcion           NVARCHAR(250)  NOT NULL,
        cantidad              DECIMAL(12,3)  NOT NULL CONSTRAINT DF_gastos_repuestos_cantidad DEFAULT (1),
        precio_unitario       DECIMAL(18,4)  NOT NULL,
        moneda                CHAR(3)        NOT NULL,
        subtotal              AS (CAST(cantidad AS DECIMAL(18,4)) * precio_unitario) PERSISTED,
        km_odometro           DECIMAL(12,2)  NULL,
        observaciones         NVARCHAR(500)  NULL,
        eliminado             BIT            NOT NULL CONSTRAINT DF_gastos_repuestos_eliminado       DEFAULT (0),
        fecha_eliminado       DATETIME2(3)   NULL,
        creado_por            NVARCHAR(100)  NOT NULL,
        fecha_creacion        DATETIME2(3)   NOT NULL CONSTRAINT DF_gastos_repuestos_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por        NVARCHAR(100)  NULL,
        fecha_modificacion    DATETIME2(3)   NULL,
        token_concurrencia    ROWVERSION     NOT NULL,
        CONSTRAINT PK_gastos_repuestos            PRIMARY KEY CLUSTERED (id_gasto_repuesto),
        CONSTRAINT FK_gastos_repuestos_empresa    FOREIGN KEY (id_empresa)            REFERENCES dbo.empresas             (id_empresa),
        CONSTRAINT FK_gastos_repuestos_vehiculo   FOREIGN KEY (id_vehiculo)           REFERENCES dbo.vehiculos            (id_vehiculo),
        CONSTRAINT FK_gastos_repuestos_categoria  FOREIGN KEY (id_categoria_repuesto) REFERENCES dbo.categorias_repuesto  (id_categoria_repuesto),
        CONSTRAINT CK_gastos_repuestos_cantidad   CHECK (cantidad > 0),
        CONSTRAINT CK_gastos_repuestos_precio     CHECK (precio_unitario >= 0)
    );

    CREATE INDEX IX_gastos_repuestos_vehiculo_fecha
        ON dbo.gastos_repuestos (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_gastos_repuestos_empresa_fecha
        ON dbo.gastos_repuestos (id_empresa, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_gastos_repuestos_categoria
        ON dbo.gastos_repuestos (id_empresa, id_categoria_repuesto, fecha)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 2. ordenes_mantenimiento
--    Servicios de mantenimiento (preventivo/correctivo) efectuados en talleres.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ordenes_mantenimiento', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ordenes_mantenimiento (
        id_orden_mantenimiento INT            IDENTITY(1,1) NOT NULL,
        id_empresa             INT            NOT NULL,
        id_vehiculo            INT            NOT NULL,
        id_taller              INT            NOT NULL,
        fecha                  DATE           NOT NULL,
        no_factura             VARCHAR(50)    NOT NULL,
        tipo_mantenimiento     VARCHAR(20)    NOT NULL,      -- PREVENTIVO | CORRECTIVO | REVISION
        descripcion            NVARCHAR(500)  NOT NULL,
        monto_mano_obra        DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ord_mant_mano_obra DEFAULT (0),
        monto_repuestos        DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ord_mant_repuestos DEFAULT (0),
        monto_otros            DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ord_mant_otros     DEFAULT (0),
        total                  AS (monto_mano_obra + monto_repuestos + monto_otros) PERSISTED,
        moneda                 CHAR(3)        NOT NULL,
        km_odometro            DECIMAL(12,2)  NULL,
        observaciones          NVARCHAR(500)  NULL,
        eliminado              BIT            NOT NULL CONSTRAINT DF_ordenes_mantenimiento_eliminado       DEFAULT (0),
        fecha_eliminado        DATETIME2(3)   NULL,
        creado_por             NVARCHAR(100)  NOT NULL,
        fecha_creacion         DATETIME2(3)   NOT NULL CONSTRAINT DF_ordenes_mantenimiento_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por         NVARCHAR(100)  NULL,
        fecha_modificacion     DATETIME2(3)   NULL,
        token_concurrencia     ROWVERSION     NOT NULL,
        CONSTRAINT PK_ordenes_mantenimiento           PRIMARY KEY CLUSTERED (id_orden_mantenimiento),
        CONSTRAINT FK_ordenes_mantenimiento_empresa   FOREIGN KEY (id_empresa)  REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_ordenes_mantenimiento_vehiculo  FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT FK_ordenes_mantenimiento_taller    FOREIGN KEY (id_taller)   REFERENCES dbo.talleres  (id_taller),
        CONSTRAINT CK_ordenes_mantenimiento_montos    CHECK (monto_mano_obra >= 0 AND monto_repuestos >= 0 AND monto_otros >= 0),
        CONSTRAINT CK_ordenes_mantenimiento_tipo      CHECK (tipo_mantenimiento IN ('PREVENTIVO','CORRECTIVO','REVISION'))
    );

    CREATE UNIQUE INDEX UX_ordenes_mantenimiento_empresa_factura
        ON dbo.ordenes_mantenimiento (id_empresa, no_factura)
        WHERE eliminado = 0;

    CREATE INDEX IX_ordenes_mantenimiento_vehiculo_fecha
        ON dbo.ordenes_mantenimiento (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_ordenes_mantenimiento_taller_fecha
        ON dbo.ordenes_mantenimiento (id_empresa, id_taller, fecha)
        WHERE eliminado = 0;
END;
GO

-- -----------------------------------------------------------------------------
-- 3. polizas_seguros
--    Una fila por póliza por vehículo. costo_diario computado para prorratear
--    la prima total entre los días de vigencia (inclusivo).
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.polizas_seguros', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.polizas_seguros (
        id_poliza_seguro    INT            IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        id_vehiculo         INT            NOT NULL,
        no_poliza           VARCHAR(50)    NOT NULL,
        aseguradora         NVARCHAR(150)  NOT NULL,
        tipo_cobertura      VARCHAR(30)    NOT NULL,      -- RC | AMPLIA | LIMITADA | OTRA
        fecha_inicio        DATE           NOT NULL,
        fecha_fin           DATE           NOT NULL,
        prima_total         DECIMAL(18,2)  NOT NULL,
        moneda              CHAR(3)        NOT NULL,
        costo_diario        AS (prima_total / NULLIF(CAST(DATEDIFF(DAY, fecha_inicio, fecha_fin) + 1 AS DECIMAL(18,4)), 0)) PERSISTED,
        observaciones       NVARCHAR(500)  NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_polizas_seguros_eliminado       DEFAULT (0),
        fecha_eliminado     DATETIME2(3)   NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_polizas_seguros_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2(3)   NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_polizas_seguros            PRIMARY KEY CLUSTERED (id_poliza_seguro),
        CONSTRAINT FK_polizas_seguros_empresa    FOREIGN KEY (id_empresa)  REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_polizas_seguros_vehiculo   FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT CK_polizas_seguros_vigencia   CHECK (fecha_fin >= fecha_inicio),
        CONSTRAINT CK_polizas_seguros_prima      CHECK (prima_total >= 0),
        CONSTRAINT CK_polizas_seguros_cobertura  CHECK (tipo_cobertura IN ('RC','AMPLIA','LIMITADA','OTRA'))
    );

    CREATE UNIQUE INDEX UX_polizas_seguros_empresa_poliza_vehiculo
        ON dbo.polizas_seguros (id_empresa, no_poliza, id_vehiculo)
        WHERE eliminado = 0;

    CREATE INDEX IX_polizas_seguros_vehiculo_vigencia
        ON dbo.polizas_seguros (id_empresa, id_vehiculo, fecha_inicio, fecha_fin)
        WHERE eliminado = 0;
END;
GO
