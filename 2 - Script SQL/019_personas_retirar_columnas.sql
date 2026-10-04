-- ============================================================
-- Script   : 019_personas_retirar_columnas.sql
-- Proposito: Retira de dbo.personas las 8 columnas viejas que describian a la persona como empleada
--            de UNA sola empresa: id_empresa, documento, tipo_documento, cargo, tarifa_diaria,
--            moneda_tarifa, fecha_ingreso y fecha_baja. Esos datos viven ahora en
--            persona_empresa (empresa, ingreso y baja), empleados (codigo, cargo, tarifa y moneda)
--            y persona_documentos (documento). Antes de borrarlas quita las dos claves foraneas
--            (FK_personas_empresa, FK_personas_moneda_tarifa) y los dos indices
--            (UX_personas_empresa_documento, IX_personas_empresa_cargo) que dependen de ellas.
--            Es la fase "contraer" del plan de personas (expandir 014, migrar 018, contraer 019).
--            NO toca nombres, apellidos, contacto, licencia ni ninguna otra columna de personas.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-03
-- BD       : eBD_SPD
-- Requiere : 014 y 018 aplicados, y la version de la aplicacion con F6 (commit 3d04132 o posterior)
--            ya PUBLICADA: desde F6 ningun codigo lee ni escribe estas columnas. Si la aplicacion
--            publicada es anterior, sus pantallas fallarian al no encontrarlas.
-- Rollback : ver la seccion ROLLBACK al final. La copia exacta es dbo.respaldo_personas_019 (la crea
--            este script); la copia dbo.respaldo_personas_018 (88 personas originales) se conserva.
-- ============================================================
-- Plan      : artifact "Mejora de datos de personas", script 019.
--
-- Que hace (PRECHECK y respaldo fuera de la transaccion; el cambio, en UNA transaccion):
--   1. PRECHECK. Aborta, sin cambiar nada, si:
--        a) alguna persona con empresa vieja no tiene su vinculo de empleado en esa empresa;
--        b) algun documento viejo (distinto de INTERNO) no tiene su fila en persona_documentos, o
--           algun numero de empleado viejo (tipo INTERNO) no esta en empleados.codigo_interno;
--        c) las columnas tienen una dependencia que este script no conoce (otro indice, otra clave
--           foranea, un valor por defecto, una vista, un procedimiento, un check o una estadistica).
--      Solo AVISA (no aborta) de dos cosas:
--        * cuantas personas tienen valores viejos distintos de los nuevos: son cambios hechos despues
--          de F6 en las tablas nuevas, que son la fuente de verdad;
--        * cuantas personas tienen todavia el nombre por revisar (sin primer nombre o primer apellido).
--          Esa lista NO impide retirar las columnas: las 8 columnas no guardan nombres, y nombres y
--          apellidos se conservan. (El plan original exigia la lista vacia; Emilio decidio quitar la
--          regla el 2026-10-03. Se corrige desde Personal o Personas del sistema, cuando se pueda.)
--   2. Respaldo: crea dbo.respaldo_personas_019 con id_persona y las 8 columnas de toda persona que
--      tenga alguno de esos datos. Si ya existe, comprueba que coincida con lo actual.
--   3. Cambio (una transaccion): quita las 2 claves foraneas, los 2 indices y las 8 columnas y
--      comprueba (POSTCHECK) que las columnas, claves e indices ya no existen, que el numero de
--      filas de personas no cambio y que las columnas que se conservan siguen ahi.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los
--     demas bloques exigen; si el PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: si las 8 columnas ya no existen, no hace nada y lo dice.
--   * Quitar columnas es una operacion de metadatos (rapida); el espacio que ocupaban se libera al
--     reconstruir la tabla (ALTER TABLE dbo.personas REBUILD), que NO hace este script. Toma un
--     bloqueo breve sobre dbo.personas: conviene ejecutarlo con poco movimiento.
--   * Despues de ejecutarlo, los scripts que usan estas columnas (013, seeds, Transgar/30, 40 y 60)
--     dejan de funcionar; estan avisados en INDICE_SCRIPTS_SQL.md.
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
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF DB_NAME() <> N'eBD_SPD'
    THROW 50019, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 13
    THROW 50019, N'Se requiere SQL Server 2016 o superior (DROP ... IF EXISTS). Abortar.', 1;

