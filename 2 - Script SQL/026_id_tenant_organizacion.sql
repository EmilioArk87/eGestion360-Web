-- ============================================================
-- Script   : 026_id_tenant_organizacion.sql
-- Proposito: Paso F1.2 del plan de la Fase 1 (ADR-001, ADR-019), expandir - grupo A, organizacion.
--            Agrega id_tenant INT NULL (con clave foranea a saas.tenants e indice) y la llena, en las tablas:
--              empresa_modulos, empresa_roles, empresa_rol_permisos, cargos
--            Las tablas de este grupo cuelgan de la empresa (id_empresa); empresa_rol_permisos no tiene id_empresa y hereda el tenant de su rol.
--            No toca ninguna otra columna ni ningun dato de negocio. La columna sigue NULABLE (ADR-001: expandir -> migrar ->
--            contraer): F1.3 la vuelve NOT NULL cuando la aplicacion ya la llena en cada insercion.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-12
-- BD       : eBD_SPD
-- Requiere : 025 aplicado (saas.tenants y empresas.id_tenant llena).
--            Despliegue: aplicar con el sitio en mantenimiento (app_offline.htm), justo antes de publicar la version con F1.2. La version
--            anterior de la aplicacion ignora las columnas nuevas, pero lo que inserte mientras tanto queda con id_tenant NULO: por eso
--            el script se vuelve a ejecutar (es idempotente) inmediatamente antes de publicar, y llena lo que haya quedado.
-- Rollback : ver la seccion ROLLBACK al final. Solo vale antes de F1.3 (que vuelve la columna NOT NULL y la usa en claves compuestas).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta si falta el script previo, si falta una tabla, si id_tenant ya existe con un tipo distinto de INT
--      o si hay filas de las que no se puede deducir el tenant.
--   2. CAMBIO (una sola transaccion): columna, clave foranea, indice y llenado, tabla por tabla.
--   3. POSTCHECK dentro de la misma transaccion: sin filas sin tenant, claves e indices presentes y ninguna fila que apunte a otro tenant.
--      Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
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
IF OBJECT_ID(N'tempdb..#precheck_026') IS NOT NULL DROP TABLE #precheck_026;

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;

-- a) el script 025 esta aplicado y todas las empresas tienen tenant
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL OR COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NULL
    SET @errores += N' Falta aplicar el script 025 (saas.tenants y empresas.id_tenant).';
ELSE
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' Hay ', @n, N' empresa(s) sin tenant: complete el script 025 antes.');
END

-- b) cada tabla existe, su id_tenant (si ya existe) es INT y toda fila tiene de donde deducir su tenant
IF OBJECT_ID(N'dbo.empresa_modulos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.empresa_modulos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_modulos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' empresa_modulos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.empresa_modulos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' empresa_modulos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.empresa_roles', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.empresa_roles.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_roles') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' empresa_roles.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.empresa_roles t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' empresa_roles: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.empresa_rol_permisos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.empresa_rol_permisos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_rol_permisos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' empresa_rol_permisos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.empresa_rol_permisos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresa_roles p WHERE p.id_rol = t.id_rol);
    IF @n > 0 SET @errores += CONCAT(N' empresa_rol_permisos: ', @n, N' fila(s) sin su registro padre en empresa_roles de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.cargos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.cargos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' cargos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.cargos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' cargos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 026 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50026, @mensaje, 1;
END

CREATE TABLE #precheck_026 (ok BIT NOT NULL);
INSERT INTO #precheck_026 (ok) VALUES (1);

PRINT N'PRECHECK 026 correcto: 4 tabla(s) por revisar.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_026') IS NULL
    THROW 50026, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

-- Objetos por crear (columna, clave foranea e indice de cada tabla); 0 si todo ya estaba aplicado.
DECLARE @hacia INT = 0;
SELECT @hacia = SUM(y.pendientes) FROM (SELECT (CASE WHEN COL_LENGTH(N'dbo.' + x.nombre, N'id_tenant') IS NULL THEN 1 ELSE 0 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + x.nombre + N'_tenants' AND parent_object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + x.nombre + N'_tenant' AND object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END) AS pendientes
      FROM (VALUES (N'empresa_modulos'), (N'empresa_roles'), (N'empresa_rol_permisos'), (N'cargos')) x (nombre)) y;
