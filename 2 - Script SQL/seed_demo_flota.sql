-- =============================================================================
-- Seed: Datos Dummy - Empresa Demo (id_empresa = 2)
-- Tablas: gastos_repuestos, ordenes_mantenimiento, polizas_seguros
-- Dependencias: vehiculos, talleres, categorias_repuesto deben existir
-- Idempotente: no inserta duplicados si ya existen registros similares
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @IdEmpresa INT = 2;
DECLARE @Hoy DATE = CAST(GETDATE() AS DATE);

-- Validar que la empresa Demo existe
IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE id_empresa = @IdEmpresa)
BEGIN
    RAISERROR('No existe la empresa Demo (id_empresa = 2). Cree la empresa primero.', 16, 1);
    RETURN;
END

-- ===========================================================================
-- 1. PRERREQUISITOS: Tipo de vehículo
-- ===========================================================================
DECLARE @IdTipoVehiculo INT;

-- Usar el primer tipo de vehículo de la empresa Demo, o crear uno si no existe
SELECT TOP 1 @IdTipoVehiculo = id_tipo_vehiculo
FROM dbo.tipos_vehiculo
WHERE id_empresa = @IdEmpresa AND eliminado = 0;

IF @IdTipoVehiculo IS NULL
BEGIN
    INSERT INTO dbo.tipos_vehiculo (id_empresa, codigo, nombre, descripcion, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'BUS', 'Bus', 'Bus de transporte público', 1, 0, 'seed', SYSUTCDATETIME());

    SET @IdTipoVehiculo = SCOPE_IDENTITY();
    PRINT 'Tipo de vehículo "Bus" creado (id=' + CAST(@IdTipoVehiculo AS NVARCHAR) + ')';
END

-- ===========================================================================
-- 2. PRERREQUISITOS: Vehículos
-- ===========================================================================
-- Asegurar que existan al menos 3 vehículos para la empresa Demo
DECLARE @VehiculosDemo TABLE (id_vehiculo INT, placa NVARCHAR(20));

-- Vehículo 1
IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-001' AND eliminado = 0)
BEGIN
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoVehiculo, 'HND-001', '001', 'Mercedes-Benz', 'OF-1721', 2019, 'Blanco',
            45, 'DIESEL', 85000.00, '2019-03-15', 1, 0, 'seed', SYSUTCDATETIME());
    PRINT 'Vehículo HND-001 creado';
END

-- Vehículo 2
IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-002' AND eliminado = 0)
BEGIN
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoVehiculo, 'HND-002', '002', 'Volvo', 'B270F', 2020, 'Azul',
            45, 'DIESEL', 62000.00, '2020-01-10', 1, 0, 'seed', SYSUTCDATETIME());
    PRINT 'Vehículo HND-002 creado';
END

-- Vehículo 3
IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-003' AND eliminado = 0)
BEGIN
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoVehiculo, 'HND-003', '003', 'Hino', 'AK8J', 2018, 'Amarillo',
            40, 'DIESEL', 120000.00, '2018-07-20', 1, 0, 'seed', SYSUTCDATETIME());
    PRINT 'Vehículo HND-003 creado';
END

-- Cargar IDs de los 3 vehículos
INSERT INTO @VehiculosDemo
SELECT TOP 3 id_vehiculo, placa
FROM dbo.vehiculos
WHERE id_empresa = @IdEmpresa AND eliminado = 0
ORDER BY id_vehiculo;

DECLARE @V1 INT, @V2 INT, @V3 INT;

SELECT @V1 = id_vehiculo FROM (SELECT id_vehiculo, ROW_NUMBER() OVER (ORDER BY id_vehiculo) AS rn FROM @VehiculosDemo) t WHERE rn = 1;
SELECT @V2 = id_vehiculo FROM (SELECT id_vehiculo, ROW_NUMBER() OVER (ORDER BY id_vehiculo) AS rn FROM @VehiculosDemo) t WHERE rn = 2;
SELECT @V3 = id_vehiculo FROM (SELECT id_vehiculo, ROW_NUMBER() OVER (ORDER BY id_vehiculo) AS rn FROM @VehiculosDemo) t WHERE rn = 3;

-- Si no hay suficientes, usar el primero para todos
IF @V2 IS NULL SET @V2 = @V1;
IF @V3 IS NULL SET @V3 = @V1;

