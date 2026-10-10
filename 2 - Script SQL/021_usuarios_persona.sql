-- ============================================================
-- Script   : 021_usuarios_persona.sql
-- Proposito: Vincula cada usuario (dbo.Users) con la persona que lo usa (dbo.personas):
--            * dbo.Users.PersonaId INT NULL, con FK_Users_personas_PersonaId (sin cascada) e
--              IX_Users_PersonaId.
--            Paso intermedio hasta F1 (ADR-003, anexo del 2026-10-10): en F1 el vinculo pasa a la membresia
--            (cuenta <-> tenant) y la columna se retira junto con dbo.Users. Hasta entonces una persona puede
--            tener varios usuarios (por ejemplo admin y egaray son la misma persona).
--            El nombre sigue el estilo de dbo.Users (EmpresaId, EmpresaRolId).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-10
-- BD       : eBD_SPD
-- Requiere : dbo.Users y dbo.personas (scripts 014 a 019 aplicados).
-- Rollback : bloque comentado al final.
-- ============================================================
-- Modelo C# : Models/User.cs (PersonaId y Persona) y Data/ApplicationDbContext.cs (relacion e indice).
-- Plan      : decisiones U1 a U5 del 2026-10-10 (artifact "Usuarios y personas").
--
-- CLASIFICACION DE LOS CAMBIOS (para /alerta-bd)
--   [VERDE / AGREGA]
--     + dbo.Users.PersonaId INT NULL (las 7 filas quedan en NULL).
--     + FK_Users_personas_PersonaId -> dbo.personas (id_persona), ON DELETE NO ACTION.
--     + IX_Users_PersonaId (no unico y sin filtro: asi ningun procedimiento ni pagina vieja que escriba en
--       dbo.Users con otras opciones SET queda bloqueado por un indice filtrado).
--   [AZUL / IMPACTO]
--     * Datos: ninguno. Los usuarios se vinculan despues, desde la pantalla de usuarios.
--     * Aplicacion publicada: no conoce la columna y sigue igual. La version nueva SI la lee: este script
--       va ANTES de publicarla (sin la columna, el inicio de sesion fallaria).
--     * Bloqueo: breve, sobre dbo.Users (7 filas).
--
-- Seguridad de ejecucion (igual que 019 y 020):
--   * Ejecutar TODO el archivo en una sola sesion. El PRECHECK deja la marca #precheck_021 que exige el
--     bloque de cambio; si el PRECHECK falla, el bloque de cambio no hace nada.
--   * Idempotente: una segunda ejecucion no cambia nada (lo informa y vuelve a verificar la estructura).
--   * El cambio corre en UNA transaccion (XACT_ABORT + TRY/CATCH) con el POSTCHECK adentro.
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
-- ============================================================

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

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
-- La marca se crea primero y vacia: si cualquier validacion aborta, queda sin la fila ok = 1.
IF OBJECT_ID(N'tempdb..#precheck_021') IS NOT NULL DROP TABLE #precheck_021;
CREATE TABLE #precheck_021 (
    ok              BIT NOT NULL,
    columna_existe  BIT NOT NULL
);

IF DB_NAME() <> N'eBD_SPD'
    THROW 50021, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.personas', N'U') IS NULL
    THROW 50021, N'Faltan dbo.Users o dbo.personas. Abortar.', 1;

-- La clave foranea exige el mismo tipo que la clave primaria de personas.
IF NOT EXISTS (SELECT 1
               FROM sys.columns c
               JOIN sys.types ty ON ty.user_type_id = c.user_type_id
               JOIN sys.index_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
               JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND i.is_primary_key = 1
               WHERE c.object_id = OBJECT_ID(N'dbo.personas') AND c.name = N'id_persona' AND ty.name = N'int')
    THROW 50021, N'dbo.personas.id_persona no es la clave primaria INT. Abortar.', 1;

-- Los nombres que usa el script, si existen, deben estar sobre dbo.Users.
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_personas_PersonaId' AND parent_object_id <> OBJECT_ID(N'dbo.Users'))
   OR EXISTS (SELECT 1 FROM sys.objects WHERE name = N'IX_Users_PersonaId')
   OR EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_PersonaId' AND object_id <> OBJECT_ID(N'dbo.Users'))
    THROW 50021, N'Hay un objeto con el nombre de la clave o del indice del script en otra tabla. Revise a mano. Abortar.', 1;

DECLARE @existe BIT = CASE WHEN COL_LENGTH(N'dbo.Users', N'PersonaId') IS NULL THEN 0 ELSE 1 END;

IF @existe = 1
BEGIN
    -- Ya se aplico (o se agrego a mano): debe ser INT NULL y apuntar solo a personas que existen.
    IF NOT EXISTS (SELECT 1
                   FROM sys.columns c
                   JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                   WHERE c.object_id = OBJECT_ID(N'dbo.Users') AND c.name = N'PersonaId'
                     AND ty.name = N'int' AND c.is_nullable = 1)
        THROW 50021, N'dbo.Users.PersonaId ya existe pero no es INT NULL. Revise a mano. Abortar.', 1;

    DECLARE @huerfanos INT;
    EXEC sys.sp_executesql
        N'SELECT @n = COUNT(*) FROM dbo.Users u
          WHERE u.PersonaId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.personas p WHERE p.id_persona = u.PersonaId);',
        N'@n INT OUTPUT', @n = @huerfanos OUTPUT;
    IF @huerfanos > 0
        THROW 50021, N'Hay usuarios con PersonaId que no existe en dbo.personas. Revise a mano. Abortar.', 1;
