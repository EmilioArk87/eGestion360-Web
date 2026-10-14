-- ============================================================
-- Script   : 032_id_tenant_contraer_personas.sql
-- Proposito: Paso F1.3b del plan de la Fase 1 (ADR-001, ADR-019, ADR-021), contraer - grupo B, personas.
--            Cierra en las tablas: personas, persona_documentos, persona_empresa, empleados, clientes, proveedores, bitacora_cambios
--              * id_tenant pasa a NOT NULL (en las tablas mixtas queda nulable: nulo es una fila global, ADR-015).
--              * Clave unica (id_tenant, ...) en las tablas que otras referencian.
--              * 8 clave(s) foranea(s) entre tablas de tenant pasan a COMPUESTAS (id_tenant, ...): una fila no puede
--                apuntar a la de otro tenant. Cada una reemplaza a la simple y se crea en el script del grupo que llega despues.
--              * UX_persona_documentos_identidad pasa a ser por tenant: (id_tenant, tipo, pais, numero normalizado) (ADR-004).
--            No toca datos. Las reglas de borrado en cascada se conservan; los indices por id_empresa no cambian.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 030 y 031 aplicados (en orden). La version de la aplicacion con F1.2 publicada: desde NOT NULL, quien inserte sin id_tenant falla
--            (la version anterior de la aplicacion deja de funcionar). Aplicar solo despues de una semana en produccion sin
--            filas con id_tenant nulo; el PRECHECK lo comprueba y aborta si no.
-- Rollback : ver la seccion ROLLBACK al final. Reabre id_tenant y vuelve a las claves foraneas simples.
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada): scripts anteriores aplicados, esquema esperado, ninguna fila sin tenant y ninguna fila
--      que apunte a otro tenant.
--   2. CAMBIO (una sola transaccion) y POSTCHECK dentro de ella: columnas NOT NULL, claves unicas y claves foraneas
--      compuestas de confianza (WITH CHECK). Si algo no cuadra, se revierte todo.
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
IF OBJECT_ID(N'tempdb..#precheck_032') IS NOT NULL DROP TABLE #precheck_032;

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;

-- a) los scripts 025 a 030 estan aplicados: toda tabla de tenant tiene id_tenant
SELECT @errores += N' Falta id_tenant en ' + v.t + N' (scripts 025 a 030).'
FROM (VALUES
    (N'empresas'),
    (N'empresa_modulos'),
    (N'empresa_roles'),
    (N'empresa_rol_permisos'),
    (N'cargos'),
    (N'personas'),
    (N'persona_documentos'),
    (N'persona_empresa'),
    (N'empleados'),
    (N'clientes'),
    (N'proveedores'),
    (N'bitacora_cambios'),
    (N'vehiculos'),
    (N'tipos_vehiculo'),
    (N'rutas'),
    (N'talleres'),
    (N'categorias_repuesto'),
    (N'odometro_diario'),
    (N'cargas_combustible'),
    (N'salarios_diarios'),
    (N'ordenes_mantenimiento'),
    (N'gastos_repuestos'),
    (N'polizas_seguros'),
    (N'peajes'),
    (N'control_salidas'),
    (N'notas'),
    (N'ingresos_operativos'),
    (N'precios_combustible'),
    (N'condiciones_pago'),
    (N'condiciones_pago_cuotas'),
    (N'formas_pago'),
    (N'impuestos'),
    (N'productos_servicios'),
    (N'facturas'),
    (N'factura_detalle'),
    (N'factura_secuencias'),
    (N'pagos'),
    (N'pago_aplicaciones'),
    (N'tasas_cambio'),
    (N'sen_boletines'),
    (N'domain_events')
) v (t)
WHERE OBJECT_ID(N'dbo.' + v.t, N'U') IS NULL OR COL_LENGTH(N'dbo.' + v.t, N'id_tenant') IS NULL;

-- b) los scripts de contraer anteriores ya crearon las claves unicas de las tablas de las que dependen
SELECT @errores += N' Falta aplicar el script ' + v.s + N' (' + v.i + N' en ' + v.t + N').'
FROM (VALUES
    (N'empresas', N'UX_empresas_tenant_id_empresa', N'031')
) v (t, i, s)
WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));