-- ===========================================================================
-- 3. PRERREQUISITOS: Talleres
-- ===========================================================================
DECLARE @IdTaller1 INT, @IdTaller2 INT;

-- Usar talleres existentes o crear dos para la empresa Demo
SELECT TOP 1 @IdTaller1 = id_taller FROM dbo.talleres
WHERE id_empresa = @IdEmpresa AND eliminado = 0 ORDER BY id_taller;

IF @IdTaller1 IS NULL
BEGIN
    INSERT INTO dbo.talleres (id_empresa, codigo, nombre, rtn, direccion, telefono, email, contacto,
                              activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'TLR-01', 'Taller Central Demo', '05019999000000', 'Col. Kennedy, Tegucigalpa',
            '2225-0001', 'taller1@demo.hn', 'Juan Flores', 1, 0, 'seed', SYSUTCDATETIME());
    SET @IdTaller1 = SCOPE_IDENTITY();
    PRINT 'Taller Central Demo creado';
END

SELECT TOP 1 @IdTaller2 = id_taller FROM dbo.talleres
WHERE id_empresa = @IdEmpresa AND eliminado = 0 AND id_taller <> @IdTaller1 ORDER BY id_taller;

IF @IdTaller2 IS NULL
BEGIN
    INSERT INTO dbo.talleres (id_empresa, codigo, nombre, rtn, direccion, telefono, email, contacto,
                              activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'TLR-02', 'Servicio Rápido Demo', '05029999000000', 'Blvd. Morazán, Tegucigalpa',
            '2226-0002', 'taller2@demo.hn', 'Pedro Reyes', 1, 0, 'seed', SYSUTCDATETIME());
    SET @IdTaller2 = SCOPE_IDENTITY();
    PRINT 'Servicio Rápido Demo creado';
END

-- ===========================================================================
-- 4. PRERREQUISITOS: Categorías de Repuesto
-- ===========================================================================
-- Asegurar categorías base (sin es_llanta) y categoría de Llantas
DECLARE @CatMotor   INT, @CatFreno  INT, @CatFiltros INT,
        @CatAceites INT, @CatElec   INT, @CatLlanta  INT;

-- Categoría: Motor
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Motor' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Motor', 'Piezas y componentes del motor', 0, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatMotor = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Motor' AND eliminado = 0;

-- Categoría: Frenos
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Frenos' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Frenos', 'Pastillas, discos, tambores y cilindros de freno', 0, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatFreno = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Frenos' AND eliminado = 0;

-- Categoría: Filtros
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Filtros' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Filtros', 'Filtro de aire, aceite, combustible y habitáculo', 0, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatFiltros = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Filtros' AND eliminado = 0;

-- Categoría: Aceites y Fluidos
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Aceites y Fluidos' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Aceites y Fluidos', 'Aceite de motor, caja, líquido de frenos y dirección', 0, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatAceites = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Aceites y Fluidos' AND eliminado = 0;

-- Categoría: Sistema Eléctrico
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Sistema Eléctrico' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Sistema Eléctrico', 'Batería, alternador, arranque, fusibles y cableado', 0, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatElec = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Sistema Eléctrico' AND eliminado = 0;

-- Categoría: Neumáticos / Llantas (es_llanta = 1)
IF NOT EXISTS (SELECT 1 FROM dbo.categorias_repuesto WHERE id_empresa = @IdEmpresa AND nombre = 'Neumáticos' AND eliminado = 0)
    INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, es_llanta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'Neumáticos', 'Neumáticos, llantas, válvulas y accesorios de rueda', 1, 1, 0, 'seed', SYSUTCDATETIME());

SELECT @CatLlanta = id_categoria_repuesto FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND nombre = 'Neumáticos' AND eliminado = 0;

-- ===========================================================================
-- 5. GASTOS DE REPUESTOS
-- ===========================================================================
-- 15 registros distribuidos en los últimos 5 meses, distintas categorías y vehículos

DECLARE @GastosRepuesto TABLE (
    id_vehiculo        INT, id_categoria INT,
    fecha              DATE, no_factura NVARCHAR(50), proveedor NVARCHAR(150),
    descripcion        NVARCHAR(250), cantidad DECIMAL(18,2), precio_unitario DECIMAL(18,2),
    km_odometro        DECIMAL(18,2), observaciones NVARCHAR(500)
);

