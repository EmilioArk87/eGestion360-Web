-- ============================================================
-- Script   : 035_id_tenant_contraer_mixtas_e_infraestructura.sql
-- Proposito: Paso F1.3b del plan de la Fase 1 (ADR-001, ADR-019, ADR-021), contraer - tablas mixtas e infraestructura.
--            Cierra en las tablas: tasas_cambio, sen_boletines, domain_events
--              * id_tenant pasa a NOT NULL (en las tablas mixtas queda nulable: nulo es una fila global, ADR-015).
--              * Clave unica (id_tenant, ...) en las tablas que otras referencian.
--              * 5 clave(s) foranea(s) entre tablas de tenant pasan a COMPUESTAS (id_tenant, ...): una fila no puede
--                apuntar a la de otro tenant. Cada una reemplaza a la simple y se crea en el script del grupo que llega despues.
--            No toca datos. Las reglas de borrado en cascada se conservan; los indices por id_empresa no cambian.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 030 y 034 aplicados (en orden). La version de la aplicacion con F1.2 publicada: desde NOT NULL, quien inserte sin id_tenant falla
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
IF OBJECT_ID(N'tempdb..#precheck_035') IS NOT NULL DROP TABLE #precheck_035;

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
    (N'FK_domain_events_empresa', N'FKT_domain_events_empresas_id_empresa', N'domain_events'),
    (N'FK_precios_combustible_boletin', N'FKT_precios_combustible_sen_boletines_id_sen_boletin', N'precios_combustible'),
    (N'FK_sen_boletines_empresa', N'FKT_sen_boletines_empresas_id_empresa', N'sen_boletines'),
    (N'FK_tasas_cambio_empresa', N'FKT_tasas_cambio_empresas_id_empresa', N'tasas_cambio'),
    (N'FK_tasas_cambio_tasa_anterior', N'FKT_tasas_cambio_tasas_cambio_id_tasa_anterior', N'tasas_cambio')
) v (viejo, nuevo, hijo)
WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name IN (v.viejo, v.nuevo) AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));