IF OBJECT_ID(N'dbo.personas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.persona_empresa', N'U') IS NULL
   OR OBJECT_ID(N'dbo.empleados', N'U') IS NULL
   OR OBJECT_ID(N'dbo.persona_documentos', N'U') IS NULL
   OR OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NULL
    THROW 50019, N'Faltan tablas (personas, persona_empresa, empleados, persona_documentos o respaldo_personas_018). Ejecute antes los scripts 014 y 018. Abortar.', 1;

IF OBJECT_ID(N'tempdb..#precheck_019') IS NOT NULL DROP TABLE #precheck_019;
CREATE TABLE #precheck_019 (ok BIT NOT NULL);

DECLARE @msg NVARCHAR(500), @n INT, @sql NVARCHAR(MAX);
DECLARE @presentes INT = (SELECT COUNT(*) FROM sys.columns
                          WHERE object_id = OBJECT_ID(N'dbo.personas')
                            AND name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo',
                                         N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja'));

IF @presentes = 0
BEGIN
    PRINT N'Las 8 columnas viejas de dbo.personas ya fueron retiradas. No hay nada que hacer.';
    INSERT INTO #precheck_019 (ok) VALUES (0);
END
ELSE
BEGIN
    IF @presentes <> 8
        THROW 50019, N'Solo existen algunas de las 8 columnas viejas: estado inesperado de dbo.personas. Revise a mano. Abortar.', 1;

    -- Aviso (no aborta): personas con el nombre por revisar. Las 8 columnas no guardan nombres.
    SELECT @n = COUNT(*)
    FROM dbo.personas
    WHERE eliminado = 0 AND id_persona_principal IS NULL
      AND (primer_nombre IS NULL OR primer_apellido IS NULL);
    PRINT CONCAT(N'Personas con el nombre por revisar (no impide el cambio; se corrigen desde Personal o Personas del sistema): ', @n);

    -- a) Toda persona con empresa vieja debe tener su vinculo de empleado en esa empresa
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.personas p
                 WHERE p.id_empresa IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa v
                                   WHERE v.id_persona = p.id_persona AND v.id_empresa = p.id_empresa
                                     AND v.tipo_vinculo = ''empleado'' AND v.eliminado = 0);';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'Hay ', @n, N' persona(s) con empresa en las columnas viejas y sin vinculo de empleado en esa empresa: ',
                          N'se perderia ese dato. Ejecute otra vez el script 018 o corrija a mano. Abortar.');
        THROW 50019, @msg, 1;
    END

    -- b1) Documentos viejos (distintos de INTERNO) sin su fila en persona_documentos
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.personas p
                 WHERE p.documento IS NOT NULL AND p.tipo_documento <> ''INTERNO''
                   AND NOT EXISTS (SELECT 1 FROM dbo.persona_documentos d
                                   WHERE d.id_persona = p.id_persona AND d.eliminado = 0
                                     AND d.numero_normalizado = REPLACE(REPLACE(p.documento, ''-'', ''''), '' '', ''''));';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'Hay ', @n, N' documento(s) viejo(s) que no estan en persona_documentos: se perderian. Abortar.');
        THROW 50019, @msg, 1;
    END

    -- b2) Numeros de empleado viejos (tipo INTERNO) sin su codigo en empleados
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.personas p
                 WHERE p.tipo_documento = ''INTERNO'' AND p.documento IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa v
                                   JOIN dbo.empleados e ON e.id_persona_empresa = v.id_persona_empresa
                                   WHERE v.id_persona = p.id_persona AND e.codigo_interno = p.documento);';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @n OUTPUT;
    IF @n > 0
    BEGIN
        SET @msg = CONCAT(N'Hay ', @n, N' numero(s) de empleado viejo(s) que no estan en empleados.codigo_interno: se perderian. Abortar.');
        THROW 50019, @msg, 1;
    END

    -- c) Dependencias que este script no conoce
    IF EXISTS (SELECT 1
               FROM sys.indexes i
               JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
               JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE i.object_id = OBJECT_ID(N'dbo.personas')
                 AND c.name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja')
                 AND i.name NOT IN (N'UX_personas_empresa_documento', N'IX_personas_empresa_cargo'))
        THROW 50019, N'Hay un indice desconocido sobre las columnas viejas (ver sys.indexes). Abortar.', 1;

    IF EXISTS (SELECT 1
               FROM sys.foreign_keys fk
               JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
               WHERE (fk.parent_object_id = OBJECT_ID(N'dbo.personas')
                      AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id)
                          IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja')
                      AND fk.name NOT IN (N'FK_personas_empresa', N'FK_personas_moneda_tarifa'))
                  OR (fk.referenced_object_id = OBJECT_ID(N'dbo.personas')
                      AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id)
                          IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja')))
        THROW 50019, N'Hay una clave foranea desconocida sobre las columnas viejas (ver sys.foreign_keys). Abortar.', 1;

    IF EXISTS (SELECT 1
               FROM sys.default_constraints dc
               JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
               WHERE dc.parent_object_id = OBJECT_ID(N'dbo.personas')
                 AND c.name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja'))
        THROW 50019, N'Hay un valor por defecto sobre las columnas viejas (ver sys.default_constraints). Abortar.', 1;

    -- (El filtro de UX_personas_empresa_documento, "documento IS NOT NULL", cuenta como dependencia de tipo INDEX:
    --  esos dos indices conocidos se quitan mas abajo y aqui no cuentan.)
    IF EXISTS (SELECT 1
               FROM sys.sql_expression_dependencies d
               JOIN sys.columns c ON c.object_id = d.referenced_id AND c.column_id = d.referenced_minor_id
               WHERE d.referenced_id = OBJECT_ID(N'dbo.personas')
                 AND c.name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja')
                 AND NOT (d.referencing_class_desc = N'INDEX'
                          AND d.referencing_id = OBJECT_ID(N'dbo.personas')
                          AND d.referencing_minor_id IN (SELECT i.index_id FROM sys.indexes i
                                                         WHERE i.object_id = OBJECT_ID(N'dbo.personas')
                                                           AND i.name IN (N'UX_personas_empresa_documento', N'IX_personas_empresa_cargo'))))
        THROW 50019, N'Hay una vista, procedimiento, funcion, disparador o check que usa las columnas viejas (ver sys.sql_expression_dependencies). Abortar.', 1;

    IF EXISTS (SELECT 1
               FROM sys.stats s
               JOIN sys.stats_columns sc ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
               JOIN sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
               WHERE s.object_id = OBJECT_ID(N'dbo.personas') AND s.user_created = 1
                 AND c.name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo', N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja'))
        THROW 50019, N'Hay una estadistica creada a mano sobre las columnas viejas. Abortar.', 1;

    -- Aviso (no aborta): valores viejos que ya no coinciden con las tablas nuevas
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.personas p
                 JOIN dbo.persona_empresa v ON v.id_persona = p.id_persona AND v.id_empresa = p.id_empresa
                                           AND v.tipo_vinculo = ''empleado'' AND v.eliminado = 0
                 JOIN dbo.empleados e ON e.id_persona_empresa = v.id_persona_empresa
                 WHERE p.id_empresa IS NOT NULL
                   AND (ISNULL(p.cargo, '''') <> ISNULL(e.cargo, '''')
                        OR ISNULL(p.tarifa_diaria, -1) <> ISNULL(e.tarifa_diaria, -1)
                        OR ISNULL(p.moneda_tarifa, '''') <> ISNULL(e.moneda_tarifa, '''')
                        OR ISNULL(CONVERT(varchar(10), p.fecha_ingreso, 120), '''') <> ISNULL(CONVERT(varchar(10), v.fecha_inicio, 120), '''')
                        OR ISNULL(CONVERT(varchar(10), p.fecha_baja, 120), '''') <> ISNULL(CONVERT(varchar(10), v.fecha_fin, 120), ''''));';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @n OUTPUT;
    PRINT CONCAT(N'Personas cuyos valores viejos difieren de las tablas nuevas (se conservan las nuevas): ', @n);

    INSERT INTO #precheck_019 (ok) VALUES (1);
END
GO

-- ------------------------------------------------------------
-- RESPALDO: copia exacta de las 8 columnas (para la reversa)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_019') IS NULL
    THROW 50019, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF EXISTS (SELECT 1 FROM #precheck_019 WHERE ok = 1)
BEGIN
    DECLARE @conDatos INT;
    EXEC sys.sp_executesql
        N'SELECT @conDatos = COUNT(*) FROM dbo.personas
          WHERE id_empresa IS NOT NULL OR documento IS NOT NULL OR tipo_documento IS NOT NULL OR cargo IS NOT NULL
             OR tarifa_diaria IS NOT NULL OR moneda_tarifa IS NOT NULL OR fecha_ingreso IS NOT NULL OR fecha_baja IS NOT NULL;',
        N'@conDatos INT OUTPUT', @conDatos = @conDatos OUTPUT;

    IF OBJECT_ID(N'dbo.respaldo_personas_019', N'U') IS NULL
    BEGIN
        EXEC sys.sp_executesql
            N'SELECT id_persona, id_empresa, documento, tipo_documento, cargo, tarifa_diaria, moneda_tarifa,
                     fecha_ingreso, fecha_baja, SYSUTCDATETIME() AS fecha_respaldo
              INTO dbo.respaldo_personas_019
              FROM dbo.personas
              WHERE id_empresa IS NOT NULL OR documento IS NOT NULL OR tipo_documento IS NOT NULL OR cargo IS NOT NULL
                 OR tarifa_diaria IS NOT NULL OR moneda_tarifa IS NOT NULL OR fecha_ingreso IS NOT NULL OR fecha_baja IS NOT NULL;';
        ALTER TABLE dbo.respaldo_personas_019 ADD CONSTRAINT PK_respaldo_personas_019 PRIMARY KEY (id_persona);
        PRINT CONCAT(N'Copia creada: dbo.respaldo_personas_019 con ', @conDatos, N' fila(s).');
    END
    ELSE IF (SELECT COUNT(*) FROM dbo.respaldo_personas_019) <> @conDatos
    BEGIN
        THROW 50019, N'Ya existe dbo.respaldo_personas_019 y no coincide con los datos actuales. Renombrela o revisela antes de repetir. Abortar.', 1;
    END
    ELSE
    BEGIN
        PRINT N'dbo.respaldo_personas_019 ya existe y coincide con los datos actuales.';
    END
END
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_019') IS NULL
    THROW 50019, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

IF EXISTS (SELECT 1 FROM #precheck_019 WHERE ok = 1)
BEGIN
    DECLARE @filas_antes INT = (SELECT COUNT(*) FROM dbo.personas);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Claves foraneas
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
            ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_empresa;
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_moneda_tarifa' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
            ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_moneda_tarifa;

        -- 2. Indices
        DROP INDEX IF EXISTS UX_personas_empresa_documento ON dbo.personas;
        DROP INDEX IF EXISTS IX_personas_empresa_cargo ON dbo.personas;

        -- 3. Columnas (una sentencia por columna: el IF EXISTS de una lista solo cubre a la primera)
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS id_empresa;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS documento;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS tipo_documento;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS cargo;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS tarifa_diaria;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS moneda_tarifa;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS fecha_ingreso;
        ALTER TABLE dbo.personas DROP COLUMN IF EXISTS fecha_baja;

        -- POSTCHECK: si algo no cuadra, se revierte todo
        IF EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.personas')
                     AND name IN (N'id_empresa', N'documento', N'tipo_documento', N'cargo',
                                  N'tarifa_diaria', N'moneda_tarifa', N'fecha_ingreso', N'fecha_baja'))
            THROW 50019, N'POSTCHECK: alguna columna vieja sigue en dbo.personas. Se revierte.', 1;

        IF EXISTS (SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.personas')
                     AND name IN (N'UX_personas_empresa_documento', N'IX_personas_empresa_cargo'))
            THROW 50019, N'POSTCHECK: algun indice viejo sigue en dbo.personas. Se revierte.', 1;

        IF EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE parent_object_id = OBJECT_ID(N'dbo.personas')
                     AND name IN (N'FK_personas_empresa', N'FK_personas_moneda_tarifa'))
            THROW 50019, N'POSTCHECK: alguna clave foranea vieja sigue en dbo.personas. Se revierte.', 1;

        IF (SELECT COUNT(*) FROM dbo.personas) <> @filas_antes
            THROW 50019, N'POSTCHECK: cambio el numero de filas de dbo.personas. Se revierte.', 1;

        IF (SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.personas')
              AND name IN (N'id_persona', N'nombres', N'apellidos', N'telefono', N'email', N'activo', N'eliminado',
                           N'primer_nombre', N'primer_apellido', N'nombre_normalizado', N'estado_identidad',
                           N'fecha_nacimiento', N'licencia_tipo', N'licencia_numero', N'licencia_vencimiento',
                           N'id_persona_principal', N'token_concurrencia')) <> 17
            THROW 50019, N'POSTCHECK: falta alguna columna que debia conservarse. Se revierte.', 1;

        COMMIT TRANSACTION;
        PRINT CONCAT(N'Listo: 2 claves foraneas, 2 indices y 8 columnas retirados de dbo.personas (', @filas_antes, N' filas intactas).');
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- Resultado: las columnas que quedan en dbo.personas
SELECT c.column_id AS orden, c.name AS columna, t.name AS tipo
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.personas')
ORDER BY c.column_id;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Devuelve las 8 columnas vacias y les vuelca los valores de dbo.respaldo_personas_019 (copia exacta
--   tomada por este script justo antes del cambio), y recrea las 2 claves foraneas y los 2 indices
--   con la definicion que tenian. Si la copia 019 no existiera, los valores de las 88 personas
--   originales estan en dbo.respaldo_personas_018 (mismas columnas). Despues de revertir hay que
--   volver tambien a una version de la aplicacion que lea esas columnas; la actual no las usa.
--   No toca la bitacora (es de solo lectura).
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.personas ADD
        id_empresa     INT           NULL,
        documento      VARCHAR(30)   COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        tipo_documento VARCHAR(20)   COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        cargo          VARCHAR(30)   COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        tarifa_diaria  DECIMAL(18,2) NULL,
        moneda_tarifa  CHAR(3)       COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        fecha_ingreso  DATE          NULL,
        fecha_baja     DATE          NULL;

    EXEC sys.sp_executesql N'
        UPDATE p
           SET p.id_empresa = r.id_empresa, p.documento = r.documento, p.tipo_documento = r.tipo_documento,
               p.cargo = r.cargo, p.tarifa_diaria = r.tarifa_diaria, p.moneda_tarifa = r.moneda_tarifa,
               p.fecha_ingreso = r.fecha_ingreso, p.fecha_baja = r.fecha_baja
        FROM dbo.personas p
        JOIN dbo.respaldo_personas_019 r ON r.id_persona = p.id_persona;';

    EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);';
    EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_moneda_tarifa FOREIGN KEY (moneda_tarifa) REFERENCES dbo.monedas (codigo_iso);';
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_personas_empresa_documento ON dbo.personas (id_empresa, documento) WHERE eliminado = 0 AND documento IS NOT NULL;';
    EXEC sys.sp_executesql N'CREATE INDEX IX_personas_empresa_cargo ON dbo.personas (id_empresa, cargo) WHERE eliminado = 0 AND activo = 1;';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
