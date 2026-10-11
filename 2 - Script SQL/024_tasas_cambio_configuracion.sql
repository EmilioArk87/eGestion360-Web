-- ============================================================
-- Script   : 024_tasas_cambio_configuracion.sql
-- Proposito: Crea dbo.tasas_cambio_configuracion: UNA fila con las opciones del job de tasas de cambio
--            que el administrador del sistema edita desde la pantalla (Catalogos > Tipos de cambio >
--            Configuracion): el interruptor del job automatico, las monedas, las horas de los intentos,
--            los reintentos, las reglas de validacion y los destinatarios de las alertas.
--            Una columna NULL significa "usar el valor de appsettings.json" (TasasCambio:...), asi que la
--            tabla vacia deja todo como estaba. NO guarda secretos: la clave del API del BCH y el token
--            del disparador externo siguen solo en variables de entorno / user-secrets.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-11
-- BD       : eBD_SPD
-- Requiere : 020_tasas_cambio_v2.sql aplicado (no depende de sus tablas, pero es el mismo modulo).
-- Rollback : ver la seccion ROLLBACK al final (elimina la tabla; vuelve a regir appsettings.json).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada): aborta si la tabla ya existe con otra forma (columnas distintas).
--   2. CAMBIO (una transaccion): crea la tabla, su CHECK de fila unica y los CHECK de rango.
--   3. POSTCHECK: comprueba columna por columna que quedo como se espera.
--   No inserta ninguna fila: la primera se crea al guardar la configuracion desde la pantalla.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion. Idempotente: si la tabla ya existe completa, no hace nada.
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK + CAMBIO + POSTCHECK
-- ------------------------------------------------------------
DECLARE @esperadas TABLE (nombre SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @esperadas (nombre) VALUES
    (N'id_configuracion'), (N'habilitado'), (N'monedas'), (N'primer_intento'), (N'ultimo_intento'),
    (N'barrido_matutino'), (N'dias_puesta_al_dia'), (N'max_intentos_por_fecha'),
    (N'variacion_diaria_max_pct'), (N'diferencia_compra_venta_max_pct'),
    (N'rango_usd_min'), (N'rango_usd_max'), (N'rango_eur_min'), (N'rango_eur_max'),
    (N'destinatarios_alerta'), (N'actualizado_por'), (N'actualizado_utc');

IF OBJECT_ID(N'dbo.tasas_cambio_configuracion', N'U') IS NOT NULL
BEGIN
    -- La tabla ya existe: debe tener exactamente las columnas esperadas; si no, se aborta sin tocar nada.
    IF EXISTS (SELECT 1 FROM @esperadas e WHERE NOT EXISTS (
                   SELECT 1 FROM sys.columns c
                   WHERE c.object_id = OBJECT_ID(N'dbo.tasas_cambio_configuracion') AND c.name = e.nombre))
       OR EXISTS (SELECT 1 FROM sys.columns c
                  WHERE c.object_id = OBJECT_ID(N'dbo.tasas_cambio_configuracion')
                    AND c.name NOT IN (SELECT nombre FROM @esperadas))
    BEGIN
        RAISERROR(N'024 ABORTADO: dbo.tasas_cambio_configuracion ya existe con columnas distintas a las esperadas. No se cambio nada.', 16, 1);
        SET NOEXEC ON;
    END
    ELSE
        PRINT N'024: dbo.tasas_cambio_configuracion ya existe completa. No se hizo nada.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.tasas_cambio_configuracion (
        id_configuracion                 TINYINT        NOT NULL CONSTRAINT DF_tcc_id DEFAULT (1),
        habilitado                       BIT            NOT NULL CONSTRAINT DF_tcc_habilitado DEFAULT (0),
        monedas                          VARCHAR(100)   NULL,      -- codigos separados por coma: USD,EUR
        primer_intento                   CHAR(5)        NULL,      -- HH:mm, hora de Honduras
        ultimo_intento                   CHAR(5)        NULL,
        barrido_matutino                 CHAR(5)        NULL,
        dias_puesta_al_dia               TINYINT        NULL,
        max_intentos_por_fecha           TINYINT        NULL,
        variacion_diaria_max_pct         DECIMAL(5,2)   NULL,
        diferencia_compra_venta_max_pct  DECIMAL(5,2)   NULL,
        rango_usd_min                    DECIMAL(12,4)  NULL,
        rango_usd_max                    DECIMAL(12,4)  NULL,
        rango_eur_min                    DECIMAL(12,4)  NULL,
        rango_eur_max                    DECIMAL(12,4)  NULL,
        destinatarios_alerta             NVARCHAR(1000) NULL,      -- correos separados por coma
        actualizado_por                  NVARCHAR(100)  NOT NULL,
        actualizado_utc                  DATETIME2(3)   NOT NULL CONSTRAINT DF_tcc_actualizado_utc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tasas_cambio_configuracion PRIMARY KEY CLUSTERED (id_configuracion),
        CONSTRAINT CK_tcc_fila_unica CHECK (id_configuracion = 1),
        CONSTRAINT CK_tcc_dias CHECK (dias_puesta_al_dia IS NULL OR dias_puesta_al_dia BETWEEN 1 AND 30),
        CONSTRAINT CK_tcc_intentos CHECK (max_intentos_por_fecha IS NULL OR max_intentos_por_fecha BETWEEN 1 AND 20),
        CONSTRAINT CK_tcc_variacion CHECK (variacion_diaria_max_pct IS NULL OR variacion_diaria_max_pct > 0),
        CONSTRAINT CK_tcc_diferencia CHECK (diferencia_compra_venta_max_pct IS NULL OR diferencia_compra_venta_max_pct > 0),
        CONSTRAINT CK_tcc_rango_usd CHECK (rango_usd_min IS NULL OR rango_usd_max IS NULL OR (rango_usd_min > 0 AND rango_usd_min < rango_usd_max)),
        CONSTRAINT CK_tcc_rango_eur CHECK (rango_eur_min IS NULL OR rango_eur_max IS NULL OR (rango_eur_min > 0 AND rango_eur_min < rango_eur_max))
    );

    -- POSTCHECK dentro de la transaccion: columna por columna.
    IF EXISTS (SELECT 1 FROM @esperadas e WHERE NOT EXISTS (
                   SELECT 1 FROM sys.columns c
                   WHERE c.object_id = OBJECT_ID(N'dbo.tasas_cambio_configuracion') AND c.name = e.nombre))
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tasas_cambio_configuracion')) <> (SELECT COUNT(*) FROM @esperadas)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'024 POSTCHECK FALLO: la tabla no quedo como se esperaba. Se revirtio.', 16, 1);
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'024: creada dbo.tasas_cambio_configuracion (sin filas).';
    END
END
GO
SET NOEXEC OFF;
GO

-- ------------------------------------------------------------
-- ROLLBACK (solo para revertir; ejecutar a mano con /alerta-bd)
-- Elimina la tabla: el job vuelve a usar solo appsettings.json / variables de entorno.
-- ------------------------------------------------------------
/*
IF OBJECT_ID(N'dbo.tasas_cambio_configuracion', N'U') IS NOT NULL
    DROP TABLE dbo.tasas_cambio_configuracion;
*/
