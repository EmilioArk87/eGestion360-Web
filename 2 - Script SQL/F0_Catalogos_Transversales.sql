/* ============================================================================
   Fase 0 — Sprint 1: Catálogos transversales
   eGestion360 — ERP multitenant modular

   Crea las tablas compartidas por todos los módulos:
   - clientes
   - proveedores
   - impuestos
   - productos_servicios
   - formas_pago
   - condiciones_pago + condiciones_pago_cuotas
   - tipos_cambio

   Todas las tablas son multitenant (id_empresa) con UNIQUE compuesto sobre
   (id_empresa, codigo) e índices de búsqueda. Auditoría estándar y
   token_concurrencia (rowversion) en todas.

   Idempotente: usa IF NOT EXISTS para que se pueda ejecutar varias veces.

   Compatible con SQL Server. Ejecutar después de la migración base
   20260429052009_FixFlotaFKRelationships.
   ========================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ─────────────────────────  IMPUESTOS  ──────────────────────────────────── */
IF OBJECT_ID('dbo.impuestos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.impuestos (
        id_impuesto          INT IDENTITY(1,1) NOT NULL,
        id_empresa           INT NOT NULL,
        codigo               NVARCHAR(20)  NOT NULL,
        nombre               NVARCHAR(100) NOT NULL,
        tipo                 NVARCHAR(20)  NOT NULL CONSTRAINT DF_impuestos_tipo DEFAULT ('isv'),
        tasa                 DECIMAL(9,4)  NOT NULL CONSTRAINT DF_impuestos_tasa DEFAULT (0),
        es_retencion         BIT           NOT NULL CONSTRAINT DF_impuestos_esret DEFAULT (0),
        vigente_desde        DATETIME2     NOT NULL,
        vigente_hasta        DATETIME2     NULL,
        activo               BIT           NOT NULL CONSTRAINT DF_impuestos_activo DEFAULT (1),
        eliminado            BIT           NOT NULL CONSTRAINT DF_impuestos_elim DEFAULT (0),
        fecha_eliminado      DATETIME2     NULL,
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2     NOT NULL,
        modificado_por       NVARCHAR(100) NULL,
        fecha_modificacion   DATETIME2     NULL,
        token_concurrencia   ROWVERSION    NOT NULL,
        CONSTRAINT PK_impuestos        PRIMARY KEY CLUSTERED (id_impuesto),
        CONSTRAINT FK_impuestos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );

    CREATE UNIQUE INDEX UX_impuestos_empresa_codigo ON dbo.impuestos(id_empresa, codigo);
END
GO

