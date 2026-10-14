-- ============================================================
-- Script   : 036_rls_politicas_apagadas.sql
-- Proposito: Paso F1.4a del plan de la Fase 1 (ADR-001, ADR-015, ADR-023): Row-Level Security, segunda barrera del aislamiento.
--            Crea APAGADA la politica seg.pol_tenant y las tres funciones de predicado del esquema seg:
--              * 37 tablas de tenant: filtro y cuatro bloqueos (AFTER INSERT, AFTER UPDATE, BEFORE UPDATE, BEFORE DELETE) por
--                id_tenant = SESSION_CONTEXT('id_tenant'), sin excepciones para la plataforma (ADR-015).
--              * 2 tablas mixtas (tasas_cambio, sen_boletines): lectura de las filas globales (id_tenant nulo) y de las propias;
--                escritura propia por su tenant y de filas globales solo en el ambito 'plataforma'.
--              * empresas: solo los cuatro bloqueos. Es el directorio de cuentas: la plataforma y el inicio de sesion la leen para saber
--                el tenant de una empresa antes de tener contexto (ADR-023). Su lectura queda abierta; su escritura, aislada.
--              * domain_events queda fuera: solo la lee el despachador, que fija el contexto de cada evento (D20).
--            Con la politica APAGADA no cambia nada: ninguna consulta se filtra. El script 037 la enciende.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Requiere : 025 a 035 aplicados (id_tenant NOT NULL en todas las tablas de tenant) y el esquema seg (025).
-- Rollback : ver la seccion ROLLBACK al final (quita la politica y las funciones).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada): scripts anteriores aplicados, id_tenant obligatorio, ninguna otra politica sobre esas tablas y, si la
--      politica ya existe, con los 199 predicados esperados.
--   2. CAMBIO (una sola transaccion) y POSTCHECK dentro de ella: funciones, politica apagada con 199 predicados.
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion. Idempotente. Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
--   * SCHEMABINDING = ON: mientras exista la politica no se puede cambiar ni quitar id_tenant de estas tablas (quitar la tabla de la
--     politica primero, o la politica entera).
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
IF OBJECT_ID(N'tempdb..#precheck_036') IS NOT NULL DROP TABLE #precheck_036;

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;

-- a) el esquema seg existe y toda tabla de tenant tiene id_tenant
IF SCHEMA_ID(N'seg') IS NULL SET @errores += N' Falta el esquema seg (script 025).';
SELECT @errores += N' Falta id_tenant en ' + v.t + N' (scripts 025 a 030).'
FROM (VALUES
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
    (N'empresas'),
    (N'domain_events')
) v (t)
WHERE OBJECT_ID(N'dbo.' + v.t, N'U') IS NULL OR COL_LENGTH(N'dbo.' + v.t, N'id_tenant') IS NULL;

-- b) id_tenant es obligatorio en las tablas con filtro (scripts 031 a 035)
SELECT @errores += N' ' + v.t + N'.id_tenant sigue nulable (scripts 031 a 035).'
FROM (VALUES
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
    (N'empresas')
) v (t)
WHERE EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + v.t) AND name = N'id_tenant' AND is_nullable = 1);

-- c) ninguna otra politica toca estas tablas (SQL Server admite un solo filtro por tabla)
SELECT @errores += N' ' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND OBJECT_NAME(p.target_object_id) IN (
N'empresa_modulos', N'empresa_roles', N'empresa_rol_permisos', N'cargos', N'personas', N'persona_documentos', N'persona_empresa', N'empleados', N'clientes', N'proveedores', N'bitacora_cambios', N'vehiculos', N'tipos_vehiculo', N'rutas', N'talleres', N'categorias_repuesto', N'odometro_diario', N'cargas_combustible', N'salarios_diarios', N'ordenes_mantenimiento', N'gastos_repuestos', N'polizas_seguros', N'peajes', N'control_salidas', N'notas', N'ingresos_operativos', N'precios_combustible', N'condiciones_pago', N'condiciones_pago_cuotas', N'formas_pago', N'impuestos', N'productos_servicios', N'facturas', N'factura_detalle', N'factura_secuencias', N'pagos', N'pago_aplicaciones', N'tasas_cambio', N'sen_boletines', N'empresas', N'domain_events'
);