END;

INSERT INTO #precheck_021 (ok, columna_existe) VALUES (1, @existe);

-- PRINT no admite subconsultas: el conteo va en una variable.
DECLARE @usuarios INT = (SELECT COUNT(*) FROM dbo.Users);
PRINT CONCAT(N'PRECHECK 021 correcto. Usuarios: ', @usuarios, N'. Columna PersonaId ya existe: ', @existe, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO + POSTCHECK (una transaccion)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_021') IS NULL OR NOT EXISTS (SELECT 1 FROM #precheck_021 WHERE ok = 1)
    THROW 50021, N'El PRECHECK no se ejecuto o no paso en esta sesion. No se cambia nada.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @cambios INT = 0;

    IF COL_LENGTH(N'dbo.Users', N'PersonaId') IS NULL
    BEGIN
        ALTER TABLE dbo.Users ADD PersonaId INT NULL;
        SET @cambios += 1;
    END;

    -- SQL dinamico: la columna recien agregada no existe todavia cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_personas_PersonaId')
    BEGIN
        EXEC (N'ALTER TABLE dbo.Users WITH CHECK
                ADD CONSTRAINT FK_Users_personas_PersonaId FOREIGN KEY (PersonaId) REFERENCES dbo.personas (id_persona);');
        SET @cambios += 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_PersonaId')
    BEGIN
        EXEC (N'CREATE NONCLUSTERED INDEX IX_Users_PersonaId ON dbo.Users (PersonaId);');
        SET @cambios += 1;
    END;

    -- POSTCHECK: la estructura final es la esperada, pieza por pieza.
    DECLARE @problemas TABLE (problema NVARCHAR(300) NOT NULL);

    IF NOT EXISTS (SELECT 1
                   FROM sys.columns c
                   JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                   WHERE c.object_id = OBJECT_ID(N'dbo.Users') AND c.name = N'PersonaId'
                     AND ty.name = N'int' AND c.is_nullable = 1)
        INSERT INTO @problemas VALUES (N'dbo.Users.PersonaId no es INT NULL.');

    IF NOT EXISTS (SELECT 1
                   FROM sys.foreign_keys fk
                   JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                   WHERE fk.name = N'FK_Users_personas_PersonaId'
                     AND fk.parent_object_id = OBJECT_ID(N'dbo.Users')
                     AND fk.referenced_object_id = OBJECT_ID(N'dbo.personas')
                     AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'PersonaId'
                     AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'id_persona'
                     AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
                     AND fk.delete_referential_action = 0 AND fk.update_referential_action = 0)
        INSERT INTO @problemas VALUES (N'FK_Users_personas_PersonaId falta, no es confiable o tiene cascada.');

    IF NOT EXISTS (SELECT 1
                   FROM sys.indexes i
                   JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   WHERE i.object_id = OBJECT_ID(N'dbo.Users') AND i.name = N'IX_Users_PersonaId'
                     AND i.is_unique = 0 AND i.has_filter = 0
                     AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'PersonaId')
        INSERT INTO @problemas VALUES (N'IX_Users_PersonaId falta o no es el esperado.');

    IF EXISTS (SELECT 1 FROM @problemas)
    BEGIN
        SELECT problema FROM @problemas;
        THROW 50021, N'POSTCHECK 021 fallo (ver la lista de problemas). Se revierte todo.', 1;
    END;

    COMMIT TRANSACTION;

    IF @cambios = 0
        PRINT N'No habia nada que cambiar: el 021 ya estaba aplicado. POSTCHECK correcto.';
    ELSE
        PRINT CONCAT(N'Listo: ', @cambios, N' cambio(s) aplicados. POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- Resultado (solo lectura): usuarios y su persona
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_021') IS NOT NULL DROP TABLE #precheck_021;

EXEC (N'SELECT COUNT(*) AS usuarios, SUM(CASE WHEN PersonaId IS NULL THEN 1 ELSE 0 END) AS sin_persona FROM dbo.Users;');
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Antes, publicar una version de la aplicacion que no lea Users.PersonaId. Se pierde a que persona
--   estaba vinculado cada usuario (queda en dbo.bitacora_cambios, entidad Users).
-- ------------------------------------------------------------
/*
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_PersonaId')
    DROP INDEX IX_Users_PersonaId ON dbo.Users;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Users_personas_PersonaId')
    ALTER TABLE dbo.Users DROP CONSTRAINT FK_Users_personas_PersonaId;
IF COL_LENGTH(N'dbo.Users', N'PersonaId') IS NOT NULL
    ALTER TABLE dbo.Users DROP COLUMN PersonaId;
*/
