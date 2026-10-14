-- ============================================================
-- Script   : 034_id_tenant_contraer_facturacion.sql
-- Proposito: Paso F1.3b del plan de la Fase 1 (ADR-001, ADR-019, ADR-021), contraer - grupo D, facturacion.
--            Cierra en las tablas: condiciones_pago, condiciones_pago_cuotas, formas_pago, impuestos, productos_servicios, facturas, factura_detalle, factura_secuencias, pagos, pago_aplicaciones
--              * id_tenant pasa a NOT NULL (en las tablas mixtas queda nulable: nulo es una fila global, ADR-015).
--              * Clave unica (id_tenant, ...) en las tablas que otras referencian.
--              * 22 clave(s) foranea(s) entre tablas de tenant pasan a COMPUESTAS (id_tenant, ...): una fila no puede
--                apuntar a la de otro tenant. Cada una reemplaza a la simple y se crea en el script del grupo que llega despues.
--            No toca datos. Las reglas de borrado en cascada se conservan; los indices por id_empresa no cambian.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 030 y 033 aplicados (en orden). La version de la aplicacion con F1.2 publicada: desde NOT NULL, quien inserte sin id_tenant falla
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
IF OBJECT_ID(N'tempdb..#precheck_034') IS NOT NULL DROP TABLE #precheck_034;

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
    (N'clientes', N'UX_clientes_tenant_id_cliente', N'032'),
    (N'empresas', N'UX_empresas_tenant_id_empresa', N'031')
) v (t, i, s)
WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));

-- c) esquema esperado: de cada clave foranea existe la simple (por convertir) o la compuesta (ya convertida)
SELECT @errores += N' Falta la clave foranea ' + v.viejo + N' de ' + v.hijo + N'.'
FROM (VALUES
    (N'FK_clientes_cp', N'FKT_clientes_condiciones_pago_id_condicion_pago_default', N'clientes'),
    (N'FK_condiciones_pago_empresa', N'FKT_condiciones_pago_empresas_id_empresa', N'condiciones_pago'),
    (N'FK_cpc_condicion', N'FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago', N'condiciones_pago_cuotas'),
    (N'FK_fd_factura', N'FKT_factura_detalle_facturas_id_factura', N'factura_detalle'),
    (N'FK_fd_impuesto', N'FKT_factura_detalle_impuestos_id_impuesto', N'factura_detalle'),
    (N'FK_fd_producto', N'FKT_factura_detalle_productos_servicios_id_producto', N'factura_detalle'),
    (N'FK_factura_secuencias_empresa', N'FKT_factura_secuencias_empresas_id_empresa', N'factura_secuencias'),
    (N'FK_facturas_cliente', N'FKT_facturas_clientes_id_cliente', N'facturas'),
    (N'FK_facturas_cp', N'FKT_facturas_condiciones_pago_id_condicion_pago', N'facturas'),
    (N'FK_facturas_empresa', N'FKT_facturas_empresas_id_empresa', N'facturas'),
    (N'FK_facturas_fp', N'FKT_facturas_formas_pago_id_forma_pago', N'facturas'),
    (N'FK_formas_pago_empresa', N'FKT_formas_pago_empresas_id_empresa', N'formas_pago'),
    (N'FK_impuestos_empresa', N'FKT_impuestos_empresas_id_empresa', N'impuestos'),
    (N'FK_notas_factura', N'FKT_notas_facturas_id_factura_origen', N'notas'),
    (N'FK_pa_factura', N'FKT_pago_aplicaciones_facturas_id_factura', N'pago_aplicaciones'),
    (N'FK_pa_pago', N'FKT_pago_aplicaciones_pagos_id_pago', N'pago_aplicaciones'),
    (N'FK_pagos_cliente', N'FKT_pagos_clientes_id_cliente', N'pagos'),
    (N'FK_pagos_empresa', N'FKT_pagos_empresas_id_empresa', N'pagos'),
    (N'FK_pagos_fp', N'FKT_pagos_formas_pago_id_forma_pago', N'pagos'),
    (N'FK_ps_empresa', N'FKT_productos_servicios_empresas_id_empresa', N'productos_servicios'),
    (N'FK_ps_impuesto', N'FKT_productos_servicios_impuestos_id_impuesto_default', N'productos_servicios'),
    (N'FK_proveedores_cp', N'FKT_proveedores_condiciones_pago_id_condicion_pago_default', N'proveedores')
) v (viejo, nuevo, hijo)
WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name IN (v.viejo, v.nuevo) AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));

