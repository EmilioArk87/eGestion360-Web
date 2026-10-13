-- ============================================================
-- Script   : 030_id_tenant_mixtas_e_infraestructura.sql
-- Proposito: Paso F1.2 del plan de la Fase 1 (ADR-001, ADR-019), expandir - tablas mixtas e infraestructura.
--            Agrega id_tenant INT NULL (con clave foranea a saas.tenants e indice) y la llena, en las tablas:
--              tasas_cambio, sen_boletines, domain_events
--            tasas_cambio y sen_boletines son tablas MIXTAS (ADR-015): id_tenant queda NULO en las filas globales (id_empresa nulo, como la tasa oficial del BCH)
--            y se llena en las que tienen empresa. domain_events es infraestructura y siempre lleva tenant.
--            Este script cierra F1.2: exige que 026 a 029 esten aplicados y comprueba TODAS las claves foraneas entre tablas de tenant (que ninguna fila apunte
--            a una fila de otro tenant) y que ninguna tabla con id_empresa se quede sin id_tenant.
--            No toca ninguna otra columna ni ningun dato de negocio. La columna sigue NULABLE (ADR-001: expandir -> migrar ->
--            contraer): F1.3 la vuelve NOT NULL cuando la aplicacion ya la llena en cada insercion.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-12
-- BD       : eBD_SPD
-- Requiere : 025 y 026 a 029 aplicados (este script cierra F1.2).
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
IF OBJECT_ID(N'tempdb..#precheck_030') IS NOT NULL DROP TABLE #precheck_030;

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

