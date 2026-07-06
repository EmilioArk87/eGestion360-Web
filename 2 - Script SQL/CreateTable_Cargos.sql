-- =============================================================================
-- SCRIPT: Creación de tabla cargos y carga de datos por defecto
-- Proyecto: eGestion360
-- Fecha: 2026-06-12
-- Descripción: Catálogo de cargos del personal de flota (conductor, cobrador, etc.)
-- =============================================================================

-- Creación de tabla
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cargos')
BEGIN
    CREATE TABLE cargos (
        id_cargo            int IDENTITY(1,1) NOT NULL,
        id_empresa          int NOT NULL,
        codigo              varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        nombre              nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        descripcion         nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        activo              bit DEFAULT 1 NOT NULL,
        eliminado           bit DEFAULT 0 NOT NULL,
        fecha_eliminado     datetime2(3) NULL,
        creado_por          nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        fecha_creacion      datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
        modificado_por      nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        fecha_modificacion  datetime2(3) NULL,
        token_concurrencia  timestamp NOT NULL,
        CONSTRAINT PK_cargos PRIMARY KEY (id_cargo),
        CONSTRAINT FK_cargos_empresa FOREIGN KEY (id_empresa) REFERENCES empresas(id_empresa)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_cargos_empresa_codigo ON cargos (id_empresa ASC, codigo ASC)
        WHERE (eliminado = 0);

    PRINT 'Tabla cargos creada.';
END
ELSE
    PRINT 'La tabla cargos ya existe.';
GO

-- Carga de datos por defecto para cada empresa (solo si aún no tiene cargos)
INSERT INTO cargos (id_empresa, codigo, nombre, descripcion, activo, creado_por, fecha_creacion)
SELECT e.id_empresa, c.codigo, c.nombre, c.descripcion, 1, 'sistema', SYSUTCDATETIME()
FROM empresas e
CROSS JOIN (
    VALUES
        ('CONDUCTOR',  N'Conductor',  N'Encargado de conducir el vehículo'),
        ('COBRADOR',   N'Cobrador',   N'Encargado del cobro de pasajes'),
        ('MECANICO',   N'Mecánico',   N'Encargado del mantenimiento de vehículos'),
        ('SUPERVISOR', N'Supervisor', N'Supervisión de operaciones'),
        ('OTRO',       N'Otro',       N'Otro cargo no clasificado')
) AS c(codigo, nombre, descripcion)
WHERE NOT EXISTS (
    SELECT 1 FROM cargos x WHERE x.id_empresa = e.id_empresa AND x.codigo = c.codigo
);

PRINT 'Cargos por defecto insertados: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
GO
