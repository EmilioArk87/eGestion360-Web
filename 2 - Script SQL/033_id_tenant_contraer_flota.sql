-- ============================================================
-- Script   : 033_id_tenant_contraer_flota.sql
-- Proposito: Paso F1.3b del plan de la Fase 1 (ADR-001, ADR-019, ADR-021), contraer - grupo C, flota.
--            Cierra en las tablas: vehiculos, tipos_vehiculo, rutas, talleres, categorias_repuesto, odometro_diario, cargas_combustible, salarios_diarios, ordenes_mantenimiento, gastos_repuestos, polizas_seguros, peajes, control_salidas, notas, ingresos_operativos, precios_combustible
--              * id_tenant pasa a NOT NULL (en las tablas mixtas queda nulable: nulo es una fila global, ADR-015).
--              * Clave unica (id_tenant, ...) en las tablas que otras referencian.
--              * 39 clave(s) foranea(s) entre tablas de tenant pasan a COMPUESTAS (id_tenant, ...): una fila no puede
--                apuntar a la de otro tenant. Cada una reemplaza a la simple y se crea en el script del grupo que llega despues.
--              * ON DELETE SET NULL pasa a NO ACTION en: cargas_combustible.id_conductor, odometro_diario.id_conductor, odometro_diario.id_ruta, vehiculos.id_ruta.
--                Una clave compuesta pondria tambien id_tenant en NULL. La aplicacion no borra esas filas (borrado logico).
--            No toca datos. Las reglas de borrado en cascada se conservan; los indices por id_empresa no cambian.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 030 y 032 aplicados (en orden). La version de la aplicacion con F1.2 publicada: desde NOT NULL, quien inserte sin id_tenant falla
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
IF OBJECT_ID(N'tempdb..#precheck_033') IS NOT NULL DROP TABLE #precheck_033;

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
    (N'empresas', N'UX_empresas_tenant_id_empresa', N'031'),
    (N'personas', N'UX_personas_tenant_id_persona', N'032')
) v (t, i, s)
WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));

