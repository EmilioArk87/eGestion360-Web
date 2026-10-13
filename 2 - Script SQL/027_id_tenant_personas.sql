-- ============================================================
-- Script   : 027_id_tenant_personas.sql
-- Proposito: Paso F1.2 del plan de la Fase 1 (ADR-001, ADR-019), expandir - grupo B, personas.
--            Agrega id_tenant INT NULL (con clave foranea a saas.tenants e indice) y la llena, en las tablas:
--              personas, persona_documentos, persona_empresa, empleados, clientes, proveedores, bitacora_cambios
--            personas no tiene id_empresa: una persona pertenece a UN solo tenant (ADR-004) y lo hereda de las empresas de sus vinculos
--            (persona_empresa); si no tiene vinculos, del usuario del sistema que la usa. El PRECHECK aborta si una persona tiene vinculos en
--            empresas de mas de un tenant o si no hay de donde deducir el suyo.
--            bitacora_cambios es de solo lectura (disparador TR_bitacora_cambios_inmutable, script 014). Para llenar id_tenant el disparador se
--            desactiva SOLO dentro de la transaccion y se reactiva antes de confirmar; el POSTCHECK exige que quede activo. El tenant de una fila
--            de la bitacora es el de la persona auditada y, si no la tiene, el de la empresa de la sesion que hizo el cambio.
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
IF OBJECT_ID(N'tempdb..#precheck_027') IS NOT NULL DROP TABLE #precheck_027;

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
IF OBJECT_ID(N'dbo.personas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.personas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' personas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.personas t WHERE NOT EXISTS (SELECT 1 FROM dbo.persona_empresa v WHERE v.id_persona = t.id_persona) AND NOT EXISTS (SELECT 1 FROM dbo.Users u JOIN dbo.empresas e ON e.id_empresa = u.EmpresaId WHERE u.PersonaId = t.id_persona);
    IF @n > 0 SET @errores += CONCAT(N' personas: ', @n, N' fila(s) sin un vinculo con una empresa ni un usuario con empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.persona_documentos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.persona_documentos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' persona_documentos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.persona_documentos t WHERE NOT EXISTS (SELECT 1 FROM dbo.personas p WHERE p.id_persona = t.id_persona);
    IF @n > 0 SET @errores += CONCAT(N' persona_documentos: ', @n, N' fila(s) sin su registro padre en personas de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.persona_empresa', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.persona_empresa.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_empresa') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' persona_empresa.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.persona_empresa t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' persona_empresa: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.empleados', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.empleados.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empleados') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' empleados.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.empleados t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' empleados: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.clientes', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.clientes.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.clientes') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' clientes.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.clientes t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' clientes: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.proveedores', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.proveedores.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.proveedores') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' proveedores.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.proveedores t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' proveedores: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.bitacora_cambios', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.bitacora_cambios.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' bitacora_cambios.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.bitacora_cambios t WHERE NOT EXISTS (SELECT 1 FROM dbo.personas p WHERE p.id_persona = t.id_persona) AND NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' bitacora_cambios: ', @n, N' fila(s) sin una persona ni una empresa de donde deducir el tenant.');
END

-- c) una persona pertenece a un solo tenant (ADR-004): sus vinculos no pueden estar en empresas de tenants distintos
IF OBJECT_ID(N'dbo.persona_empresa', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.empresas', N'id_tenant') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM (SELECT v.id_persona FROM dbo.persona_empresa v JOIN dbo.empresas e ON e.id_empresa = v.id_empresa GROUP BY v.id_persona HAVING COUNT(DISTINCT e.id_tenant) > 1) x;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' ', @n, N' persona(s) con vinculos en empresas de mas de un tenant: decida a cual pertenece cada una antes de seguir.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 027 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50027, @mensaje, 1;
END

CREATE TABLE #precheck_027 (ok BIT NOT NULL);
INSERT INTO #precheck_027 (ok) VALUES (1);

PRINT N'PRECHECK 027 correcto: 7 tabla(s) por revisar.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_027') IS NULL
    THROW 50027, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

-- Objetos por crear (columna, clave foranea e indice de cada tabla); 0 si todo ya estaba aplicado.
DECLARE @hacia INT = 0;
SELECT @hacia = SUM(y.pendientes) FROM (SELECT (CASE WHEN COL_LENGTH(N'dbo.' + x.nombre, N'id_tenant') IS NULL THEN 1 ELSE 0 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + x.nombre + N'_tenants' AND parent_object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + x.nombre + N'_tenant' AND object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END) AS pendientes
      FROM (VALUES (N'personas'), (N'persona_documentos'), (N'persona_empresa'), (N'empleados'), (N'clientes'), (N'proveedores'), (N'bitacora_cambios')) x (nombre)) y;
DECLARE @llenadas INT = 0, @n INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- personas
    IF COL_LENGTH(N'dbo.personas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_tenant' AND object_id = OBJECT_ID(N'dbo.personas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_personas_tenant ON dbo.personas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE p SET p.id_tenant = x.id_tenant FROM dbo.personas p JOIN (SELECT v.id_persona, MIN(e.id_tenant) AS id_tenant FROM dbo.persona_empresa v JOIN dbo.empresas e ON e.id_empresa = v.id_empresa GROUP BY v.id_persona) x ON x.id_persona = p.id_persona WHERE p.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;
    EXEC sys.sp_executesql N'UPDATE p SET p.id_tenant = x.id_tenant FROM dbo.personas p JOIN (SELECT u.PersonaId, MIN(e.id_tenant) AS id_tenant FROM dbo.Users u JOIN dbo.empresas e ON e.id_empresa = u.EmpresaId WHERE u.PersonaId IS NOT NULL GROUP BY u.PersonaId) x ON x.PersonaId = p.id_persona WHERE p.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- persona_documentos
    IF COL_LENGTH(N'dbo.persona_documentos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_documentos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_documentos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_documentos ADD CONSTRAINT FK_persona_documentos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_documentos_tenant' AND object_id = OBJECT_ID(N'dbo.persona_documentos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_persona_documentos_tenant ON dbo.persona_documentos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = p.id_tenant FROM dbo.persona_documentos t JOIN dbo.personas p ON p.id_persona = t.id_persona WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- persona_empresa
    IF COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa ADD CONSTRAINT FK_persona_empresa_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_empresa_tenant' AND object_id = OBJECT_ID(N'dbo.persona_empresa'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_persona_empresa_tenant ON dbo.persona_empresa (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.persona_empresa t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- empleados
    IF COL_LENGTH(N'dbo.empleados', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empleados ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empleados ADD CONSTRAINT FK_empleados_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empleados_tenant' AND object_id = OBJECT_ID(N'dbo.empleados'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_empleados_tenant ON dbo.empleados (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.empleados t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- clientes
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ADD CONSTRAINT FK_clientes_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_clientes_tenant' AND object_id = OBJECT_ID(N'dbo.clientes'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_clientes_tenant ON dbo.clientes (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.clientes t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- proveedores
    IF COL_LENGTH(N'dbo.proveedores', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_tenants' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores ADD CONSTRAINT FK_proveedores_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_proveedores_tenant' AND object_id = OBJECT_ID(N'dbo.proveedores'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_proveedores_tenant ON dbo.proveedores (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.proveedores t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- bitacora_cambios
    IF COL_LENGTH(N'dbo.bitacora_cambios', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_bitacora_cambios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.bitacora_cambios'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios ADD CONSTRAINT FK_bitacora_cambios_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bitacora_cambios_tenant' AND object_id = OBJECT_ID(N'dbo.bitacora_cambios'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios (id_tenant);';
    -- La bitacora es de solo lectura (TR_bitacora_cambios_inmutable, script 014). Llenar id_tenant es la unica excepcion: el
    -- disparador se desactiva solo dentro de esta transaccion (si algo falla, el ROLLBACK lo deja activo) y se reactiva abajo.
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_bitacora_cambios_inmutable' AND parent_id = OBJECT_ID(N'dbo.bitacora_cambios') AND is_disabled = 0)
        EXEC sys.sp_executesql N'DISABLE TRIGGER dbo.TR_bitacora_cambios_inmutable ON dbo.bitacora_cambios;';
    EXEC sys.sp_executesql N'UPDATE b SET b.id_tenant = p.id_tenant FROM dbo.bitacora_cambios b JOIN dbo.personas p ON p.id_persona = b.id_persona WHERE b.id_tenant IS NULL AND p.id_tenant IS NOT NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;
    EXEC sys.sp_executesql N'UPDATE b SET b.id_tenant = e.id_tenant FROM dbo.bitacora_cambios b JOIN dbo.empresas e ON e.id_empresa = b.id_empresa WHERE b.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_bitacora_cambios_inmutable' AND parent_id = OBJECT_ID(N'dbo.bitacora_cambios') AND is_disabled = 1)
        EXEC sys.sp_executesql N'ENABLE TRIGGER dbo.TR_bitacora_cambios_inmutable ON dbo.bitacora_cambios;';

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.personas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: personas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_tenant' AND object_id = OBJECT_ID(N'dbo.personas'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de personas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_documentos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: persona_documentos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_documentos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_documentos_tenant' AND object_id = OBJECT_ID(N'dbo.persona_documentos'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de persona_documentos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_empresa WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: persona_empresa quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_empresa_tenant' AND object_id = OBJECT_ID(N'dbo.persona_empresa'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de persona_empresa.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empleados WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: empleados quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empleados_tenant' AND object_id = OBJECT_ID(N'dbo.empleados'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de empleados.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.clientes WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: clientes quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_clientes_tenant' AND object_id = OBJECT_ID(N'dbo.clientes'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de clientes.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.proveedores WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: proveedores quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_tenants' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_proveedores_tenant' AND object_id = OBJECT_ID(N'dbo.proveedores'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de proveedores.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.bitacora_cambios WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: bitacora_cambios quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50027, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_bitacora_cambios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.bitacora_cambios'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bitacora_cambios_tenant' AND object_id = OBJECT_ID(N'dbo.bitacora_cambios'))
        THROW 50027, N'POSTCHECK: falta la clave foranea o el indice de bitacora_cambios.id_tenant. Se revierte.', 1;
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.clientes c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de clientes (id_condicion_pago_default) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.clientes c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de clientes (id_persona_empresa, id_empresa, tipo_vinculo) apuntan a persona_empresa de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.empleados', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empleados c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de empleados (id_persona_empresa, id_empresa, tipo_vinculo) apuntan a persona_empresa de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.persona_documentos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_documentos c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de persona_documentos (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_empresa c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de persona_empresa (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.personas c JOIN dbo.personas p ON p.id_persona = c.id_persona_principal WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de personas (id_persona_principal) apuntan a personas de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.proveedores', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.proveedores c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de proveedores (id_condicion_pago_default) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.bitacora_cambios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.bitacora_cambios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de bitacora_cambios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50027, @msg, 1;
        END
    END
    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_bitacora_cambios_inmutable' AND parent_id = OBJECT_ID(N'dbo.bitacora_cambios') AND is_instead_of_trigger = 1 AND is_disabled = 0)
        THROW 50027, N'POSTCHECK: el disparador TR_bitacora_cambios_inmutable no quedo activo. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @llenadas = 0
        PRINT N'Nada que hacer: las columnas, claves e indices id_tenant de este grupo ya estaban aplicados y llenos.';
    ELSE
        PRINT CONCAT(N'CAMBIO 027 aplicado: ', @hacia, N' objeto(s) creados; ', @llenadas, N' fila(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: filas de cada tabla y cuantas siguen sin tenant (en una tabla mixta, las filas globales sin empresa deben quedar sin tenant)
SELECT r.tabla, r.filas, r.sin_tenant
FROM (
    SELECT N'personas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.personas
    UNION ALL
    SELECT N'persona_documentos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.persona_documentos
    UNION ALL
    SELECT N'persona_empresa' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.persona_empresa
    UNION ALL
    SELECT N'empleados' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.empleados
    UNION ALL
    SELECT N'clientes' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.clientes
    UNION ALL
    SELECT N'proveedores' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.proveedores
    UNION ALL
    SELECT N'bitacora_cambios' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.bitacora_cambios
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_bitacora_cambios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.bitacora_cambios')) ALTER TABLE dbo.bitacora_cambios DROP CONSTRAINT FK_bitacora_cambios_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bitacora_cambios_tenant' AND object_id = OBJECT_ID(N'dbo.bitacora_cambios')) DROP INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios;
    IF COL_LENGTH(N'dbo.bitacora_cambios', N'id_tenant') IS NOT NULL ALTER TABLE dbo.bitacora_cambios DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_tenants' AND parent_object_id = OBJECT_ID(N'dbo.proveedores')) ALTER TABLE dbo.proveedores DROP CONSTRAINT FK_proveedores_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_proveedores_tenant' AND object_id = OBJECT_ID(N'dbo.proveedores')) DROP INDEX IX_proveedores_tenant ON dbo.proveedores;
    IF COL_LENGTH(N'dbo.proveedores', N'id_tenant') IS NOT NULL ALTER TABLE dbo.proveedores DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes DROP CONSTRAINT FK_clientes_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_clientes_tenant' AND object_id = OBJECT_ID(N'dbo.clientes')) DROP INDEX IX_clientes_tenant ON dbo.clientes;
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL ALTER TABLE dbo.clientes DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_tenants' AND parent_object_id = OBJECT_ID(N'dbo.empleados')) ALTER TABLE dbo.empleados DROP CONSTRAINT FK_empleados_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empleados_tenant' AND object_id = OBJECT_ID(N'dbo.empleados')) DROP INDEX IX_empleados_tenant ON dbo.empleados;
    IF COL_LENGTH(N'dbo.empleados', N'id_tenant') IS NOT NULL ALTER TABLE dbo.empleados DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')) ALTER TABLE dbo.persona_empresa DROP CONSTRAINT FK_persona_empresa_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_empresa_tenant' AND object_id = OBJECT_ID(N'dbo.persona_empresa')) DROP INDEX IX_persona_empresa_tenant ON dbo.persona_empresa;
    IF COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL ALTER TABLE dbo.persona_empresa DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_documentos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos')) ALTER TABLE dbo.persona_documentos DROP CONSTRAINT FK_persona_documentos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_documentos_tenant' AND object_id = OBJECT_ID(N'dbo.persona_documentos')) DROP INDEX IX_persona_documentos_tenant ON dbo.persona_documentos;
    IF COL_LENGTH(N'dbo.persona_documentos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.persona_documentos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.personas')) ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_tenant' AND object_id = OBJECT_ID(N'dbo.personas')) DROP INDEX IX_personas_tenant ON dbo.personas;
    IF COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.personas DROP COLUMN id_tenant;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