-- c) esquema esperado: de cada clave foranea existe la simple (por convertir) o la compuesta (ya convertida)
SELECT @errores += N' Falta la clave foranea ' + v.viejo + N' de ' + v.hijo + N'.'
FROM (VALUES
    (N'FK_clientes_empresa', N'FKT_clientes_empresas_id_empresa', N'clientes'),
    (N'FK_clientes_vinculo', N'FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo', N'clientes'),
    (N'FK_empleados_vinculo', N'FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo', N'empleados'),
    (N'FK_persona_documentos_persona', N'FKT_persona_documentos_personas_id_persona', N'persona_documentos'),
    (N'FK_persona_empresa_empresa', N'FKT_persona_empresa_empresas_id_empresa', N'persona_empresa'),
    (N'FK_persona_empresa_persona', N'FKT_persona_empresa_personas_id_persona', N'persona_empresa'),
    (N'FK_personas_principal', N'FKT_personas_personas_id_persona_principal', N'personas'),
    (N'FK_proveedores_empresa', N'FKT_proveedores_empresas_id_empresa', N'proveedores')
) v (viejo, nuevo, hijo)
WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name IN (v.viejo, v.nuevo) AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));

-- d) ninguna fila sin tenant y e) ninguna fila que apunte a otro tenant (solo si lo anterior esta en orden)
IF LEN(@errores) = 0
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.personas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' personas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.persona_documentos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' persona_documentos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.persona_empresa WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' persona_empresa: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empleados WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' empleados: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.clientes WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' clientes: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.proveedores WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' proveedores: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.bitacora_cambios WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' bitacora_cambios: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.clientes c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' clientes (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.clientes c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' clientes (id_persona_empresa, id_empresa, tipo_vinculo): ', @n, N' fila(s) apuntan a persona_empresa de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empleados c JOIN dbo.persona_empresa p ON p.id_persona_empresa = c.id_persona_empresa AND p.id_empresa = c.id_empresa AND p.tipo_vinculo = c.tipo_vinculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' empleados (id_persona_empresa, id_empresa, tipo_vinculo): ', @n, N' fila(s) apuntan a persona_empresa de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_documentos_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.persona_documentos c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' persona_documentos (id_persona): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.persona_empresa c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' persona_empresa (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.persona_empresa c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' persona_empresa (id_persona): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_personas_personas_id_persona_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.personas c JOIN dbo.personas p ON p.id_persona = c.id_persona_principal WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' personas (id_persona_principal): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.proveedores c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' proveedores (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 032 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50032, @mensaje, 1;
END

CREATE TABLE #precheck_032 (ok BIT NOT NULL);
INSERT INTO #precheck_032 (ok) VALUES (1);

PRINT N'PRECHECK 032 correcto: 7 tabla(s), 8 clave(s) foranea(s) por convertir.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_032') IS NULL
    THROW 50032, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @columnas INT = 0, @unicas INT = 0, @claves INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- personas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_tenant' AND object_id = OBJECT_ID(N'dbo.personas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_personas_tenant ON dbo.personas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_personas_tenant ON dbo.personas (id_tenant);';
        SET @columnas += 1;
    END
    -- persona_documentos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_documentos_tenant' AND object_id = OBJECT_ID(N'dbo.persona_documentos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_persona_documentos_tenant ON dbo.persona_documentos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_documentos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_persona_documentos_tenant ON dbo.persona_documentos (id_tenant);';
        SET @columnas += 1;
    END
    -- persona_empresa.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_empresa') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_empresa_tenant' AND object_id = OBJECT_ID(N'dbo.persona_empresa'))
            EXEC sys.sp_executesql N'DROP INDEX IX_persona_empresa_tenant ON dbo.persona_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_persona_empresa_tenant ON dbo.persona_empresa (id_tenant);';
        SET @columnas += 1;
    END
    -- empleados.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empleados') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empleados_tenant' AND object_id = OBJECT_ID(N'dbo.empleados'))
            EXEC sys.sp_executesql N'DROP INDEX IX_empleados_tenant ON dbo.empleados;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empleados ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_empleados_tenant ON dbo.empleados (id_tenant);';
        SET @columnas += 1;
    END
    -- clientes.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.clientes') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_clientes_tenant' AND object_id = OBJECT_ID(N'dbo.clientes'))
            EXEC sys.sp_executesql N'DROP INDEX IX_clientes_tenant ON dbo.clientes;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_clientes_tenant ON dbo.clientes (id_tenant);';
        SET @columnas += 1;
    END
    -- proveedores.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.proveedores') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_proveedores_tenant' AND object_id = OBJECT_ID(N'dbo.proveedores'))
            EXEC sys.sp_executesql N'DROP INDEX IX_proveedores_tenant ON dbo.proveedores;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_proveedores_tenant ON dbo.proveedores (id_tenant);';
        SET @columnas += 1;
    END
    -- bitacora_cambios.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bitacora_cambios_tenant' AND object_id = OBJECT_ID(N'dbo.bitacora_cambios'))
            EXEC sys.sp_executesql N'DROP INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.bitacora_cambios ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios (id_tenant);';
        SET @columnas += 1;
    END
    -- Unicidad por tenant del documento de identidad (ADR-004): el mismo documento puede existir en dos tenants.
    IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = N'UX_persona_documentos_identidad' AND i.object_id = OBJECT_ID(N'dbo.persona_documentos') AND c.name = N'id_tenant')
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_persona_documentos_identidad' AND object_id = OBJECT_ID(N'dbo.persona_documentos'))
            EXEC sys.sp_executesql N'DROP INDEX UX_persona_documentos_identidad ON dbo.persona_documentos;';
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_persona_documentos_identidad ON dbo.persona_documentos (id_tenant, tipo_documento, pais_emisor, numero_normalizado) WHERE eliminado = 0;';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_personas_tenant_id_persona' AND object_id = OBJECT_ID(N'dbo.personas'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_personas_tenant_id_persona ON dbo.personas (id_tenant, id_persona);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo' AND object_id = OBJECT_ID(N'dbo.persona_empresa'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo ON dbo.persona_empresa (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_clientes_tenant_id_cliente' AND object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_clientes_tenant_id_cliente ON dbo.clientes (id_tenant, id_cliente);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_empresa' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes DROP CONSTRAINT FK_clientes_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ADD CONSTRAINT FKT_clientes_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes DROP CONSTRAINT FK_clientes_vinculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ADD CONSTRAINT FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo FOREIGN KEY (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo) REFERENCES dbo.persona_empresa (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.empleados DROP CONSTRAINT FK_empleados_vinculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empleados ADD CONSTRAINT FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo FOREIGN KEY (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo) REFERENCES dbo.persona_empresa (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_documentos_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_documentos_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_documentos DROP CONSTRAINT FK_persona_documentos_persona;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_documentos ADD CONSTRAINT FKT_persona_documentos_personas_id_persona FOREIGN KEY (id_tenant, id_persona) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_empresa' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa DROP CONSTRAINT FK_persona_empresa_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa ADD CONSTRAINT FKT_persona_empresa_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa DROP CONSTRAINT FK_persona_empresa_persona;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.persona_empresa ADD CONSTRAINT FKT_persona_empresa_personas_id_persona FOREIGN KEY (id_tenant, id_persona) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_personas_personas_id_persona_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_principal;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.personas ADD CONSTRAINT FKT_personas_personas_id_persona_principal FOREIGN KEY (id_tenant, id_persona_principal) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_empresa' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores DROP CONSTRAINT FK_proveedores_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores ADD CONSTRAINT FKT_proveedores_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'personas'),
    (N'persona_documentos'),
    (N'persona_empresa'),
    (N'empleados'),
    (N'clientes'),
    (N'proveedores'),
    (N'bitacora_cambios')
    ) v (t) WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);
    IF @malas > 0 THROW 50032, N'POSTCHECK: alguna tabla del grupo conserva id_tenant nulable. Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'personas', N'UX_personas_tenant_id_persona'),
    (N'persona_empresa', N'UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo'),
    (N'clientes', N'UX_clientes_tenant_id_cliente')
    ) v (t, i) WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));
    IF @malas > 0 THROW 50032, N'POSTCHECK: falta alguna clave unica (id_tenant, ...). Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'FKT_clientes_empresas_id_empresa', N'FK_clientes_empresa', N'clientes'),
    (N'FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo', N'FK_clientes_vinculo', N'clientes'),
    (N'FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo', N'FK_empleados_vinculo', N'empleados'),
    (N'FKT_persona_documentos_personas_id_persona', N'FK_persona_documentos_persona', N'persona_documentos'),
    (N'FKT_persona_empresa_empresas_id_empresa', N'FK_persona_empresa_empresa', N'persona_empresa'),
    (N'FKT_persona_empresa_personas_id_persona', N'FK_persona_empresa_persona', N'persona_empresa'),
    (N'FKT_personas_personas_id_persona_principal', N'FK_personas_principal', N'personas'),
    (N'FKT_proveedores_empresas_id_empresa', N'FK_proveedores_empresa', N'proveedores')
    ) v (nuevo, viejo, hijo) WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nuevo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo) AND f.is_not_trusted = 0 AND f.is_disabled = 0)
       OR EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.viejo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));
    IF @malas > 0 THROW 50032, N'POSTCHECK: alguna clave foranea compuesta falta, no es de confianza o la simple sigue. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @columnas = 0 AND @unicas = 0 AND @claves = 0
        PRINT N'Nada que hacer: este grupo ya estaba contraido.';
    ELSE
        PRINT CONCAT(N'CAMBIO 032 aplicado: ', @columnas, N' columna(s) NOT NULL; ', @unicas, N' clave(s) unica(s); ', @claves, N' clave(s) foranea(s) compuesta(s).');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: estado de id_tenant y de las claves compuestas de cada tabla del grupo