/* ─────────────────────────  CONDICIONES DE PAGO  ────────────────────────── */
IF OBJECT_ID('dbo.condiciones_pago', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.condiciones_pago (
        id_condicion_pago    INT IDENTITY(1,1) NOT NULL,
        id_empresa           INT NOT NULL,
        codigo               NVARCHAR(20)  NOT NULL,
        nombre               NVARCHAR(80)  NOT NULL,
        tipo                 NVARCHAR(20)  NOT NULL CONSTRAINT DF_cp_tipo DEFAULT ('contado'),
        dias_credito         INT           NOT NULL CONSTRAINT DF_cp_dias DEFAULT (0),
        numero_cuotas        INT           NOT NULL CONSTRAINT DF_cp_cuotas DEFAULT (1),
        activo               BIT           NOT NULL CONSTRAINT DF_cp_activo DEFAULT (1),
        eliminado            BIT           NOT NULL CONSTRAINT DF_cp_elim DEFAULT (0),
        fecha_eliminado      DATETIME2     NULL,
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2     NOT NULL,
        modificado_por       NVARCHAR(100) NULL,
        fecha_modificacion   DATETIME2     NULL,
        token_concurrencia   ROWVERSION    NOT NULL,
        CONSTRAINT PK_condiciones_pago        PRIMARY KEY CLUSTERED (id_condicion_pago),
        CONSTRAINT FK_condiciones_pago_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );

    CREATE UNIQUE INDEX UX_condiciones_pago_empresa_codigo ON dbo.condiciones_pago(id_empresa, codigo);
END
GO

IF OBJECT_ID('dbo.condiciones_pago_cuotas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.condiciones_pago_cuotas (
        id_cuota             INT IDENTITY(1,1) NOT NULL,
        id_condicion_pago    INT NOT NULL,
        numero_cuota         INT NOT NULL,
        dias_vencimiento     INT NOT NULL,
        porcentaje           DECIMAL(9,4) NOT NULL,
        CONSTRAINT PK_condiciones_pago_cuotas        PRIMARY KEY CLUSTERED (id_cuota),
        CONSTRAINT FK_cpc_condicion FOREIGN KEY (id_condicion_pago)
            REFERENCES dbo.condiciones_pago(id_condicion_pago) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_cpc_condicion_numero ON dbo.condiciones_pago_cuotas(id_condicion_pago, numero_cuota);
END
GO

/* ─────────────────────────  CLIENTES  ───────────────────────────────────── */
IF OBJECT_ID('dbo.clientes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.clientes (
        id_cliente               INT IDENTITY(1,1) NOT NULL,
        id_empresa               INT NOT NULL,
        codigo                   NVARCHAR(30)  NOT NULL,
        razon_social             NVARCHAR(200) NOT NULL,
        nombre_comercial         NVARCHAR(150) NULL,
        tipo                     NVARCHAR(20)  NOT NULL CONSTRAINT DF_cli_tipo DEFAULT ('natural'),
        identificador_fiscal     NVARCHAR(50)  NULL,
        email                    NVARCHAR(100) NULL,
        telefono                 NVARCHAR(30)  NULL,
        direccion                NVARCHAR(300) NULL,
        ciudad                   NVARCHAR(100) NULL,
        moneda_iso_default       NVARCHAR(3)   NOT NULL CONSTRAINT DF_cli_mon DEFAULT ('HNL'),
        id_condicion_pago_default INT          NULL,
        limite_credito           DECIMAL(18,2) NOT NULL CONSTRAINT DF_cli_lim DEFAULT (0),
        activo                   BIT           NOT NULL CONSTRAINT DF_cli_act DEFAULT (1),
        eliminado                BIT           NOT NULL CONSTRAINT DF_cli_elim DEFAULT (0),
        fecha_eliminado          DATETIME2     NULL,
        creado_por               NVARCHAR(100) NOT NULL,
        fecha_creacion           DATETIME2     NOT NULL,
        modificado_por           NVARCHAR(100) NULL,
        fecha_modificacion       DATETIME2     NULL,
        token_concurrencia       ROWVERSION    NOT NULL,
        CONSTRAINT PK_clientes         PRIMARY KEY CLUSTERED (id_cliente),
        CONSTRAINT FK_clientes_empresa  FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_clientes_cp       FOREIGN KEY (id_condicion_pago_default)
            REFERENCES dbo.condiciones_pago(id_condicion_pago)
    );

    CREATE UNIQUE INDEX UX_clientes_empresa_codigo ON dbo.clientes(id_empresa, codigo);
    CREATE INDEX IX_clientes_empresa_razon ON dbo.clientes(id_empresa, razon_social);
END
GO

/* ─────────────────────────  PROVEEDORES  ────────────────────────────────── */
IF OBJECT_ID('dbo.proveedores', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.proveedores (
        id_proveedor             INT IDENTITY(1,1) NOT NULL,
        id_empresa               INT NOT NULL,
        codigo                   NVARCHAR(30)  NOT NULL,
        razon_social             NVARCHAR(200) NOT NULL,
        nombre_comercial         NVARCHAR(150) NULL,
        tipo                     NVARCHAR(20)  NOT NULL CONSTRAINT DF_prov_tipo DEFAULT ('juridica'),
        identificador_fiscal     NVARCHAR(50)  NULL,
        email                    NVARCHAR(100) NULL,
        telefono                 NVARCHAR(30)  NULL,
        direccion                NVARCHAR(300) NULL,
        ciudad                   NVARCHAR(100) NULL,
        moneda_iso_default       NVARCHAR(3)   NOT NULL CONSTRAINT DF_prov_mon DEFAULT ('HNL'),
        id_condicion_pago_default INT          NULL,
        retencion_isr            BIT           NOT NULL CONSTRAINT DF_prov_ret DEFAULT (0),
        activo                   BIT           NOT NULL CONSTRAINT DF_prov_act DEFAULT (1),
        eliminado                BIT           NOT NULL CONSTRAINT DF_prov_elim DEFAULT (0),
        fecha_eliminado          DATETIME2     NULL,
        creado_por               NVARCHAR(100) NOT NULL,
        fecha_creacion           DATETIME2     NOT NULL,
        modificado_por           NVARCHAR(100) NULL,
        fecha_modificacion       DATETIME2     NULL,
        token_concurrencia       ROWVERSION    NOT NULL,
        CONSTRAINT PK_proveedores         PRIMARY KEY CLUSTERED (id_proveedor),
        CONSTRAINT FK_proveedores_empresa  FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_proveedores_cp       FOREIGN KEY (id_condicion_pago_default)
            REFERENCES dbo.condiciones_pago(id_condicion_pago)
    );

    CREATE UNIQUE INDEX UX_proveedores_empresa_codigo ON dbo.proveedores(id_empresa, codigo);
    CREATE INDEX IX_proveedores_empresa_razon ON dbo.proveedores(id_empresa, razon_social);
END
GO

/* ─────────────────────────  PRODUCTOS Y SERVICIOS  ──────────────────────── */
IF OBJECT_ID('dbo.productos_servicios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.productos_servicios (
        id_producto          INT IDENTITY(1,1) NOT NULL,
        id_empresa           INT NOT NULL,
        codigo               NVARCHAR(30)  NOT NULL,
        descripcion          NVARCHAR(200) NOT NULL,
        tipo                 NVARCHAR(20)  NOT NULL CONSTRAINT DF_ps_tipo DEFAULT ('producto'),
        unidad_medida        NVARCHAR(20)  NULL,
        precio_default       DECIMAL(18,2) NOT NULL CONSTRAINT DF_ps_precio DEFAULT (0),
        costo_default        DECIMAL(18,2) NOT NULL CONSTRAINT DF_ps_costo DEFAULT (0),
        id_impuesto_default  INT           NULL,
        lleva_inventario     BIT           NOT NULL CONSTRAINT DF_ps_inv DEFAULT (0),
        activo               BIT           NOT NULL CONSTRAINT DF_ps_act DEFAULT (1),
        eliminado            BIT           NOT NULL CONSTRAINT DF_ps_elim DEFAULT (0),
        fecha_eliminado      DATETIME2     NULL,
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2     NOT NULL,
        modificado_por       NVARCHAR(100) NULL,
        fecha_modificacion   DATETIME2     NULL,
        token_concurrencia   ROWVERSION    NOT NULL,
        CONSTRAINT PK_productos_servicios         PRIMARY KEY CLUSTERED (id_producto),
        CONSTRAINT FK_ps_empresa  FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ps_impuesto FOREIGN KEY (id_impuesto_default) REFERENCES dbo.impuestos(id_impuesto)
    );

    CREATE UNIQUE INDEX UX_ps_empresa_codigo ON dbo.productos_servicios(id_empresa, codigo);
    CREATE INDEX IX_ps_empresa_desc ON dbo.productos_servicios(id_empresa, descripcion);
END
GO

/* ─────────────────────────  FORMAS DE PAGO  ─────────────────────────────── */
IF OBJECT_ID('dbo.formas_pago', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.formas_pago (
        id_forma_pago        INT IDENTITY(1,1) NOT NULL,
        id_empresa           INT NOT NULL,
        codigo               NVARCHAR(20)  NOT NULL,
        nombre               NVARCHAR(80)  NOT NULL,
        tipo                 NVARCHAR(20)  NOT NULL CONSTRAINT DF_fp_tipo DEFAULT ('efectivo'),
        afecta_caja          BIT           NOT NULL CONSTRAINT DF_fp_caja DEFAULT (1),
        afecta_banco         BIT           NOT NULL CONSTRAINT DF_fp_banco DEFAULT (0),
        activo               BIT           NOT NULL CONSTRAINT DF_fp_act DEFAULT (1),
        eliminado            BIT           NOT NULL CONSTRAINT DF_fp_elim DEFAULT (0),
        fecha_eliminado      DATETIME2     NULL,
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2     NOT NULL,
        modificado_por       NVARCHAR(100) NULL,
        fecha_modificacion   DATETIME2     NULL,
        token_concurrencia   ROWVERSION    NOT NULL,
        CONSTRAINT PK_formas_pago        PRIMARY KEY CLUSTERED (id_forma_pago),
        CONSTRAINT FK_formas_pago_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );

    CREATE UNIQUE INDEX UX_formas_pago_empresa_codigo ON dbo.formas_pago(id_empresa, codigo);
END
GO

/* ─────────────────────────  TIPOS DE CAMBIO  ────────────────────────────── */
IF OBJECT_ID('dbo.tipos_cambio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tipos_cambio (
        id_tipo_cambio       INT IDENTITY(1,1) NOT NULL,
        id_empresa           INT NOT NULL,
        moneda_origen        NVARCHAR(3)   NOT NULL,
        moneda_destino       NVARCHAR(3)   NOT NULL,
        fecha                DATETIME2     NOT NULL,
        tasa                 DECIMAL(18,8) NOT NULL,
        fuente               NVARCHAR(20)  NOT NULL CONSTRAINT DF_tc_fuente DEFAULT ('manual'),
        creado_por           NVARCHAR(100) NOT NULL,
        fecha_creacion       DATETIME2     NOT NULL,
        CONSTRAINT PK_tipos_cambio        PRIMARY KEY CLUSTERED (id_tipo_cambio),
        CONSTRAINT FK_tipos_cambio_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );

    CREATE UNIQUE INDEX UX_tc_empresa_par_fecha ON dbo.tipos_cambio(id_empresa, moneda_origen, moneda_destino, fecha);
END
GO

/* ─────────────────────────  MODULOS NUEVOS  ─────────────────────────────── */
-- modulos.id_modulo es IDENTITY. Activamos IDENTITY_INSERT para alinear con el
-- seed de EF Core (HasData) que asigna ids fijos 5..8.
SET IDENTITY_INSERT dbo.modulos ON;

IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'catalogos')
    INSERT INTO dbo.modulos (id_modulo, codigo, nombre, descripcion, icono, orden, activo)
    VALUES (5, 'catalogos', 'Catálogos', 'Clientes, proveedores, productos, impuestos, formas y condiciones de pago', 'fa-book', 5, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'facturacion')
    INSERT INTO dbo.modulos (id_modulo, codigo, nombre, descripcion, icono, orden, activo)
    VALUES (6, 'facturacion', 'Facturación', 'Emisión de facturas contado/crédito, notas de crédito/débito, CxC', 'fa-file-invoice-dollar', 6, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'bancos')
    INSERT INTO dbo.modulos (id_modulo, codigo, nombre, descripcion, icono, orden, activo)
    VALUES (7, 'bancos', 'Bancos', 'Cuentas bancarias, depósitos, cheques y conciliación', 'fa-university', 7, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'contabilidad')
    INSERT INTO dbo.modulos (id_modulo, codigo, nombre, descripcion, icono, orden, activo)
    VALUES (8, 'contabilidad', 'Contabilidad', 'Plan de cuentas, asientos, libros y estados financieros (opcional por empresa)', 'fa-calculator', 8, 1);

SET IDENTITY_INSERT dbo.modulos OFF;
GO

PRINT '✓ Fase 0 — Sprint 1: catálogos transversales creados';
GO
