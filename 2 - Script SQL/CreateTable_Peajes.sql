-- =============================================================================
-- SCRIPT: Creación de tabla peajes
-- Proyecto: eGestion360
-- Fecha: 2026-06-28
-- Descripción: Registro de peajes pagados en los recorridos de cada vehículo
--              (gasto operativo por vehículo / fecha / ruta).
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'peajes')
BEGIN
    CREATE TABLE peajes (
        id_peaje            int IDENTITY(1,1) NOT NULL,
        id_empresa          int NOT NULL,
        id_vehiculo         int NOT NULL,
        fecha               date NOT NULL,
        hora                time(0) NULL,
        id_ruta             int NULL,
        id_conductor        int NULL,
        nombre_caseta       nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        monto               decimal(18, 2) NOT NULL,
        moneda              varchar(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL DEFAULT 'HNL',
        km_odometro         decimal(18, 2) NULL,
        no_comprobante      nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        observaciones       nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        eliminado           bit DEFAULT 0 NOT NULL,
        fecha_eliminado     datetime2(3) NULL,
        creado_por          nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        fecha_creacion      datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
        modificado_por      nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        fecha_modificacion  datetime2(3) NULL,
        token_concurrencia  timestamp NOT NULL,
        CONSTRAINT PK_peajes PRIMARY KEY (id_peaje),
        CONSTRAINT FK_peajes_empresa   FOREIGN KEY (id_empresa)   REFERENCES empresas(id_empresa),
        CONSTRAINT FK_peajes_vehiculo  FOREIGN KEY (id_vehiculo)  REFERENCES vehiculos(id_vehiculo),
        CONSTRAINT FK_peajes_ruta      FOREIGN KEY (id_ruta)      REFERENCES rutas(id_ruta),
        CONSTRAINT FK_peajes_conductor FOREIGN KEY (id_conductor) REFERENCES personas(id_persona)
    );

    CREATE NONCLUSTERED INDEX IX_peajes_empresa_fecha ON peajes (id_empresa ASC, fecha ASC);
    CREATE NONCLUSTERED INDEX IX_peajes_vehiculo ON peajes (id_vehiculo ASC);

    PRINT 'Tabla peajes creada.';
END
ELSE
    PRINT 'La tabla peajes ya existe.';
GO
