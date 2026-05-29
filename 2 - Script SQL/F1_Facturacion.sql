/* ============================================================================
   Fase 1 — Sprint 3: Módulo Facturación
   eGestion360 — ERP multitenant modular

   Tablas:
   - factura_secuencias   : correlativos por (empresa, tipo_documento, serie)
                            con soporte CAI Honduras (rango + fecha límite).
   - facturas             : cabecera con totales calculados server-side.
   - factura_detalle      : líneas con snapshot de tasa de impuesto.

   El servicio FacturacionService.EmitirAsync:
   - reserva número con UPDATE atómico sobre factura_secuencias
   - publica evento factura.emitida.{contado|credito} al outbox
   - commitea todo en la misma transacción

   Idempotente. Ejecutar después de F0_Catalogos_Transversales.sql y F0_Outbox.sql.
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

/* ─────────────────────────  FACTURA_SECUENCIAS  ──────────────────────────── */
IF OBJECT_ID('dbo.factura_secuencias', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.factura_secuencias (
        id_secuencia          INT IDENTITY(1,1) NOT NULL,
        id_empresa            INT            NOT NULL,
        tipo_documento        NVARCHAR(20)   NOT NULL CONSTRAINT DF_secfac_tipo DEFAULT ('factura'),
        serie                 NVARCHAR(20)   NOT NULL CONSTRAINT DF_secfac_serie DEFAULT ('F-01'),
        proximo_numero        INT            NOT NULL CONSTRAINT DF_secfac_prox DEFAULT (1),
        rango_inicial         INT            NULL,
        rango_final           INT            NULL,
        cai_numero            NVARCHAR(50)   NULL,
        fecha_limite_emision  DATETIME2      NULL,
        activo                BIT            NOT NULL CONSTRAINT DF_secfac_act DEFAULT (1),
        creado_por            NVARCHAR(100)  NOT NULL,
        fecha_creacion        DATETIME2      NOT NULL,
        CONSTRAINT PK_factura_secuencias         PRIMARY KEY CLUSTERED (id_secuencia),
        CONSTRAINT FK_factura_secuencias_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_factura_secuencias_tipo    CHECK (tipo_documento IN ('factura','nota_credito','nota_debito'))
    );

    CREATE UNIQUE INDEX UX_factura_secuencias_tenant_tipo_serie
        ON dbo.factura_secuencias(id_empresa, tipo_documento, serie);
END
GO

/* ─────────────────────────  FACTURAS  ───────────────────────────────────── */
IF OBJECT_ID('dbo.facturas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.facturas (
        id_factura          INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,

        estado              NVARCHAR(30)   NOT NULL CONSTRAINT DF_fac_estado DEFAULT ('borrador'),
        tipo_venta          NVARCHAR(20)   NOT NULL CONSTRAINT DF_fac_tipov  DEFAULT ('contado'),

        serie               NVARCHAR(20)   NOT NULL CONSTRAINT DF_fac_serie  DEFAULT ('F-01'),
        numero              INT            NULL,
        cai_numero          NVARCHAR(50)   NULL,

        id_cliente          INT            NOT NULL,
        fecha_emision       DATETIME2      NOT NULL,
        fecha_vencimiento   DATETIME2      NULL,

        id_forma_pago       INT            NULL,
        id_condicion_pago   INT            NULL,

        moneda              NVARCHAR(3)    NOT NULL CONSTRAINT DF_fac_mon  DEFAULT ('HNL'),
        tipo_cambio         DECIMAL(18,8)  NOT NULL CONSTRAINT DF_fac_tc   DEFAULT (1),

        subtotal            DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_sub  DEFAULT (0),
        descuento_global    DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_dg   DEFAULT (0),
        base_imponible      DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_bi   DEFAULT (0),
        isv_15              DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_i15  DEFAULT (0),
        isv_18              DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_i18  DEFAULT (0),
        exento              DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_ex   DEFAULT (0),
        retencion           DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_ret  DEFAULT (0),
        total               DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_tot  DEFAULT (0),
        saldo_pendiente     DECIMAL(18,2)  NOT NULL CONSTRAINT DF_fac_sp   DEFAULT (0),

        observaciones       NVARCHAR(500)  NULL,
        motivo_anulacion    NVARCHAR(500)  NULL,
        fecha_anulacion     DATETIME2      NULL,

        eliminado           BIT            NOT NULL CONSTRAINT DF_fac_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL,
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        token_concurrencia  ROWVERSION     NOT NULL,

        CONSTRAINT PK_facturas         PRIMARY KEY CLUSTERED (id_factura),
        CONSTRAINT FK_facturas_empresa FOREIGN KEY (id_empresa)        REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_facturas_cliente FOREIGN KEY (id_cliente)        REFERENCES dbo.clientes(id_cliente),
        CONSTRAINT FK_facturas_fp      FOREIGN KEY (id_forma_pago)     REFERENCES dbo.formas_pago(id_forma_pago),
        CONSTRAINT FK_facturas_cp      FOREIGN KEY (id_condicion_pago) REFERENCES dbo.condiciones_pago(id_condicion_pago),
        CONSTRAINT CK_facturas_estado  CHECK (estado IN ('borrador','emitida','parcialmente_pagada','pagada','anulada')),
        CONSTRAINT CK_facturas_tipov   CHECK (tipo_venta IN ('contado','credito'))
    );

    -- Único por número emitido (los borradores no tienen numero, por eso el filtro)
    CREATE UNIQUE INDEX UX_facturas_empresa_serie_numero
        ON dbo.facturas(id_empresa, serie, numero)
        WHERE numero IS NOT NULL;

    CREATE INDEX IX_facturas_empresa_cliente_estado
        ON dbo.facturas(id_empresa, id_cliente, estado);

    CREATE INDEX IX_facturas_empresa_fecha
        ON dbo.facturas(id_empresa, fecha_emision DESC);
END
GO

/* ─────────────────────────  FACTURA_DETALLE  ────────────────────────────── */
IF OBJECT_ID('dbo.factura_detalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.factura_detalle (
        id_factura_detalle  INT IDENTITY(1,1) NOT NULL,
        id_factura          INT            NOT NULL,
        numero_linea        INT            NOT NULL,

        id_producto         INT            NULL,
        descripcion         NVARCHAR(300)  NOT NULL,
        cantidad            DECIMAL(18,4)  NOT NULL,
        precio_unitario     DECIMAL(18,4)  NOT NULL,
        descuento_porc      DECIMAL(9,4)   NOT NULL CONSTRAINT DF_fd_desc DEFAULT (0),

        id_impuesto         INT            NULL,
        impuesto_tasa       DECIMAL(9,4)   NOT NULL CONSTRAINT DF_fd_tasa DEFAULT (0),

        base_imponible      DECIMAL(18,2)  NOT NULL,
        monto_impuesto      DECIMAL(18,2)  NOT NULL,
        total_linea         DECIMAL(18,2)  NOT NULL,

        CONSTRAINT PK_factura_detalle         PRIMARY KEY CLUSTERED (id_factura_detalle),
        CONSTRAINT FK_fd_factura  FOREIGN KEY (id_factura)  REFERENCES dbo.facturas(id_factura) ON DELETE CASCADE,
        CONSTRAINT FK_fd_producto FOREIGN KEY (id_producto) REFERENCES dbo.productos_servicios(id_producto),
        CONSTRAINT FK_fd_impuesto FOREIGN KEY (id_impuesto) REFERENCES dbo.impuestos(id_impuesto)
    );

    CREATE UNIQUE INDEX UX_factura_detalle_factura_linea
        ON dbo.factura_detalle(id_factura, numero_linea);
END
GO

PRINT '✓ Fase 1 — Sprint 3: facturas + detalle + secuencias creadas';
GO
