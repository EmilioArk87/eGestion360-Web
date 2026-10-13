-- ============================================================
-- Script   : 029_id_tenant_facturacion.sql
-- Proposito: Paso F1.2 del plan de la Fase 1 (ADR-001, ADR-019), expandir - grupo D, facturacion.
--            Agrega id_tenant INT NULL (con clave foranea a saas.tenants e indice) y la llena, en las tablas:
--              condiciones_pago, condiciones_pago_cuotas, formas_pago, impuestos, productos_servicios, facturas, factura_detalle, factura_secuencias, pagos, pago_aplicaciones
--            El grupo esta vacio hoy (0 filas): sirve para estrenar el patron sin datos. condiciones_pago_cuotas, factura_detalle y pago_aplicaciones heredan el tenant de su tabla madre.
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
IF OBJECT_ID(N'tempdb..#precheck_029') IS NOT NULL DROP TABLE #precheck_029;

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
IF OBJECT_ID(N'dbo.condiciones_pago', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.condiciones_pago.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' condiciones_pago.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.condiciones_pago t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' condiciones_pago: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.condiciones_pago_cuotas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.condiciones_pago_cuotas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' condiciones_pago_cuotas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.condiciones_pago_cuotas t WHERE NOT EXISTS (SELECT 1 FROM dbo.condiciones_pago p WHERE p.id_condicion_pago = t.id_condicion_pago);
    IF @n > 0 SET @errores += CONCAT(N' condiciones_pago_cuotas: ', @n, N' fila(s) sin su registro padre en condiciones_pago de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.formas_pago', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.formas_pago.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.formas_pago') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' formas_pago.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.formas_pago t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' formas_pago: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.impuestos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.impuestos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.impuestos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' impuestos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.impuestos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' impuestos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.productos_servicios', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.productos_servicios.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.productos_servicios') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' productos_servicios.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.productos_servicios t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' productos_servicios: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.facturas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.facturas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.facturas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' facturas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.facturas t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' facturas: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.factura_detalle', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.factura_detalle.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_detalle') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' factura_detalle.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.factura_detalle t WHERE NOT EXISTS (SELECT 1 FROM dbo.facturas p WHERE p.id_factura = t.id_factura);
    IF @n > 0 SET @errores += CONCAT(N' factura_detalle: ', @n, N' fila(s) sin su registro padre en facturas de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.factura_secuencias', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.factura_secuencias.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_secuencias') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' factura_secuencias.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.factura_secuencias t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' factura_secuencias: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.pagos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.pagos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pagos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' pagos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.pagos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' pagos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.pago_aplicaciones', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.pago_aplicaciones.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pago_aplicaciones') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' pago_aplicaciones.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.pago_aplicaciones t WHERE NOT EXISTS (SELECT 1 FROM dbo.pagos p WHERE p.id_pago = t.id_pago);
    IF @n > 0 SET @errores += CONCAT(N' pago_aplicaciones: ', @n, N' fila(s) sin su registro padre en pagos de donde deducir el tenant.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 029 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50029, @mensaje, 1;
END

CREATE TABLE #precheck_029 (ok BIT NOT NULL);
INSERT INTO #precheck_029 (ok) VALUES (1);

PRINT N'PRECHECK 029 correcto: 10 tabla(s) por revisar.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_029') IS NULL
    THROW 50029, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

-- Objetos por crear (columna, clave foranea e indice de cada tabla); 0 si todo ya estaba aplicado.
DECLARE @hacia INT = 0;
SELECT @hacia = SUM(y.pendientes) FROM (SELECT (CASE WHEN COL_LENGTH(N'dbo.' + x.nombre, N'id_tenant') IS NULL THEN 1 ELSE 0 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + x.nombre + N'_tenants' AND parent_object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + x.nombre + N'_tenant' AND object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END) AS pendientes
      FROM (VALUES (N'condiciones_pago'), (N'condiciones_pago_cuotas'), (N'formas_pago'), (N'impuestos'), (N'productos_servicios'), (N'facturas'), (N'factura_detalle'), (N'factura_secuencias'), (N'pagos'), (N'pago_aplicaciones')) x (nombre)) y;
DECLARE @llenadas INT = 0, @n INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- condiciones_pago
    IF COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago ADD CONSTRAINT FK_condiciones_pago_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.condiciones_pago t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- condiciones_pago_cuotas
    IF COL_LENGTH(N'dbo.condiciones_pago_cuotas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago_cuotas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_cuotas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago_cuotas ADD CONSTRAINT FK_condiciones_pago_cuotas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_cuotas_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = p.id_tenant FROM dbo.condiciones_pago_cuotas t JOIN dbo.condiciones_pago p ON p.id_condicion_pago = t.id_condicion_pago WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- formas_pago
    IF COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.formas_pago ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_formas_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.formas_pago ADD CONSTRAINT FK_formas_pago_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_formas_pago_tenant' AND object_id = OBJECT_ID(N'dbo.formas_pago'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_formas_pago_tenant ON dbo.formas_pago (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.formas_pago t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- impuestos
    IF COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.impuestos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_impuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.impuestos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.impuestos ADD CONSTRAINT FK_impuestos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_impuestos_tenant' AND object_id = OBJECT_ID(N'dbo.impuestos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_impuestos_tenant ON dbo.impuestos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.impuestos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- productos_servicios
    IF COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_productos_servicios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios ADD CONSTRAINT FK_productos_servicios_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_productos_servicios_tenant' AND object_id = OBJECT_ID(N'dbo.productos_servicios'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_productos_servicios_tenant ON dbo.productos_servicios (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.productos_servicios t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- facturas
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD CONSTRAINT FK_facturas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_facturas_tenant' AND object_id = OBJECT_ID(N'dbo.facturas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_facturas_tenant ON dbo.facturas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.facturas t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- factura_detalle
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_detalle_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FK_factura_detalle_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_detalle_tenant' AND object_id = OBJECT_ID(N'dbo.factura_detalle'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_factura_detalle_tenant ON dbo.factura_detalle (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = p.id_tenant FROM dbo.factura_detalle t JOIN dbo.facturas p ON p.id_factura = t.id_factura WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- factura_secuencias
    IF COL_LENGTH(N'dbo.factura_secuencias', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_secuencias ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_secuencias_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_secuencias ADD CONSTRAINT FK_factura_secuencias_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_secuencias_tenant' AND object_id = OBJECT_ID(N'dbo.factura_secuencias'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.factura_secuencias t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- pagos
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ADD CONSTRAINT FK_pagos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pagos_tenant' AND object_id = OBJECT_ID(N'dbo.pagos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_pagos_tenant ON dbo.pagos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.pagos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- pago_aplicaciones
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pago_aplicaciones_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones ADD CONSTRAINT FK_pago_aplicaciones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pago_aplicaciones_tenant' AND object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = p.id_tenant FROM dbo.pago_aplicaciones t JOIN dbo.pagos p ON p.id_pago = t.id_pago WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.condiciones_pago WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: condiciones_pago quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de condiciones_pago.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.condiciones_pago_cuotas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: condiciones_pago_cuotas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_cuotas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_cuotas_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de condiciones_pago_cuotas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.formas_pago WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: formas_pago quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_formas_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_formas_pago_tenant' AND object_id = OBJECT_ID(N'dbo.formas_pago'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de formas_pago.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.impuestos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: impuestos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_impuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.impuestos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_impuestos_tenant' AND object_id = OBJECT_ID(N'dbo.impuestos'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de impuestos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.productos_servicios WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: productos_servicios quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_productos_servicios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_productos_servicios_tenant' AND object_id = OBJECT_ID(N'dbo.productos_servicios'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de productos_servicios.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: facturas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_facturas_tenant' AND object_id = OBJECT_ID(N'dbo.facturas'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de facturas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: factura_detalle quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_detalle_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_detalle_tenant' AND object_id = OBJECT_ID(N'dbo.factura_detalle'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de factura_detalle.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_secuencias WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: factura_secuencias quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_secuencias_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_secuencias_tenant' AND object_id = OBJECT_ID(N'dbo.factura_secuencias'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de factura_secuencias.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pagos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: pagos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pagos_tenant' AND object_id = OBJECT_ID(N'dbo.pagos'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de pagos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pago_aplicaciones WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: pago_aplicaciones quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50029, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pago_aplicaciones_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pago_aplicaciones_tenant' AND object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
        THROW 50029, N'POSTCHECK: falta la clave foranea o el indice de pago_aplicaciones.id_tenant. Se revierte.', 1;
    IF COL_LENGTH(N'dbo.condiciones_pago_cuotas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.condiciones_pago_cuotas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de condiciones_pago_cuotas (id_condicion_pago) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_factura) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_impuesto) apuntan a impuestos de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.productos_servicios p ON p.id_producto = c.id_producto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_producto) apuntan a productos_servicios de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_condicion_pago) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_forma_pago) apuntan a formas_pago de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pago_aplicaciones (id_factura) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.pagos p ON p.id_pago = c.id_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pago_aplicaciones (id_pago) apuntan a pagos de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pagos c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pagos (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pagos c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pagos (id_forma_pago) apuntan a formas_pago de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.productos_servicios c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de productos_servicios (id_impuesto_default) apuntan a impuestos de otro tenant. Se revierte.');
            THROW 50029, @msg, 1;
        END
    END

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @llenadas = 0
        PRINT N'Nada que hacer: las columnas, claves e indices id_tenant de este grupo ya estaban aplicados y llenos.';
    ELSE
        PRINT CONCAT(N'CAMBIO 029 aplicado: ', @hacia, N' objeto(s) creados; ', @llenadas, N' fila(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: filas de cada tabla y cuantas siguen sin tenant (en una tabla mixta, las filas globales sin empresa deben quedar sin tenant)
SELECT r.tabla, r.filas, r.sin_tenant
FROM (
    SELECT N'condiciones_pago' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.condiciones_pago
    UNION ALL
    SELECT N'condiciones_pago_cuotas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.condiciones_pago_cuotas
    UNION ALL
    SELECT N'formas_pago' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.formas_pago
    UNION ALL
    SELECT N'impuestos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.impuestos
    UNION ALL
    SELECT N'productos_servicios' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.productos_servicios
    UNION ALL
    SELECT N'facturas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.facturas
    UNION ALL
    SELECT N'factura_detalle' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.factura_detalle
    UNION ALL
    SELECT N'factura_secuencias' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.factura_secuencias
    UNION ALL
    SELECT N'pagos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.pagos
    UNION ALL
    SELECT N'pago_aplicaciones' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.pago_aplicaciones
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pago_aplicaciones_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) ALTER TABLE dbo.pago_aplicaciones DROP CONSTRAINT FK_pago_aplicaciones_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pago_aplicaciones_tenant' AND object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) DROP INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones;
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NOT NULL ALTER TABLE dbo.pago_aplicaciones DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos DROP CONSTRAINT FK_pagos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pagos_tenant' AND object_id = OBJECT_ID(N'dbo.pagos')) DROP INDEX IX_pagos_tenant ON dbo.pagos;
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.pagos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_secuencias_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias')) ALTER TABLE dbo.factura_secuencias DROP CONSTRAINT FK_factura_secuencias_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_secuencias_tenant' AND object_id = OBJECT_ID(N'dbo.factura_secuencias')) DROP INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias;
    IF COL_LENGTH(N'dbo.factura_secuencias', N'id_tenant') IS NOT NULL ALTER TABLE dbo.factura_secuencias DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_detalle_tenants' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FK_factura_detalle_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_detalle_tenant' AND object_id = OBJECT_ID(N'dbo.factura_detalle')) DROP INDEX IX_factura_detalle_tenant ON dbo.factura_detalle;
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL ALTER TABLE dbo.factura_detalle DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas DROP CONSTRAINT FK_facturas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_facturas_tenant' AND object_id = OBJECT_ID(N'dbo.facturas')) DROP INDEX IX_facturas_tenant ON dbo.facturas;
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.facturas DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_productos_servicios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios')) ALTER TABLE dbo.productos_servicios DROP CONSTRAINT FK_productos_servicios_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_productos_servicios_tenant' AND object_id = OBJECT_ID(N'dbo.productos_servicios')) DROP INDEX IX_productos_servicios_tenant ON dbo.productos_servicios;
    IF COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NOT NULL ALTER TABLE dbo.productos_servicios DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_impuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.impuestos')) ALTER TABLE dbo.impuestos DROP CONSTRAINT FK_impuestos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_impuestos_tenant' AND object_id = OBJECT_ID(N'dbo.impuestos')) DROP INDEX IX_impuestos_tenant ON dbo.impuestos;
    IF COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.impuestos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_formas_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago')) ALTER TABLE dbo.formas_pago DROP CONSTRAINT FK_formas_pago_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_formas_pago_tenant' AND object_id = OBJECT_ID(N'dbo.formas_pago')) DROP INDEX IX_formas_pago_tenant ON dbo.formas_pago;
    IF COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NOT NULL ALTER TABLE dbo.formas_pago DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_cuotas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas')) ALTER TABLE dbo.condiciones_pago_cuotas DROP CONSTRAINT FK_condiciones_pago_cuotas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_cuotas_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas')) DROP INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas;
    IF COL_LENGTH(N'dbo.condiciones_pago_cuotas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.condiciones_pago_cuotas DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_tenants' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago')) ALTER TABLE dbo.condiciones_pago DROP CONSTRAINT FK_condiciones_pago_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago')) DROP INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago;
    IF COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL ALTER TABLE dbo.condiciones_pago DROP COLUMN id_tenant;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
