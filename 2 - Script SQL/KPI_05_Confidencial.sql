-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 5: Confidencial
-- -----------------------------------------------------------------------------
-- Tablas creadas (en orden):
--   1. ingresos_operativos  (ingresos diarios por vehículo; base para rentabilidad)
--
-- Prerequisitos: Grupo 1 (vehiculos, rutas).
-- Notas:
--   - Esta tabla contiene información sensible. El acceso debe estar controlado
--     a nivel aplicación (autorización por rol) y, opcionalmente, por
--     permisos a nivel objeto en SQL Server (GRANT/DENY).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. ingresos_operativos
--    Una fila por (empresa, vehículo, fecha). Recaudación diaria.
--    utilidad_bruta es un espejo: total de gastos diarios no se calcula aquí,
--    se resuelve en vistas/consultas que unen ingresos + gastos.
-- -----------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ingresos_operativos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ingresos_operativos (
        id_ingreso_operativo INT            IDENTITY(1,1) NOT NULL,
        id_empresa           INT            NOT NULL,
        id_vehiculo          INT            NOT NULL,
        fecha                DATE           NOT NULL,
        id_ruta              INT            NULL,
        viajes               INT            NULL,
        pasajeros            INT            NULL,      -- aplica sólo a transporte de personas
        km_recorridos        DECIMAL(12,2)  NULL,
        monto_ingreso        DECIMAL(18,2)  NOT NULL,
        moneda               CHAR(3)        NOT NULL,
        observaciones        NVARCHAR(500)  NULL,
        eliminado            BIT            NOT NULL CONSTRAINT DF_ingresos_operativos_eliminado       DEFAULT (0),
        fecha_eliminado      DATETIME2(3)   NULL,
        creado_por           NVARCHAR(100)  NOT NULL,
        fecha_creacion       DATETIME2(3)   NOT NULL CONSTRAINT DF_ingresos_operativos_fecha_creacion  DEFAULT (SYSUTCDATETIME()),
        modificado_por       NVARCHAR(100)  NULL,
        fecha_modificacion   DATETIME2(3)   NULL,
        token_concurrencia   ROWVERSION     NOT NULL,
        CONSTRAINT PK_ingresos_operativos            PRIMARY KEY CLUSTERED (id_ingreso_operativo),
        CONSTRAINT FK_ingresos_operativos_empresa    FOREIGN KEY (id_empresa)  REFERENCES dbo.empresas  (id_empresa),
        CONSTRAINT FK_ingresos_operativos_vehiculo   FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo),
        CONSTRAINT FK_ingresos_operativos_ruta       FOREIGN KEY (id_ruta)     REFERENCES dbo.rutas     (id_ruta),
        CONSTRAINT CK_ingresos_operativos_monto      CHECK (monto_ingreso >= 0),
        CONSTRAINT CK_ingresos_operativos_viajes     CHECK (viajes IS NULL OR viajes >= 0),
        CONSTRAINT CK_ingresos_operativos_pasajeros  CHECK (pasajeros IS NULL OR pasajeros >= 0)
    );

    CREATE UNIQUE INDEX UX_ingresos_operativos_vehiculo_fecha
        ON dbo.ingresos_operativos (id_empresa, id_vehiculo, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_ingresos_operativos_empresa_fecha
        ON dbo.ingresos_operativos (id_empresa, fecha)
        WHERE eliminado = 0;

    CREATE INDEX IX_ingresos_operativos_ruta_fecha
        ON dbo.ingresos_operativos (id_empresa, id_ruta, fecha)
        WHERE eliminado = 0 AND id_ruta IS NOT NULL;
END;
GO
