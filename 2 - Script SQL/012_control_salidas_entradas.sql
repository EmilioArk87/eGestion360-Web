-- ============================================================
-- Script   : 012_control_salidas_entradas.sql
-- Proposito: Control de salidas y entradas de vehiculos (operacion de porteria/garita en tiempo real)
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-24
-- BD       : eBD_SPD
-- Requiere : dbo.empresas, dbo.vehiculos, dbo.personas, dbo.rutas
-- Rollback : DROP TABLE dbo.control_salidas;
-- ============================================================
-- Convenciones: snake_case en plural (ver 1 - Documetacion/ESTANDARES_ERP.md).
-- Idempotente: la tabla y sus indices se crean solo si no existen.
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
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'empresas')
BEGIN
    RAISERROR('Falta dbo.empresas. Ejecute la estructura base / multitenant antes. Abortar.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'vehiculos')
BEGIN
    RAISERROR('Falta dbo.vehiculos. Ejecute KPI_01_Catalogos.sql antes. Abortar.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'personas')
BEGIN
    RAISERROR('Falta dbo.personas. Ejecute KPI_01_Catalogos.sql antes. Abortar.', 16, 1);
    RETURN;
END;

-- ------------------------------------------------------------
-- CAMBIO: [VERDE / AGREGA] dbo.control_salidas
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.control_salidas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.control_salidas (
        id_control_salida      INT IDENTITY(1,1) NOT NULL,
        id_empresa             INT               NOT NULL,
        id_vehiculo            INT               NOT NULL,
        id_conductor           INT               NULL,
        id_ruta                INT               NULL,
        destino                NVARCHAR(200)     NULL,
        fecha_hora_salida      DATETIME2         NOT NULL CONSTRAINT DF_ctrl_sal_fhs DEFAULT (SYSUTCDATETIME()),
        odometro_salida        DECIMAL(12,2)     NOT NULL,
        observaciones_salida   NVARCHAR(500)     NULL,
        fecha_hora_entrada     DATETIME2         NULL,
        odometro_entrada       DECIMAL(12,2)     NULL,
        km_recorridos          AS (CASE WHEN odometro_entrada IS NOT NULL THEN odometro_entrada - odometro_salida ELSE NULL END),
        observaciones_entrada  NVARCHAR(500)     NULL,
        estado                 NVARCHAR(20)      NOT NULL CONSTRAINT DF_ctrl_sal_est DEFAULT ('ABIERTO'),
        activo                 BIT               NOT NULL CONSTRAINT DF_ctrl_sal_act DEFAULT (1),
        eliminado              BIT               NOT NULL CONSTRAINT DF_ctrl_sal_elim DEFAULT (0),
        fecha_eliminado        DATETIME2         NULL,
        creado_por             NVARCHAR(100)     NOT NULL,
        fecha_creacion         DATETIME2         NOT NULL CONSTRAINT DF_ctrl_sal_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por         NVARCHAR(100)     NULL,
        fecha_modificacion     DATETIME2         NULL,
        token_concurrencia     ROWVERSION        NOT NULL,

        CONSTRAINT PK_control_salidas           PRIMARY KEY CLUSTERED (id_control_salida),
        CONSTRAINT FK_control_salidas_empresa   FOREIGN KEY (id_empresa)   REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_control_salidas_vehiculo  FOREIGN KEY (id_vehiculo)  REFERENCES dbo.vehiculos(id_vehiculo),
        CONSTRAINT FK_control_salidas_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas(id_persona),
        CONSTRAINT FK_control_salidas_ruta      FOREIGN KEY (id_ruta)      REFERENCES dbo.rutas(id_ruta),
        CONSTRAINT CK_control_salidas_estado    CHECK (estado IN ('ABIERTO', 'CERRADO', 'ANULADO')),
        CONSTRAINT CK_control_salidas_odo       CHECK (odometro_entrada IS NULL OR odometro_entrada >= odometro_salida)
    );

    -- Unico vehiculo abierto: un vehiculo solo puede tener un viaje ABIERTO a la vez
    CREATE UNIQUE INDEX UX_control_salidas_vehiculo_abierto
    ON dbo.control_salidas(id_vehiculo)
    WHERE estado = 'ABIERTO' AND eliminado = 0;

    -- Indices de busqueda operativa y filtrado por empresa
    CREATE INDEX IX_control_salidas_empresa_estado
    ON dbo.control_salidas(id_empresa, estado, id_vehiculo);

    CREATE INDEX IX_control_salidas_empresa_salida
    ON dbo.control_salidas(id_empresa, fecha_hora_salida DESC);

    CREATE INDEX IX_control_salidas_vehiculo_fecha
    ON dbo.control_salidas(id_vehiculo, fecha_hora_salida DESC);
END;
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.control_salidas', 'U') IS NOT NULL
BEGIN
    SELECT 
        t.name AS tabla,
        i.name AS indice,
        i.type_desc,
        i.is_unique
    FROM sys.tables t
    INNER JOIN sys.indexes i ON t.object_id = i.object_id
    WHERE t.name = 'control_salidas';
END
ELSE
BEGIN
    RAISERROR('Error: la tabla dbo.control_salidas no fue creada.', 16, 1);
END;
GO

-- ------------------------------------------------------------
-- ROLLBACK (ejecutar manualmente en caso de revertir el cambio)
-- ------------------------------------------------------------
/*
IF OBJECT_ID('dbo.control_salidas', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.control_salidas;
END;
*/