-- d) si la politica ya existe, es la esperada; las funciones no existen sueltas
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    IF @n <> 199 SET @errores += CONCAT(N' La politica seg.pol_tenant ya existe con ', @n, N' predicados y se esperaban 199.');
END
ELSE IF OBJECT_ID(N'seg.fn_tenant_actual') IS NOT NULL OR OBJECT_ID(N'seg.fn_mixta_lectura') IS NOT NULL OR OBJECT_ID(N'seg.fn_mixta_escritura') IS NOT NULL
    SET @errores += N' Hay funciones de seg sin la politica: revise antes de seguir.';

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 036 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50036, @mensaje, 1;
END

CREATE TABLE #precheck_036 (ok BIT NOT NULL);
INSERT INTO #precheck_036 (ok) VALUES (1);

PRINT N'PRECHECK 036 correcto: 41 tabla(s) revisadas, 199 predicados por crear.';
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_036') IS NULL
    THROW 50036, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @creadas INT = 0, @malas INT, @msg NVARCHAR(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'seg.fn_tenant_actual', N'IF') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION seg.fn_tenant_actual (@id_tenant INT)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS permitido
       WHERE @id_tenant = CAST(SESSION_CONTEXT(N''id_tenant'') AS INT);';
        SET @creadas += 1;
    END
    IF OBJECT_ID(N'seg.fn_mixta_lectura', N'IF') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION seg.fn_mixta_lectura (@id_tenant INT)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS permitido
       WHERE @id_tenant IS NULL
          OR @id_tenant = CAST(SESSION_CONTEXT(N''id_tenant'') AS INT);';
        SET @creadas += 1;
    END
    IF OBJECT_ID(N'seg.fn_mixta_escritura', N'IF') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION seg.fn_mixta_escritura (@id_tenant INT)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS permitido
       WHERE @id_tenant = CAST(SESSION_CONTEXT(N''id_tenant'') AS INT)
          OR (@id_tenant IS NULL AND CAST(SESSION_CONTEXT(N''ambito'') AS NVARCHAR(20)) = N''plataforma'');';
        SET @creadas += 1;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    BEGIN
        EXEC sys.sp_executesql N'CREATE SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_modulos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_modulos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_modulos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_modulos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_modulos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_roles,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_roles AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_roles AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_roles BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_roles BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_rol_permisos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_rol_permisos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_rol_permisos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_rol_permisos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresa_rol_permisos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.personas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.personas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.personas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.personas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.personas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_documentos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_documentos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_documentos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_documentos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_documentos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_empresa,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_empresa AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_empresa AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_empresa BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.persona_empresa BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empleados,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empleados AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empleados AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empleados BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empleados BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.clientes,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.clientes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.clientes AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.clientes BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.clientes BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.proveedores,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.proveedores AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.proveedores AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.proveedores BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.proveedores BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.bitacora_cambios,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.bitacora_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.bitacora_cambios AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.bitacora_cambios BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.bitacora_cambios BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.vehiculos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.vehiculos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.vehiculos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.vehiculos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.vehiculos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.tipos_vehiculo,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.tipos_vehiculo AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.tipos_vehiculo AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.tipos_vehiculo BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.tipos_vehiculo BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rutas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rutas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rutas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rutas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rutas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.talleres,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.talleres AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.talleres AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.talleres BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.talleres BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.categorias_repuesto,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.categorias_repuesto AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.categorias_repuesto AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.categorias_repuesto BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.categorias_repuesto BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.odometro_diario,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.odometro_diario AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.odometro_diario AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.odometro_diario BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.odometro_diario BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargas_combustible,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargas_combustible AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargas_combustible AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargas_combustible BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.cargas_combustible BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.salarios_diarios,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.salarios_diarios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.salarios_diarios AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.salarios_diarios BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.salarios_diarios BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ordenes_mantenimiento,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ordenes_mantenimiento AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ordenes_mantenimiento AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ordenes_mantenimiento BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ordenes_mantenimiento BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.gastos_repuestos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.gastos_repuestos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.gastos_repuestos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.gastos_repuestos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.gastos_repuestos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.polizas_seguros,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.polizas_seguros AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.polizas_seguros AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.polizas_seguros BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.polizas_seguros BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.peajes,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.peajes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.peajes AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.peajes BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.peajes BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.control_salidas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.control_salidas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.control_salidas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.control_salidas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.control_salidas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.notas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.notas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.notas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.notas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.notas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ingresos_operativos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ingresos_operativos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ingresos_operativos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ingresos_operativos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.ingresos_operativos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.precios_combustible,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.precios_combustible AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.precios_combustible AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.precios_combustible BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.precios_combustible BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago_cuotas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago_cuotas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago_cuotas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago_cuotas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.condiciones_pago_cuotas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.formas_pago,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.formas_pago AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.formas_pago AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.formas_pago BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.formas_pago BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.impuestos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.impuestos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.impuestos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.impuestos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.impuestos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.productos_servicios,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.productos_servicios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.productos_servicios AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.productos_servicios BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.productos_servicios BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.facturas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.facturas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.facturas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.facturas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.facturas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_detalle,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_detalle AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_detalle AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_detalle BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_detalle BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_secuencias,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_secuencias AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_secuencias AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_secuencias BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.factura_secuencias BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pagos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pagos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pagos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pagos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pagos BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pago_aplicaciones,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pago_aplicaciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pago_aplicaciones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pago_aplicaciones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.pago_aplicaciones BEFORE DELETE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.empresas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant) ON dbo.tasas_cambio,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.tasas_cambio AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.tasas_cambio AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.tasas_cambio BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.tasas_cambio BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant) ON dbo.sen_boletines,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.sen_boletines AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.sen_boletines AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.sen_boletines BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_mixta_escritura(id_tenant) ON dbo.sen_boletines BEFORE DELETE