INSERT INTO @GastosRepuesto VALUES
-- Filtros
(@V1, @CatFiltros, DATEADD(MONTH,-5,@Hoy), 'F-2024-0010', 'AutoRepuestos El Central',   'Filtro de aceite Fleetguard LF9009',          2,  450.00,  88500, NULL),
(@V2, @CatFiltros, DATEADD(MONTH,-4,@Hoy), 'F-2024-0028', 'AutoRepuestos El Central',   'Filtro de aire primario AF25708',             1,  890.00,  65200, NULL),
(@V3, @CatFiltros, DATEADD(MONTH,-2,@Hoy), 'F-2025-0005', 'Repuestos El Rápido',        'Filtro de combustible FF5319',                2,  320.00, 122800, NULL),

-- Aceites y Fluidos
(@V1, @CatAceites, DATEADD(MONTH,-5,@Hoy), 'F-2024-0011', 'Lubricantes del Norte',      'Aceite Shell Rimula R6 M 10W-40 (20L)',       3, 1850.00,  88500, 'Cambio de aceite cada 15,000 km'),
(@V2, @CatAceites, DATEADD(MONTH,-3,@Hoy), 'F-2024-0045', 'Lubricantes del Norte',      'Aceite Mobil Delvac 1 ESP 5W-40 (20L)',       2, 1950.00,  67800, NULL),
(@V3, @CatAceites, DATEADD(MONTH,-1,@Hoy), 'F-2025-0018', 'Distribuidora La Palma',     'Líquido de frenos DOT4 (1L)',                 4,  185.00, 123500, NULL),

-- Frenos
(@V1, @CatFreno,  DATEADD(MONTH,-4,@Hoy), 'F-2024-0031', 'Frenos y Más Honduras',       'Pastillas de freno delantera Bendix DB1765',  2,  780.00,  90200, 'Desgaste prematuro lado conductor'),
(@V3, @CatFreno,  DATEADD(MONTH,-3,@Hoy), 'F-2024-0050', 'Frenos y Más Honduras',       'Disco de freno trasero Brembo 09.A157.11',    2, 1250.00, 121000, NULL),
(@V2, @CatFreno,  DATEADD(MONTH,-1,@Hoy), 'F-2025-0022', 'Repuestos El Rápido',         'Cilindro de rueda trasero 19,05mm',           1,  650.00,  70100, NULL),

-- Motor
(@V2, @CatMotor,  DATEADD(MONTH,-5,@Hoy), 'F-2024-0009', 'Motor y Partes S.A.',         'Junta de culata Volvo 20740593',              1, 3200.00,  63000, 'Revisión preventiva'),
(@V1, @CatMotor,  DATEADD(MONTH,-2,@Hoy), 'F-2024-0065', 'Motor y Partes S.A.',         'Correa de distribución Gates K025660XS',      1, 1450.00,  92000, NULL),
(@V3, @CatMotor,  DATEADD(MONTH,-1,@Hoy), 'F-2025-0015', 'Importadora Veloz',           'Bomba de agua Hino S1300-11080',              1, 2100.00, 123000, 'Reemplazo preventivo 120k km'),

-- Sistema Eléctrico
(@V1, @CatElec,   DATEADD(MONTH,-3,@Hoy), 'F-2024-0048', 'Electro Auto Honduras',       'Batería Optima 8014-045 12V 75Ah',            1, 2800.00,  91000, NULL),
(@V3, @CatElec,   DATEADD(MONTH,-2,@Hoy), 'F-2024-0060', 'Electro Auto Honduras',       'Alternador reconstruido 24V 90A',             1, 4500.00, 122000, 'Falla en carga a plena carga'),
(@V2, @CatElec,   DATEADD(MONTH,-1,@Hoy), 'F-2025-0030', 'Electro Auto Honduras',       'Sensor MAP Bosch 0 281 002 576',              1,  950.00,  70500, NULL);

-- Insertar únicamente registros que no existan ya (basado en no_factura + id_vehiculo)
INSERT INTO dbo.gastos_repuestos
    (id_empresa, id_vehiculo, id_categoria_repuesto, fecha, no_factura, proveedor,
     descripcion, cantidad, precio_unitario, moneda, km_odometro, observaciones,
     eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, g.id_vehiculo, g.id_categoria,
    g.fecha, g.no_factura, g.proveedor,
    g.descripcion, g.cantidad, g.precio_unitario, 'HNL',
    g.km_odometro, g.observaciones,
    0, 'seed', SYSUTCDATETIME()
FROM @GastosRepuesto g
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.gastos_repuestos gr
    WHERE gr.id_empresa  = @IdEmpresa
      AND gr.id_vehiculo = g.id_vehiculo
      AND gr.no_factura  = g.no_factura
      AND gr.eliminado   = 0
);

