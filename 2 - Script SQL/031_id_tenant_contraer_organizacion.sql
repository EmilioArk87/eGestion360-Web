-- ============================================================
-- Script   : 031_id_tenant_contraer_organizacion.sql
-- Proposito: Paso F1.3b del plan de la Fase 1 (ADR-001, ADR-019, ADR-021), contraer - grupo A, organizacion.
--            Cierra en las tablas: empresas, empresa_modulos, empresa_roles, empresa_rol_permisos, cargos
--              * id_tenant pasa a NOT NULL (en las tablas mixtas queda nulable: nulo es una fila global, ADR-015).
--              * Clave unica (id_tenant, ...) en las tablas que otras referencian.
--              * 4 clave(s) foranea(s) entre tablas de tenant pasan a COMPUESTAS (id_tenant, ...): una fila no puede
--                apuntar a la de otro tenant. Cada una reemplaza a la simple y se crea en el script del grupo que llega despues.
--            No toca datos. Las reglas de borrado en cascada se conservan; los indices por id_empresa no cambian.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 030 aplicados. La version de la aplicacion con F1.2 publicada: desde NOT NULL, quien inserte sin id_tenant falla
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
IF OBJECT_ID(N'tempdb..#precheck_031') IS NOT NULL DROP TABLE #precheck_031;

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

-- c) esquema esperado: de cada clave foranea existe la simple (por convertir) o la compuesta (ya convertida)
SELECT @errores += N' Falta la clave foranea ' + v.viejo + N' de ' + v.hijo + N'.'
FROM (VALUES
    (N'FK_cargos_empresa', N'FKT_cargos_empresas_id_empresa', N'cargos'),
    (N'FK_empresa_modulos_empresas_id_empresa', N'FKT_empresa_modulos_empresas_id_empresa', N'empresa_modulos'),
    (N'FK_empresa_rol_permisos_empresa_roles_id_rol', N'FKT_empresa_rol_permisos_empresa_roles_id_rol', N'empresa_rol_permisos'),
    (N'FK_empresa_roles_empresas_id_empresa', N'FKT_empresa_roles_empresas_id_empresa', N'empresa_roles')
) v (viejo, nuevo, hijo)
WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name IN (v.viejo, v.nuevo) AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));

