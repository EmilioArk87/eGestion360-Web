-- ============================================================
-- Script   : 023_retirar_tablas_legado.sql
-- Proposito: Saca de dbo las tablas y procedimientos que la aplicacion no usa (paso F0.7 del plan
--            de arquitectura SaaS, decisiones de Emilio del 2026-10-10):
--              * usuarios y usuarios_empresas: gemela vieja de dbo.Users y su detalle, 0 filas.
--              * paises: gemela vieja de dbo.catalogo_paises.
--              * EmailConfiguration (singular): gemela de dbo.EmailConfigurations (plural, la unica
--                que lee la aplicacion), con su trigger y sus checks, y los 5 procedimientos que
--                escriben en ella: SP_ConfigurarHostingerEmail, sp_GetActiveEmailConfiguration,
--                sp_SetDefaultEmailConfiguration, sp_SetDefaultEmailConfigurationSafe y
--                sp_UpdateEmailTestStats. La pagina /ConfigurarHostinger que los usaba se retira en
--                el mismo cambio de la aplicacion.
--            Esos objetos NO se borran: se mueven al esquema retirado, de donde la seccion ROLLBACK
--            los devuelve. Se borran del todo en un script posterior, cuando se confirme que nada
--            los echa de menos.
--            Ademas BORRA dbo.respaldo_personas_018: copia de las 88 personas tomada por el script
--            018 (con datos personales); la copia dbo.respaldo_personas_019 se conserva para poder
--            revertir el 019 y se retira en F1.
--            NO toca ingresos_operativos ni precios_combustible: se conservan como tablas del KPI.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-10
-- BD       : eBD_SPD
-- Requiere : la version de la aplicacion SIN /ConfigurarHostinger, /ResetAdmin ni /EncryptPasswords
--            (paso F0.7) ya PUBLICADA. La version anterior sigue funcionando para todo lo demas, pero
--            su pagina /ConfigurarHostinger fallaria al no encontrar SP_ConfigurarHostingerEmail.
-- Rollback : ver la seccion ROLLBACK al final: devuelve a dbo todo lo que esta en retirado.
--            dbo.respaldo_personas_018 no se recupera: era una copia y se borra a proposito.
-- ============================================================
-- Plan      : artifact "Arquitectura SaaS eGestion360", Fase 0, paso F0.7. ADR-014 y la clasificacion
--             de tablas de eGestion360Web.Tests/Arquitectura/ClasificacionDeTablas.cs.
--
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta si:
--        a) algun objeto a retirar esta a la vez en dbo y en retirado, o no esta en ninguno;
--        b) usuarios o usuarios_empresas tienen filas;
--        c) algun objeto ajeno al conjunto depende de los que se retiran (vista, procedimiento,
--           funcion, trigger, check de otra tabla o clave foranea);
--        d) algo depende de dbo.respaldo_personas_018.
--      Guarda el numero de filas de cada tabla para compararlo despues.
--   2. Crea el esquema retirado si no existe.
--   3. CAMBIO (una transaccion): mueve las 4 tablas y los 5 procedimientos a retirado, borra
--      dbo.respaldo_personas_018 y comprueba (POSTCHECK) que nada quedo en dbo, que todo esta en
--      retirado con las mismas filas y que el trigger viajo con su tabla.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los
--     demas bloques exigen; si el PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: si todo ya esta en retirado y la copia 018 ya no existe, no hace nada y lo dice.
--   * Mover de esquema es una operacion de metadatos (rapida). Los procedimientos movidos dejan de
--     funcionar (apuntan a dbo.EmailConfiguration); es lo buscado.
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
IF OBJECT_ID(N'tempdb..#precheck_023') IS NOT NULL DROP TABLE #precheck_023;
IF OBJECT_ID(N'tempdb..#objetos_023') IS NOT NULL DROP TABLE #objetos_023;

CREATE TABLE #objetos_023 (
    nombre   SYSNAME  NOT NULL PRIMARY KEY,
    tipo     CHAR(2)  NOT NULL,          -- U = tabla, P = procedimiento
    id_dbo   INT      NULL,
    id_ret   INT      NULL,
    filas    BIGINT   NULL
);

INSERT INTO #objetos_023 (nombre, tipo) VALUES
    (N'usuarios_empresas', 'U'),
    (N'usuarios', 'U'),
    (N'paises', 'U'),
    (N'EmailConfiguration', 'U'),
    (N'SP_ConfigurarHostingerEmail', 'P'),
    (N'sp_GetActiveEmailConfiguration', 'P'),
    (N'sp_SetDefaultEmailConfiguration', 'P'),
    (N'sp_SetDefaultEmailConfigurationSafe', 'P'),
    (N'sp_UpdateEmailTestStats', 'P');

UPDATE #objetos_023
   SET id_dbo = OBJECT_ID(N'dbo.' + QUOTENAME(nombre), tipo),
       id_ret = OBJECT_ID(N'retirado.' + QUOTENAME(nombre), tipo);