PRINT 'Gastos de repuestos insertados: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- 6. GASTOS DE LLANTAS (gastos_repuestos con categoría es_llanta = 1)
-- ===========================================================================
DECLARE @GastosLlanta TABLE (
    id_vehiculo        INT,
    fecha              DATE, no_factura NVARCHAR(50), proveedor NVARCHAR(150),
    descripcion        NVARCHAR(250), cantidad DECIMAL(18,2), precio_unitario DECIMAL(18,2),
    km_odometro        DECIMAL(18,2), observaciones NVARCHAR(500)
);

INSERT INTO @GastosLlanta VALUES
(@V1, DATEADD(MONTH,-5,@Hoy), 'L-2024-0003', 'Llantera Central SA',  'Llanta Bridgestone R22.5 M749 (directriz)',   2, 6800.00,  87000, 'Cambio preventivo por desgaste'),
(@V1, DATEADD(MONTH,-2,@Hoy), 'L-2024-0018', 'Llantera Central SA',  'Llanta Bridgestone R22.5 R150 (tracción)',    4, 7200.00,  91500, 'Eje trasero completo'),
(@V2, DATEADD(MONTH,-4,@Hoy), 'L-2024-0008', 'Distribuidora Michelin','Llanta Michelin X Multi Z 295/80R22.5',      2, 7500.00,  64000, NULL),
(@V2, DATEADD(MONTH,-1,@Hoy), 'L-2025-0002', 'Distribuidora Michelin','Llanta Michelin X Multi D 295/80R22.5',      4, 7800.00,  70200, 'Renovación eje trasero'),
(@V3, DATEADD(MONTH,-3,@Hoy), 'L-2024-0012', 'Goodyear Honduras',    'Llanta Goodyear KMAX D 315/70R22.5',          4, 6500.00, 120500, NULL),
(@V3, DATEADD(MONTH,-1,@Hoy), 'L-2025-0005', 'Goodyear Honduras',    'Llanta Goodyear KMAX S 315/70R22.5 (dir.)',   2, 6200.00, 123500, 'Directriz reemplazada por corte');

INSERT INTO dbo.gastos_repuestos
    (id_empresa, id_vehiculo, id_categoria_repuesto, fecha, no_factura, proveedor,
     descripcion, cantidad, precio_unitario, moneda, km_odometro, observaciones,
     eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, l.id_vehiculo, @CatLlanta,
    l.fecha, l.no_factura, l.proveedor,
    l.descripcion, l.cantidad, l.precio_unitario, 'HNL',
    l.km_odometro, l.observaciones,
    0, 'seed', SYSUTCDATETIME()
FROM @GastosLlanta l
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.gastos_repuestos gr
    WHERE gr.id_empresa  = @IdEmpresa
      AND gr.id_vehiculo = l.id_vehiculo
      AND gr.no_factura  = l.no_factura
      AND gr.eliminado   = 0
);

PRINT 'Gastos de llantas insertados: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- 7. ÓRDENES DE MANTENIMIENTO
-- ===========================================================================
DECLARE @OrdenesMant TABLE (
    id_vehiculo     INT, id_taller INT,
    fecha           DATE, no_factura NVARCHAR(50), tipo NVARCHAR(20),
    descripcion     NVARCHAR(500),
    mano_obra       DECIMAL(18,2), repuestos DECIMAL(18,2), otros DECIMAL(18,2),
    km_odometro     DECIMAL(18,2), observaciones NVARCHAR(500)
);

INSERT INTO @OrdenesMant VALUES
-- Preventivos (servicio regular)
(@V1, @IdTaller1, DATEADD(MONTH,-5,@Hoy), 'OM-2024-0001', 'PREVENTIVO',
 'Servicio de 90,000 km: cambio de aceite, filtros, revisión de frenos y suspensión',
  850.00,  2100.00,  0.00,  90000, NULL),

(@V2, @IdTaller1, DATEADD(MONTH,-4,@Hoy), 'OM-2024-0004', 'PREVENTIVO',
 'Mantenimiento preventivo 60,000 km: afinación, filtros aire y aceite, chequeo general',
  650.00,  1800.00,  0.00,  65000, 'Sin novedad en sistema de frenos'),

