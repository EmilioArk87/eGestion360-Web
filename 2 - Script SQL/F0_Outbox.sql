/* ============================================================================
   Fase 0 — Sprint 2: Outbox / Bus de eventos
   eGestion360 — ERP multitenant modular

   Crea la tabla domain_events que actúa como outbox transaccional:
   - El INSERT ocurre en la MISMA transacción que la operación de negocio.
   - Un BackgroundService (OutboxDispatcher) reclama lotes con UPDATE atómico
     (locked_until + worker_id) y los entrega a los IDomainEventHandler
     registrados en DI.
   - Idempotencia: responsabilidad del handler (usar id_evento como clave única).
   - Reintentos con backoff exponencial; al pasar max_intentos → status='dead'.

   Idempotente. Ejecutar después de F0_Catalogos_Transversales.sql.
   ========================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.domain_events', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.domain_events (
        id_evento            BIGINT IDENTITY(1,1) NOT NULL,
        id_empresa           INT            NOT NULL,
        event_type           NVARCHAR(100)  NOT NULL,
        aggregate_type       NVARCHAR(50)   NOT NULL,
        aggregate_id         NVARCHAR(50)   NOT NULL,
        event_version        INT            NOT NULL CONSTRAINT DF_de_ver  DEFAULT (1),
        payload              NVARCHAR(MAX)  NOT NULL,
        occurred_at          DATETIME2      NOT NULL,
        status               NVARCHAR(20)   NOT NULL CONSTRAINT DF_de_st   DEFAULT ('pending'),
        intentos             INT            NOT NULL CONSTRAINT DF_de_int  DEFAULT (0),
        max_intentos         INT            NOT NULL CONSTRAINT DF_de_max  DEFAULT (10),
        locked_until         DATETIME2      NULL,
        worker_id            NVARCHAR(100)  NULL,
        proximo_intento_en   DATETIME2      NULL,
        ultimo_error         NVARCHAR(MAX)  NULL,
        processed_at         DATETIME2      NULL,
        creado_por           NVARCHAR(100)  NOT NULL,
        fecha_creacion       DATETIME2      NOT NULL,
        CONSTRAINT PK_domain_events         PRIMARY KEY CLUSTERED (id_evento),
        CONSTRAINT FK_domain_events_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_domain_events_status  CHECK (status IN ('pending','processing','processed','failed','dead'))
    );

    -- Índice del worker: trae elegibles ordenados por id (orden de ocurrencia)
    CREATE INDEX IX_domain_events_status_proximo
        ON dbo.domain_events(status, proximo_intento_en)
        INCLUDE (id_empresa, event_type, intentos, max_intentos, locked_until);

    -- Idempotencia / trazabilidad por agregado (para que un handler verifique
    -- si ya procesó eventos para una factura/pago específico).
    CREATE INDEX IX_domain_events_aggregate
        ON dbo.domain_events(id_empresa, aggregate_type, aggregate_id);

    -- Reporte por tenant + tipo
    CREATE INDEX IX_domain_events_tenant_type_status
        ON dbo.domain_events(id_empresa, event_type, status);
END
GO

PRINT '✓ Fase 0 — Sprint 2: outbox domain_events creado';
GO