DECLARE @llenadas INT = 0, @n INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- empresa_modulos
    IF COL_LENGTH(N'dbo.empresa_modulos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_modulos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_modulos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_modulos ADD CONSTRAINT FK_empresa_modulos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_modulos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_modulos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.empresa_modulos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- empresa_roles
    IF COL_LENGTH(N'dbo.empresa_roles', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_roles ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_roles_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_roles ADD CONSTRAINT FK_empresa_roles_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_roles_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_roles'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_roles_tenant ON dbo.empresa_roles (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.empresa_roles t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- empresa_rol_permisos
    IF COL_LENGTH(N'dbo.empresa_rol_permisos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_rol_permisos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_rol_permisos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_rol_permisos ADD CONSTRAINT FK_empresa_rol_permisos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_rol_permisos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = p.id_tenant FROM dbo.empresa_rol_permisos t JOIN dbo.empresa_roles p ON p.id_rol = t.id_rol WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- cargos
    IF COL_LENGTH(N'dbo.cargos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargos ADD CONSTRAINT FK_cargos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargos_tenant' AND object_id = OBJECT_ID(N'dbo.cargos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_cargos_tenant ON dbo.cargos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.cargos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresa_modulos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: empresa_modulos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50026, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_modulos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_modulos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_modulos'))
        THROW 50026, N'POSTCHECK: falta la clave foranea o el indice de empresa_modulos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresa_roles WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: empresa_roles quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50026, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_roles_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_roles_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_roles'))
        THROW 50026, N'POSTCHECK: falta la clave foranea o el indice de empresa_roles.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresa_rol_permisos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: empresa_rol_permisos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50026, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_rol_permisos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_rol_permisos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
        THROW 50026, N'POSTCHECK: falta la clave foranea o el indice de empresa_rol_permisos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: cargos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50026, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargos_tenant' AND object_id = OBJECT_ID(N'dbo.cargos'))
        THROW 50026, N'POSTCHECK: falta la clave foranea o el indice de cargos.id_tenant. Se revierte.', 1;
    IF COL_LENGTH(N'dbo.empresa_rol_permisos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.empresa_roles', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresa_rol_permisos c JOIN dbo.empresa_roles p ON p.id_rol = c.id_rol WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de empresa_rol_permisos (id_rol) apuntan a empresa_roles de otro tenant. Se revierte.');
            THROW 50026, @msg, 1;
        END
    END

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @llenadas = 0
        PRINT N'Nada que hacer: las columnas, claves e indices id_tenant de este grupo ya estaban aplicados y llenos.';
    ELSE
        PRINT CONCAT(N'CAMBIO 026 aplicado: ', @hacia, N' objeto(s) creados; ', @llenadas, N' fila(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: filas de cada tabla y cuantas siguen sin tenant (en una tabla mixta, las filas globales sin empresa deben quedar sin tenant)
SELECT r.tabla, r.filas, r.sin_tenant
FROM (
    SELECT N'empresa_modulos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.empresa_modulos
    UNION ALL
    SELECT N'empresa_roles' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.empresa_roles
    UNION ALL
    SELECT N'empresa_rol_permisos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.empresa_rol_permisos
    UNION ALL
    SELECT N'cargos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.cargos
) r
ORDER BY r.tabla;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita, tabla por tabla, el indice, la clave foranea y la columna id_tenant. No pierde datos de negocio: el tenant de cada
--   fila se puede volver a deducir con este mismo script. Solo vale antes de F1.3 (despues, la columna es NOT NULL y la usan claves e indices).
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargos')) ALTER TABLE dbo.cargos DROP CONSTRAINT FK_cargos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargos_tenant' AND object_id = OBJECT_ID(N'dbo.cargos')) DROP INDEX IX_cargos_tenant ON dbo.cargos;
    IF COL_LENGTH(N'dbo.cargos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.cargos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_rol_permisos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos')) ALTER TABLE dbo.empresa_rol_permisos DROP CONSTRAINT FK_empresa_rol_permisos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_rol_permisos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_rol_permisos')) DROP INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos;
    IF COL_LENGTH(N'dbo.empresa_rol_permisos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.empresa_rol_permisos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_roles_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles')) ALTER TABLE dbo.empresa_roles DROP CONSTRAINT FK_empresa_roles_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_roles_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_roles')) DROP INDEX IX_empresa_roles_tenant ON dbo.empresa_roles;
    IF COL_LENGTH(N'dbo.empresa_roles', N'id_tenant') IS NOT NULL ALTER TABLE dbo.empresa_roles DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_modulos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos')) ALTER TABLE dbo.empresa_modulos DROP CONSTRAINT FK_empresa_modulos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_modulos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_modulos')) DROP INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos;
    IF COL_LENGTH(N'dbo.empresa_modulos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.empresa_modulos DROP COLUMN id_tenant;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