(@V3, @IdTaller2, DATEADD(MONTH,-3,@Hoy), 'OM-2024-0007', 'PREVENTIVO',
 'Servicio de 120,000 km: cambio aceite y filtros, ajuste de válvulas, revisión distribución',
 1200.00,  3500.00, 250.00, 120000, 'Se recomienda cambio de correa de distribución próximo servicio'),

(@V1, @IdTaller2, DATEADD(MONTH,-2,@Hoy), 'OM-2024-0010', 'PREVENTIVO',
 'Servicio de 90,000 km: lubricación general, frenos y revisión de ruedas',
  750.00,  1500.00,   0.00,  91500, NULL),

-- Correctivos (reparaciones)
(@V1, @IdTaller1, DATEADD(MONTH,-4,@Hoy), 'OM-2024-0003', 'CORRECTIVO',
 'Reparación de sistema de refrigeración: sustitución de termostato y mangueras',
  950.00,  1850.00, 150.00,  89200, 'Vehículo presentó sobrecalentamiento en ruta'),

(@V3, @IdTaller1, DATEADD(MONTH,-2,@Hoy), 'OM-2024-0009', 'CORRECTIVO',
 'Reparación de compresor de frenos de aire: sustitución de válvula de control',
 1400.00,  2200.00, 200.00, 121800, 'Falla detectada en revisión diaria del conductor'),

(@V2, @IdTaller2, DATEADD(MONTH,-1,@Hoy), 'OM-2025-0001', 'CORRECTIVO',
 'Diagnóstico y reparación falla ECU: limpieza de inyectores y actualización de firmware',
 1800.00,   950.00, 300.00,  70500, 'Falla de arranque en frío'),

-- Predictivo
(@V2, @IdTaller1, DATEADD(MONTH,-5,@Hoy), 'OM-2024-0002', 'REVISION',
 'Análisis de aceite y vibración: medición de desgaste en cojinetes de motor',
  600.00,     0.00, 450.00,  63500, 'Resultados normales, próxima revisión en 30,000 km'),

(@V3, @IdTaller2, DATEADD(MONTH,-1,@Hoy), 'OM-2025-0002', 'REVISION',
 'Inspección de motor y transmisión por alta kilometraje',
  700.00,     0.00, 350.00, 123500, 'Se recomienda revisión de clutch en próximo servicio');

INSERT INTO dbo.ordenes_mantenimiento
    (id_empresa, id_vehiculo, id_taller, fecha, no_factura, tipo_mantenimiento,
     descripcion, monto_mano_obra, monto_repuestos, monto_otros, moneda,
     km_odometro, observaciones, eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, o.id_vehiculo, o.id_taller,
    o.fecha, o.no_factura, o.tipo,
    o.descripcion, o.mano_obra, o.repuestos, o.otros, 'HNL',
    o.km_odometro, o.observaciones,
    0, 'seed', SYSUTCDATETIME()
FROM @OrdenesMant o
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ordenes_mantenimiento om
    WHERE om.id_empresa  = @IdEmpresa
      AND om.id_vehiculo = o.id_vehiculo
      AND om.no_factura  = o.no_factura
      AND om.eliminado   = 0
);

PRINT 'Órdenes de mantenimiento insertadas: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- 8. PÓLIZAS DE SEGUROS
-- ===========================================================================
DECLARE @PolizasSeguros TABLE (
    id_vehiculo    INT,
    no_poliza      NVARCHAR(50), aseguradora NVARCHAR(150), tipo_cobertura NVARCHAR(30),
    fecha_inicio   DATE, fecha_fin DATE, prima_total DECIMAL(18,2),
    observaciones  NVARCHAR(500)
);

INSERT INTO @PolizasSeguros VALUES
-- Vehículo 1: cobertura amplia vigente (año completo)
(@V1, 'SEGUHN-2025-00145', 'Seguros Atlántida S.A.',         'AMPLIA',
 DATEADD(MONTH,-2,@Hoy), DATEADD(MONTH,10,@Hoy),  48500.00,
 'Cubre daños propios, terceros, robo total y parcial. Deducible L 5,000.'),