SELECT t.name AS tabla, c.is_nullable AS id_tenant_nulable,
       (SELECT COUNT(*) FROM sys.foreign_keys f WHERE f.parent_object_id = t.object_id AND f.name LIKE N'FKT[_]%') AS fk_compuestas
FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'id_tenant'
WHERE t.name IN (N'personas', N'persona_documentos', N'persona_empresa', N'empleados', N'clientes', N'proveedores', N'bitacora_cambios')
ORDER BY t.name;
GO
-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Vuelve a las claves foraneas simples (con su regla de borrado original), quita las claves unicas (id_tenant, ...) y deja
--   id_tenant nulable. Revertir en orden inverso: 035, 034, 033, 032, 031 (las claves compuestas de un grupo pueden apoyarse
--   en claves unicas de otro).
--   Solo se puede volver al indice global de documentos si no hay un mismo documento en dos tenants.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.proveedores')) ALTER TABLE dbo.proveedores DROP CONSTRAINT FKT_proveedores_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_empresa' AND parent_object_id = OBJECT_ID(N'dbo.proveedores')) ALTER TABLE dbo.proveedores ADD CONSTRAINT FK_proveedores_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_personas_personas_id_persona_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas')) ALTER TABLE dbo.personas DROP CONSTRAINT FKT_personas_personas_id_persona_principal;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_principal' AND parent_object_id = OBJECT_ID(N'dbo.personas')) ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_principal FOREIGN KEY (id_persona_principal) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')) ALTER TABLE dbo.persona_empresa DROP CONSTRAINT FKT_persona_empresa_personas_id_persona;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')) ALTER TABLE dbo.persona_empresa ADD CONSTRAINT FK_persona_empresa_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_empresa_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')) ALTER TABLE dbo.persona_empresa DROP CONSTRAINT FKT_persona_empresa_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_empresa_empresa' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')) ALTER TABLE dbo.persona_empresa ADD CONSTRAINT FK_persona_empresa_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_persona_documentos_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos')) ALTER TABLE dbo.persona_documentos DROP CONSTRAINT FKT_persona_documentos_personas_id_persona;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_persona_documentos_persona' AND parent_object_id = OBJECT_ID(N'dbo.persona_documentos')) ALTER TABLE dbo.persona_documentos ADD CONSTRAINT FK_persona_documentos_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.empleados')) ALTER TABLE dbo.empleados DROP CONSTRAINT FKT_empleados_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.empleados')) ALTER TABLE dbo.empleados ADD CONSTRAINT FK_empleados_vinculo FOREIGN KEY (id_persona_empresa, id_empresa, tipo_vinculo) REFERENCES dbo.persona_empresa (id_persona_empresa, id_empresa, tipo_vinculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes DROP CONSTRAINT FKT_clientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_vinculo' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes ADD CONSTRAINT FK_clientes_vinculo FOREIGN KEY (id_persona_empresa, id_empresa, tipo_vinculo) REFERENCES dbo.persona_empresa (id_persona_empresa, id_empresa, tipo_vinculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes DROP CONSTRAINT FKT_clientes_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_empresa' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes ADD CONSTRAINT FK_clientes_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_personas_tenant_id_persona' AND object_id = OBJECT_ID(N'dbo.personas')) DROP INDEX UX_personas_tenant_id_persona ON dbo.personas;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo' AND object_id = OBJECT_ID(N'dbo.persona_empresa')) DROP INDEX UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo ON dbo.persona_empresa;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_clientes_tenant_id_cliente' AND object_id = OBJECT_ID(N'dbo.clientes')) DROP INDEX UX_clientes_tenant_id_cliente ON dbo.clientes;
    IF EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.name = N'UX_persona_documentos_identidad' AND i.object_id = OBJECT_ID(N'dbo.persona_documentos') AND c.name = N'id_tenant')
    BEGIN
        DROP INDEX UX_persona_documentos_identidad ON dbo.persona_documentos;
        CREATE UNIQUE INDEX UX_persona_documentos_identidad ON dbo.persona_documentos (tipo_documento, pais_emisor, numero_normalizado) WHERE eliminado = 0;
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.personas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_personas_tenant' AND object_id = OBJECT_ID(N'dbo.personas')) DROP INDEX IX_personas_tenant ON dbo.personas;
        ALTER TABLE dbo.personas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_personas_tenant ON dbo.personas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_documentos_tenant' AND object_id = OBJECT_ID(N'dbo.persona_documentos')) DROP INDEX IX_persona_documentos_tenant ON dbo.persona_documentos;
        ALTER TABLE dbo.persona_documentos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_persona_documentos_tenant ON dbo.persona_documentos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_empresa') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_persona_empresa_tenant' AND object_id = OBJECT_ID(N'dbo.persona_empresa')) DROP INDEX IX_persona_empresa_tenant ON dbo.persona_empresa;
        ALTER TABLE dbo.persona_empresa ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_persona_empresa_tenant ON dbo.persona_empresa (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empleados') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empleados_tenant' AND object_id = OBJECT_ID(N'dbo.empleados')) DROP INDEX IX_empleados_tenant ON dbo.empleados;
        ALTER TABLE dbo.empleados ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_empleados_tenant ON dbo.empleados (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.clientes') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_clientes_tenant' AND object_id = OBJECT_ID(N'dbo.clientes')) DROP INDEX IX_clientes_tenant ON dbo.clientes;
        ALTER TABLE dbo.clientes ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_clientes_tenant ON dbo.clientes (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.proveedores') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_proveedores_tenant' AND object_id = OBJECT_ID(N'dbo.proveedores')) DROP INDEX IX_proveedores_tenant ON dbo.proveedores;
        ALTER TABLE dbo.proveedores ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_proveedores_tenant ON dbo.proveedores (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.bitacora_cambios') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bitacora_cambios_tenant' AND object_id = OBJECT_ID(N'dbo.bitacora_cambios')) DROP INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios;
        ALTER TABLE dbo.bitacora_cambios ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_bitacora_cambios_tenant ON dbo.bitacora_cambios (id_tenant);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
