-- ============================================================
-- Script   : 048_cuentas_estilo_menu.sql
-- Proposito: Plan secundario "Menu configurable por usuario" (1 - Documetacion/PLAN_SECUNDARIO_MENU_CONFIGURABLE.md, etapa 1):
--              * Agrega a saas.cuentas la columna estilo_menu VARCHAR(20) NOT NULL con el valor por defecto 'hub'
--                (DF_cuentas_estilo_menu) y la restriccion CK_cuentas_estilo_menu CHECK (estilo_menu IN ('hub', 'sidebar', 'topbar')).
--              * Las cuentas que ya existen quedan con 'hub' (el valor por defecto se aplica al agregar la columna NOT NULL).
--            La preferencia vive en la cuenta global (ADR-003, ADR-027): la persona ve el mismo estilo en todos sus tenants y equipos.
--            saas.cuentas es del plano de control y no tiene RLS: la aplicacion actualiza la columna SOLO filtrando por el id_cuenta de la
--            cookie autenticada (Pages/Preferencias/EstiloMenu.cshtml.cs).
--            Solo AGREGA: no borra ni modifica ninguna fila ni ningun objeto existente.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-22
-- BD       : eBD_SPD
-- Requiere : 038 aplicado (saas.cuentas).
-- Numero   : 046 lo usa F1.8c (permisos de auditoria) y 047 el motor de reportes; este cambio toma el 048.
-- Rollback : ver la seccion ROLLBACK al final (quitar la columna borra la preferencia de cada persona; todas vuelven a 'hub').
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) no existe saas.cuentas (falta el 038);
--        b) los objetos nuevos existen A MEDIAS (la columna sin su valor por defecto o sin su CHECK, o al reves) o la columna existe
--           con otro tipo, otro largo o nulable.
--   2. CAMBIO (una sola transaccion): la columna con su valor por defecto y la restriccion CHECK. Solo crea lo que falta.
--   3. POSTCHECK dentro de la misma transaccion: columna varchar(20) NOT NULL, valor por defecto 'hub', CHECK confiable y habilitado,
--      ninguna fila con un valor fuera de la lista y el mismo numero de cuentas que antes. Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * ASCII puro.
--   * Aplicar ANTES de publicar el codigo del menu configurable: el modelo Cuenta lee estilo_menu y, sin la columna, el inicio de
--     sesion falla.
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
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_048') IS NOT NULL DROP TABLE #precheck_048;
CREATE TABLE #precheck_048 (estado VARCHAR(20) NOT NULL, cuentas INT NOT NULL);

DECLARE @problemas NVARCHAR(MAX) = N'';

IF OBJECT_ID(N'saas.cuentas', N'U') IS NULL
    SET @problemas += N'Falta saas.cuentas (aplicar antes el 038). ';

DECLARE @hay_columna BIT = CASE WHEN COL_LENGTH(N'saas.cuentas', N'estilo_menu') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @hay_default BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.default_constraints
                                             WHERE name = N'DF_cuentas_estilo_menu' AND parent_object_id = OBJECT_ID(N'saas.cuentas'))
                                THEN 1 ELSE 0 END;
DECLARE @hay_check BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints
                                           WHERE name = N'CK_cuentas_estilo_menu' AND parent_object_id = OBJECT_ID(N'saas.cuentas'))
                              THEN 1 ELSE 0 END;

IF @problemas = N'' AND NOT ((@hay_columna = 0 AND @hay_default = 0 AND @hay_check = 0)
                          OR (@hay_columna = 1 AND @hay_default = 1 AND @hay_check = 1))
    SET @problemas += CONCAT(N'El cambio esta a medias: columna=', @hay_columna, N', valor por defecto=', @hay_default,
                             N', CHECK=', @hay_check, N'. Revisar a mano antes de seguir. ');

IF @problemas = N'' AND @hay_columna = 1
   AND NOT EXISTS (SELECT 1 FROM sys.columns c
                   WHERE c.object_id = OBJECT_ID(N'saas.cuentas') AND c.name = N'estilo_menu'
                     AND TYPE_NAME(c.user_type_id) = N'varchar' AND c.max_length = 20 AND c.is_nullable = 0)
    SET @problemas += N'saas.cuentas.estilo_menu existe pero no es VARCHAR(20) NOT NULL. ';

IF @problemas <> N''
BEGIN
    RAISERROR(N'PRECHECK 048 fallido: %s', 16, 1, @problemas);
    RETURN;
END;