-- a2) los scripts 026 a 029 estan aplicados: todas las demas tablas de tenant ya tienen id_tenant
IF COL_LENGTH(N'dbo.empresa_modulos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en empresa_modulos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.empresa_roles', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en empresa_roles (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.empresa_rol_permisos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en empresa_rol_permisos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.cargos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en cargos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.personas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en personas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.persona_documentos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en persona_documentos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en persona_empresa (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.empleados', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en empleados (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en clientes (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.proveedores', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en proveedores (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.bitacora_cambios', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en bitacora_cambios (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en vehiculos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en tipos_vehiculo (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en rutas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en talleres (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en categorias_repuesto (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en odometro_diario (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en cargas_combustible (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en salarios_diarios (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en ordenes_mantenimiento (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en gastos_repuestos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en polizas_seguros (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en peajes (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en control_salidas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en notas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en ingresos_operativos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.precios_combustible', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en precios_combustible (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en condiciones_pago (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.condiciones_pago_cuotas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en condiciones_pago_cuotas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en formas_pago (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en impuestos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en productos_servicios (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en facturas (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en factura_detalle (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.factura_secuencias', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en factura_secuencias (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en pagos (scripts 026 a 029).';
IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NULL SET @errores += N' Falta id_tenant en pago_aplicaciones (scripts 026 a 029).';

-- b) cada tabla existe, su id_tenant (si ya existe) es INT y toda fila tiene de donde deducir su tenant
IF OBJECT_ID(N'dbo.tasas_cambio', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.tasas_cambio.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tasas_cambio') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' tasas_cambio.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.tasas_cambio t WHERE t.id_empresa IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' tasas_cambio: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.sen_boletines', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.sen_boletines.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.sen_boletines') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' sen_boletines.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.sen_boletines t WHERE t.id_empresa IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' sen_boletines: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END
IF OBJECT_ID(N'dbo.domain_events', N'U') IS NULL
    SET @errores += N' Falta la tabla dbo.domain_events.';
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.domain_events') AND name = N'id_tenant' AND system_type_id <> 56)
        SET @errores += N' domain_events.id_tenant ya existe con un tipo distinto de INT.';
    SELECT @n = COUNT(*) FROM dbo.domain_events t WHERE NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = t.id_empresa);
    IF @n > 0 SET @errores += CONCAT(N' domain_events: ', @n, N' fila(s) sin una empresa de donde deducir el tenant.');
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 030 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50030, @mensaje, 1;
END

CREATE TABLE #precheck_030 (ok BIT NOT NULL);
INSERT INTO #precheck_030 (ok) VALUES (1);

PRINT N'PRECHECK 030 correcto: 3 tabla(s) por revisar.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_030') IS NULL
    THROW 50030, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

-- Objetos por crear (columna, clave foranea e indice de cada tabla); 0 si todo ya estaba aplicado.
DECLARE @hacia INT = 0;
SELECT @hacia = SUM(y.pendientes) FROM (SELECT (CASE WHEN COL_LENGTH(N'dbo.' + x.nombre, N'id_tenant') IS NULL THEN 1 ELSE 0 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + x.nombre + N'_tenants' AND parent_object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + x.nombre + N'_tenant' AND object_id = OBJECT_ID(N'dbo.' + x.nombre)) THEN 0 ELSE 1 END) AS pendientes
      FROM (VALUES (N'tasas_cambio'), (N'sen_boletines'), (N'domain_events')) x (nombre)) y;
DECLARE @llenadas INT = 0, @n INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- tasas_cambio
    IF COL_LENGTH(N'dbo.tasas_cambio', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tasas_cambio_tenant' AND object_id = OBJECT_ID(N'dbo.tasas_cambio'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_tasas_cambio_tenant ON dbo.tasas_cambio (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.tasas_cambio t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- sen_boletines
    IF COL_LENGTH(N'dbo.sen_boletines', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.sen_boletines ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sen_boletines_tenants' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.sen_boletines ADD CONSTRAINT FK_sen_boletines_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sen_boletines_tenant' AND object_id = OBJECT_ID(N'dbo.sen_boletines'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_sen_boletines_tenant ON dbo.sen_boletines (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.sen_boletines t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- domain_events
    IF COL_LENGTH(N'dbo.domain_events', N'id_tenant') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.domain_events ADD id_tenant INT NULL;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_domain_events_tenants' AND parent_object_id = OBJECT_ID(N'dbo.domain_events'))
        EXEC sys.sp_executesql N'ALTER TABLE dbo.domain_events ADD CONSTRAINT FK_domain_events_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant);';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_domain_events_tenant' AND object_id = OBJECT_ID(N'dbo.domain_events'))
        EXEC sys.sp_executesql N'CREATE INDEX IX_domain_events_tenant ON dbo.domain_events (id_tenant);';
    EXEC sys.sp_executesql N'UPDATE t SET t.id_tenant = e.id_tenant FROM dbo.domain_events t JOIN dbo.empresas e ON e.id_empresa = t.id_empresa WHERE t.id_tenant IS NULL; SET @n = @@ROWCOUNT;', N'@n INT OUTPUT', @n = @n OUTPUT;
    SET @llenadas += @n;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.tasas_cambio WHERE id_tenant IS NULL AND id_empresa IS NOT NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: tasas_cambio quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50030, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tasas_cambio_tenant' AND object_id = OBJECT_ID(N'dbo.tasas_cambio'))
        THROW 50030, N'POSTCHECK: falta la clave foranea o el indice de tasas_cambio.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.sen_boletines WHERE id_tenant IS NULL AND id_empresa IS NOT NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: sen_boletines quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50030, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sen_boletines_tenants' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sen_boletines_tenant' AND object_id = OBJECT_ID(N'dbo.sen_boletines'))
        THROW 50030, N'POSTCHECK: falta la clave foranea o el indice de sen_boletines.id_tenant. Se revierte.', 1;
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.domain_events WHERE id_tenant IS NULL;', N'@m INT OUTPUT', @m = @malas OUTPUT;
    IF @malas > 0
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: domain_events quedo con ', @malas, N' fila(s) sin tenant. Se revierte.');
        THROW 50030, @msg, 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_domain_events_tenants' AND parent_object_id = OBJECT_ID(N'dbo.domain_events'))
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_domain_events_tenant' AND object_id = OBJECT_ID(N'dbo.domain_events'))
        THROW 50030, N'POSTCHECK: falta la clave foranea o el indice de domain_events.id_tenant. Se revierte.', 1;
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.cargas_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de cargas_combustible (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.clientes c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de clientes (id_condicion_pago_default) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.clientes c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de clientes (id_persona_empresa, id_empresa, tipo_vinculo) apuntan a persona_empresa de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.condiciones_pago_cuotas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.condiciones_pago_cuotas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de condiciones_pago_cuotas (id_condicion_pago) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.control_salidas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.control_salidas c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de control_salidas (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.empleados', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empleados c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de empleados (id_persona_empresa, id_empresa, tipo_vinculo) apuntan a persona_empresa de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.empresa_rol_permisos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.empresa_roles', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.empresa_rol_permisos c JOIN dbo.empresa_roles p ON p.id_rol = c.id_rol WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de empresa_rol_permisos (id_rol) apuntan a empresa_roles de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_factura) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_impuesto) apuntan a impuestos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.factura_detalle', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.productos_servicios p ON p.id_producto = c.id_producto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de factura_detalle (id_producto) apuntan a productos_servicios de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_condicion_pago) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.facturas c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de facturas (id_forma_pago) apuntan a formas_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.categorias_repuesto p ON p.id_categoria_repuesto = c.id_categoria_repuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_categoria_repuesto) apuntan a categorias_repuesto de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.categorias_repuesto', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.categorias_repuesto p ON p.id_categoria_repuesto = c.id_categoria_repuesto WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_categoria_repuesto) apuntan a categorias_repuesto de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.gastos_repuestos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de gastos_repuestos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ingresos_operativos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ingresos_operativos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ingresos_operativos (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.notas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de notas (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.notas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.notas c JOIN dbo.facturas p ON p.id_factura = c.id_factura_origen WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de notas (id_factura_origen) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.odometro_diario', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de odometro_diario (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.talleres p ON p.id_taller = c.id_taller WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_taller) apuntan a talleres de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.talleres', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.talleres p ON p.id_taller = c.id_taller WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_taller) apuntan a talleres de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.ordenes_mantenimiento', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de ordenes_mantenimiento (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.facturas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pago_aplicaciones (id_factura) apuntan a facturas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pago_aplicaciones', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.pagos p ON p.id_pago = c.id_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pago_aplicaciones (id_pago) apuntan a pagos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.clientes', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pagos c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pagos (id_cliente) apuntan a clientes de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.pagos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.formas_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.pagos c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de pagos (id_forma_pago) apuntan a formas_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_conductor) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.peajes', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.peajes c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de peajes (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.persona_documentos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_documentos c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de persona_documentos (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.persona_empresa', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.persona_empresa c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de persona_empresa (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.personas c JOIN dbo.personas p ON p.id_persona = c.id_persona_principal WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de personas (id_persona_principal) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de polizas_seguros (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.polizas_seguros', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de polizas_seguros (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.precios_combustible', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.sen_boletines', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.precios_combustible c JOIN dbo.sen_boletines p ON p.id_sen_boletin = c.id_sen_boletin WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de precios_combustible (id_sen_boletin) apuntan a sen_boletines de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.productos_servicios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.impuestos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.productos_servicios c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de productos_servicios (id_impuesto_default) apuntan a impuestos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.proveedores', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.condiciones_pago', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.proveedores c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de proveedores (id_condicion_pago_default) apuntan a condiciones_pago de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.salarios_diarios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de salarios_diarios (id_vehiculo) apuntan a vehiculos de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.tasas_cambio', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.tasas_cambio', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.tasas_cambio c JOIN dbo.tasas_cambio p ON p.id_tasa_cambio = c.id_tasa_anterior WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de tasas_cambio (id_tasa_anterior) apuntan a tasas_cambio de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.rutas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_ruta) apuntan a rutas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.tipos_vehiculo p ON p.id_tipo_vehiculo = c.id_tipo_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_tipo_vehiculo) apuntan a tipos_vehiculo de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.vehiculos', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.tipos_vehiculo', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.vehiculos c JOIN dbo.tipos_vehiculo p ON p.id_tipo_vehiculo = c.id_tipo_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de vehiculos (id_tipo_vehiculo) apuntan a tipos_vehiculo de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    IF COL_LENGTH(N'dbo.bitacora_cambios', N'id_tenant') IS NOT NULL AND COL_LENGTH(N'dbo.personas', N'id_tenant') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM dbo.bitacora_cambios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@m INT OUTPUT', @m = @malas OUTPUT;
        IF @malas > 0
        BEGIN
            SET @msg = CONCAT(N'POSTCHECK: ', @malas, N' fila(s) de bitacora_cambios (id_persona) apuntan a personas de otro tenant. Se revierte.');
            THROW 50030, @msg, 1;
        END
    END
    -- Ninguna tabla con id_empresa se queda sin id_tenant (las copias respaldo_* del legado no cuentan).
    DECLARE @sinColumna NVARCHAR(MAX) = N'';
    SELECT @sinColumna += N' ' + t.name
    FROM sys.tables t
    WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name NOT LIKE N'respaldo[_]%'
      AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'id_empresa')
      AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'id_tenant');
    IF LEN(@sinColumna) > 0
    BEGIN
        SET @msg = LEFT(N'POSTCHECK: tablas con id_empresa y sin id_tenant:' + @sinColumna + N'. Se revierte.', 2048);
        THROW 50030, @msg, 1;
    END

    COMMIT TRANSACTION;

    IF @hacia = 0 AND @llenadas = 0
        PRINT N'Nada que hacer: las columnas, claves e indices id_tenant de este grupo ya estaban aplicados y llenos.';
    ELSE
        PRINT CONCAT(N'CAMBIO 030 aplicado: ', @hacia, N' objeto(s) creados; ', @llenadas, N' fila(s) con id_tenant llenado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: filas de cada tabla y cuantas siguen sin tenant (en una tabla mixta, las filas globales sin empresa deben quedar sin tenant)
SELECT r.tabla, r.filas, r.sin_tenant
FROM (
    SELECT N'tasas_cambio' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.tasas_cambio
    UNION ALL
    SELECT N'sen_boletines' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.sen_boletines
    UNION ALL
    SELECT N'domain_events' AS tabla, COUNT(*) AS filas, COALESCE(SUM(CASE WHEN id_tenant IS NULL THEN 1 ELSE 0 END), 0) AS sin_tenant FROM dbo.domain_events
) r
ORDER BY r.tabla;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita, tabla por tabla, el indice, la clave foranea y la columna id_tenant. No pierde datos de negocio: el tenant de cada
--   fila se puede volver a deducir con este mismo script. Solo vale antes de F1.3 (despues, la columna es NOT NULL y la usan claves e indices).
--   Revertir 030 antes que 029, 029 antes que 028, etc.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_domain_events_tenants' AND parent_object_id = OBJECT_ID(N'dbo.domain_events')) ALTER TABLE dbo.domain_events DROP CONSTRAINT FK_domain_events_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_domain_events_tenant' AND object_id = OBJECT_ID(N'dbo.domain_events')) DROP INDEX IX_domain_events_tenant ON dbo.domain_events;
    IF COL_LENGTH(N'dbo.domain_events', N'id_tenant') IS NOT NULL ALTER TABLE dbo.domain_events DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sen_boletines_tenants' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines')) ALTER TABLE dbo.sen_boletines DROP CONSTRAINT FK_sen_boletines_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sen_boletines_tenant' AND object_id = OBJECT_ID(N'dbo.sen_boletines')) DROP INDEX IX_sen_boletines_tenant ON dbo.sen_boletines;
    IF COL_LENGTH(N'dbo.sen_boletines', N'id_tenant') IS NOT NULL ALTER TABLE dbo.sen_boletines DROP COLUMN id_tenant;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_tenants' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')) ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_tenants;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tasas_cambio_tenant' AND object_id = OBJECT_ID(N'dbo.tasas_cambio')) DROP INDEX IX_tasas_cambio_tenant ON dbo.tasas_cambio;
    IF COL_LENGTH(N'dbo.tasas_cambio', N'id_tenant') IS NOT NULL ALTER TABLE dbo.tasas_cambio DROP COLUMN id_tenant;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