-- c) esquema esperado: de cada clave foranea existe la simple (por convertir) o la compuesta (ya convertida)
SELECT @errores += N' Falta la clave foranea ' + v.viejo + N' de ' + v.hijo + N'.'
FROM (VALUES
    (N'FK_cargas_combustible_conductor', N'FKT_cargas_combustible_personas_id_conductor', N'cargas_combustible'),
    (N'FK_cargas_combustible_personas_id_conductor', N'FKT_cargas_combustible_personas_id_conductor', N'cargas_combustible'),
    (N'FK_cargas_combustible_empresa', N'FKT_cargas_combustible_empresas_id_empresa', N'cargas_combustible'),
    (N'FK_cargas_combustible_vehiculo', N'FKT_cargas_combustible_vehiculos_id_vehiculo', N'cargas_combustible'),
    (N'FK_cargas_combustible_vehiculos_id_vehiculo', N'FKT_cargas_combustible_vehiculos_id_vehiculo', N'cargas_combustible'),
    (N'FK_categorias_repuesto_empresa', N'FKT_categorias_repuesto_empresas_id_empresa', N'categorias_repuesto'),
    (N'FK_control_salidas_conductor', N'FKT_control_salidas_personas_id_conductor', N'control_salidas'),
    (N'FK_control_salidas_empresa', N'FKT_control_salidas_empresas_id_empresa', N'control_salidas'),
    (N'FK_control_salidas_ruta', N'FKT_control_salidas_rutas_id_ruta', N'control_salidas'),
    (N'FK_control_salidas_vehiculo', N'FKT_control_salidas_vehiculos_id_vehiculo', N'control_salidas'),
    (N'FK_gastos_repuestos_categoria', N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'gastos_repuestos'),
    (N'FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'gastos_repuestos'),
    (N'FK_gastos_repuestos_empresa', N'FKT_gastos_repuestos_empresas_id_empresa', N'gastos_repuestos'),
    (N'FK_gastos_repuestos_vehiculo', N'FKT_gastos_repuestos_vehiculos_id_vehiculo', N'gastos_repuestos'),
    (N'FK_gastos_repuestos_vehiculos_id_vehiculo', N'FKT_gastos_repuestos_vehiculos_id_vehiculo', N'gastos_repuestos'),
    (N'FK_ingresos_operativos_empresa', N'FKT_ingresos_operativos_empresas_id_empresa', N'ingresos_operativos'),
    (N'FK_ingresos_operativos_ruta', N'FKT_ingresos_operativos_rutas_id_ruta', N'ingresos_operativos'),
    (N'FK_ingresos_operativos_vehiculo', N'FKT_ingresos_operativos_vehiculos_id_vehiculo', N'ingresos_operativos'),
    (N'FK_notas_cliente', N'FKT_notas_clientes_id_cliente', N'notas'),
    (N'FK_notas_empresa', N'FKT_notas_empresas_id_empresa', N'notas'),
    (N'FK_odometro_diario_conductor', N'FKT_odometro_diario_personas_id_conductor', N'odometro_diario'),
    (N'FK_odometro_diario_personas_id_conductor', N'FKT_odometro_diario_personas_id_conductor', N'odometro_diario'),
    (N'FK_odometro_diario_empresa', N'FKT_odometro_diario_empresas_id_empresa', N'odometro_diario'),
    (N'FK_odometro_diario_ruta', N'FKT_odometro_diario_rutas_id_ruta', N'odometro_diario'),
    (N'FK_odometro_diario_rutas_id_ruta', N'FKT_odometro_diario_rutas_id_ruta', N'odometro_diario'),
    (N'FK_odometro_diario_vehiculo', N'FKT_odometro_diario_vehiculos_id_vehiculo', N'odometro_diario'),
    (N'FK_odometro_diario_vehiculos_id_vehiculo', N'FKT_odometro_diario_vehiculos_id_vehiculo', N'odometro_diario'),
    (N'FK_ordenes_mantenimiento_empresa', N'FKT_ordenes_mantenimiento_empresas_id_empresa', N'ordenes_mantenimiento'),
    (N'FK_ordenes_mantenimiento_taller', N'FKT_ordenes_mantenimiento_talleres_id_taller', N'ordenes_mantenimiento'),
    (N'FK_ordenes_mantenimiento_talleres_id_taller', N'FKT_ordenes_mantenimiento_talleres_id_taller', N'ordenes_mantenimiento'),
    (N'FK_ordenes_mantenimiento_vehiculo', N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo', N'ordenes_mantenimiento'),
    (N'FK_ordenes_mantenimiento_vehiculos_id_vehiculo', N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo', N'ordenes_mantenimiento'),
    (N'FK_peajes_conductor', N'FKT_peajes_personas_id_conductor', N'peajes'),
    (N'FK_peajes_empresa', N'FKT_peajes_empresas_id_empresa', N'peajes'),
    (N'FK_peajes_ruta', N'FKT_peajes_rutas_id_ruta', N'peajes'),
    (N'FK_peajes_vehiculo', N'FKT_peajes_vehiculos_id_vehiculo', N'peajes'),
    (N'FK_polizas_seguros_empresa', N'FKT_polizas_seguros_empresas_id_empresa', N'polizas_seguros'),
    (N'FK_polizas_seguros_vehiculo', N'FKT_polizas_seguros_vehiculos_id_vehiculo', N'polizas_seguros'),
    (N'FK_polizas_seguros_vehiculos_id_vehiculo', N'FKT_polizas_seguros_vehiculos_id_vehiculo', N'polizas_seguros'),
    (N'FK_precios_combustible_empresa', N'FKT_precios_combustible_empresas_id_empresa', N'precios_combustible'),
    (N'FK_rutas_empresa', N'FKT_rutas_empresas_id_empresa', N'rutas'),
    (N'FK_salarios_diarios_empresa', N'FKT_salarios_diarios_empresas_id_empresa', N'salarios_diarios'),
    (N'FK_salarios_diarios_persona', N'FKT_salarios_diarios_personas_id_persona', N'salarios_diarios'),
    (N'FK_salarios_diarios_personas_id_persona', N'FKT_salarios_diarios_personas_id_persona', N'salarios_diarios'),
    (N'FK_salarios_diarios_vehiculo', N'FKT_salarios_diarios_vehiculos_id_vehiculo', N'salarios_diarios'),
    (N'FK_salarios_diarios_vehiculos_id_vehiculo', N'FKT_salarios_diarios_vehiculos_id_vehiculo', N'salarios_diarios'),
    (N'FK_talleres_empresa', N'FKT_talleres_empresas_id_empresa', N'talleres'),
    (N'FK_tipos_vehiculo_empresa', N'FKT_tipos_vehiculo_empresas_id_empresa', N'tipos_vehiculo'),
    (N'FK_vehiculos_empresa', N'FKT_vehiculos_empresas_id_empresa', N'vehiculos'),
    (N'FK_vehiculos_ruta', N'FKT_vehiculos_rutas_id_ruta', N'vehiculos'),
    (N'FK_vehiculos_rutas_id_ruta', N'FKT_vehiculos_rutas_id_ruta', N'vehiculos'),
    (N'FK_vehiculos_tipo', N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'vehiculos'),
    (N'FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'vehiculos')
) v (viejo, nuevo, hijo)
WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name IN (v.viejo, v.nuevo) AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));

-- d) ninguna fila sin tenant y e) ninguna fila que apunte a otro tenant (solo si lo anterior esta en orden)
IF LEN(@errores) = 0
BEGIN
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.vehiculos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' vehiculos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.tipos_vehiculo WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' tipos_vehiculo: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.rutas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' rutas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.talleres WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' talleres: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.categorias_repuesto WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' categorias_repuesto: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.odometro_diario WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' odometro_diario: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargas_combustible WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' cargas_combustible: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.salarios_diarios WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' salarios_diarios: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ordenes_mantenimiento WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' ordenes_mantenimiento: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.gastos_repuestos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' gastos_repuestos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.polizas_seguros WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' polizas_seguros: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.peajes WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' peajes: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.control_salidas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' control_salidas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.notas WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' notas: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ingresos_operativos WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' ingresos_operativos: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.precios_combustible WHERE id_tenant IS NULL;', N'@c INT OUTPUT', @c = @n OUTPUT;
    IF @n > 0 SET @errores += CONCAT(N' precios_combustible: ', @n, N' fila(s) con id_tenant nulo; vuelva a ejecutar el script de F1.2 de ese grupo.');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' cargas_combustible (id_conductor): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' cargas_combustible (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.cargas_combustible c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' cargas_combustible (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_categorias_repuesto_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.categorias_repuesto c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' categorias_repuesto (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.control_salidas c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' control_salidas (id_conductor): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.control_salidas c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' control_salidas (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.control_salidas c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' control_salidas (id_ruta): ', @n, N' fila(s) apuntan a rutas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.control_salidas c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' control_salidas (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.categorias_repuesto p ON p.id_categoria_repuesto = c.id_categoria_repuesto WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' gastos_repuestos (id_categoria_repuesto): ', @n, N' fila(s) apuntan a categorias_repuesto de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' gastos_repuestos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.gastos_repuestos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' gastos_repuestos (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ingresos_operativos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ingresos_operativos (id_ruta): ', @n, N' fila(s) apuntan a rutas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ingresos_operativos c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ingresos_operativos (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.notas c JOIN dbo.clientes p ON p.id_cliente = c.id_cliente WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' notas (id_cliente): ', @n, N' fila(s) apuntan a clientes de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.notas c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' notas (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' odometro_diario (id_conductor): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' odometro_diario (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' odometro_diario (id_ruta): ', @n, N' fila(s) apuntan a rutas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.odometro_diario c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' odometro_diario (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ordenes_mantenimiento (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_talleres_id_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.talleres p ON p.id_taller = c.id_taller WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ordenes_mantenimiento (id_taller): ', @n, N' fila(s) apuntan a talleres de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.ordenes_mantenimiento c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' ordenes_mantenimiento (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.peajes c JOIN dbo.personas p ON p.id_persona = c.id_conductor WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' peajes (id_conductor): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.peajes c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' peajes (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.peajes c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' peajes (id_ruta): ', @n, N' fila(s) apuntan a rutas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.peajes c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' peajes (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' polizas_seguros (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.polizas_seguros c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' polizas_seguros (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.precios_combustible c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' precios_combustible (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_rutas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.rutas'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.rutas c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' rutas (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' salarios_diarios (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.personas p ON p.id_persona = c.id_persona WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' salarios_diarios (id_persona): ', @n, N' fila(s) apuntan a personas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.salarios_diarios c JOIN dbo.vehiculos p ON p.id_vehiculo = c.id_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' salarios_diarios (id_vehiculo): ', @n, N' fila(s) apuntan a vehiculos de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_talleres_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.talleres'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.talleres c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' talleres (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tipos_vehiculo_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.tipos_vehiculo c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' tipos_vehiculo (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.vehiculos c JOIN dbo.empresas p ON p.id_empresa = c.id_empresa WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' vehiculos (id_empresa): ', @n, N' fila(s) apuntan a empresas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.vehiculos c JOIN dbo.rutas p ON p.id_ruta = c.id_ruta WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' vehiculos (id_ruta): ', @n, N' fila(s) apuntan a rutas de otro tenant.');
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        EXEC sys.sp_executesql N'SELECT @c = COUNT(*) FROM dbo.vehiculos c JOIN dbo.tipos_vehiculo p ON p.id_tipo_vehiculo = c.id_tipo_vehiculo WHERE c.id_tenant <> p.id_tenant;', N'@c INT OUTPUT', @c = @n OUTPUT;
        IF @n > 0 SET @errores += CONCAT(N' vehiculos (id_tipo_vehiculo): ', @n, N' fila(s) apuntan a tipos_vehiculo de otro tenant.');
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 033 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50033, @mensaje, 1;
END

CREATE TABLE #precheck_033 (ok BIT NOT NULL);
INSERT INTO #precheck_033 (ok) VALUES (1);

PRINT N'PRECHECK 033 correcto: 16 tabla(s), 39 clave(s) foranea(s) por convertir.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_033') IS NULL
    THROW 50033, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @columnas INT = 0, @unicas INT = 0, @claves INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- vehiculos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vehiculos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_vehiculos_tenant' AND object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_vehiculos_tenant ON dbo.vehiculos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_vehiculos_tenant ON dbo.vehiculos (id_tenant);';
        SET @columnas += 1;
    END
    -- tipos_vehiculo.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tipos_vehiculo') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tipos_vehiculo_tenant' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
            EXEC sys.sp_executesql N'DROP INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tipos_vehiculo ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo (id_tenant);';
        SET @columnas += 1;
    END
    -- rutas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.rutas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_rutas_tenant' AND object_id = OBJECT_ID(N'dbo.rutas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_rutas_tenant ON dbo.rutas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.rutas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_rutas_tenant ON dbo.rutas (id_tenant);';
        SET @columnas += 1;
    END
    -- talleres.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.talleres') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_talleres_tenant' AND object_id = OBJECT_ID(N'dbo.talleres'))
            EXEC sys.sp_executesql N'DROP INDEX IX_talleres_tenant ON dbo.talleres;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.talleres ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_talleres_tenant ON dbo.talleres (id_tenant);';
        SET @columnas += 1;
    END
    -- categorias_repuesto.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.categorias_repuesto') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_categorias_repuesto_tenant' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
            EXEC sys.sp_executesql N'DROP INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.categorias_repuesto ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto (id_tenant);';
        SET @columnas += 1;
    END
    -- odometro_diario.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.odometro_diario') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_odometro_diario_tenant' AND object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'DROP INDEX IX_odometro_diario_tenant ON dbo.odometro_diario;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_odometro_diario_tenant ON dbo.odometro_diario (id_tenant);';
        SET @columnas += 1;
    END
    -- cargas_combustible.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargas_combustible') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargas_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'DROP INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible (id_tenant);';
        SET @columnas += 1;
    END
    -- salarios_diarios.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.salarios_diarios') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_salarios_diarios_tenant' AND object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'DROP INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios (id_tenant);';
        SET @columnas += 1;
    END
    -- ordenes_mantenimiento.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ordenes_mantenimiento_tenant' AND object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'DROP INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento (id_tenant);';
        SET @columnas += 1;
    END
    -- gastos_repuestos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.gastos_repuestos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_gastos_repuestos_tenant' AND object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos (id_tenant);';
        SET @columnas += 1;
    END
    -- polizas_seguros.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.polizas_seguros') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_polizas_seguros_tenant' AND object_id = OBJECT_ID(N'dbo.polizas_seguros'))
            EXEC sys.sp_executesql N'DROP INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros (id_tenant);';
        SET @columnas += 1;
    END
    -- peajes.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.peajes') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_peajes_tenant' AND object_id = OBJECT_ID(N'dbo.peajes'))
            EXEC sys.sp_executesql N'DROP INDEX IX_peajes_tenant ON dbo.peajes;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_peajes_tenant ON dbo.peajes (id_tenant);';
        SET @columnas += 1;
    END
    -- control_salidas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.control_salidas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_control_salidas_tenant' AND object_id = OBJECT_ID(N'dbo.control_salidas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_control_salidas_tenant ON dbo.control_salidas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_control_salidas_tenant ON dbo.control_salidas (id_tenant);';
        SET @columnas += 1;
    END
    -- notas.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.notas') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notas_tenant' AND object_id = OBJECT_ID(N'dbo.notas'))
            EXEC sys.sp_executesql N'DROP INDEX IX_notas_tenant ON dbo.notas;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_notas_tenant ON dbo.notas (id_tenant);';
        SET @columnas += 1;
    END
    -- ingresos_operativos.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ingresos_operativos') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ingresos_operativos_tenant' AND object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
            EXEC sys.sp_executesql N'DROP INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos (id_tenant);';
        SET @columnas += 1;
    END
    -- precios_combustible.id_tenant pasa a NOT NULL (el indice que depende de la columna se suelta y se vuelve a crear)
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.precios_combustible') AND name = N'id_tenant' AND is_nullable = 1)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_precios_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.precios_combustible'))
            EXEC sys.sp_executesql N'DROP INDEX IX_precios_combustible_tenant ON dbo.precios_combustible;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible ALTER COLUMN id_tenant INT NOT NULL;';
        EXEC sys.sp_executesql N'CREATE INDEX IX_precios_combustible_tenant ON dbo.precios_combustible (id_tenant);';
        SET @columnas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_vehiculos_tenant_id_vehiculo' AND object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_vehiculos_tenant_id_vehiculo ON dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_rutas_tenant_id_ruta' AND object_id = OBJECT_ID(N'dbo.rutas'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_rutas_tenant_id_ruta ON dbo.rutas (id_tenant, id_ruta);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_categorias_repuesto_tenant_id_categoria_repuesto' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_categorias_repuesto_tenant_id_categoria_repuesto ON dbo.categorias_repuesto (id_tenant, id_categoria_repuesto);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_talleres_tenant_id_taller' AND object_id = OBJECT_ID(N'dbo.talleres'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_talleres_tenant_id_taller ON dbo.talleres (id_tenant, id_taller);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tipos_vehiculo_tenant_id_tipo_vehiculo' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_tipos_vehiculo_tenant_id_tipo_vehiculo ON dbo.tipos_vehiculo (id_tenant, id_tipo_vehiculo);';
        SET @unicas += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_conductor;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_personas_id_conductor;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FKT_cargas_combustible_personas_id_conductor FOREIGN KEY (id_tenant, id_conductor) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FKT_cargas_combustible_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FKT_cargas_combustible_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_categorias_repuesto_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_categorias_repuesto_empresa' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.categorias_repuesto DROP CONSTRAINT FK_categorias_repuesto_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.categorias_repuesto ADD CONSTRAINT FKT_categorias_repuesto_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_conductor' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas DROP CONSTRAINT FK_control_salidas_conductor;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD CONSTRAINT FKT_control_salidas_personas_id_conductor FOREIGN KEY (id_tenant, id_conductor) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas DROP CONSTRAINT FK_control_salidas_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD CONSTRAINT FKT_control_salidas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_ruta' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas DROP CONSTRAINT FK_control_salidas_ruta;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD CONSTRAINT FKT_control_salidas_rutas_id_ruta FOREIGN KEY (id_tenant, id_ruta) REFERENCES dbo.rutas (id_tenant, id_ruta);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas DROP CONSTRAINT FK_control_salidas_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.control_salidas ADD CONSTRAINT FKT_control_salidas_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_categoria' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_categoria;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto FOREIGN KEY (id_tenant, id_categoria_repuesto) REFERENCES dbo.categorias_repuesto (id_tenant, id_categoria_repuesto);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FKT_gastos_repuestos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FKT_gastos_repuestos_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FK_ingresos_operativos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FKT_ingresos_operativos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_ruta' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FK_ingresos_operativos_ruta;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FKT_ingresos_operativos_rutas_id_ruta FOREIGN KEY (id_tenant, id_ruta) REFERENCES dbo.rutas (id_tenant, id_ruta);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FK_ingresos_operativos_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FKT_ingresos_operativos_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_cliente' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.notas DROP CONSTRAINT FK_notas_cliente;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ADD CONSTRAINT FKT_notas_clientes_id_cliente FOREIGN KEY (id_tenant, id_cliente) REFERENCES dbo.clientes (id_tenant, id_cliente);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.notas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.notas DROP CONSTRAINT FK_notas_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.notas ADD CONSTRAINT FKT_notas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_conductor;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_personas_id_conductor;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FKT_odometro_diario_personas_id_conductor FOREIGN KEY (id_tenant, id_conductor) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_empresa' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FKT_odometro_diario_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_ruta;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_rutas_id_ruta;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FKT_odometro_diario_rutas_id_ruta FOREIGN KEY (id_tenant, id_ruta) REFERENCES dbo.rutas (id_tenant, id_ruta);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FK_odometro_diario_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FKT_odometro_diario_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FKT_ordenes_mantenimiento_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_talleres_id_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_taller;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_talleres_id_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_talleres_id_taller;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FKT_ordenes_mantenimiento_talleres_id_taller FOREIGN KEY (id_tenant, id_taller) REFERENCES dbo.talleres (id_tenant, id_taller);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FKT_ordenes_mantenimiento_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_conductor' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes DROP CONSTRAINT FK_peajes_conductor;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD CONSTRAINT FKT_peajes_personas_id_conductor FOREIGN KEY (id_tenant, id_conductor) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_empresa' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes DROP CONSTRAINT FK_peajes_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD CONSTRAINT FKT_peajes_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_ruta' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes DROP CONSTRAINT FK_peajes_ruta;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD CONSTRAINT FKT_peajes_rutas_id_ruta FOREIGN KEY (id_tenant, id_ruta) REFERENCES dbo.rutas (id_tenant, id_ruta);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.peajes'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes DROP CONSTRAINT FK_peajes_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.peajes ADD CONSTRAINT FKT_peajes_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_empresa' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FK_polizas_seguros_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FKT_polizas_seguros_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FK_polizas_seguros_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FK_polizas_seguros_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FKT_polizas_seguros_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_empresa' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FK_precios_combustible_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FKT_precios_combustible_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_rutas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.rutas'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_rutas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.rutas'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.rutas DROP CONSTRAINT FK_rutas_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.rutas ADD CONSTRAINT FKT_rutas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_empresa' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FKT_salarios_diarios_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_persona;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_personas_id_persona;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FKT_salarios_diarios_personas_id_persona FOREIGN KEY (id_tenant, id_persona) REFERENCES dbo.personas (id_tenant, id_persona);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_vehiculo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_vehiculos_id_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FKT_salarios_diarios_vehiculos_id_vehiculo FOREIGN KEY (id_tenant, id_vehiculo) REFERENCES dbo.vehiculos (id_tenant, id_vehiculo);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_talleres_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.talleres'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_talleres_empresa' AND parent_object_id = OBJECT_ID(N'dbo.talleres'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.talleres DROP CONSTRAINT FK_talleres_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.talleres ADD CONSTRAINT FKT_talleres_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tipos_vehiculo_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tipos_vehiculo_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.tipos_vehiculo DROP CONSTRAINT FK_tipos_vehiculo_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tipos_vehiculo ADD CONSTRAINT FKT_tipos_vehiculo_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_empresa;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ADD CONSTRAINT FKT_vehiculos_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_ruta;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_rutas_id_ruta;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ADD CONSTRAINT FKT_vehiculos_rutas_id_ruta FOREIGN KEY (id_tenant, id_ruta) REFERENCES dbo.rutas (id_tenant, id_ruta);';
        SET @claves += 1;
    END
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tipo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_tipo;';
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos'))
            EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos DROP CONSTRAINT FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.vehiculos ADD CONSTRAINT FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo FOREIGN KEY (id_tenant, id_tipo_vehiculo) REFERENCES dbo.tipos_vehiculo (id_tenant, id_tipo_vehiculo);';
        SET @claves += 1;
    END
    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES
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
    (N'precios_combustible')
    ) v (t) WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);
    IF @malas > 0 THROW 50033, N'POSTCHECK: alguna tabla del grupo conserva id_tenant nulable. Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'vehiculos', N'UX_vehiculos_tenant_id_vehiculo'),
    (N'rutas', N'UX_rutas_tenant_id_ruta'),
    (N'categorias_repuesto', N'UX_categorias_repuesto_tenant_id_categoria_repuesto'),
    (N'talleres', N'UX_talleres_tenant_id_taller'),
    (N'tipos_vehiculo', N'UX_tipos_vehiculo_tenant_id_tipo_vehiculo')
    ) v (t, i) WHERE NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = v.i AND object_id = OBJECT_ID(N'dbo.' + v.t));
    IF @malas > 0 THROW 50033, N'POSTCHECK: falta alguna clave unica (id_tenant, ...). Se revierte.', 1;
    SELECT @malas = COUNT(*) FROM (VALUES
    (N'FKT_cargas_combustible_personas_id_conductor', N'FK_cargas_combustible_conductor', N'cargas_combustible'),
    (N'FKT_cargas_combustible_personas_id_conductor', N'FK_cargas_combustible_personas_id_conductor', N'cargas_combustible'),
    (N'FKT_cargas_combustible_empresas_id_empresa', N'FK_cargas_combustible_empresa', N'cargas_combustible'),
    (N'FKT_cargas_combustible_vehiculos_id_vehiculo', N'FK_cargas_combustible_vehiculo', N'cargas_combustible'),
    (N'FKT_cargas_combustible_vehiculos_id_vehiculo', N'FK_cargas_combustible_vehiculos_id_vehiculo', N'cargas_combustible'),
    (N'FKT_categorias_repuesto_empresas_id_empresa', N'FK_categorias_repuesto_empresa', N'categorias_repuesto'),
    (N'FKT_control_salidas_personas_id_conductor', N'FK_control_salidas_conductor', N'control_salidas'),
    (N'FKT_control_salidas_empresas_id_empresa', N'FK_control_salidas_empresa', N'control_salidas'),
    (N'FKT_control_salidas_rutas_id_ruta', N'FK_control_salidas_ruta', N'control_salidas'),
    (N'FKT_control_salidas_vehiculos_id_vehiculo', N'FK_control_salidas_vehiculo', N'control_salidas'),
    (N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'FK_gastos_repuestos_categoria', N'gastos_repuestos'),
    (N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto', N'gastos_repuestos'),
    (N'FKT_gastos_repuestos_empresas_id_empresa', N'FK_gastos_repuestos_empresa', N'gastos_repuestos'),
    (N'FKT_gastos_repuestos_vehiculos_id_vehiculo', N'FK_gastos_repuestos_vehiculo', N'gastos_repuestos'),
    (N'FKT_gastos_repuestos_vehiculos_id_vehiculo', N'FK_gastos_repuestos_vehiculos_id_vehiculo', N'gastos_repuestos'),
    (N'FKT_ingresos_operativos_empresas_id_empresa', N'FK_ingresos_operativos_empresa', N'ingresos_operativos'),
    (N'FKT_ingresos_operativos_rutas_id_ruta', N'FK_ingresos_operativos_ruta', N'ingresos_operativos'),
    (N'FKT_ingresos_operativos_vehiculos_id_vehiculo', N'FK_ingresos_operativos_vehiculo', N'ingresos_operativos'),
    (N'FKT_notas_clientes_id_cliente', N'FK_notas_cliente', N'notas'),
    (N'FKT_notas_empresas_id_empresa', N'FK_notas_empresa', N'notas'),
    (N'FKT_odometro_diario_personas_id_conductor', N'FK_odometro_diario_conductor', N'odometro_diario'),
    (N'FKT_odometro_diario_personas_id_conductor', N'FK_odometro_diario_personas_id_conductor', N'odometro_diario'),
    (N'FKT_odometro_diario_empresas_id_empresa', N'FK_odometro_diario_empresa', N'odometro_diario'),
    (N'FKT_odometro_diario_rutas_id_ruta', N'FK_odometro_diario_ruta', N'odometro_diario'),
    (N'FKT_odometro_diario_rutas_id_ruta', N'FK_odometro_diario_rutas_id_ruta', N'odometro_diario'),
    (N'FKT_odometro_diario_vehiculos_id_vehiculo', N'FK_odometro_diario_vehiculo', N'odometro_diario'),
    (N'FKT_odometro_diario_vehiculos_id_vehiculo', N'FK_odometro_diario_vehiculos_id_vehiculo', N'odometro_diario'),
    (N'FKT_ordenes_mantenimiento_empresas_id_empresa', N'FK_ordenes_mantenimiento_empresa', N'ordenes_mantenimiento'),
    (N'FKT_ordenes_mantenimiento_talleres_id_taller', N'FK_ordenes_mantenimiento_taller', N'ordenes_mantenimiento'),
    (N'FKT_ordenes_mantenimiento_talleres_id_taller', N'FK_ordenes_mantenimiento_talleres_id_taller', N'ordenes_mantenimiento'),
    (N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo', N'FK_ordenes_mantenimiento_vehiculo', N'ordenes_mantenimiento'),
    (N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo', N'FK_ordenes_mantenimiento_vehiculos_id_vehiculo', N'ordenes_mantenimiento'),
    (N'FKT_peajes_personas_id_conductor', N'FK_peajes_conductor', N'peajes'),
    (N'FKT_peajes_empresas_id_empresa', N'FK_peajes_empresa', N'peajes'),
    (N'FKT_peajes_rutas_id_ruta', N'FK_peajes_ruta', N'peajes'),
    (N'FKT_peajes_vehiculos_id_vehiculo', N'FK_peajes_vehiculo', N'peajes'),
    (N'FKT_polizas_seguros_empresas_id_empresa', N'FK_polizas_seguros_empresa', N'polizas_seguros'),
    (N'FKT_polizas_seguros_vehiculos_id_vehiculo', N'FK_polizas_seguros_vehiculo', N'polizas_seguros'),
    (N'FKT_polizas_seguros_vehiculos_id_vehiculo', N'FK_polizas_seguros_vehiculos_id_vehiculo', N'polizas_seguros'),
    (N'FKT_precios_combustible_empresas_id_empresa', N'FK_precios_combustible_empresa', N'precios_combustible'),
    (N'FKT_rutas_empresas_id_empresa', N'FK_rutas_empresa', N'rutas'),
    (N'FKT_salarios_diarios_empresas_id_empresa', N'FK_salarios_diarios_empresa', N'salarios_diarios'),
    (N'FKT_salarios_diarios_personas_id_persona', N'FK_salarios_diarios_persona', N'salarios_diarios'),
    (N'FKT_salarios_diarios_personas_id_persona', N'FK_salarios_diarios_personas_id_persona', N'salarios_diarios'),
    (N'FKT_salarios_diarios_vehiculos_id_vehiculo', N'FK_salarios_diarios_vehiculo', N'salarios_diarios'),
    (N'FKT_salarios_diarios_vehiculos_id_vehiculo', N'FK_salarios_diarios_vehiculos_id_vehiculo', N'salarios_diarios'),
    (N'FKT_talleres_empresas_id_empresa', N'FK_talleres_empresa', N'talleres'),
    (N'FKT_tipos_vehiculo_empresas_id_empresa', N'FK_tipos_vehiculo_empresa', N'tipos_vehiculo'),
    (N'FKT_vehiculos_empresas_id_empresa', N'FK_vehiculos_empresa', N'vehiculos'),
    (N'FKT_vehiculos_rutas_id_ruta', N'FK_vehiculos_ruta', N'vehiculos'),
    (N'FKT_vehiculos_rutas_id_ruta', N'FK_vehiculos_rutas_id_ruta', N'vehiculos'),
    (N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'FK_vehiculos_tipo', N'vehiculos'),
    (N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo', N'vehiculos')
    ) v (nuevo, viejo, hijo) WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nuevo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo) AND f.is_not_trusted = 0 AND f.is_disabled = 0)
       OR EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.viejo AND f.parent_object_id = OBJECT_ID(N'dbo.' + v.hijo));
    IF @malas > 0 THROW 50033, N'POSTCHECK: alguna clave foranea compuesta falta, no es de confianza o la simple sigue. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @columnas = 0 AND @unicas = 0 AND @claves = 0
        PRINT N'Nada que hacer: este grupo ya estaba contraido.';
    ELSE
        PRINT CONCAT(N'CAMBIO 033 aplicado: ', @columnas, N' columna(s) NOT NULL; ', @unicas, N' clave(s) unica(s); ', @claves, N' clave(s) foranea(s) compuesta(s).');
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
WHERE t.name IN (N'vehiculos', N'tipos_vehiculo', N'rutas', N'talleres', N'categorias_repuesto', N'odometro_diario', N'cargas_combustible', N'salarios_diarios', N'ordenes_mantenimiento', N'gastos_repuestos', N'polizas_seguros', N'peajes', N'control_salidas', N'notas', N'ingresos_operativos', N'precios_combustible')
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
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos DROP CONSTRAINT FKT_vehiculos_tipos_vehiculo_id_tipo_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tipo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_tipo FOREIGN KEY (id_tipo_vehiculo) REFERENCES dbo.tipos_vehiculo (id_tipo_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_tipos_vehiculo_id_tipo_vehiculo FOREIGN KEY (id_tipo_vehiculo) REFERENCES dbo.tipos_vehiculo (id_tipo_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos DROP CONSTRAINT FKT_vehiculos_rutas_id_ruta;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_rutas_id_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta) ON DELETE SET NULL;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_vehiculos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos DROP CONSTRAINT FKT_vehiculos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_vehiculos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.vehiculos')) ALTER TABLE dbo.vehiculos ADD CONSTRAINT FK_vehiculos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_tipos_vehiculo_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) ALTER TABLE dbo.tipos_vehiculo DROP CONSTRAINT FKT_tipos_vehiculo_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tipos_vehiculo_empresa' AND parent_object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) ALTER TABLE dbo.tipos_vehiculo ADD CONSTRAINT FK_tipos_vehiculo_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_talleres_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.talleres')) ALTER TABLE dbo.talleres DROP CONSTRAINT FKT_talleres_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_talleres_empresa' AND parent_object_id = OBJECT_ID(N'dbo.talleres')) ALTER TABLE dbo.talleres ADD CONSTRAINT FK_talleres_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FKT_salarios_diarios_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FKT_salarios_diarios_personas_id_persona;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_personas_id_persona' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_personas_id_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_salarios_diarios_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FKT_salarios_diarios_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_empresa' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios')) ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_rutas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.rutas')) ALTER TABLE dbo.rutas DROP CONSTRAINT FKT_rutas_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_rutas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.rutas')) ALTER TABLE dbo.rutas ADD CONSTRAINT FK_rutas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_precios_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible')) ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FKT_precios_combustible_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_empresa' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible')) ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FK_precios_combustible_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FKT_polizas_seguros_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FK_polizas_seguros_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FK_polizas_seguros_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_polizas_seguros_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FKT_polizas_seguros_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_empresa' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros')) ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FK_polizas_seguros_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes DROP CONSTRAINT FKT_peajes_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes ADD CONSTRAINT FK_peajes_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes DROP CONSTRAINT FKT_peajes_rutas_id_ruta;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_ruta' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes ADD CONSTRAINT FK_peajes_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes DROP CONSTRAINT FKT_peajes_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_empresa' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes ADD CONSTRAINT FK_peajes_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_peajes_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes DROP CONSTRAINT FKT_peajes_personas_id_conductor;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_peajes_conductor' AND parent_object_id = OBJECT_ID(N'dbo.peajes')) ALTER TABLE dbo.peajes ADD CONSTRAINT FK_peajes_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FKT_ordenes_mantenimiento_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_talleres_id_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FKT_ordenes_mantenimiento_talleres_id_taller;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_taller FOREIGN KEY (id_taller) REFERENCES dbo.talleres (id_taller);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_talleres_id_taller' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_talleres_id_taller FOREIGN KEY (id_taller) REFERENCES dbo.talleres (id_taller);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ordenes_mantenimiento_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FKT_ordenes_mantenimiento_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FKT_odometro_diario_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FKT_odometro_diario_rutas_id_ruta;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_rutas_id_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta) ON DELETE SET NULL;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FKT_odometro_diario_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_empresa' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_odometro_diario_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario DROP CONSTRAINT FKT_odometro_diario_personas_id_conductor;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_odometro_diario_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.odometro_diario')) ALTER TABLE dbo.odometro_diario ADD CONSTRAINT FK_odometro_diario_personas_id_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona) ON DELETE SET NULL;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas DROP CONSTRAINT FKT_notas_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas ADD CONSTRAINT FK_notas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_notas_clientes_id_cliente' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas DROP CONSTRAINT FKT_notas_clientes_id_cliente;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_notas_cliente' AND parent_object_id = OBJECT_ID(N'dbo.notas')) ALTER TABLE dbo.notas ADD CONSTRAINT FK_notas_cliente FOREIGN KEY (id_cliente) REFERENCES dbo.clientes (id_cliente);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FKT_ingresos_operativos_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FK_ingresos_operativos_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FKT_ingresos_operativos_rutas_id_ruta;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_ruta' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FK_ingresos_operativos_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_ingresos_operativos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FKT_ingresos_operativos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos')) ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FK_ingresos_operativos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FKT_gastos_repuestos_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FKT_gastos_repuestos_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_empresa' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FKT_gastos_repuestos_categorias_repuesto_id_categoria_repuesto;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_categoria' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_categoria FOREIGN KEY (id_categoria_repuesto) REFERENCES dbo.categorias_repuesto (id_categoria_repuesto);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos')) ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_categorias_repuesto_id_categoria_repuesto FOREIGN KEY (id_categoria_repuesto) REFERENCES dbo.categorias_repuesto (id_categoria_repuesto);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas DROP CONSTRAINT FKT_control_salidas_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas ADD CONSTRAINT FK_control_salidas_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_rutas_id_ruta' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas DROP CONSTRAINT FKT_control_salidas_rutas_id_ruta;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_ruta' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas ADD CONSTRAINT FK_control_salidas_ruta FOREIGN KEY (id_ruta) REFERENCES dbo.rutas (id_ruta);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas DROP CONSTRAINT FKT_control_salidas_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_empresa' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas ADD CONSTRAINT FK_control_salidas_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_control_salidas_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas DROP CONSTRAINT FKT_control_salidas_personas_id_conductor;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_control_salidas_conductor' AND parent_object_id = OBJECT_ID(N'dbo.control_salidas')) ALTER TABLE dbo.control_salidas ADD CONSTRAINT FK_control_salidas_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_categorias_repuesto_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto')) ALTER TABLE dbo.categorias_repuesto DROP CONSTRAINT FKT_categorias_repuesto_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_categorias_repuesto_empresa' AND parent_object_id = OBJECT_ID(N'dbo.categorias_repuesto')) ALTER TABLE dbo.categorias_repuesto ADD CONSTRAINT FK_categorias_repuesto_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FKT_cargas_combustible_vehiculos_id_vehiculo;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_vehiculos_id_vehiculo' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_vehiculos_id_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES dbo.vehiculos (id_vehiculo);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_empresas_id_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FKT_cargas_combustible_empresas_id_empresa;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_empresa' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas (id_empresa);
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FKT_cargas_combustible_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FKT_cargas_combustible_personas_id_conductor;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_personas_id_conductor' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible')) ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_personas_id_conductor FOREIGN KEY (id_conductor) REFERENCES dbo.personas (id_persona) ON DELETE SET NULL;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_vehiculos_tenant_id_vehiculo' AND object_id = OBJECT_ID(N'dbo.vehiculos')) DROP INDEX UX_vehiculos_tenant_id_vehiculo ON dbo.vehiculos;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_rutas_tenant_id_ruta' AND object_id = OBJECT_ID(N'dbo.rutas')) DROP INDEX UX_rutas_tenant_id_ruta ON dbo.rutas;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_categorias_repuesto_tenant_id_categoria_repuesto' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto')) DROP INDEX UX_categorias_repuesto_tenant_id_categoria_repuesto ON dbo.categorias_repuesto;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_talleres_tenant_id_taller' AND object_id = OBJECT_ID(N'dbo.talleres')) DROP INDEX UX_talleres_tenant_id_taller ON dbo.talleres;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tipos_vehiculo_tenant_id_tipo_vehiculo' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) DROP INDEX UX_tipos_vehiculo_tenant_id_tipo_vehiculo ON dbo.tipos_vehiculo;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vehiculos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_vehiculos_tenant' AND object_id = OBJECT_ID(N'dbo.vehiculos')) DROP INDEX IX_vehiculos_tenant ON dbo.vehiculos;
        ALTER TABLE dbo.vehiculos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_vehiculos_tenant ON dbo.vehiculos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tipos_vehiculo') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tipos_vehiculo_tenant' AND object_id = OBJECT_ID(N'dbo.tipos_vehiculo')) DROP INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo;
        ALTER TABLE dbo.tipos_vehiculo ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_tipos_vehiculo_tenant ON dbo.tipos_vehiculo (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.rutas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_rutas_tenant' AND object_id = OBJECT_ID(N'dbo.rutas')) DROP INDEX IX_rutas_tenant ON dbo.rutas;
        ALTER TABLE dbo.rutas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_rutas_tenant ON dbo.rutas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.talleres') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_talleres_tenant' AND object_id = OBJECT_ID(N'dbo.talleres')) DROP INDEX IX_talleres_tenant ON dbo.talleres;
        ALTER TABLE dbo.talleres ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_talleres_tenant ON dbo.talleres (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.categorias_repuesto') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_categorias_repuesto_tenant' AND object_id = OBJECT_ID(N'dbo.categorias_repuesto')) DROP INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto;
        ALTER TABLE dbo.categorias_repuesto ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_categorias_repuesto_tenant ON dbo.categorias_repuesto (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.odometro_diario') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_odometro_diario_tenant' AND object_id = OBJECT_ID(N'dbo.odometro_diario')) DROP INDEX IX_odometro_diario_tenant ON dbo.odometro_diario;
        ALTER TABLE dbo.odometro_diario ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_odometro_diario_tenant ON dbo.odometro_diario (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cargas_combustible') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_cargas_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.cargas_combustible')) DROP INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible;
        ALTER TABLE dbo.cargas_combustible ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_cargas_combustible_tenant ON dbo.cargas_combustible (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.salarios_diarios') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_salarios_diarios_tenant' AND object_id = OBJECT_ID(N'dbo.salarios_diarios')) DROP INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios;
        ALTER TABLE dbo.salarios_diarios ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_salarios_diarios_tenant ON dbo.salarios_diarios (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ordenes_mantenimiento_tenant' AND object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento')) DROP INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento;
        ALTER TABLE dbo.ordenes_mantenimiento ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_ordenes_mantenimiento_tenant ON dbo.ordenes_mantenimiento (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.gastos_repuestos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_gastos_repuestos_tenant' AND object_id = OBJECT_ID(N'dbo.gastos_repuestos')) DROP INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos;
        ALTER TABLE dbo.gastos_repuestos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_gastos_repuestos_tenant ON dbo.gastos_repuestos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.polizas_seguros') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_polizas_seguros_tenant' AND object_id = OBJECT_ID(N'dbo.polizas_seguros')) DROP INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros;
        ALTER TABLE dbo.polizas_seguros ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_polizas_seguros_tenant ON dbo.polizas_seguros (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.peajes') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_peajes_tenant' AND object_id = OBJECT_ID(N'dbo.peajes')) DROP INDEX IX_peajes_tenant ON dbo.peajes;
        ALTER TABLE dbo.peajes ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_peajes_tenant ON dbo.peajes (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.control_salidas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_control_salidas_tenant' AND object_id = OBJECT_ID(N'dbo.control_salidas')) DROP INDEX IX_control_salidas_tenant ON dbo.control_salidas;
        ALTER TABLE dbo.control_salidas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_control_salidas_tenant ON dbo.control_salidas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.notas') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notas_tenant' AND object_id = OBJECT_ID(N'dbo.notas')) DROP INDEX IX_notas_tenant ON dbo.notas;
        ALTER TABLE dbo.notas ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_notas_tenant ON dbo.notas (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ingresos_operativos') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ingresos_operativos_tenant' AND object_id = OBJECT_ID(N'dbo.ingresos_operativos')) DROP INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos;
        ALTER TABLE dbo.ingresos_operativos ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_ingresos_operativos_tenant ON dbo.ingresos_operativos (id_tenant);
    END
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.precios_combustible') AND name = N'id_tenant' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_precios_combustible_tenant' AND object_id = OBJECT_ID(N'dbo.precios_combustible')) DROP INDEX IX_precios_combustible_tenant ON dbo.precios_combustible;
        ALTER TABLE dbo.precios_combustible ALTER COLUMN id_tenant INT NULL;
        CREATE INDEX IX_precios_combustible_tenant ON dbo.precios_combustible (id_tenant);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
