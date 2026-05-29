/* ============================================================================
   Fase 1 — Sprint 4: Pagos, aplicaciones y notas (crédito / débito)
   eGestion360 — ERP multitenant modular

   Tablas:
   - pagos                : cabecera del recibo. Tiene saldo_favor (anticipo).
   - pago_aplicaciones    : N:M entre pago y facturas; cada fila aplica un monto.
   - notas                : NC y ND con tipo discriminador (afectan saldo de factura).

   Ajustes a tablas existentes:
   - factura_secuencias.tipo_documento: ampliar CHECK para incluir
     'recibo', 'nota_credito', 'nota_debito'.

   Eventos publicados por los servicios:
   - pago.recibido / pago.aplicado / pago.anulado
   - nota_credito.emitida / nota_debito.emitida

   Idempotente. Ejecutar después de F1_Facturacion.sql.
   ========================================================================== */

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

/* ── 1. Ampliar CHECK de factura_secuencias.tipo_documento ──────────────── */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_factura_secuencias_tipo')
    ALTER TABLE dbo.factura_secuencias DROP CONSTRAINT CK_factura_secuencias_tipo;
GO

ALTER TABLE dbo.factura_secuencias
    ADD CONSTRAINT CK_factura_secuencias_tipo
    CHECK (tipo_documento IN ('factura','recibo','nota_credito','nota_debito'));
GO

/* ── 2. PAGOS ───────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.pagos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.pagos (
        id_pago             INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        estado              NVARCHAR(30)   NOT NULL CONSTRAINT DF_pag_estado DEFAULT ('recibido'),
        serie               NVARCHAR(20)   NOT NULL CONSTRAINT DF_pag_serie  DEFAULT ('RC-01'),
        numero              INT            NULL,
        id_cliente          INT            NOT NULL,
        fecha               DATETIME2      NOT NULL,
        id_forma_pago       INT            NOT NULL,
        monto               DECIMAL(18,2)  NOT NULL,
        saldo_favor         DECIMAL(18,2)  NOT NULL CONSTRAINT DF_pag_sf DEFAULT (0),
        moneda              NVARCHAR(3)    NOT NULL CONSTRAINT DF_pag_mon DEFAULT ('HNL'),
        tipo_cambio         DECIMAL(18,8)  NOT NULL CONSTRAINT DF_pag_tc DEFAULT (1),
        referencia          NVARCHAR(100)  NULL,
        observaciones       NVARCHAR(500)  NULL,
        motivo_anulacion    NVARCHAR(500)  NULL,
        fecha_anulacion     DATETIME2      NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_pag_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL,
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_pagos         PRIMARY KEY CLUSTERED (id_pago),
        CONSTRAINT FK_pagos_empresa FOREIGN KEY (id_empresa)    REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_pagos_cliente FOREIGN KEY (id_cliente)    REFERENCES dbo.clientes(id_cliente),
        CONSTRAINT FK_pagos_fp      FOREIGN KEY (id_forma_pago) REFERENCES dbo.formas_pago(id_forma_pago),
        CONSTRAINT CK_pagos_estado  CHECK (estado IN ('recibido','parcialmente_aplicado','aplicado','anulado'))
    );

    CREATE UNIQUE INDEX UX_pagos_empresa_serie_numero
        ON dbo.pagos(id_empresa, serie, numero) WHERE numero IS NOT NULL;
    CREATE INDEX IX_pagos_empresa_cliente_estado ON dbo.pagos(id_empresa, id_cliente, estado);
    CREATE INDEX IX_pagos_empresa_fecha          ON dbo.pagos(id_empresa, fecha DESC);
END
GO

/* ── 3. PAGO_APLICACIONES ──────────────────────────────────────────────── */
IF OBJECT_ID('dbo.pago_aplicaciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.pago_aplicaciones (
        id_aplicacion       INT IDENTITY(1,1) NOT NULL,
        id_pago             INT            NOT NULL,
        id_factura          INT            NOT NULL,
        monto               DECIMAL(18,2)  NOT NULL,
        fecha_aplicacion    DATETIME2      NOT NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        CONSTRAINT PK_pago_aplicaciones        PRIMARY KEY CLUSTERED (id_aplicacion),
        CONSTRAINT FK_pa_pago    FOREIGN KEY (id_pago)    REFERENCES dbo.pagos(id_pago) ON DELETE CASCADE,
        CONSTRAINT FK_pa_factura FOREIGN KEY (id_factura) REFERENCES dbo.facturas(id_factura)
    );

    CREATE INDEX IX_pago_aplicaciones_factura ON dbo.pago_aplicaciones(id_factura);
END
GO

/* ── 4. NOTAS ──────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.notas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notas (
        id_nota             INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        tipo                NVARCHAR(20)   NOT NULL CONSTRAINT DF_nota_tipo  DEFAULT ('credito'),
        estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_nota_est   DEFAULT ('emitida'),
        serie               NVARCHAR(20)   NOT NULL CONSTRAINT DF_nota_serie DEFAULT ('NC-01'),
        numero              INT            NULL,
        id_cliente          INT            NOT NULL,
        id_factura_origen   INT            NOT NULL,
        fecha_emision       DATETIME2      NOT NULL,
        monto               DECIMAL(18,2)  NOT NULL,
        moneda              NVARCHAR(3)    NOT NULL CONSTRAINT DF_nota_mon DEFAULT ('HNL'),
        motivo              NVARCHAR(500)  NOT NULL,
        observaciones       NVARCHAR(500)  NULL,
        fecha_anulacion     DATETIME2      NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_nota_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL,
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_notas         PRIMARY KEY CLUSTERED (id_nota),
        CONSTRAINT FK_notas_empresa  FOREIGN KEY (id_empresa)         REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_notas_cliente  FOREIGN KEY (id_cliente)         REFERENCES dbo.clientes(id_cliente),
        CONSTRAINT FK_notas_factura  FOREIGN KEY (id_factura_origen)  REFERENCES dbo.facturas(id_factura),
        CONSTRAINT CK_notas_tipo     CHECK (tipo   IN ('credito','debito')),
        CONSTRAINT CK_notas_estado   CHECK (estado IN ('emitida','anulada'))
    );

    CREATE UNIQUE INDEX UX_notas_empresa_tipo_serie_numero
        ON dbo.notas(id_empresa, tipo, serie, numero) WHERE numero IS NOT NULL;
    CREATE INDEX IX_notas_empresa_factura ON dbo.notas(id_empresa, id_factura_origen);
END
GO

PRINT '✓ Fase 1 — Sprint 4: pagos + aplicaciones + notas creadas';
GO
