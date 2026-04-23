-- =============================================================================
-- Módulo: Control de Gastos de Vehículos y KPIs
-- Grupo 6: Seguimiento de boletines SEN (Secretaría de Energía de Honduras)
-- -----------------------------------------------------------------------------
-- El SEN publica precios de combustibles semanalmente (lunes) como imágenes.
-- Esta tabla guarda la referencia a cada boletín detectado y marca si ya
-- fue procesado (es decir, si el admin ya ingresó los precios en la app).
-- =============================================================================

IF OBJECT_ID(N'dbo.sen_boletines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sen_boletines (
        id_sen_boletin      INT            IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        fecha_vigencia      DATE           NOT NULL,        -- lunes al que aplica el precio
        url_imagen          NVARCHAR(500)  NOT NULL,        -- URL directo de la imagen en sen.hn
        fecha_publicacion   DATETIME2(3)   NULL,            -- fecha_publicacion en WordPress
        wp_media_id         INT            NULL,            -- id del media post en WordPress
        procesado           BIT            NOT NULL CONSTRAINT DF_sen_boletines_procesado     DEFAULT (0),
        fecha_procesado     DATETIME2(3)   NULL,
        procesado_por       NVARCHAR(100)  NULL,
        observaciones       NVARCHAR(500)  NULL,
        fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_sen_boletines_fecha_creacion DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_sen_boletines PRIMARY KEY CLUSTERED (id_sen_boletin),
        CONSTRAINT FK_sen_boletines_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa)
    );

    CREATE UNIQUE INDEX UX_sen_boletines_empresa_fecha
        ON dbo.sen_boletines (id_empresa, fecha_vigencia);

    CREATE INDEX IX_sen_boletines_pendientes
        ON dbo.sen_boletines (id_empresa, procesado, fecha_vigencia DESC)
        WHERE procesado = 0;
END;
GO

-- Columna url_referencia en precios_combustible para trazar el boletín de origen
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.precios_combustible')
      AND name = N'id_sen_boletin'
)
BEGIN
    ALTER TABLE dbo.precios_combustible
    ADD id_sen_boletin INT NULL
        CONSTRAINT FK_precios_combustible_boletin FOREIGN KEY REFERENCES dbo.sen_boletines (id_sen_boletin);
END;
GO