UPDATE o
   SET filas = (SELECT SUM(p.rows) FROM sys.partitions p
                WHERE p.object_id = COALESCE(o.id_dbo, o.id_ret) AND p.index_id IN (0, 1))
FROM #objetos_023 o
WHERE o.tipo = 'U';

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @ids TABLE (id INT NOT NULL PRIMARY KEY);
INSERT INTO @ids (id) SELECT COALESCE(id_dbo, id_ret) FROM #objetos_023 WHERE COALESCE(id_dbo, id_ret) IS NOT NULL;

-- a) cada objeto esta en un solo lugar
SELECT @errores += N' Esta en dbo y en retirado: ' + nombre + N'.' FROM #objetos_023 WHERE id_dbo IS NOT NULL AND id_ret IS NOT NULL;
SELECT @errores += N' No esta ni en dbo ni en retirado: ' + nombre + N'.' FROM #objetos_023 WHERE id_dbo IS NULL AND id_ret IS NULL;

-- b) las gemelas de Users estan vacias
SELECT @errores += N' ' + nombre + N' tiene ' + CAST(filas AS NVARCHAR(20)) + N' fila(s); se esperaba 0.'
FROM #objetos_023 WHERE nombre IN (N'usuarios', N'usuarios_empresas') AND ISNULL(filas, 0) > 0;

-- c) dependencias de objetos ajenos al conjunto (por nombre, para no perder referencias sin resolver)
SELECT @errores += N' Dependencia no prevista: ' + OBJECT_SCHEMA_NAME(d.referencing_id) + N'.' + OBJECT_NAME(d.referencing_id)
                 + N' usa ' + d.referenced_entity_name + N'.'
FROM sys.sql_expression_dependencies d
JOIN sys.objects r ON r.object_id = d.referencing_id
WHERE d.referenced_entity_name IN (SELECT nombre FROM #objetos_023)
  AND ISNULL(d.referenced_schema_name, N'dbo') IN (N'dbo', N'retirado')
  AND r.object_id NOT IN (SELECT id FROM @ids)
  AND r.parent_object_id NOT IN (SELECT id FROM @ids);

SELECT @errores += N' Clave foranea no prevista: ' + fk.name + N'.'
FROM sys.foreign_keys fk
WHERE fk.referenced_object_id IN (SELECT id FROM @ids)
  AND fk.parent_object_id NOT IN (SELECT id FROM @ids);

-- d) nada depende de la copia 018
IF OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NOT NULL
BEGIN
    SELECT @errores += N' Dependencia no prevista sobre respaldo_personas_018: ' + OBJECT_NAME(d.referencing_id) + N'.'
    FROM sys.sql_expression_dependencies d
    WHERE d.referenced_entity_name = N'respaldo_personas_018'
      AND d.referencing_id <> OBJECT_ID(N'dbo.respaldo_personas_018');

    SELECT @errores += N' Clave foranea no prevista sobre respaldo_personas_018: ' + fk.name + N'.'
    FROM sys.foreign_keys fk
    WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.respaldo_personas_018');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 023 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50023, @mensaje, 1;
END

CREATE TABLE #precheck_023 (ok BIT NOT NULL);
INSERT INTO #precheck_023 (ok) VALUES (1);

