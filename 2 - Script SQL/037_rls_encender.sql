-- ============================================================
-- Script   : 037_rls_encender.sql
-- Proposito: Paso F1.4a del plan de la Fase 1 (ADR-015): ENCIENDE la politica seg.pol_tenant. Desde este momento la base entrega a cada
--            conexion solo las filas de su tenant, aunque la aplicacion consulte sin filtro, y rechaza escribir en otro tenant.
--            Una conexion SIN contexto (SESSION_CONTEXT 'id_tenant' nulo) no ve ninguna fila de tenant.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 036 aplicado y la VERSION DE LA APLICACION que fija el contexto en cada conexion (F1.4b y F1.4c) ya publicada. Sin ella,
--            todas las pantallas quedarian vacias. Ensayar antes sobre una copia con la politica encendida (recorrido de la aplicacion).
-- Rollback : un solo comando, tambien en 2 - Script SQL/emergencia_rls_apagar.sql:  ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = OFF);
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada): la politica existe con sus 199 predicados y esta apagada (o ya encendida: no hace nada).
--   2. CAMBIO (una transaccion): ALTER SECURITY POLICY ... WITH (STATE = ON), y POSTCHECK de que quedo encendida.
-- Seguridad de ejecucion: ejecutar TODO el archivo en una sola sesion. Idempotente. Requiere aprobacion (/alerta-bd).
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_037') IS NOT NULL DROP TABLE #precheck_037;

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;

IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';
ELSE
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    IF @n <> 199 SET @errores += CONCAT(N' La politica tiene ', @n, N' predicados y se esperaban 199.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 037 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50037, @mensaje, 1;
END

CREATE TABLE #precheck_037 (ok BIT NOT NULL);
INSERT INTO #precheck_037 (ok) VALUES (1);

PRINT N'PRECHECK 037 correcto.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_037') IS NULL
    THROW 50037, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @estabaEncendida BIT = (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'));

BEGIN TRY
    BEGIN TRANSACTION;

    IF @estabaEncendida = 0
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = ON);';

    IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg') AND is_enabled = 1)
        THROW 50037, N'POSTCHECK: la politica no quedo encendida. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @estabaEncendida = 1
        PRINT N'Nada que hacer: la politica ya estaba encendida.';
    ELSE
        PRINT N'CAMBIO 037 aplicado: seg.pol_tenant ENCENDIDA. Si algo falla: 2 - Script SQL/emergencia_rls_apagar.sql.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT p.name AS politica, p.is_enabled AS encendida FROM sys.security_policies p WHERE p.name = N'pol_tenant';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd, salvo la emergencia)
-- ------------------------------------------------------------
/*
ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = OFF);
*/
