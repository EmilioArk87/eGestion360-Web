-- ============================================================
-- Script   : 010_ct_nucleo_contable_reversa.sql
-- Proposito: Deshacer el 010: elimina las 6 tablas del nucleo contable
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-06
-- BD       : eBD_SPD
-- ============================================================
-- Clasificacion de los cambios:
--   [ROJO / ELIMINA] ct_asiento_movimientos, ct_asientos, ct_centros_costo, ct_periodos, ct_ejercicios, ct_cuentas.
--   [IMPACTO] solo se ejecuta con las 6 tablas VACIAS: si alguna tiene filas, el PRECHECK aborta sin cambiar nada.
--             Tampoco sigue si otra tabla, vista o procedimiento ajeno al modulo depende de ellas.
-- Idempotente: si ya no existe ninguna, no hace nada.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK (no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_010r') IS NOT NULL DROP TABLE #precheck_010r;
CREATE TABLE #precheck_010r (ok BIT NOT NULL);

IF DB_NAME() <> N'eBD_SPD'
    THROW 50011, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

DECLARE @tablas TABLE (nombre SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @tablas (nombre) VALUES
    (N'ct_asiento_movimientos'), (N'ct_asientos'), (N'ct_centros_costo'), (N'ct_periodos'), (N'ct_ejercicios'), (N'ct_cuentas');

-- Filas: cada tabla existente debe estar vacia
DECLARE @nombre SYSNAME, @n INT, @msg NVARCHAR(400), @sql NVARCHAR(200);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT nombre FROM @tablas WHERE OBJECT_ID(N'dbo.' + nombre, N'U') IS NOT NULL;
OPEN c;
FETCH NEXT FROM c INTO @nombre;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.' + QUOTENAME(@nombre) + N';';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'dbo.', @nombre, N' tiene ', @n, N' fila(s). La reversa solo se ejecuta con el modulo vacio. No se cambio nada. Abortar.');
        CLOSE c; DEALLOCATE c;
        THROW 50011, @msg, 1;
    END;
    FETCH NEXT FROM c INTO @nombre;
END;
CLOSE c;
DEALLOCATE c;

-- Dependencias ajenas al modulo
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk
           WHERE fk.referenced_object_id IN (SELECT OBJECT_ID(N'dbo.' + nombre) FROM @tablas)
             AND fk.parent_object_id NOT IN (SELECT ISNULL(OBJECT_ID(N'dbo.' + nombre), 0) FROM @tablas))
    THROW 50011, N'Otra tabla tiene una clave foranea hacia el nucleo contable (ver sys.foreign_keys). Revise a mano. Abortar.', 1;

IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
           JOIN sys.objects o ON o.object_id = d.referencing_id
           WHERE d.referenced_entity_name COLLATE DATABASE_DEFAULT IN (SELECT nombre FROM @tablas)
             AND ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
             AND d.referencing_id NOT IN (SELECT ISNULL(OBJECT_ID(N'dbo.' + nombre), 0) FROM @tablas)
             AND o.parent_object_id NOT IN (SELECT ISNULL(OBJECT_ID(N'dbo.' + nombre), 0) FROM @tablas))
    THROW 50011, N'Una vista, procedimiento o funcion usa el nucleo contable (ver sys.sql_expression_dependencies). Revise a mano. Abortar.', 1;

INSERT INTO #precheck_010r (ok) VALUES (1);
PRINT N'PRECHECK reversa 010 superado.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_010r') IS NULL OR NOT EXISTS (SELECT 1 FROM #precheck_010r WHERE ok = 1)
    THROW 50011, N'El PRECHECK no termino bien o no se ejecuto en esta sesion. No se cambio nada.', 1;

DECLARE @eliminadas INT = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Orden inverso por dependencias. Se vuelve a contar con bloqueo exclusivo antes de eliminar.
    DECLARE @tabla SYSNAME, @filas INT, @consulta NVARCHAR(300);
    DECLARE orden CURSOR LOCAL FAST_FORWARD FOR
        SELECT nombre FROM (VALUES (1, N'ct_asiento_movimientos'), (2, N'ct_asientos'), (3, N'ct_centros_costo'),
                                   (4, N'ct_periodos'), (5, N'ct_ejercicios'), (6, N'ct_cuentas')) v (orden, nombre)
        ORDER BY orden;
    OPEN orden;
    FETCH NEXT FROM orden INTO @tabla;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID(N'dbo.' + @tabla, N'U') IS NOT NULL
        BEGIN
            SET @consulta = N'SELECT @filas = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' WITH (TABLOCKX, HOLDLOCK);';
            EXEC sys.sp_executesql @consulta, N'@filas INT OUTPUT', @filas = @filas OUTPUT;
            IF @filas > 0
                THROW 50011, N'Una tabla del nucleo contable recibio filas despues del PRECHECK. Se revierte.', 1;

            SET @consulta = N'DROP TABLE dbo.' + QUOTENAME(@tabla) + N';';
            EXEC sys.sp_executesql @consulta;
            SET @eliminadas += 1;
            PRINT CONCAT(N'  - dbo.', @tabla);
        END;
        FETCH NEXT FROM orden INTO @tabla;
    END;
    CLOSE orden;
    DEALLOCATE orden;

    -- POSTCHECK: no debe quedar ninguna
    IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')
               AND name IN (N'ct_cuentas', N'ct_ejercicios', N'ct_periodos', N'ct_centros_costo', N'ct_asientos', N'ct_asiento_movimientos'))
        THROW 50011, N'POSTCHECK reversa 010: todavia existe alguna tabla ct_*. Se revierte todo.', 1;

    COMMIT TRANSACTION;

    IF @eliminadas = 0
        PRINT N'No habia nada que eliminar: el nucleo contable no existe.';
    ELSE
        PRINT CONCAT(N'Listo: ', @eliminadas, N' tabla(s) eliminada(s). POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local', 'orden') >= -1
    BEGIN
        IF CURSOR_STATUS('local', 'orden') >= 0 CLOSE orden;
        DEALLOCATE orden;
    END;
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'tempdb..#precheck_010r') IS NOT NULL DROP TABLE #precheck_010r;
GO