-- d) ninguna fila sin tenant y e) ninguna fila que apunte a otro tenant (solo si lo anterior esta en orden)
IF LEN(@errores) = 0
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.domain_events WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' domain_events: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.tasas_cambio WHERE id_tenant IS NULL AND id_empresa IS NOT NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' tasas_cambio: ', @n, N' fila(s) con empresa y sin id_tenant; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.sen_boletines WHERE id_tenant IS NULL AND id_empresa IS NOT NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' sen_boletines: ', @n, N' fila(s) con empresa y sin id_tenant; vuelva a ejecutar el script de F1.2 de ese grupo.');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_domain_events_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.domain_events'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.domain_events c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' domain_events (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_sen_boletines_id_sen_boletin' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.precios_combustible c JOIN dbo.sen_boletines p ON p.id_sen_boletin = c.id_sen_boletin WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' precios_combustible (id_sen_boletin): ', @n, N' fila(s) apuntan a sen_boletines de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_sen_boletines_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.sen_boletines c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' sen_boletines (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.tasas_cambio c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' tasas_cambio (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_tasas_cambio_id_tasa_anterior' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.tasas_cambio c JOIN dbo.tasas_cambio p ON p.id_tasa_cambio = c.id_tasa_anterior WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' tasas_cambio (id_tasa_anterior): ', @n, N' fila(s) apuntan a tasas_cambio de otro tenant.');
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 035 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50035, @mensaje, 1;
END

CREATE TABLE #precheck_035 (ok BIT NOT NULL);
INSERT INTO #precheck_035 (ok) VALUES (1);

PRINT N'PRECHECK 035 correcto: 3 tabla(s), 5 clave(s) foranea(s) por convertir.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_035') IS NULL
    THROW 50035, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @columnas INT = 0, @unicas INT = 0, @claves INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- domain_events.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.domain_events') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_domain_events_tenant' AND object_id = OBJECT_ID(N'dbo.domain_events'))
            EXEC sys.sp_executesql N'DROP INDEX IX_domain_events_tenant ON dbo.domain_events;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.domain_events ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_domain_events_tenant ON dbo.domain_events (id_tenant);';
        SET @columnas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_sen_boletines_tenant_id_sen_boletin' AND object_id = OBJECT_ID(N'dbo.sen_boletines'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_sen_boletines_tenant_id_sen_boletin ON dbo.sen_boletines (id_tenant, id_sen_boletin);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tasas_cambio_tenant_id_tasa_cambio' AND object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_tasas_cambio_tenant_id_tasa_cambio ON dbo.tasas_cambio (id_tenant, id_tasa_cambio);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_domain_events_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.domain_events'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_domain_events_empresa' AND parent_object_id = OBJECT_ID(N'dbo.domain_events'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.domain_events DROP CONSTRAINT FK_domain_events_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.domain_events ADD CONSTRAINT FKT_domain_events_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_sen_boletines_id_sen_boletin' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_boletin' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FK_precios_combustible_boletin;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FKT_precios_combustible_sen_boletines_id_sen_boletin FOREIGN KEY (id_tenant, id_sen_boletin) REFERENCES dbo.sen_boletines (id_tenant, id_sen_boletin);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_sen_boletines_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sen_boletines_empresa' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.sen_boletines DROP CONSTRAINT FK_sen_boletines_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.sen_boletines ADD CONSTRAINT FKT_sen_boletines_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FKT_tasas_cambio_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_tasas_cambio_id_tasa_anterior' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_tasa_anterior' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_tasa_anterior;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FKT_tasas_cambio_tasas_cambio_id_tasa_anterior FOREIGN KEY (id_tenant, id_tasa_anterior) REFERENCES dbo.tasas_cambio (id_tenant, id_tasa_cambio);';
        SET @claves += 1;
    END
    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'domain_events')
    ) v (t) WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);
    IF @malas > 0 THROW 50035, N'POSTCHECK: alguna tabla del grupo conserva id_tenant nulable. Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'sen_boletines', N'UX_sen_boletines_tenant_id_sen_boletin'),
    (N'tasas_cambio', N'UX_tasas_cambio_tenant_id_tasa_cambio')
    ) v (t, i) WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));
    IF @malas > 0 THROW 50035, N'POSTCHECK: falta alguna clave unica (id_tenant, ...). Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'FKT_domain_events_empresas_id_empresa', N'FK_domain_events_empresa', N'domain_events'),
    (N'FKT_precios_combustible_sen_boletines_id_sen_boletin', N'FK_precios_combustible_boletin', N'precios_combustible'),
    (N'FKT_sen_boletines_empresas_id_empresa', N'FK_sen_boletines_empresa', N'sen_boletines'),
    (N'FKT_tasas_cambio_empresas_id_empresa', N'FK_tasas_cambio_empresa', N'tasas_cambio'),
    (N'FKT_tasas_cambio_tasas_cambio_id_tasa_anterior', N'FK_tasas_cambio_tasa_anterior', N'tasas_cambio')
    ) v (nuevo, viejo, hijo) WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nuevo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo) AND f.is_not_trusted = 0 AND f.is_disabled = 0)
       OR EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.viejo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));
    IF @malas > 0 THROW 50035, N'POSTCHECK: alguna clave foranea compuesta falta, no es de confianza o la simple sigue. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @columnas = 0 AND @unicas = 0 AND @claves = 0
        PRINT N'Nada que hacer: este grupo ya estaba contraido.';
    ELSE
        PRINT CONCAT(N'CAMBIO 035 aplicado: ', @columnas, N' columna(s) NOT NULL; ', @unicas, N' clave(s) unica(s); ', @claves, N' clave(s) foranea(s) compuesta(s).');
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
WHERE t.name IN (N'tasas_cambio', N'sen_boletines', N'domain_events')
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_tasas_cambio_id_tasa_anterior' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')) ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FKT_tasas_cambio_tasas_cambio_id_tasa_anterior;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_tasa_anterior' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')) ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_tasa_anterior FOREIGN KEY (id_tasa_anterior) REFERENCES dbo.tasas_cambio (id_tasa_cambio);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tasas_cambio_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')) ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FKT_tasas_cambio_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio')) ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_sen_boletines_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines')) ALTER TABLE dbo.sen_boletines DROP CONSTRAINT FKT_sen_boletines_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_sen_boletines_empresa' AND parent_object_id = OBJECT_ID(N'dbo.sen_boletines')) ALTER TABLE dbo.sen_boletines ADD CONSTRAINT FK_sen_boletines_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_sen_boletines_id_sen_boletin' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible')) ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FKT_precios_combustible_sen_boletines_id_sen_boletin;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_boletin' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible')) ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FK_precios_combustible_boletin FOREIGN KEY (id_sen_boletin) REFERENCES dbo.sen_boletines (id_sen_boletin);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_domain_events_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.domain_events')) ALTER TABLE dbo.domain_events DROP CONSTRAINT FKT_domain_events_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_domain_events_empresa' AND parent_object_id = OBJECT_ID(N'dbo.domain_events')) ALTER TABLE dbo.domain_events ADD CONSTRAINT FK_domain_events_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_sen_boletines_tenant_id_sen_boletin' AND object_id = OBJECT_ID(N'dbo.sen_boletines')) DROP INDEX UX_sen_boletines_tenant_id_sen_boletin ON dbo.sen_boletines;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tasas_cambio_tenant_id_tasa_cambio' AND object_id = OBJECT_ID(N'dbo.tasas_cambio')) DROP INDEX UX_tasas_cambio_tenant_id_tasa_cambio ON dbo.tasas_cambio;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.domain_events') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_domain_events_tenant' AND object_id = OBJECT_ID(N'dbo.domain_events')) DROP INDEX IX_domain_events_tenant ON dbo.domain_events;
        ALTER TABLE dbo.domain_events ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_domain_events_tenant ON dbo.domain_events (id_tenant);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
