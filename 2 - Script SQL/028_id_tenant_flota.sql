-- ============================================================
-- Script   : 028_id_tenant_flota.sql
-- Proposito: Paso F1.2 del plan de la Fase 1 (ADR-001, ADR-019), expandir - grupo C, flota.
--            Agrega id_tenant INT NULL (con clave foranea a saas.tenants e indice) y la llena, en las tablas:
--              vehiculos, tipos_vehiculo, rutas, talleres, categorias_repuesto, odometro_diario, cargas_combustible, salarios_diarios, ordenes_mantenimiento, gastos_repuestos, polizas_seguros, peajes, control_salidas, notas, ingresos_operativos, precios_combustible
--            Todas las tablas de este grupo llevan id_empresa NOT NULL: el tenant sale de la empresa. ingresos_operativos y precios_combustible existen en la base pero no en el modelo de EF.
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
IF OBJECT_ID(N'tempdb..#precheck_028') IS NOT NULL DROP TABLE #precheck_028;

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
IF OBJECT_ID(N'dbo.vehiculos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.vehiculos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vehiculos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' vehiculos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.vehiculos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' vehiculos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.tipos_vehiculo', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.tipos_vehiculo.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tipos_vehiculo') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' tipos_vehiculo.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.tipos_vehiculo t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' tipos_vehiculo: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.rutas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.rutas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.rutas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' rutas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.rutas t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' rutas: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.talleres', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.talleres.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.talleres') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' talleres.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.talleres t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' talleres: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.categorias_repuesto', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.categorias_repuesto.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.categorias_repuesto') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' categorias_repuesto.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.categorias_repuesto t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' categorias_repuesto: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.odometro_diario', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.odometro_diario.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.odometro_diario') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' odometro_diario.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.odometro_diario t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' odometro_diario: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.cargas_combustible', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.cargas_combustible.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargas_combustible') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' cargas_combustible.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.cargas_combustible t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' cargas_combustible: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.salarios_diarios', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.salarios_diarios.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.salarios_diarios') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' salarios_diarios.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.salarios_diarios t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' salarios_diarios: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.ordenes_mantenimiento', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.ordenes_mantenimiento.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' ordenes_mantenimiento.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.ordenes_mantenimiento t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' ordenes_mantenimiento: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.gastos_repuestos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.gastos_repuestos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.gastos_repuestos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' gastos_repuestos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.gastos_repuestos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' gastos_repuestos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.polizas_seguros', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.polizas_seguros.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.polizas_seguros') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' polizas_seguros.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.polizas_seguros t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' polizas_seguros: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.peajes', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.peajes.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.peajes') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' peajes.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.peajes t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' peajes: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.control_salidas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.control_salidas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.control_salidas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' control_salidas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.control_salidas t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' control_salidas: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.notas', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.notas.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.notas') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' notas.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.notas t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' notas: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.ingresos_operativos', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.ingresos_operativos.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ingresos_operativos') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' ingresos_operativos.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.ingresos_operativos t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' ingresos_operativos: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.precios_combustible', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.precios_combustible.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.precios_combustible') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' precios_combustible.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.precios_combustible t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' precios_combustible: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 028 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50028, @mensaje, 1;
END

CREATE TABLE #precheck_028 (ok BIT NOT NULL);
INSERT INTO #precheck_028 (ok) VALUES (1);

PRINT N'PRECHECK 028 correcto: 16 tabla(s) por revisar.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_028') IS NULL
    THROW 50028, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

-- Objetos por crear (columna, clave foranea e indice de cada tabla); 0 si todo ya estaba aplicado.
DECLARE @hacia INT = 0;
SELECT @hacia = SUM(y.pendientes) FROM (SELECT (CASE WHEN COL_LENGTH(N'dbo.' + x.nombre, N'id_tenant') IS NULL THEN 1 ELSE 0 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + x.nombre + N'_tenants' AND parent_object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + x.nombre + N'_tenant' AND object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END) AS pendientes
      FROM (VALUES (N'vehiculos'), (N'tipos_vehiculo'), (N'rutas'), (N'talleres'), (N'categorias_repuesto'), (N'odometro_diario'), (N'cargas_combustible'), (N'salarios_diarios'), (N'ordenes_mantenimiento'), (N'gastos_repuestos'), (N'polizas_seguros'), (N'peajes'), (N'control_salidas'), (N'notas'), (N'ingresos_operativos'), (N'precios_combustible')) x (nombre)) y;