-- d) ninguna fila sin tenant y e) ninguna fila que apunte a otro tenant (solo si lo anterior esta en orden)
IF LEN(@errores) = 0
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' empresas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_modulos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' empresa_modulos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_roles WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' empresa_roles: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_rol_permisos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' empresa_rol_permisos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' cargos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' cargos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_modulos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_modulos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' empresa_modulos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_rol_permisos_empresa_roles_id_rol' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_rol_permisos c JOIN dbo.empresa_roles p ON p.id_rol = c.id_rol WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' empresa_rol_permisos (id_rol): ', @n, N' fila(s) apuntan a empresa_roles de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_roles_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.empresa_roles c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' empresa_roles (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 031 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50031, @mensaje, 1;
END

CREATE TABLE #precheck_031 (ok BIT NOT NULL);
INSERT INTO #precheck_031 (ok) VALUES (1);

PRINT N'PRECHECK 031 correcto: 5 tabla(s), 4 clave(s) foranea(s) por convertir.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_031') IS NULL
    THROW 50031, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @columnas INT = 0, @unicas INT = 0, @claves INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- empresas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_empresas_tenant ON dbo.empresas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresas_tenant ON dbo.empresas (id_tenant);';
        SET @columnas += 1;
    END
    -- empresa_modulos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_modulos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_modulos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_modulos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_modulos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos (id_tenant);';
        SET @columnas += 1;
    END
    -- empresa_roles.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_roles') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_roles_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_roles'))
            EXEC sys.sp_executesql N'DROP INDEX IX_empresa_roles_tenant ON dbo.empresa_roles;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_roles ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_roles_tenant ON dbo.empresa_roles (id_tenant);';
        SET @columnas += 1;
    END
    -- empresa_rol_permisos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_rol_permisos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_rol_permisos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_rol_permisos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos (id_tenant);';
        SET @columnas += 1;
    END
    -- cargos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargos_tenant' AND object_id = OBJECT_ID(N'dbo.cargos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_cargos_tenant ON dbo.cargos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_cargos_tenant ON dbo.cargos (id_tenant);';
        SET @columnas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresas_tenant_id_empresa' AND object_id = OBJECT_ID(N'dbo.empresas'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_empresas_tenant_id_empresa ON dbo.empresas (id_tenant, id_empresa);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresa_roles_tenant_id_rol' AND object_id = OBJECT_ID(N'dbo.empresa_roles'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_empresa_roles_tenant_id_rol ON dbo.empresa_roles (id_tenant, id_rol);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargos DROP CONSTRAINT FK_cargos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargos ADD CONSTRAINT FKT_cargos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_modulos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_modulos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_modulos DROP CONSTRAINT FK_empresa_modulos_empresas_id_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_modulos ADD CONSTRAINT FKT_empresa_modulos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_rol_permisos_empresa_roles_id_rol' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_rol_permisos_empresa_roles_id_rol' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_rol_permisos DROP CONSTRAINT FK_empresa_rol_permisos_empresa_roles_id_rol;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_rol_permisos ADD CONSTRAINT FKT_empresa_rol_permisos_empresa_roles_id_rol FOREIGN KEY (id_tenant, id_rol) REFERENCES dbo.empresa_roles (id_tenant, id_rol) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_roles_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_roles_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_roles DROP CONSTRAINT FK_empresa_roles_empresas_id_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.empresa_roles ADD CONSTRAINT FKT_empresa_roles_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'empresas'),
    (N'empresa_modulos'),
    (N'empresa_roles'),
    (N'empresa_rol_permisos'),
    (N'cargos')
    ) v (t) WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);
    IF @malas > 0 THROW 50031, N'POSTCHECK: alguna tabla del grupo conserva id_tenant nulable. Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'empresas', N'UX_empresas_tenant_id_empresa'),
    (N'empresa_roles', N'UX_empresa_roles_tenant_id_rol')
    ) v (t, i) WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));
    IF @malas > 0 THROW 50031, N'POSTCHECK: falta alguna clave unica (id_tenant, ...). Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'FKT_cargos_empresas_id_empresa', N'FK_cargos_empresa', N'cargos'),
    (N'FKT_empresa_modulos_empresas_id_empresa', N'FK_empresa_modulos_empresas_id_empresa', N'empresa_modulos'),
    (N'FKT_empresa_rol_permisos_empresa_roles_id_rol', N'FK_empresa_rol_permisos_empresa_roles_id_rol', N'empresa_rol_permisos'),
    (N'FKT_empresa_roles_empresas_id_empresa', N'FK_empresa_roles_empresas_id_empresa', N'empresa_roles')
    ) v (nuevo, viejo, hijo) WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nuevo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo) AND f.is_not_trusted = 0 AND f.is_disabled = 0)
       OR EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.viejo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));
    IF @malas > 0 THROW 50031, N'POSTCHECK: alguna clave foranea compuesta falta, no es de confianza o la simple sigue. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @columnas = 0 AND @unicas = 0 AND @claves = 0
        PRINT N'Nada que hacer: este grupo ya estaba contraido.';
    ELSE
        PRINT CONCAT(N'CAMBIO 031 aplicado: ', @columnas, N' columna(s) NOT NULL; ', @unicas, N' clave(s) unica(s); ', @claves, N' clave(s) foranea(s) compuesta(s).');
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
WHERE t.name IN (N'empresas', N'empresa_modulos', N'empresa_roles', N'empresa_rol_permisos', N'cargos')
ORDER BY t.name;
GO
-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Vuelve a las claves foraneas simples (con su regla de borrado original), quita las claves unicas (id_tenant, ...) y deja
--   id_tenant nulable. Revertir en orden inverso: 035, 034, 033, 032, 031 (las claves compuestas de un grupo pueden apoyarse
--   en claves unicas de otro).
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_roles_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles')) ALTER TABLE dbo.empresa_roles DROP CONSTRAINT FKT_empresa_roles_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_roles_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_roles')) ALTER TABLE dbo.empresa_roles ADD CONSTRAINT FK_empresa_roles_empresas_id_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_rol_permisos_empresa_roles_id_rol' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos')) ALTER TABLE dbo.empresa_rol_permisos DROP CONSTRAINT FKT_empresa_rol_permisos_empresa_roles_id_rol;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_rol_permisos_empresa_roles_id_rol' AND parent_object_id = OBJECT_ID(N'dbo.empresa_rol_permisos')) ALTER TABLE dbo.empresa_rol_permisos ADD CONSTRAINT FK_empresa_rol_permisos_empresa_roles_id_rol FOREIGN KEY (id_rol) REFERENCES dbo.empresa_roles (id_rol) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_empresa_modulos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos')) ALTER TABLE dbo.empresa_modulos DROP CONSTRAINT FKT_empresa_modulos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresa_modulos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.empresa_modulos')) ALTER TABLE dbo.empresa_modulos ADD CONSTRAINT FK_empresa_modulos_empresas_id_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargos')) ALTER TABLE dbo.cargos DROP CONSTRAINT FKT_cargos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargos')) ALTER TABLE dbo.cargos ADD CONSTRAINT FK_cargos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresas_tenant_id_empresa' AND object_id = OBJECT_ID(N'dbo.empresas')) DROP INDEX UX_empresas_tenant_id_empresa ON dbo.empresas;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresa_roles_tenant_id_rol' AND object_id = OBJECT_ID(N'dbo.empresa_roles')) DROP INDEX UX_empresa_roles_tenant_id_rol ON dbo.empresa_roles;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresas_tenant' AND object_id = OBJECT_ID(N'dbo.empresas')) DROP INDEX IX_empresas_tenant ON dbo.empresas;
        ALTER TABLE dbo.empresas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_empresas_tenant ON dbo.empresas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_modulos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_modulos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_modulos')) DROP INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos;
        ALTER TABLE dbo.empresa_modulos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_empresa_modulos_tenant ON dbo.empresa_modulos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_roles') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_roles_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_roles')) DROP INDEX IX_empresa_roles_tenant ON dbo.empresa_roles;
        ALTER TABLE dbo.empresa_roles ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_empresa_roles_tenant ON dbo.empresa_roles (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.empresa_rol_permisos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_empresa_rol_permisos_tenant' AND object_id = OBJECT_ID(N'dbo.empresa_rol_permisos')) DROP INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos;
        ALTER TABLE dbo.empresa_rol_permisos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_empresa_rol_permisos_tenant ON dbo.empresa_rol_permisos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargos_tenant' AND object_id = OBJECT_ID(N'dbo.cargos')) DROP INDEX IX_cargos_tenant ON dbo.cargos;
        ALTER TABLE dbo.cargos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_cargos_tenant ON dbo.cargos (id_tenant);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