WITH (STATE = OFF, SCHEMABINDING = ON);';
        SET @creadas += 1;
    END

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*) FROM (VALUES (N'fn_tenant_actual'), (N'fn_mixta_lectura'), (N'fn_mixta_escritura')) v (f)
    WHERE OBJECT_ID(N'seg.' + v.f, N'IF') IS NULL;
    IF @malas > 0 THROW 50036, N'POSTCHECK: falta alguna funcion de predicado en seg. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    IF @malas <> 199
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: la politica tiene ', @malas, N' predicados y se esperaban 199. Se revierte.');
        THROW 50036, @msg, 1;
    END

    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND predicate_type_desc = N'FILTER';
    IF @malas <> 39 THROW 50036, N'POSTCHECK: deberia haber un filtro por tabla (39). Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @creadas = 0
        PRINT N'Nada que hacer: la politica y sus funciones ya existian.';
    ELSE
        PRINT CONCAT(N'CAMBIO 036 aplicado: ', @creadas, N' objeto(s) creados. La politica queda APAGADA: aplique el 037 para encenderla.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: la politica, su estado y sus predicados
SELECT p.name AS politica, p.is_enabled AS encendida, p.is_schema_bound AS schemabinding,
       (SELECT COUNT(*) FROM sys.security_predicates x WHERE x.object_id = p.object_id) AS predicados
FROM sys.security_policies p WHERE p.name = N'pol_tenant';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita la politica y las tres funciones. No toca datos. Si la politica esta encendida, se apaga con la misma sentencia.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) DROP SECURITY POLICY seg.pol_tenant;
    IF OBJECT_ID(N'seg.fn_tenant_actual', N'IF') IS NOT NULL DROP FUNCTION seg.fn_tenant_actual;
    IF OBJECT_ID(N'seg.fn_mixta_lectura', N'IF') IS NOT NULL DROP FUNCTION seg.fn_mixta_lectura;
    IF OBJECT_ID(N'seg.fn_mixta_escritura', N'IF') IS NOT NULL DROP FUNCTION seg.fn_mixta_escritura;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