INSERT INTO #precheck_048 (estado, cuentas)
SELECT CASE WHEN @hay_columna = 1 THEN 'aplicado' ELSE 'pendiente' END, COUNT(*) FROM saas.cuentas;

PRINT CASE WHEN @hay_columna = 1 THEN N'PRECHECK 048 correcto: el cambio ya esta aplicado; no se hara nada.'
           ELSE N'PRECHECK 048 correcto: se agregara saas.cuentas.estilo_menu.' END;
GO

-- ------------------------------------------------------------
-- CAMBIO + POSTCHECK (una sola transaccion)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_048') IS NULL OR NOT EXISTS (SELECT 1 FROM #precheck_048)
BEGIN
    RAISERROR(N'048: el PRECHECK no se ejecuto o fallo en esta sesion. Ejecutar el archivo completo.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM #precheck_048 WHERE estado = 'aplicado')
BEGIN
    PRINT N'048: nada que hacer.';
    RETURN;
END;

DECLARE @cuentas_antes INT = (SELECT cuentas FROM #precheck_048);

BEGIN TRANSACTION;

    ALTER TABLE saas.cuentas
        ADD estilo_menu VARCHAR(20) NOT NULL CONSTRAINT DF_cuentas_estilo_menu DEFAULT 'hub';

    -- En otro lote para que la columna ya exista al compilar la restriccion.
    EXEC (N'ALTER TABLE saas.cuentas WITH CHECK
              ADD CONSTRAINT CK_cuentas_estilo_menu CHECK (estilo_menu IN (''hub'', ''sidebar'', ''topbar''));');

    -- POSTCHECK
    DECLARE @fallas NVARCHAR(MAX) = N'';

    IF NOT EXISTS (SELECT 1 FROM sys.columns c
                   WHERE c.object_id = OBJECT_ID(N'saas.cuentas') AND c.name = N'estilo_menu'
                     AND TYPE_NAME(c.user_type_id) = N'varchar' AND c.max_length = 20 AND c.is_nullable = 0)
        SET @fallas += N'columna; ';

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints d
                   WHERE d.name = N'DF_cuentas_estilo_menu' AND d.parent_object_id = OBJECT_ID(N'saas.cuentas')
                     AND REPLACE(REPLACE(d.definition, N'(', N''), N')', N'') = N'''hub''')
        SET @fallas += N'valor por defecto; ';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints k
                   WHERE k.name = N'CK_cuentas_estilo_menu' AND k.parent_object_id = OBJECT_ID(N'saas.cuentas')
                     AND k.is_disabled = 0 AND k.is_not_trusted = 0)
        SET @fallas += N'CHECK; ';

    DECLARE @fuera INT;
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM saas.cuentas WHERE estilo_menu NOT IN (''hub'', ''sidebar'', ''topbar'');',
                       N'@n INT OUTPUT', @n = @fuera OUTPUT;
    IF @fuera <> 0 SET @fallas += CONCAT(N'filas fuera de la lista: ', @fuera, N'; ');

    IF (SELECT COUNT(*) FROM saas.cuentas) <> @cuentas_antes
        SET @fallas += N'cambio el numero de cuentas; ';

    IF @fallas <> N''
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'POSTCHECK 048 fallido (se revirtio todo): %s', 16, 1, @fallas);
        RETURN;
    END;

COMMIT TRANSACTION;

PRINT CONCAT(N'048 aplicado: saas.cuentas.estilo_menu agregado; ', @cuentas_antes, N' cuentas quedan con ''hub''.');
GO

-- ------------------------------------------------------------
-- ROLLBACK (comentado; ejecutar solo con aprobacion)
-- Quita la preferencia de cada persona: todas vuelven al estilo 'hub'. Publicar ANTES el codigo anterior al menu configurable
-- (el modelo Cuenta de este cambio lee la columna).
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_cuentas_estilo_menu' AND parent_object_id = OBJECT_ID(N'saas.cuentas'))
        ALTER TABLE saas.cuentas DROP CONSTRAINT CK_cuentas_estilo_menu;
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_cuentas_estilo_menu' AND parent_object_id = OBJECT_ID(N'saas.cuentas'))
        ALTER TABLE saas.cuentas DROP CONSTRAINT DF_cuentas_estilo_menu;
    IF COL_LENGTH(N'saas.cuentas', N'estilo_menu') IS NOT NULL
        ALTER TABLE saas.cuentas DROP COLUMN estilo_menu;
COMMIT TRANSACTION;
*/