-- Vehículo 1: póliza anterior (ya vencida, histórico)
(@V1, 'SEGUHN-2024-00098', 'Seguros Atlántida S.A.',         'AMPLIA',
 DATEADD(MONTH,-14,@Hoy), DATEADD(MONTH,-2,@Hoy), 45000.00,
 'Póliza anterior renovada sin siniestros'),

-- Vehículo 2: cobertura amplia vigente
(@V2, 'HSHN-2025-00892',   'HSBC Seguros Honduras',         'AMPLIA',
 DATEADD(MONTH,-1,@Hoy),  DATEADD(MONTH,11,@Hoy),  52000.00,
 'Primera póliza nueva unidad. Incluye asistencia en carretera 24h.'),

-- Vehículo 3: cobertura RC (responsabilidad civil)
(@V3, 'AIG-HN-2025-0341',  'AIG Seguros Honduras',          'RC',
 DATEADD(MONTH,-3,@Hoy), DATEADD(MONTH,9,@Hoy),   18000.00,
 'Seguro de responsabilidad civil obligatoria (SOAT equivalente).'),

-- Vehículo 3: póliza amplia vigente (adicional)
(@V3, 'AIG-HN-2025-0342',  'AIG Seguros Honduras',          'AMPLIA',
 DATEADD(MONTH,-3,@Hoy), DATEADD(MONTH,9,@Hoy),   38500.00,
 'Cobertura amplia adicional al SOAT. Deducible L 7,500.'),

-- Vehículo 2: póliza de responsabilidad civil
(@V2, 'AXAHN-2025-00210',  'AXA Seguros Honduras',          'RC',
 DATEADD(MONTH,-1,@Hoy),  DATEADD(MONTH,11,@Hoy),  15000.00,
 'Póliza de responsabilidad civil para pasajeros. Cobertura L 500,000 por evento.');

INSERT INTO dbo.polizas_seguros
    (id_empresa, id_vehiculo, no_poliza, aseguradora, tipo_cobertura,
     fecha_inicio, fecha_fin, prima_total, moneda, observaciones,
     eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, p.id_vehiculo, p.no_poliza, p.aseguradora, p.tipo_cobertura,
    p.fecha_inicio, p.fecha_fin, p.prima_total, 'HNL', p.observaciones,
    0, 'seed', SYSUTCDATETIME()
FROM @PolizasSeguros p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.polizas_seguros ps
    WHERE ps.id_empresa  = @IdEmpresa
      AND ps.id_vehiculo = p.id_vehiculo
      AND ps.no_poliza   = p.no_poliza
      AND ps.eliminado   = 0
);

PRINT 'Pólizas de seguros insertadas: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- RESUMEN FINAL
-- ===========================================================================
PRINT '';
PRINT '=== RESUMEN - Empresa Demo (id=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';

SELECT 'Vehículos'            AS Tabla, COUNT(*) AS Registros FROM dbo.vehiculos           WHERE id_empresa = @IdEmpresa AND eliminado = 0
UNION ALL
SELECT 'Talleres',                       COUNT(*)             FROM dbo.talleres             WHERE id_empresa = @IdEmpresa AND eliminado = 0
UNION ALL
SELECT 'Categorías Repuesto',            COUNT(*)             FROM dbo.categorias_repuesto  WHERE id_empresa = @IdEmpresa AND eliminado = 0
UNION ALL
SELECT 'Gastos Repuestos (sin llantas)', COUNT(*)             FROM dbo.gastos_repuestos gr
    JOIN dbo.categorias_repuesto cr ON cr.id_categoria_repuesto = gr.id_categoria_repuesto
    WHERE gr.id_empresa = @IdEmpresa AND gr.eliminado = 0 AND cr.es_llanta = 0
UNION ALL
SELECT 'Gastos Llantas',                 COUNT(*)             FROM dbo.gastos_repuestos gr
    JOIN dbo.categorias_repuesto cr ON cr.id_categoria_repuesto = gr.id_categoria_repuesto
    WHERE gr.id_empresa = @IdEmpresa AND gr.eliminado = 0 AND cr.es_llanta = 1
UNION ALL
SELECT 'Órdenes Mantenimiento',          COUNT(*)             FROM dbo.ordenes_mantenimiento WHERE id_empresa = @IdEmpresa AND eliminado = 0
UNION ALL
SELECT 'Pólizas Seguros',                COUNT(*)             FROM dbo.polizas_seguros       WHERE id_empresa = @IdEmpresa AND eliminado = 0
ORDER BY Tabla;