DECLARE @llenadas INT = 0, @n INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- vehiculos
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_vehiculos_tenant' AND object_id = OBJECT_ID(N'dbo.vehiculos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_vehiculos_tenant ON dbo.vehiculos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.vehiculos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- tipos_vehiculo
    IF COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tipos_vehiculo ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tipos_vehiculo_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tipos_vehiculo ADD CONSTRAINT FK_tipos_vehiculo_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tipos_vehiculo_tenant' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.tipos_vehiculo t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- rutas
    IF COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.rutas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_rutas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.rutas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.rutas ADD CONSTRAINT FK_rutas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_rutas_tenant' AND object_id = OBJECT_ID(N'dbo.rutas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_rutas_tenant ON dbo.rutas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.rutas t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- talleres
    IF COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.talleres ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_talleres_tenants' AND parent_object_id = OBJECT_ID(N'dbo.talleres'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.talleres ADD CONSTRAINT FK_talleres_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_talleres_tenant' AND object_id = OBJECT_ID(N'dbo.talleres'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_talleres_tenant ON dbo.talleres (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.talleres t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- categorias_repuesto
    IF COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.categorias_repuesto ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_categorias_repuesto_tenants' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.categorias_repuesto ADD CONSTRAINT FK_categorias_repuesto_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_categorias_repuesto_tenant' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.categorias_repuesto t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- odometro_diario
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_tenants' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_odometro_diario_tenant' AND object_id = OBJECT_ID(N'dbo.odometro_diario'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_odometro_diario_tenant ON dbo.odometro_diario (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.odometro_diario t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- cargas_combustible
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargas_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.cargas_combustible'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.cargas_combustible t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- salarios_diarios
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_salarios_diarios_tenant' AND object_id = OBJECT_ID(N'dbo.salarios_diarios'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.salarios_diarios t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- ordenes_mantenimiento
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ordenes_mantenimiento_tenant' AND object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.ordenes_mantenimiento t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- gastos_repuestos
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_gastos_repuestos_tenant' AND object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.gastos_repuestos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- polizas_seguros
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_tenants' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FK_polizas_seguros_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_polizas_seguros_tenant' AND object_id = OBJECT_ID(N'dbo.polizas_seguros'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.polizas_seguros t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- peajes
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD CONSTRAINT FK_peajes_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_peajes_tenant' AND object_id = OBJECT_ID(N'dbo.peajes'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_peajes_tenant ON dbo.peajes (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.peajes t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- control_salidas
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD CONSTRAINT FK_control_salidas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_control_salidas_tenant' AND object_id = OBJECT_ID(N'dbo.control_salidas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_control_salidas_tenant ON dbo.control_salidas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.control_salidas t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- notas
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ADD CONSTRAINT FK_notas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notas_tenant' AND object_id = OBJECT_ID(N'dbo.notas'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_notas_tenant ON dbo.notas (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.notas t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- ingresos_operativos
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FK_ingresos_operativos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ingresos_operativos_tenant' AND object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.ingresos_operativos t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- precios_combustible
    IF COL_LENGTH(N'dbo.precios_combustible', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FK_precios_combustible_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_precios_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.precios_combustible'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_precios_combustible_tenant ON dbo.precios_combustible (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.precios_combustible t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: vehiculos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_vehiculos_tenant' AND object_id = OBJECT_ID(N'dbo.vehiculos'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de vehiculos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.tipos_vehiculo WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: tipos_vehiculo quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tipos_vehiculo_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tipos_vehiculo_tenant' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de tipos_vehiculo.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.rutas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: rutas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_rutas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.rutas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_rutas_tenant' AND object_id = OBJECT_ID(N'dbo.rutas'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de rutas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.talleres WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: talleres quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_talleres_tenants' AND parent_object_id = OBJECT_ID(N'dbo.talleres'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_talleres_tenant' AND object_id = OBJECT_ID(N'dbo.talleres'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de talleres.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.categorias_repuesto WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: categorias_repuesto quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_categorias_repuesto_tenants' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_categorias_repuesto_tenant' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de categorias_repuesto.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: odometro_diario quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_tenants' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_odometro_diario_tenant' AND object_id = OBJECT_ID(N'dbo.odometro_diario'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de odometro_diario.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: cargas_combustible quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargas_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.cargas_combustible'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de cargas_combustible.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: salarios_diarios quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_salarios_diarios_tenant' AND object_id = OBJECT_ID(N'dbo.salarios_diarios'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de salarios_diarios.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: ordenes_mantenimiento quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ordenes_mantenimiento_tenant' AND object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de ordenes_mantenimiento.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: gastos_repuestos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_gastos_repuestos_tenant' AND object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de gastos_repuestos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.polizas_seguros WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: polizas_seguros quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_tenants' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_polizas_seguros_tenant' AND object_id = OBJECT_ID(N'dbo.polizas_seguros'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de polizas_seguros.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: peajes quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_peajes_tenant' AND object_id = OBJECT_ID(N'dbo.peajes'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de peajes.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: control_salidas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_control_salidas_tenant' AND object_id = OBJECT_ID(N'dbo.control_salidas'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de control_salidas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.notas WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: notas quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notas_tenant' AND object_id = OBJECT_ID(N'dbo.notas'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de notas.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ingresos_operativos WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: ingresos_operativos quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ingresos_operativos_tenant' AND object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de ingresos_operativos.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.precios_combustible WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: precios_combustible quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50028, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_precios_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.precios_combustible'))
        THROW 50028, N'POSTCHECK: falta la clave foranea o el indice de precios_combustible.id_tenant. Se revierte.', 1;
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.categorias_repuesto p ON p.id_categoria_repuesto = c.id_categoria_repuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_categoria_repuesto) apuntan a categorias_repuesto de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.categorias_repuesto p ON p.id_categoria_repuesto = c.id_categoria_repuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_categoria_repuesto) apuntan a categorias_repuesto de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ingresos_operativos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ingresos_operativos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.notas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de notas (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.notas c JOIN dbo.facturas p ON p.id_factura = c.id_factura_origen WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de notas (id_factura_origen) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.talleres p ON p.id_taller = c.id_taller WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_taller) apuntan a talleres de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.talleres p ON p.id_taller = c.id_taller WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_taller) apuntan a talleres de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de polizas_seguros (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de polizas_seguros (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.precios_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.sen_boletines', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.precios_combustible c JOIN dbo.sen_boletines p ON p.id_sen_boletin = c.id_sen_boletin WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de precios_combustible (id_sen_boletin) apuntan a sen_boletines de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.tipos_vehiculo p ON p.id_tipo_vehiculo = c.id_tipo_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_tipo_vehiculo) apuntan a tipos_vehiculo de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.tipos_vehiculo p ON p.id_tipo_vehiculo = c.id_tipo_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_tipo_vehiculo) apuntan a tipos_vehiculo de otro tenant. Se revierte.');
            THROW 50028, @msg, 1;
        END
    END

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @llenadas = 0
        PRINT N'Nada que hacer: las columnas, claves e indices id_tenant de este grupo ya estaban aplicados y llenos.';
    ELSE
        PRINT CONCAT(N'CAMBIO 028 aplicado: ', @hacia, N' objeto(s) creados; ', @llenadas, N' fila(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: filas de cada tabla y cuantas siguen sin tenant (en una tabla mixta, las filas globales sin empresa deben quedar sin tenant)
SELECT r.tabla, r.filas, r.sin_tenant
FROM (
    SELECT N'vehiculos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.vehiculos
    UNION ALL
    SELECT N'tipos_vehiculo' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.tipos_vehiculo
    UNION ALL
    SELECT N'rutas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.rutas
    UNION ALL
    SELECT N'talleres' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.talleres
    UNION ALL
    SELECT N'categorias_repuesto' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.categorias_repuesto
    UNION ALL
    SELECT N'odometro_diario' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.odometro_diario
    UNION ALL
    SELECT N'cargas_combustible' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.cargas_combustible
    UNION ALL
    SELECT N'salarios_diarios' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.salarios_diarios
    UNION ALL
    SELECT N'ordenes_mantenimiento' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.ordenes_mantenimiento
    UNION ALL
    SELECT N'gastos_repuestos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.gastos_repuestos
    UNION ALL
    SELECT N'polizas_seguros' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.polizas_seguros
    UNION ALL
    SELECT N'peajes' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.peajes
    UNION ALL
    SELECT N'control_salidas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.control_salidas
    UNION ALL
    SELECT N'notas' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.notas
    UNION ALL
    SELECT N'ingresos_operativos' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.ingresos_operativos
    UNION ALL
    SELECT N'precios_combustible' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.precios_combustible
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible')) ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FK_precios_combustible_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_precios_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.precios_combustible')) DROP INDEX IX_precios_combustible_tenant ON dbo.precios_combustible;
    IF COL_LENGTH(N'dbo.precios_combustible', N'id_tenant') IS NOT NULL ALTER TABLE dbo.precios_combustible DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FK_ingresos_operativos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ingresos_operativos_tenant' AND object_id = OBJECT_ID(N'dbo.ingresos_operativos')) DROP INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos;
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.ingresos_operativos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas DROP CONSTRAINT FK_notas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notas_tenant' AND object_id = OBJECT_ID(N'dbo.notas')) DROP INDEX IX_notas_tenant ON dbo.notas;
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.notas DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas DROP CONSTRAINT FK_control_salidas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_control_salidas_tenant' AND object_id = OBJECT_ID(N'dbo.control_salidas')) DROP INDEX IX_control_salidas_tenant ON dbo.control_salidas;
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.control_salidas DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_tenants' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes DROP CONSTRAINT FK_peajes_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_peajes_tenant' AND object_id = OBJECT_ID(N'dbo.peajes')) DROP INDEX IX_peajes_tenant ON dbo.peajes;
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL ALTER TABLE dbo.peajes DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_tenants' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FK_polizas_seguros_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_polizas_seguros_tenant' AND object_id = OBJECT_ID(N'dbo.polizas_seguros')) DROP INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros;
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NOT NULL ALTER TABLE dbo.polizas_seguros DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_gastos_repuestos_tenant' AND object_id = OBJECT_ID(N'dbo.gastos_repuestos')) DROP INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos;
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.gastos_repuestos DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_tenants' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ordenes_mantenimiento_tenant' AND object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) DROP INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento;
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL ALTER TABLE dbo.ordenes_mantenimiento DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_tenants' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_salarios_diarios_tenant' AND object_id = OBJECT_ID(N'dbo.salarios_diarios')) DROP INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios;
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL ALTER TABLE dbo.salarios_diarios DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_tenants' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargas_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.cargas_combustible')) DROP INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible;
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL ALTER TABLE dbo.cargas_combustible DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_tenants' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_odometro_diario_tenant' AND object_id = OBJECT_ID(N'dbo.odometro_diario')) DROP INDEX IX_odometro_diario_tenant ON dbo.odometro_diario;
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL ALTER TABLE dbo.odometro_diario DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_categorias_repuesto_tenants' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto')) ALTER TABLE dbo.categorias_repuesto DROP CONSTRAINT FK_categorias_repuesto_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_categorias_repuesto_tenant' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto')) DROP INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto;
    IF COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NOT NULL ALTER TABLE dbo.categorias_repuesto DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_talleres_tenants' AND parent_object_id = OBJECT_ID(N'dbo.talleres')) ALTER TABLE dbo.talleres DROP CONSTRAINT FK_talleres_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_talleres_tenant' AND object_id = OBJECT_ID(N'dbo.talleres')) DROP INDEX IX_talleres_tenant ON dbo.talleres;
    IF COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NOT NULL ALTER TABLE dbo.talleres DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_rutas_tenants' AND parent_object_id = OBJECT_ID(N'dbo.rutas')) ALTER TABLE dbo.rutas DROP CONSTRAINT FK_rutas_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_rutas_tenant' AND object_id = OBJECT_ID(N'dbo.rutas')) DROP INDEX IX_rutas_tenant ON dbo.rutas;
    IF COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL ALTER TABLE dbo.rutas DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tipos_vehiculo_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) ALTER TABLE dbo.tipos_vehiculo DROP CONSTRAINT FK_tipos_vehiculo_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tipos_vehiculo_tenant' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) DROP INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo;
    IF COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NOT NULL ALTER TABLE dbo.tipos_vehiculo DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tenants' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_vehiculos_tenant' AND object_id = OBJECT_ID(N'dbo.vehiculos')) DROP INDEX IX_vehiculos_tenant ON dbo.vehiculos;
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL ALTER TABLE dbo.vehiculos DROP COLUMN id_tenant;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