-- d) ninguna fila sin tenant y e) ninguna fila que apunte a otro tenant (solo si lo anterior esta en orden)
IF LEN(@errores) = 0
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.condiciones_pago WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' condiciones_pago: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.condiciones_pago_cuotas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' condiciones_pago_cuotas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.formas_pago WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' formas_pago: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.impuestos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' impuestos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.productos_servicios WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' productos_servicios: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.facturas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' facturas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_detalle WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' factura_detalle: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_secuencias WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' factura_secuencias: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pagos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' pagos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pago_aplicaciones WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' pago_aplicaciones: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.clientes c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' clientes (id_condicion_pago_default): ', @n, N' fila(s) apuntan a condiciones_pago de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.condiciones_pago c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' condiciones_pago (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.condiciones_pago_cuotas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' condiciones_pago_cuotas (id_condicion_pago): ', @n, N' fila(s) apuntan a condiciones_pago de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' factura_detalle (id_factura): ', @n, N' fila(s) apuntan a facturas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_impuestos_id_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' factura_detalle (id_impuesto): ', @n, N' fila(s) apuntan a impuestos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_productos_servicios_id_producto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_detalle c JOIN dbo.productos_servicios p ON p.id_producto = c.id_producto WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' factura_detalle (id_producto): ', @n, N' fila(s) apuntan a productos_servicios de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_secuencias_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.factura_secuencias c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' factura_secuencias (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.facturas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' facturas (id_cliente): ', @n, N' fila(s) apuntan a clientes de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.facturas c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' facturas (id_condicion_pago): ', @n, N' fila(s) apuntan a condiciones_pago de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.facturas c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' facturas (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.facturas c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' facturas (id_forma_pago): ', @n, N' fila(s) apuntan a formas_pago de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_formas_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.formas_pago c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' formas_pago (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_impuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.impuestos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.impuestos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' impuestos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_facturas_id_factura_origen' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.notas c JOIN dbo.facturas p ON p.id_factura = c.id_factura_origen WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' notas (id_factura_origen): ', @n, N' fila(s) apuntan a facturas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.facturas p ON p.id_factura = c.id_factura WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' pago_aplicaciones (id_factura): ', @n, N' fila(s) apuntan a facturas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_pagos_id_pago' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pago_aplicaciones c JOIN dbo.pagos p ON p.id_pago = c.id_pago WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' pago_aplicaciones (id_pago): ', @n, N' fila(s) apuntan a pagos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pagos c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' pagos (id_cliente): ', @n, N' fila(s) apuntan a clientes de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pagos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' pagos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.pagos c JOIN dbo.formas_pago p ON p.id_forma_pago = c.id_forma_pago WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' pagos (id_forma_pago): ', @n, N' fila(s) apuntan a formas_pago de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.productos_servicios c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' productos_servicios (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_impuestos_id_impuesto_default' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.productos_servicios c JOIN dbo.impuestos p ON p.id_impuesto = c.id_impuesto_default WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' productos_servicios (id_impuesto_default): ', @n, N' fila(s) apuntan a impuestos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.proveedores c JOIN dbo.condiciones_pago p ON p.id_condicion_pago = c.id_condicion_pago_default WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' proveedores (id_condicion_pago_default): ', @n, N' fila(s) apuntan a condiciones_pago de otro tenant.');
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 034 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50034, @mensaje, 1;
END

CREATE TABLE #precheck_034 (ok BIT NOT NULL);
INSERT INTO #precheck_034 (ok) VALUES (1);

PRINT N'PRECHECK 034 correcto: 10 tabla(s), 22 clave(s) foranea(s) por convertir.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_034') IS NULL
    THROW 50034, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @columnas INT = 0, @unicas INT = 0, @claves INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- condiciones_pago.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago'))
            EXEC sys.sp_executesql N'DROP INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago (id_tenant);';
        SET @columnas += 1;
    END
    -- condiciones_pago_cuotas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_cuotas_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago_cuotas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas (id_tenant);';
        SET @columnas += 1;
    END
    -- formas_pago.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.formas_pago') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_formas_pago_tenant' AND object_id = OBJECT_ID(N'dbo.formas_pago'))
            EXEC sys.sp_executesql N'DROP INDEX IX_formas_pago_tenant ON dbo.formas_pago;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.formas_pago ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_formas_pago_tenant ON dbo.formas_pago (id_tenant);';
        SET @columnas += 1;
    END
    -- impuestos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.impuestos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_impuestos_tenant' AND object_id = OBJECT_ID(N'dbo.impuestos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_impuestos_tenant ON dbo.impuestos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.impuestos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_impuestos_tenant ON dbo.impuestos (id_tenant);';
        SET @columnas += 1;
    END
    -- productos_servicios.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.productos_servicios') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_productos_servicios_tenant' AND object_id = OBJECT_ID(N'dbo.productos_servicios'))
            EXEC sys.sp_executesql N'DROP INDEX IX_productos_servicios_tenant ON dbo.productos_servicios;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_productos_servicios_tenant ON dbo.productos_servicios (id_tenant);';
        SET @columnas += 1;
    END
    -- facturas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.facturas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_facturas_tenant' AND object_id = OBJECT_ID(N'dbo.facturas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_facturas_tenant ON dbo.facturas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_facturas_tenant ON dbo.facturas (id_tenant);';
        SET @columnas += 1;
    END
    -- factura_detalle.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_detalle') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_detalle_tenant' AND object_id = OBJECT_ID(N'dbo.factura_detalle'))
            EXEC sys.sp_executesql N'DROP INDEX IX_factura_detalle_tenant ON dbo.factura_detalle;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_factura_detalle_tenant ON dbo.factura_detalle (id_tenant);';
        SET @columnas += 1;
    END
    -- factura_secuencias.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_secuencias') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_secuencias_tenant' AND object_id = OBJECT_ID(N'dbo.factura_secuencias'))
            EXEC sys.sp_executesql N'DROP INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_secuencias ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias (id_tenant);';
        SET @columnas += 1;
    END
    -- pagos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pagos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pagos_tenant' AND object_id = OBJECT_ID(N'dbo.pagos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_pagos_tenant ON dbo.pagos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_pagos_tenant ON dbo.pagos (id_tenant);';
        SET @columnas += 1;
    END
    -- pago_aplicaciones.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pago_aplicaciones') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pago_aplicaciones_tenant' AND object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
            EXEC sys.sp_executesql N'DROP INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones (id_tenant);';
        SET @columnas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_condiciones_pago_tenant_id_condicion_pago' AND object_id = OBJECT_ID(N'dbo.condiciones_pago'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_condiciones_pago_tenant_id_condicion_pago ON dbo.condiciones_pago (id_tenant, id_condicion_pago);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_facturas_tenant_id_factura' AND object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_facturas_tenant_id_factura ON dbo.facturas (id_tenant, id_factura);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_impuestos_tenant_id_impuesto' AND object_id = OBJECT_ID(N'dbo.impuestos'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_impuestos_tenant_id_impuesto ON dbo.impuestos (id_tenant, id_impuesto);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_productos_servicios_tenant_id_producto' AND object_id = OBJECT_ID(N'dbo.productos_servicios'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_productos_servicios_tenant_id_producto ON dbo.productos_servicios (id_tenant, id_producto);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_formas_pago_tenant_id_forma_pago' AND object_id = OBJECT_ID(N'dbo.formas_pago'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_formas_pago_tenant_id_forma_pago ON dbo.formas_pago (id_tenant, id_forma_pago);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_pagos_tenant_id_pago' AND object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_pagos_tenant_id_pago ON dbo.pagos (id_tenant, id_pago);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_cp' AND parent_object_id = OBJECT_ID(N'dbo.clientes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes DROP CONSTRAINT FK_clientes_cp;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.clientes ADD CONSTRAINT FKT_clientes_condiciones_pago_id_condicion_pago_default FOREIGN KEY (id_tenant, id_condicion_pago_default) REFERENCES dbo.condiciones_pago (id_tenant, id_condicion_pago);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_empresa' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago DROP CONSTRAINT FK_condiciones_pago_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago ADD CONSTRAINT FKT_condiciones_pago_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cpc_condicion' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago_cuotas DROP CONSTRAINT FK_cpc_condicion;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.condiciones_pago_cuotas ADD CONSTRAINT FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago FOREIGN KEY (id_tenant, id_condicion_pago) REFERENCES dbo.condiciones_pago (id_tenant, id_condicion_pago) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_factura' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FK_fd_factura;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FKT_factura_detalle_facturas_id_factura FOREIGN KEY (id_tenant, id_factura) REFERENCES dbo.facturas (id_tenant, id_factura) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_impuestos_id_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FK_fd_impuesto;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FKT_factura_detalle_impuestos_id_impuesto FOREIGN KEY (id_tenant, id_impuesto) REFERENCES dbo.impuestos (id_tenant, id_impuesto);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_productos_servicios_id_producto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_producto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FK_fd_producto;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FKT_factura_detalle_productos_servicios_id_producto FOREIGN KEY (id_tenant, id_producto) REFERENCES dbo.productos_servicios (id_tenant, id_producto);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_secuencias_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_secuencias_empresa' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_secuencias DROP CONSTRAINT FK_factura_secuencias_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.factura_secuencias ADD CONSTRAINT FKT_factura_secuencias_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_cliente' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas DROP CONSTRAINT FK_facturas_cliente;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD CONSTRAINT FKT_facturas_clientes_id_cliente FOREIGN KEY (id_tenant, id_cliente) REFERENCES dbo.clientes (id_tenant, id_cliente);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_cp' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas DROP CONSTRAINT FK_facturas_cp;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD CONSTRAINT FKT_facturas_condiciones_pago_id_condicion_pago FOREIGN KEY (id_tenant, id_condicion_pago) REFERENCES dbo.condiciones_pago (id_tenant, id_condicion_pago);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas DROP CONSTRAINT FK_facturas_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD CONSTRAINT FKT_facturas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_fp' AND parent_object_id = OBJECT_ID(N'dbo.facturas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas DROP CONSTRAINT FK_facturas_fp;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.facturas ADD CONSTRAINT FKT_facturas_formas_pago_id_forma_pago FOREIGN KEY (id_tenant, id_forma_pago) REFERENCES dbo.formas_pago (id_tenant, id_forma_pago);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_formas_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_formas_pago_empresa' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.formas_pago DROP CONSTRAINT FK_formas_pago_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.formas_pago ADD CONSTRAINT FKT_formas_pago_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_impuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.impuestos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_impuestos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.impuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.impuestos DROP CONSTRAINT FK_impuestos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.impuestos ADD CONSTRAINT FKT_impuestos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_facturas_id_factura_origen' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_factura' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.notas DROP CONSTRAINT FK_notas_factura;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ADD CONSTRAINT FKT_notas_facturas_id_factura_origen FOREIGN KEY (id_tenant, id_factura_origen) REFERENCES dbo.facturas (id_tenant, id_factura);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pa_factura' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones DROP CONSTRAINT FK_pa_factura;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones ADD CONSTRAINT FKT_pago_aplicaciones_facturas_id_factura FOREIGN KEY (id_tenant, id_factura) REFERENCES dbo.facturas (id_tenant, id_factura);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_pagos_id_pago' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pa_pago' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones DROP CONSTRAINT FK_pa_pago;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pago_aplicaciones ADD CONSTRAINT FKT_pago_aplicaciones_pagos_id_pago FOREIGN KEY (id_tenant, id_pago) REFERENCES dbo.pagos (id_tenant, id_pago) ON DELETE CASCADE;';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_cliente' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos DROP CONSTRAINT FK_pagos_cliente;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ADD CONSTRAINT FKT_pagos_clientes_id_cliente FOREIGN KEY (id_tenant, id_cliente) REFERENCES dbo.clientes (id_tenant, id_cliente);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos DROP CONSTRAINT FK_pagos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ADD CONSTRAINT FKT_pagos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_fp' AND parent_object_id = OBJECT_ID(N'dbo.pagos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos DROP CONSTRAINT FK_pagos_fp;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.pagos ADD CONSTRAINT FKT_pagos_formas_pago_id_forma_pago FOREIGN KEY (id_tenant, id_forma_pago) REFERENCES dbo.formas_pago (id_tenant, id_forma_pago);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ps_empresa' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios DROP CONSTRAINT FK_ps_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios ADD CONSTRAINT FKT_productos_servicios_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_impuestos_id_impuesto_default' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ps_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios DROP CONSTRAINT FK_ps_impuesto;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.productos_servicios ADD CONSTRAINT FKT_productos_servicios_impuestos_id_impuesto_default FOREIGN KEY (id_tenant, id_impuesto_default) REFERENCES dbo.impuestos (id_tenant, id_impuesto);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_cp' AND parent_object_id = OBJECT_ID(N'dbo.proveedores'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores DROP CONSTRAINT FK_proveedores_cp;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.proveedores ADD CONSTRAINT FKT_proveedores_condiciones_pago_id_condicion_pago_default FOREIGN KEY (id_tenant, id_condicion_pago_default) REFERENCES dbo.condiciones_pago (id_tenant, id_condicion_pago);';
        SET @claves += 1;
    END
    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'condiciones_pago'),
    (N'condiciones_pago_cuotas'),
    (N'formas_pago'),
    (N'impuestos'),
    (N'productos_servicios'),
    (N'facturas'),
    (N'factura_detalle'),
    (N'factura_secuencias'),
    (N'pagos'),
    (N'pago_aplicaciones')
    ) v (t) WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);
    IF @malas > 0 THROW 50034, N'POSTCHECK: alguna tabla del grupo conserva id_tenant nulable. Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'condiciones_pago', N'UX_condiciones_pago_tenant_id_condicion_pago'),
    (N'facturas', N'UX_facturas_tenant_id_factura'),
    (N'impuestos', N'UX_impuestos_tenant_id_impuesto'),
    (N'productos_servicios', N'UX_productos_servicios_tenant_id_producto'),
    (N'formas_pago', N'UX_formas_pago_tenant_id_forma_pago'),
    (N'pagos', N'UX_pagos_tenant_id_pago')
    ) v (t, i) WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));
    IF @malas > 0 THROW 50034, N'POSTCHECK: falta alguna clave unica (id_tenant, ...). Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'FKT_clientes_condiciones_pago_id_condicion_pago_default', N'FK_clientes_cp', N'clientes'),
    (N'FKT_condiciones_pago_empresas_id_empresa', N'FK_condiciones_pago_empresa', N'condiciones_pago'),
    (N'FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago', N'FK_cpc_condicion', N'condiciones_pago_cuotas'),
    (N'FKT_factura_detalle_facturas_id_factura', N'FK_fd_factura', N'factura_detalle'),
    (N'FKT_factura_detalle_impuestos_id_impuesto', N'FK_fd_impuesto', N'factura_detalle'),
    (N'FKT_factura_detalle_productos_servicios_id_producto', N'FK_fd_producto', N'factura_detalle'),
    (N'FKT_factura_secuencias_empresas_id_empresa', N'FK_factura_secuencias_empresa', N'factura_secuencias'),
    (N'FKT_facturas_clientes_id_cliente', N'FK_facturas_cliente', N'facturas'),
    (N'FKT_facturas_condiciones_pago_id_condicion_pago', N'FK_facturas_cp', N'facturas'),
    (N'FKT_facturas_empresas_id_empresa', N'FK_facturas_empresa', N'facturas'),
    (N'FKT_facturas_formas_pago_id_forma_pago', N'FK_facturas_fp', N'facturas'),
    (N'FKT_formas_pago_empresas_id_empresa', N'FK_formas_pago_empresa', N'formas_pago'),
    (N'FKT_impuestos_empresas_id_empresa', N'FK_impuestos_empresa', N'impuestos'),
    (N'FKT_notas_facturas_id_factura_origen', N'FK_notas_factura', N'notas'),
    (N'FKT_pago_aplicaciones_facturas_id_factura', N'FK_pa_factura', N'pago_aplicaciones'),
    (N'FKT_pago_aplicaciones_pagos_id_pago', N'FK_pa_pago', N'pago_aplicaciones'),
    (N'FKT_pagos_clientes_id_cliente', N'FK_pagos_cliente', N'pagos'),
    (N'FKT_pagos_empresas_id_empresa', N'FK_pagos_empresa', N'pagos'),
    (N'FKT_pagos_formas_pago_id_forma_pago', N'FK_pagos_fp', N'pagos'),
    (N'FKT_productos_servicios_empresas_id_empresa', N'FK_ps_empresa', N'productos_servicios'),
    (N'FKT_productos_servicios_impuestos_id_impuesto_default', N'FK_ps_impuesto', N'productos_servicios'),
    (N'FKT_proveedores_condiciones_pago_id_condicion_pago_default', N'FK_proveedores_cp', N'proveedores')
    ) v (nuevo, viejo, hijo) WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nuevo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo) AND f.is_not_trusted = 0 AND f.is_disabled = 0)
       OR EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.viejo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));
    IF @malas > 0 THROW 50034, N'POSTCHECK: alguna clave foranea compuesta falta, no es de confianza o la simple sigue. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @columnas = 0 AND @unicas = 0 AND @claves = 0
        PRINT N'Nada que hacer: este grupo ya estaba contraido.';
    ELSE
        PRINT CONCAT(N'CAMBIO 034 aplicado: ', @columnas, N' columna(s) NOT NULL; ', @unicas, N' clave(s) unica(s); ', @claves, N' clave(s) foranea(s) compuesta(s).');
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
WHERE t.name IN (N'condiciones_pago', N'condiciones_pago_cuotas', N'formas_pago', N'impuestos', N'productos_servicios', N'facturas', N'factura_detalle', N'factura_secuencias', N'pagos', N'pago_aplicaciones')
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_proveedores_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.proveedores')) ALTER TABLE dbo.proveedores DROP CONSTRAINT FKT_proveedores_condiciones_pago_id_condicion_pago_default;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_proveedores_cp' AND parent_object_id = OBJECT_ID(N'dbo.proveedores')) ALTER TABLE dbo.proveedores ADD CONSTRAINT FK_proveedores_cp FOREIGN KEY (id_condicion_pago_default) REFERENCES dbo.condiciones_pago (id_condicion_pago);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_impuestos_id_impuesto_default' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios')) ALTER TABLE dbo.productos_servicios DROP CONSTRAINT FKT_productos_servicios_impuestos_id_impuesto_default;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ps_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios')) ALTER TABLE dbo.productos_servicios ADD CONSTRAINT FK_ps_impuesto FOREIGN KEY (id_impuesto_default) REFERENCES dbo.impuestos (id_impuesto);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_productos_servicios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios')) ALTER TABLE dbo.productos_servicios DROP CONSTRAINT FKT_productos_servicios_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ps_empresa' AND parent_object_id = OBJECT_ID(N'dbo.productos_servicios')) ALTER TABLE dbo.productos_servicios ADD CONSTRAINT FK_ps_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos DROP CONSTRAINT FKT_pagos_formas_pago_id_forma_pago;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_fp' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos ADD CONSTRAINT FK_pagos_fp FOREIGN KEY (id_forma_pago) REFERENCES dbo.formas_pago (id_forma_pago);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos DROP CONSTRAINT FKT_pagos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos ADD CONSTRAINT FK_pagos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pagos_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos DROP CONSTRAINT FKT_pagos_clientes_id_cliente;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pagos_cliente' AND parent_object_id = OBJECT_ID(N'dbo.pagos')) ALTER TABLE dbo.pagos ADD CONSTRAINT FK_pagos_cliente FOREIGN KEY (id_cliente) REFERENCES dbo.clientes (id_cliente);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_pagos_id_pago' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) ALTER TABLE dbo.pago_aplicaciones DROP CONSTRAINT FKT_pago_aplicaciones_pagos_id_pago;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pa_pago' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) ALTER TABLE dbo.pago_aplicaciones ADD CONSTRAINT FK_pa_pago FOREIGN KEY (id_pago) REFERENCES dbo.pagos (id_pago) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_pago_aplicaciones_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) ALTER TABLE dbo.pago_aplicaciones DROP CONSTRAINT FKT_pago_aplicaciones_facturas_id_factura;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_pa_factura' AND parent_object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) ALTER TABLE dbo.pago_aplicaciones ADD CONSTRAINT FK_pa_factura FOREIGN KEY (id_factura) REFERENCES dbo.facturas (id_factura);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_facturas_id_factura_origen' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas DROP CONSTRAINT FKT_notas_facturas_id_factura_origen;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_factura' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas ADD CONSTRAINT FK_notas_factura FOREIGN KEY (id_factura_origen) REFERENCES dbo.facturas (id_factura);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_impuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.impuestos')) ALTER TABLE dbo.impuestos DROP CONSTRAINT FKT_impuestos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_impuestos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.impuestos')) ALTER TABLE dbo.impuestos ADD CONSTRAINT FK_impuestos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_formas_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago')) ALTER TABLE dbo.formas_pago DROP CONSTRAINT FKT_formas_pago_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_formas_pago_empresa' AND parent_object_id = OBJECT_ID(N'dbo.formas_pago')) ALTER TABLE dbo.formas_pago ADD CONSTRAINT FK_formas_pago_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_formas_pago_id_forma_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas DROP CONSTRAINT FKT_facturas_formas_pago_id_forma_pago;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_fp' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas ADD CONSTRAINT FK_facturas_fp FOREIGN KEY (id_forma_pago) REFERENCES dbo.formas_pago (id_forma_pago);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas DROP CONSTRAINT FKT_facturas_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas ADD CONSTRAINT FK_facturas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas DROP CONSTRAINT FKT_facturas_condiciones_pago_id_condicion_pago;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_cp' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas ADD CONSTRAINT FK_facturas_cp FOREIGN KEY (id_condicion_pago) REFERENCES dbo.condiciones_pago (id_condicion_pago);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_facturas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas DROP CONSTRAINT FKT_facturas_clientes_id_cliente;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_facturas_cliente' AND parent_object_id = OBJECT_ID(N'dbo.facturas')) ALTER TABLE dbo.facturas ADD CONSTRAINT FK_facturas_cliente FOREIGN KEY (id_cliente) REFERENCES dbo.clientes (id_cliente);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_secuencias_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias')) ALTER TABLE dbo.factura_secuencias DROP CONSTRAINT FKT_factura_secuencias_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_factura_secuencias_empresa' AND parent_object_id = OBJECT_ID(N'dbo.factura_secuencias')) ALTER TABLE dbo.factura_secuencias ADD CONSTRAINT FK_factura_secuencias_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_productos_servicios_id_producto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FKT_factura_detalle_productos_servicios_id_producto;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_producto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FK_fd_producto FOREIGN KEY (id_producto) REFERENCES dbo.productos_servicios (id_producto);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_impuestos_id_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FKT_factura_detalle_impuestos_id_impuesto;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_impuesto' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FK_fd_impuesto FOREIGN KEY (id_impuesto) REFERENCES dbo.impuestos (id_impuesto);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_factura_detalle_facturas_id_factura' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle DROP CONSTRAINT FKT_factura_detalle_facturas_id_factura;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_fd_factura' AND parent_object_id = OBJECT_ID(N'dbo.factura_detalle')) ALTER TABLE dbo.factura_detalle ADD CONSTRAINT FK_fd_factura FOREIGN KEY (id_factura) REFERENCES dbo.facturas (id_factura) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas')) ALTER TABLE dbo.condiciones_pago_cuotas DROP CONSTRAINT FKT_condiciones_pago_cuotas_condiciones_pago_id_condicion_pago;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cpc_condicion' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas')) ALTER TABLE dbo.condiciones_pago_cuotas ADD CONSTRAINT FK_cpc_condicion FOREIGN KEY (id_condicion_pago) REFERENCES dbo.condiciones_pago (id_condicion_pago) ON DELETE CASCADE;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_condiciones_pago_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago')) ALTER TABLE dbo.condiciones_pago DROP CONSTRAINT FKT_condiciones_pago_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_condiciones_pago_empresa' AND parent_object_id = OBJECT_ID(N'dbo.condiciones_pago')) ALTER TABLE dbo.condiciones_pago ADD CONSTRAINT FK_condiciones_pago_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_clientes_condiciones_pago_id_condicion_pago_default' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes DROP CONSTRAINT FKT_clientes_condiciones_pago_id_condicion_pago_default;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_clientes_cp' AND parent_object_id = OBJECT_ID(N'dbo.clientes')) ALTER TABLE dbo.clientes ADD CONSTRAINT FK_clientes_cp FOREIGN KEY (id_condicion_pago_default) REFERENCES dbo.condiciones_pago (id_condicion_pago);
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_condiciones_pago_tenant_id_condicion_pago' AND object_id = OBJECT_ID(N'dbo.condiciones_pago')) DROP INDEX UX_condiciones_pago_tenant_id_condicion_pago ON dbo.condiciones_pago;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_facturas_tenant_id_factura' AND object_id = OBJECT_ID(N'dbo.facturas')) DROP INDEX UX_facturas_tenant_id_factura ON dbo.facturas;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_impuestos_tenant_id_impuesto' AND object_id = OBJECT_ID(N'dbo.impuestos')) DROP INDEX UX_impuestos_tenant_id_impuesto ON dbo.impuestos;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_productos_servicios_tenant_id_producto' AND object_id = OBJECT_ID(N'dbo.productos_servicios')) DROP INDEX UX_productos_servicios_tenant_id_producto ON dbo.productos_servicios;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_formas_pago_tenant_id_forma_pago' AND object_id = OBJECT_ID(N'dbo.formas_pago')) DROP INDEX UX_formas_pago_tenant_id_forma_pago ON dbo.formas_pago;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_pagos_tenant_id_pago' AND object_id = OBJECT_ID(N'dbo.pagos')) DROP INDEX UX_pagos_tenant_id_pago ON dbo.pagos;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago')) DROP INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago;
        ALTER TABLE dbo.condiciones_pago ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_condiciones_pago_tenant ON dbo.condiciones_pago (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_condiciones_pago_cuotas_tenant' AND object_id = OBJECT_ID(N'dbo.condiciones_pago_cuotas')) DROP INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas;
        ALTER TABLE dbo.condiciones_pago_cuotas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_condiciones_pago_cuotas_tenant ON dbo.condiciones_pago_cuotas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.formas_pago') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_formas_pago_tenant' AND object_id = OBJECT_ID(N'dbo.formas_pago')) DROP INDEX IX_formas_pago_tenant ON dbo.formas_pago;
        ALTER TABLE dbo.formas_pago ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_formas_pago_tenant ON dbo.formas_pago (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.impuestos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_impuestos_tenant' AND object_id = OBJECT_ID(N'dbo.impuestos')) DROP INDEX IX_impuestos_tenant ON dbo.impuestos;
        ALTER TABLE dbo.impuestos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_impuestos_tenant ON dbo.impuestos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.productos_servicios') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_productos_servicios_tenant' AND object_id = OBJECT_ID(N'dbo.productos_servicios')) DROP INDEX IX_productos_servicios_tenant ON dbo.productos_servicios;
        ALTER TABLE dbo.productos_servicios ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_productos_servicios_tenant ON dbo.productos_servicios (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.facturas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_facturas_tenant' AND object_id = OBJECT_ID(N'dbo.facturas')) DROP INDEX IX_facturas_tenant ON dbo.facturas;
        ALTER TABLE dbo.facturas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_facturas_tenant ON dbo.facturas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_detalle') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_detalle_tenant' AND object_id = OBJECT_ID(N'dbo.factura_detalle')) DROP INDEX IX_factura_detalle_tenant ON dbo.factura_detalle;
        ALTER TABLE dbo.factura_detalle ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_factura_detalle_tenant ON dbo.factura_detalle (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.factura_secuencias') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_factura_secuencias_tenant' AND object_id = OBJECT_ID(N'dbo.factura_secuencias')) DROP INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias;
        ALTER TABLE dbo.factura_secuencias ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_factura_secuencias_tenant ON dbo.factura_secuencias (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pagos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pagos_tenant' AND object_id = OBJECT_ID(N'dbo.pagos')) DROP INDEX IX_pagos_tenant ON dbo.pagos;
        ALTER TABLE dbo.pagos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_pagos_tenant ON dbo.pagos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.pago_aplicaciones') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pago_aplicaciones_tenant' AND object_id = OBJECT_ID(N'dbo.pago_aplicaciones')) DROP INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones;
        ALTER TABLE dbo.pago_aplicaciones ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_pago_aplicaciones_tenant ON dbo.pago_aplicaciones (id_tenant);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