DECLARE @enDbo INT = (SELECT COUNT(*) FROM #objetos_023 WHERE id_dbo IS NOT NULL);
DECLARE @enRetirado INT = (SELECT COUNT(*) FROM #objetos_023 WHERE id_ret IS NOT NULL);
DECLARE @copia018 NVARCHAR(20) = CASE WHEN OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NULL THEN N'ya no existe' ELSE N'existe' END;
PRINT CONCAT(N'PRECHECK 023 correcto. Objetos en dbo por mover: ', @enDbo, N'; ya en retirado: ', @enRetirado,
             N'; dbo.respaldo_personas_018 ', @copia018, N'.');
GO

-- ------------------------------------------------------------
-- ESQUEMA retirado (fuera de la transaccion: CREATE SCHEMA debe ir solo en su lote)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_023') IS NULL
    THROW 50023, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF SCHEMA_ID(N'retirado') IS NULL
BEGIN
    EXEC sys.sp_executesql N'CREATE SCHEMA retirado AUTHORIZATION dbo;';
    PRINT N'Esquema retirado creado.';
END
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_023') IS NULL
    THROW 50023, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @porMover INT = (SELECT COUNT(*) FROM #objetos_023 WHERE id_dbo IS NOT NULL);
DECLARE @habia018 BIT = CASE WHEN OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NULL THEN 0 ELSE 1 END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.usuarios_empresas', N'U') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.usuarios_empresas;
    IF OBJECT_ID(N'dbo.usuarios', N'U') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.usuarios;
    IF OBJECT_ID(N'dbo.paises', N'U') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.paises;
    IF OBJECT_ID(N'dbo.EmailConfiguration', N'U') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.EmailConfiguration;
    IF OBJECT_ID(N'dbo.SP_ConfigurarHostingerEmail', N'P') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.SP_ConfigurarHostingerEmail;
    IF OBJECT_ID(N'dbo.sp_GetActiveEmailConfiguration', N'P') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.sp_GetActiveEmailConfiguration;
    IF OBJECT_ID(N'dbo.sp_SetDefaultEmailConfiguration', N'P') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.sp_SetDefaultEmailConfiguration;
    IF OBJECT_ID(N'dbo.sp_SetDefaultEmailConfigurationSafe', N'P') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.sp_SetDefaultEmailConfigurationSafe;
    IF OBJECT_ID(N'dbo.sp_UpdateEmailTestStats', N'P') IS NOT NULL ALTER SCHEMA retirado TRANSFER OBJECT::dbo.sp_UpdateEmailTestStats;

    IF OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.respaldo_personas_018;
        PRINT N'dbo.respaldo_personas_018 borrada.';
    END

    -- POSTCHECK: si algo no cuadra, se revierte todo
    IF EXISTS (SELECT 1 FROM #objetos_023 WHERE OBJECT_ID(N'dbo.' + QUOTENAME(nombre), tipo) IS NOT NULL)
        THROW 50023, N'POSTCHECK: algun objeto sigue en dbo. Se revierte.', 1;

    IF EXISTS (SELECT 1 FROM #objetos_023 WHERE OBJECT_ID(N'retirado.' + QUOTENAME(nombre), tipo) IS NULL)
        THROW 50023, N'POSTCHECK: falta algun objeto en retirado. Se revierte.', 1;

    IF EXISTS (SELECT 1 FROM #objetos_023 o
               WHERE o.tipo = 'U'
                 AND ISNULL(o.filas, 0) <> ISNULL((SELECT SUM(p.rows) FROM sys.partitions p
                                                  WHERE p.object_id = OBJECT_ID(N'retirado.' + QUOTENAME(o.nombre), 'U')
                                                    AND p.index_id IN (0, 1)), 0))
        THROW 50023, N'POSTCHECK: cambio el numero de filas de alguna tabla movida. Se revierte.', 1;

    IF EXISTS (SELECT 1 FROM sys.triggers t WHERE t.name = N'TR_EmailConfiguration_UpdatedAt'
                                               AND t.parent_id <> ISNULL(OBJECT_ID(N'retirado.EmailConfiguration', N'U'), 0))
        THROW 50023, N'POSTCHECK: el trigger TR_EmailConfiguration_UpdatedAt no quedo con su tabla. Se revierte.', 1;

    IF OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NOT NULL
        THROW 50023, N'POSTCHECK: dbo.respaldo_personas_018 sigue existiendo. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @porMover = 0 AND @habia018 = 0
        PRINT N'Nada que hacer: todo ya estaba en retirado y la copia 018 ya no existia.';
    ELSE
        PRINT CONCAT(N'CAMBIO 023 aplicado: ', @porMover, N' objeto(s) movidos a retirado',
                     CASE WHEN @habia018 = 1 THEN N'; copia 018 borrada' ELSE N'' END, N'.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: lo que quedo en el esquema retirado
SELECT o.type_desc AS tipo, o.name AS objeto,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)) AS filas
FROM sys.objects o
WHERE o.schema_id = SCHEMA_ID(N'retirado') AND o.type IN ('U', 'P')
ORDER BY o.type_desc, o.name;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Devuelve a dbo las 4 tablas (con sus filas, trigger, checks y claves) y los 5 procedimientos.
--   dbo.respaldo_personas_018 NO se recupera: era una copia de las 88 personas del 2026-10-01 y se
--   borro a proposito; la copia del 019 (dbo.respaldo_personas_019) sigue en su lugar.
--   Despues de revertir, /ConfigurarHostinger solo vuelve si tambien se vuelve a una version de la
--   aplicacion que la tenga.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'retirado.usuarios', N'U') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.usuarios;
    IF OBJECT_ID(N'retirado.usuarios_empresas', N'U') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.usuarios_empresas;
    IF OBJECT_ID(N'retirado.paises', N'U') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.paises;
    IF OBJECT_ID(N'retirado.EmailConfiguration', N'U') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.EmailConfiguration;
    IF OBJECT_ID(N'retirado.SP_ConfigurarHostingerEmail', N'P') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.SP_ConfigurarHostingerEmail;
    IF OBJECT_ID(N'retirado.sp_GetActiveEmailConfiguration', N'P') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.sp_GetActiveEmailConfiguration;
    IF OBJECT_ID(N'retirado.sp_SetDefaultEmailConfiguration', N'P') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.sp_SetDefaultEmailConfiguration;
    IF OBJECT_ID(N'retirado.sp_SetDefaultEmailConfigurationSafe', N'P') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.sp_SetDefaultEmailConfigurationSafe;
    IF OBJECT_ID(N'retirado.sp_UpdateEmailTestStats', N'P') IS NOT NULL ALTER SCHEMA dbo TRANSFER OBJECT::retirado.sp_UpdateEmailTestStats;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
