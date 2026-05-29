-- =============================================================================
-- Seed EXTRA: Datos Dummy adicionales - Empresa Demo (id_empresa = 2)
-- Complementa seed_demo_flota.sql con:
--   - Más vehículos (V4..V8)
--   - Personas (conductores y auxiliares)
--   - Rutas
--   - Odómetro diario (varios meses)
--   - Cargas de combustible
--   - Salarios diarios
--   - Gastos de repuestos adicionales
--   - Órdenes de mantenimiento adicionales
--   - Pólizas de seguros adicionales
-- Idempotente: usa NOT EXISTS para no duplicar.
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @IdEmpresa INT = 2;
DECLARE @Hoy DATE = CAST(GETDATE() AS DATE);

-- Validar empresa
IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE id_empresa = @IdEmpresa)
BEGIN
    RAISERROR('No existe la empresa Demo (id_empresa = 2). Cree la empresa primero.', 16, 1);
    RETURN;
END

-- ===========================================================================
-- 1. TIPO DE VEHÍCULO (reutilizar / crear adicional)
-- ===========================================================================
DECLARE @IdTipoBus INT, @IdTipoMicro INT, @IdTipoCamion INT;

SELECT @IdTipoBus = id_tipo_vehiculo FROM dbo.tipos_vehiculo
WHERE id_empresa = @IdEmpresa AND codigo = 'BUS' AND eliminado = 0;

IF @IdTipoBus IS NULL
BEGIN
    INSERT INTO dbo.tipos_vehiculo (id_empresa, codigo, nombre, descripcion, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'BUS', 'Bus', 'Bus de transporte público', 1, 0, 'seed', SYSUTCDATETIME());
    SET @IdTipoBus = SCOPE_IDENTITY();
END

IF NOT EXISTS (SELECT 1 FROM dbo.tipos_vehiculo WHERE id_empresa = @IdEmpresa AND codigo = 'MICRO' AND eliminado = 0)
    INSERT INTO dbo.tipos_vehiculo (id_empresa, codigo, nombre, descripcion, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'MICRO', 'Microbús', 'Microbús urbano de 20-30 asientos', 1, 0, 'seed', SYSUTCDATETIME());

SELECT @IdTipoMicro = id_tipo_vehiculo FROM dbo.tipos_vehiculo
WHERE id_empresa = @IdEmpresa AND codigo = 'MICRO' AND eliminado = 0;

IF NOT EXISTS (SELECT 1 FROM dbo.tipos_vehiculo WHERE id_empresa = @IdEmpresa AND codigo = 'CAM' AND eliminado = 0)
    INSERT INTO dbo.tipos_vehiculo (id_empresa, codigo, nombre, descripcion, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, 'CAM', 'Camión', 'Camión de carga liviana / mediana', 1, 0, 'seed', SYSUTCDATETIME());

SELECT @IdTipoCamion = id_tipo_vehiculo FROM dbo.tipos_vehiculo
WHERE id_empresa = @IdEmpresa AND codigo = 'CAM' AND eliminado = 0;

-- ===========================================================================
-- 2. VEHÍCULOS ADICIONALES (HND-004 .. HND-008)
-- ===========================================================================
IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-004' AND eliminado = 0)
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoBus,    'HND-004', '004', 'Scania',        'K310UB',    2021, 'Verde',    48, 'DIESEL',  42000.00, '2021-05-12', 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-005' AND eliminado = 0)
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoMicro,  'HND-005', '005', 'Toyota',        'Coaster',   2022, 'Blanco',   26, 'DIESEL',  18000.00, '2022-02-08', 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-006' AND eliminado = 0)
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoMicro,  'HND-006', '006', 'Hyundai',       'County',    2020, 'Azul',     28, 'DIESEL',  55000.00, '2020-09-22', 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-007' AND eliminado = 0)
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoCamion, 'HND-007', '007', 'Isuzu',         'NPR-75',    2019, 'Rojo',      3, 'DIESEL',  98000.00, '2019-11-05', 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND placa = 'HND-008' AND eliminado = 0)
    INSERT INTO dbo.vehiculos (id_empresa, id_tipo_vehiculo, placa, numero_interno, marca, modelo, anio, color,
                               capacidad, tipo_combustible, km_inicial, fecha_alta, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa, @IdTipoBus,    'HND-008', '008', 'Mercedes-Benz', 'OF-1721',   2017, 'Gris',     45, 'DIESEL', 145000.00, '2017-06-15', 1, 0, 'seed', SYSUTCDATETIME());

-- Capturar IDs (ordenados por placa para estabilidad)
DECLARE @V1 INT,@V2 INT,@V3 INT,@V4 INT,@V5 INT,@V6 INT,@V7 INT,@V8 INT;
SELECT @V1 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-001' AND eliminado=0;
SELECT @V2 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-002' AND eliminado=0;
SELECT @V3 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-003' AND eliminado=0;
SELECT @V4 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-004' AND eliminado=0;
SELECT @V5 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-005' AND eliminado=0;
SELECT @V6 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-006' AND eliminado=0;
SELECT @V7 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-007' AND eliminado=0;
SELECT @V8 = id_vehiculo FROM dbo.vehiculos WHERE id_empresa=@IdEmpresa AND placa='HND-008' AND eliminado=0;

PRINT 'Vehículos disponibles: V1..V8 cargados';

-- ===========================================================================
-- 3. RUTAS
-- ===========================================================================
IF NOT EXISTS (SELECT 1 FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-01' AND eliminado=0)
    INSERT INTO dbo.rutas (id_empresa, codigo, nombre, descripcion, distancia_km, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa,'R-01','Tegucigalpa - Comayagua','Ruta interurbana TGU-CMY vía CA-5', 85.50, 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-02' AND eliminado=0)
    INSERT INTO dbo.rutas (id_empresa, codigo, nombre, descripcion, distancia_km, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa,'R-02','Tegucigalpa - San Pedro Sula','Ruta troncal TGU-SPS', 240.00, 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-03' AND eliminado=0)
    INSERT INTO dbo.rutas (id_empresa, codigo, nombre, descripcion, distancia_km, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa,'R-03','Centro - Kennedy','Ruta urbana Tegucigalpa (Centro - Col. Kennedy)', 18.20, 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-04' AND eliminado=0)
    INSERT INTO dbo.rutas (id_empresa, codigo, nombre, descripcion, distancia_km, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa,'R-04','Tegucigalpa - Choluteca','Ruta sur vía CA-1', 145.00, 1, 0, 'seed', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-05' AND eliminado=0)
    INSERT INTO dbo.rutas (id_empresa, codigo, nombre, descripcion, distancia_km, activo, eliminado, creado_por, fecha_creacion)
    VALUES (@IdEmpresa,'R-05','UNAH - Toncontín','Ruta urbana universitaria', 22.40, 1, 0, 'seed', SYSUTCDATETIME());

DECLARE @R1 INT,@R2 INT,@R3 INT,@R4 INT,@R5 INT;
SELECT @R1 = id_ruta FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-01' AND eliminado=0;
SELECT @R2 = id_ruta FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-02' AND eliminado=0;
SELECT @R3 = id_ruta FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-03' AND eliminado=0;
SELECT @R4 = id_ruta FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-04' AND eliminado=0;
SELECT @R5 = id_ruta FROM dbo.rutas WHERE id_empresa=@IdEmpresa AND codigo='R-05' AND eliminado=0;

-- ===========================================================================
-- 4. PERSONAS (Conductores y Auxiliares)
-- ===========================================================================
DECLARE @Personas TABLE (
    documento NVARCHAR(30), nombres NVARCHAR(100), apellidos NVARCHAR(100),
    cargo NVARCHAR(30), tarifa DECIMAL(18,2), telefono NVARCHAR(30), email NVARCHAR(150),
    fecha_ingreso DATE
);

INSERT INTO @Personas VALUES
('0801-1985-12345','Carlos Alberto','Mejía López',     'CONDUCTOR', 650.00, '9991-0001', 'cmejia@demo.hn',     '2022-01-10'),
('0801-1990-22310','Luis Fernando', 'Reyes Castillo',  'CONDUCTOR', 650.00, '9991-0002', 'lreyes@demo.hn',     '2021-06-15'),
('0801-1988-30021','José Manuel',   'Hernández Pineda','CONDUCTOR', 700.00, '9991-0003', 'jhernandez@demo.hn', '2020-03-22'),
('0801-1992-45567','Marvin Adolfo', 'Sánchez Cruz',    'CONDUCTOR', 650.00, '9991-0004', 'msanchez@demo.hn',   '2023-02-01'),
('0801-1995-50098','Edgar Antonio', 'Funes Romero',    'CONDUCTOR', 600.00, '9991-0005', 'efunes@demo.hn',     '2023-08-12'),
('0801-1991-61122','Roberto',       'Banegas Murillo', 'CONDUCTOR', 700.00, '9991-0006', 'rbanegas@demo.hn',   '2019-11-04'),
('0801-1998-71234','Diego Eduardo', 'Maradiaga Paz',   'AUXILIAR',  400.00, '9991-0007', 'dmaradiaga@demo.hn', '2024-01-15'),
('0801-1996-80045','Henry Daniel',  'Pavón Aguilar',   'AUXILIAR',  400.00, '9991-0008', 'hpavon@demo.hn',     '2023-05-20'),
('0801-1993-90112','Marvin Steven', 'Andino Rivas',    'AUXILIAR',  400.00, '9991-0009', 'mandino@demo.hn',    '2024-03-01'),
('0801-1987-10078','Edwin Josué',   'Lara Bonilla',    'MECANICO', 1100.00, '9991-0010', 'elara@demo.hn',      '2018-04-18');

INSERT INTO dbo.personas
    (id_empresa, documento, tipo_documento, nombres, apellidos, cargo, tarifa_diaria, moneda_tarifa,
     telefono, email, fecha_ingreso, activo, eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, p.documento, 'DNI', p.nombres, p.apellidos, p.cargo, p.tarifa, 'HNL',
    p.telefono, p.email, p.fecha_ingreso, 1, 0, 'seed', SYSUTCDATETIME()
FROM @Personas p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.personas pe
    WHERE pe.id_empresa = @IdEmpresa AND pe.documento = p.documento AND pe.eliminado = 0
);

PRINT 'Personas insertadas: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- Capturar IDs de personas (conductores y auxiliares)
DECLARE @P1 INT,@P2 INT,@P3 INT,@P4 INT,@P5 INT,@P6 INT,@P7 INT,@P8 INT,@P9 INT,@P10 INT;
SELECT @P1  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1985-12345' AND eliminado=0;
SELECT @P2  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1990-22310' AND eliminado=0;
SELECT @P3  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1988-30021' AND eliminado=0;
SELECT @P4  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1992-45567' AND eliminado=0;
SELECT @P5  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1995-50098' AND eliminado=0;
SELECT @P6  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1991-61122' AND eliminado=0;
SELECT @P7  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1998-71234' AND eliminado=0;
SELECT @P8  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1996-80045' AND eliminado=0;
SELECT @P9  = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1993-90112' AND eliminado=0;
SELECT @P10 = id_persona FROM dbo.personas WHERE id_empresa=@IdEmpresa AND documento='0801-1987-10078' AND eliminado=0;

-- ===========================================================================
-- 5. ODÓMETRO DIARIO (60 días recientes, 4 vehículos rotando)
-- ===========================================================================
-- Generamos registros diarios para V1, V2, V3, V4 con KMs crecientes.
DECLARE @i INT = 0;
DECLARE @maxDias INT = 60;
DECLARE @baseKmV1 DECIMAL(18,2) = 92000;
DECLARE @baseKmV2 DECIMAL(18,2) = 70500;
DECLARE @baseKmV3 DECIMAL(18,2) = 124000;
DECLARE @baseKmV4 DECIMAL(18,2) = 43000;

WHILE @i < @maxDias
BEGIN
    DECLARE @Fecha DATE = DATEADD(DAY, -@i, @Hoy);

    -- V1 - Ruta R3 urbana, ~180km/día, conductor P1
    DECLARE @kmIniV1 DECIMAL(18,2) = @baseKmV1 + (@maxDias - @i - 1) * 180;
    DECLARE @kmFinV1 DECIMAL(18,2) = @kmIniV1 + 180 + (@i % 5) * 4;
    IF NOT EXISTS (SELECT 1 FROM dbo.odometro_diario WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V1 AND fecha=@Fecha AND eliminado=0)
        INSERT INTO dbo.odometro_diario (id_empresa, id_vehiculo, fecha, km_inicial, km_final, id_ruta, id_conductor, observaciones, eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V1, @Fecha, @kmIniV1, @kmFinV1, @R3, @P1, NULL, 0, 'seed', SYSUTCDATETIME());

    -- V2 - Ruta R1 interurbana, ~340km/día (4 viajes), conductor P2
    DECLARE @kmIniV2 DECIMAL(18,2) = @baseKmV2 + (@maxDias - @i - 1) * 342;
    DECLARE @kmFinV2 DECIMAL(18,2) = @kmIniV2 + 342 + (@i % 3) * 5;
    IF NOT EXISTS (SELECT 1 FROM dbo.odometro_diario WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V2 AND fecha=@Fecha AND eliminado=0)
        INSERT INTO dbo.odometro_diario (id_empresa, id_vehiculo, fecha, km_inicial, km_final, id_ruta, id_conductor, observaciones, eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V2, @Fecha, @kmIniV2, @kmFinV2, @R1, @P2, NULL, 0, 'seed', SYSUTCDATETIME());

    -- V3 - Ruta R2 troncal, ~480km/día (un solo viaje TGU-SPS), conductor P3
    -- Solo días alternos para simular tour de 2 días
    IF (@i % 2) = 0
    BEGIN
        DECLARE @kmIniV3 DECIMAL(18,2) = @baseKmV3 + ((@maxDias - @i - 1) / 2) * 480;
        DECLARE @kmFinV3 DECIMAL(18,2) = @kmIniV3 + 480 + (@i % 7) * 3;
        IF NOT EXISTS (SELECT 1 FROM dbo.odometro_diario WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V3 AND fecha=@Fecha AND eliminado=0)
            INSERT INTO dbo.odometro_diario (id_empresa, id_vehiculo, fecha, km_inicial, km_final, id_ruta, id_conductor, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V3, @Fecha, @kmIniV3, @kmFinV3, @R2, @P3, 'Viaje completo TGU-SPS', 0, 'seed', SYSUTCDATETIME());
    END

    -- V4 - Ruta R4 sur, ~290km/día, conductor P4
    DECLARE @kmIniV4 DECIMAL(18,2) = @baseKmV4 + (@maxDias - @i - 1) * 290;
    DECLARE @kmFinV4 DECIMAL(18,2) = @kmIniV4 + 290 + (@i % 4) * 6;
    IF NOT EXISTS (SELECT 1 FROM dbo.odometro_diario WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V4 AND fecha=@Fecha AND eliminado=0)
        INSERT INTO dbo.odometro_diario (id_empresa, id_vehiculo, fecha, km_inicial, km_final, id_ruta, id_conductor, observaciones, eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V4, @Fecha, @kmIniV4, @kmFinV4, @R4, @P4, NULL, 0, 'seed', SYSUTCDATETIME());

    SET @i = @i + 1;
END

PRINT 'Odómetro diario: ' + CAST((SELECT COUNT(*) FROM dbo.odometro_diario WHERE id_empresa=@IdEmpresa AND eliminado=0) AS NVARCHAR) + ' registros totales';

-- ===========================================================================
-- 6. CARGAS DE COMBUSTIBLE (cada 3-4 días por vehículo, últimos 5 meses)
-- ===========================================================================
DECLARE @j INT = 0;
DECLARE @maxCargas INT = 30;  -- ~30 cargas por vehículo = cada 5 días
DECLARE @PrecioGal DECIMAL(18,2) = 105.50;

WHILE @j < @maxCargas
BEGIN
    DECLARE @FechaC DATE = DATEADD(DAY, -@j * 5, @Hoy);
    DECLARE @PrecioVar DECIMAL(18,2) = @PrecioGal + (((@j % 7) - 3) * 1.25);
    DECLARE @NoFactBase NVARCHAR(20) = 'CC-' + CONVERT(NVARCHAR(8), @FechaC, 112);

    -- V1: ~30 galones, conductor P1
    IF NOT EXISTS (SELECT 1 FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V1 AND fecha=@FechaC AND eliminado=0)
        INSERT INTO dbo.cargas_combustible
            (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
             cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
             eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V1, @FechaC, '06:30', @NoFactBase + '-V1', 'Uno Petrol Centro', 'DIESEL', 'GAL',
                30.00 + (@j % 5), @PrecioVar, 'HNL', @baseKmV1 - (@j * 5 * 180), @P1, NULL,
                0, 'seed', SYSUTCDATETIME());

    -- V2: ~45 galones (interurbano consume más)
    IF NOT EXISTS (SELECT 1 FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V2 AND fecha=@FechaC AND eliminado=0)
        INSERT INTO dbo.cargas_combustible
            (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
             cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
             eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V2, @FechaC, '07:15', @NoFactBase + '-V2', 'Texaco Mayaland', 'DIESEL', 'GAL',
                45.00 + (@j % 6), @PrecioVar + 0.50, 'HNL', @baseKmV2 - (@j * 5 * 342), @P2, NULL,
                0, 'seed', SYSUTCDATETIME());

    -- V3: ~60 galones cada 10 días (troncal larga, tanque grande)
    IF (@j % 2) = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V3 AND fecha=@FechaC AND eliminado=0)
            INSERT INTO dbo.cargas_combustible
                (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
                 cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
                 eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V3, @FechaC, '05:45', @NoFactBase + '-V3', 'Puma Energy SPS', 'DIESEL', 'GAL',
                    60.00 + (@j % 4), @PrecioVar - 0.25, 'HNL', @baseKmV3 - (@j * 5 * 240), @P3, 'Carga en SPS',
                    0, 'seed', SYSUTCDATETIME());
    END

    -- V4: ~38 galones
    IF NOT EXISTS (SELECT 1 FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V4 AND fecha=@FechaC AND eliminado=0)
        INSERT INTO dbo.cargas_combustible
            (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
             cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
             eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V4, @FechaC, '06:00', @NoFactBase + '-V4', 'Uno Petrol Choluteca', 'DIESEL', 'GAL',
                38.00 + (@j % 4), @PrecioVar, 'HNL', @baseKmV4 - (@j * 5 * 290), @P4, NULL,
                0, 'seed', SYSUTCDATETIME());

    -- V5: microbús pequeño ~15 galones
    IF NOT EXISTS (SELECT 1 FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V5 AND fecha=@FechaC AND eliminado=0)
        INSERT INTO dbo.cargas_combustible
            (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
             cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
             eliminado, creado_por, fecha_creacion)
        VALUES (@IdEmpresa, @V5, @FechaC, '07:00', @NoFactBase + '-V5', 'Dippsa Boulevard', 'DIESEL', 'GAL',
                15.00 + (@j % 3), @PrecioVar, 'HNL', 18500 + (@maxCargas - @j) * 50, @P5, NULL,
                0, 'seed', SYSUTCDATETIME());

    SET @j = @j + 1;
END

PRINT 'Cargas de combustible: ' + CAST((SELECT COUNT(*) FROM dbo.cargas_combustible WHERE id_empresa=@IdEmpresa AND eliminado=0) AS NVARCHAR) + ' registros totales';

-- ===========================================================================
-- 7. SALARIOS DIARIOS (últimos 30 días para conductores asignados)
-- ===========================================================================
DECLARE @k INT = 0;
WHILE @k < 30
BEGIN
    DECLARE @FechaS DATE = DATEADD(DAY, -@k, @Hoy);

    -- Domingo libre (los conductores no trabajan los domingos)
    IF DATEPART(WEEKDAY, @FechaS) <> 1
    BEGIN
        -- P1 conductor V1
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V1 AND id_persona=@P1 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V1, @P1, @FechaS, 'CONDUCTOR', 650.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());

        -- P2 conductor V2
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V2 AND id_persona=@P2 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V2, @P2, @FechaS, 'CONDUCTOR', 650.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());

        -- P3 conductor V3
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V3 AND id_persona=@P3 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V3, @P3, @FechaS, 'CONDUCTOR', 700.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());

        -- P4 conductor V4
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V4 AND id_persona=@P4 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V4, @P4, @FechaS, 'CONDUCTOR', 650.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());

        -- P7 auxiliar V1
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V1 AND id_persona=@P7 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V1, @P7, @FechaS, 'AUXILIAR', 400.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());

        -- P8 auxiliar V2
        IF NOT EXISTS (SELECT 1 FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND id_vehiculo=@V2 AND id_persona=@P8 AND fecha=@FechaS AND eliminado=0)
            INSERT INTO dbo.salarios_diarios (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones, eliminado, creado_por, fecha_creacion)
            VALUES (@IdEmpresa, @V2, @P8, @FechaS, 'AUXILIAR', 400.00, 'HNL', NULL, 0, 'seed', SYSUTCDATETIME());
    END

    SET @k = @k + 1;
END

PRINT 'Salarios diarios: ' + CAST((SELECT COUNT(*) FROM dbo.salarios_diarios WHERE id_empresa=@IdEmpresa AND eliminado=0) AS NVARCHAR) + ' registros totales';

-- ===========================================================================
-- 8. GASTOS DE REPUESTOS ADICIONALES (vehículos V4..V8)
-- ===========================================================================
DECLARE @CatMotor   INT, @CatFreno  INT, @CatFiltros INT,
        @CatAceites INT, @CatElec   INT, @CatLlanta  INT;

SELECT @CatMotor   = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Motor'             AND eliminado=0;
SELECT @CatFreno   = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Frenos'            AND eliminado=0;
SELECT @CatFiltros = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Filtros'           AND eliminado=0;
SELECT @CatAceites = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Aceites y Fluidos' AND eliminado=0;
SELECT @CatElec    = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Sistema Eléctrico' AND eliminado=0;
SELECT @CatLlanta  = id_categoria_repuesto FROM dbo.categorias_repuesto WHERE id_empresa=@IdEmpresa AND nombre='Neumáticos'        AND eliminado=0;

DECLARE @GastosExtra TABLE (
    id_vehiculo INT, id_categoria INT,
    fecha DATE, no_factura NVARCHAR(50), proveedor NVARCHAR(150),
    descripcion NVARCHAR(250), cantidad DECIMAL(18,2), precio_unitario DECIMAL(18,2),
    km_odometro DECIMAL(18,2), observaciones NVARCHAR(500)
);

INSERT INTO @GastosExtra VALUES
-- V4 (Scania)
(@V4, @CatFiltros, DATEADD(MONTH,-4,@Hoy), 'F-2024-0080', 'Scania Honduras',         'Filtro de aceite Scania 1873016',          2, 980.00,  44500, NULL),
(@V4, @CatAceites, DATEADD(MONTH,-3,@Hoy), 'F-2024-0095', 'Lubricantes del Norte',   'Aceite Scania LDF-4 10W-40 (20L)',         3, 2100.00, 46800, NULL),
(@V4, @CatFreno,   DATEADD(MONTH,-2,@Hoy), 'F-2024-0110', 'Frenos y Más Honduras',   'Pastillas freno traseras Bendix 7234',     2, 850.00,  48200, NULL),
(@V4, @CatLlanta,  DATEADD(MONTH,-1,@Hoy), 'L-2025-0010','Llantera Central SA',     'Llanta Bridgestone R22.5 R150 (tracción)', 2, 7400.00, 50000, 'Cambio eje delantero'),

-- V5 (Coaster - microbús)
(@V5, @CatFiltros, DATEADD(MONTH,-3,@Hoy), 'F-2024-0102', 'Toyota Comercial HN',     'Filtro de aire Toyota 17801-78010',        1, 320.00,  19800, NULL),
(@V5, @CatAceites, DATEADD(MONTH,-2,@Hoy), 'F-2024-0118', 'Distribuidora La Palma',  'Aceite Toyota 5W-30 sintético (5L)',       2, 650.00,  20500, NULL),
(@V5, @CatLlanta,  DATEADD(MONTH,-1,@Hoy), 'L-2025-0012','Distribuidora Michelin',  'Llanta Michelin Agilis 195R15',           4, 2900.00, 21000, 'Cambio juego completo'),

-- V6 (Hyundai County)
(@V6, @CatMotor,   DATEADD(MONTH,-4,@Hoy), 'F-2024-0078', 'Importadora Veloz',       'Inyector Hyundai 33800-45700',             2, 3800.00, 58000, 'Falla por agua en combustible'),
(@V6, @CatFiltros, DATEADD(MONTH,-2,@Hoy), 'F-2024-0125', 'AutoRepuestos El Central','Filtro combustible Hyundai 31922-4F900',   1, 480.00,  60200, NULL),

-- V7 (Isuzu camión)
(@V7, @CatAceites, DATEADD(MONTH,-3,@Hoy), 'F-2024-0099', 'Lubricantes del Norte',   'Aceite Isuzu DEO 15W-40 (20L)',            2, 1750.00, 100500, NULL),
(@V7, @CatFreno,   DATEADD(MONTH,-2,@Hoy), 'F-2024-0115', 'Frenos y Más Honduras',   'Banda de freno trasero Isuzu',             1, 1850.00, 101800, NULL),
(@V7, @CatElec,    DATEADD(MONTH,-1,@Hoy), 'F-2025-0040', 'Electro Auto Honduras',   'Batería 12V 100Ah ETNA H8',                1, 3500.00, 103200, 'Reemplazo por baja carga'),

-- V8 (Mercedes-Benz viejo, mantenimiento más frecuente)
(@V8, @CatMotor,   DATEADD(MONTH,-5,@Hoy), 'F-2024-0070', 'Motor y Partes S.A.',     'Kit reparación bomba inyectora',           1, 8500.00, 146000, 'Mantenimiento mayor por kilometraje'),
(@V8, @CatFreno,   DATEADD(MONTH,-3,@Hoy), 'F-2024-0095', 'Frenos y Más Honduras',   'Disco de freno delantero Mercedes',        2, 1400.00, 147500, NULL),
(@V8, @CatLlanta,  DATEADD(MONTH,-2,@Hoy), 'L-2024-0025','Goodyear Honduras',       'Llanta Goodyear KMAX D 295/80R22.5',       4, 6700.00, 148800, 'Eje trasero completo'),
(@V8, @CatElec,    DATEADD(MONTH,-1,@Hoy), 'F-2025-0035', 'Electro Auto Honduras',   'Arranque reconstruido Mercedes 24V',       1, 5200.00, 150100, 'Falla en arranque en frío');

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
FROM @GastosExtra g
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.gastos_repuestos gr
    WHERE gr.id_empresa=@IdEmpresa AND gr.id_vehiculo=g.id_vehiculo
      AND gr.no_factura=g.no_factura AND gr.eliminado=0
);

PRINT 'Gastos de repuestos extra insertados: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- 9. ÓRDENES DE MANTENIMIENTO ADICIONALES
-- ===========================================================================
DECLARE @IdTaller1 INT, @IdTaller2 INT;
SELECT TOP 1 @IdTaller1 = id_taller FROM dbo.talleres WHERE id_empresa=@IdEmpresa AND eliminado=0 ORDER BY id_taller;
SELECT TOP 1 @IdTaller2 = id_taller FROM dbo.talleres WHERE id_empresa=@IdEmpresa AND eliminado=0 AND id_taller <> @IdTaller1 ORDER BY id_taller;
IF @IdTaller2 IS NULL SET @IdTaller2 = @IdTaller1;

DECLARE @OrdenesExtra TABLE (
    id_vehiculo INT, id_taller INT,
    fecha DATE, no_factura NVARCHAR(50), tipo NVARCHAR(20),
    descripcion NVARCHAR(500),
    mano_obra DECIMAL(18,2), repuestos DECIMAL(18,2), otros DECIMAL(18,2),
    km_odometro DECIMAL(18,2), observaciones NVARCHAR(500)
);

INSERT INTO @OrdenesExtra VALUES
(@V4, @IdTaller1, DATEADD(MONTH,-4,@Hoy), 'OM-2024-0020', 'PREVENTIVO',
 'Servicio 45,000 km Scania: cambio aceite, filtros, revisión sistema neumático',
  950.00, 2400.00, 100.00, 45000, NULL),
(@V4, @IdTaller2, DATEADD(MONTH,-1,@Hoy), 'OM-2025-0010', 'CORRECTIVO',
 'Reparación de embrague: sustitución de plato y disco',
 2200.00, 4500.00, 350.00, 49500, 'Patinaje detectado en cuestas'),

(@V5, @IdTaller1, DATEADD(MONTH,-3,@Hoy), 'OM-2024-0022', 'PREVENTIVO',
 'Servicio 20,000 km Coaster: aceite, filtros, alineación',
  450.00, 1100.00,  80.00, 20000, NULL),

(@V6, @IdTaller2, DATEADD(MONTH,-4,@Hoy), 'OM-2024-0024', 'CORRECTIVO',
 'Diagnóstico ECM y reemplazo de bomba de combustible',
 1700.00, 3900.00, 200.00, 57500, 'Pérdida de potencia en arranque'),
(@V6, @IdTaller1, DATEADD(MONTH,-2,@Hoy), 'OM-2024-0026', 'PREVENTIVO',
 'Mantenimiento 60,000 km: ABCD completo',
  700.00, 1850.00, 0.00,    60500, NULL),

(@V7, @IdTaller1, DATEADD(MONTH,-3,@Hoy), 'OM-2024-0028', 'PREVENTIVO',
 'Servicio 100,000 km Isuzu: motor, frenos, suspensión',
 1100.00, 3200.00, 180.00, 100200, 'Recomendado cambio sistema de embrague próximo servicio'),
(@V7, @IdTaller2, DATEADD(MONTH,-1,@Hoy), 'OM-2025-0012', 'CORRECTIVO',
 'Reparación caja de cambios: sincronizador 3a y 4a',
 2800.00, 5500.00, 400.00, 103000, NULL),

(@V8, @IdTaller2, DATEADD(MONTH,-5,@Hoy), 'OM-2024-0006', 'CORRECTIVO',
 'Reparación mayor de motor: rectificado de cabezote y junta de culata',
 4500.00, 9800.00, 600.00, 145500, 'Mantenimiento mayor por sobrecalentamiento'),
(@V8, @IdTaller1, DATEADD(MONTH,-2,@Hoy), 'OM-2024-0030', 'PREVENTIVO',
 'Servicio 150,000 km: lubricación general, revisión transmisión',
  900.00, 2100.00, 150.00, 149000, 'Recomendado análisis de aceite cada 5,000 km'),
(@V8, @IdTaller2, DATEADD(MONTH,-1,@Hoy), 'OM-2025-0015', 'REVISION',
 'Inspección estructural por antigüedad y kilometraje',
  650.00,    0.00, 280.00, 150300, 'Vehículo apto. Recomendado cambio próximo año');

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
FROM @OrdenesExtra o
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ordenes_mantenimiento om
    WHERE om.id_empresa=@IdEmpresa AND om.id_vehiculo=o.id_vehiculo
      AND om.no_factura=o.no_factura AND om.eliminado=0
);

PRINT 'Órdenes de mantenimiento extra insertadas: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- 10. PÓLIZAS DE SEGUROS ADICIONALES (V4..V8)
-- ===========================================================================
DECLARE @PolizasExtra TABLE (
    id_vehiculo INT,
    no_poliza NVARCHAR(50), aseguradora NVARCHAR(150), tipo_cobertura NVARCHAR(30),
    fecha_inicio DATE, fecha_fin DATE, prima_total DECIMAL(18,2),
    observaciones NVARCHAR(500)
);

INSERT INTO @PolizasExtra VALUES
(@V4, 'SEGUHN-2025-00210', 'Seguros Atlántida S.A.',  'AMPLIA',
 DATEADD(MONTH,-2,@Hoy), DATEADD(MONTH,10,@Hoy), 55000.00, 'Unidad nueva. Cobertura completa.'),

(@V5, 'HSHN-2025-00910',   'HSBC Seguros Honduras',   'AMPLIA',
 DATEADD(MONTH,-1,@Hoy), DATEADD(MONTH,11,@Hoy), 28000.00, 'Microbús. Cobertura amplia.'),

(@V6, 'AIG-HN-2025-0410',  'AIG Seguros Honduras',    'RC',
 DATEADD(MONTH,-3,@Hoy), DATEADD(MONTH,9,@Hoy),  16500.00, 'RC obligatorio para microbús.'),
(@V6, 'AIG-HN-2025-0411',  'AIG Seguros Honduras',    'AMPLIA',
 DATEADD(MONTH,-3,@Hoy), DATEADD(MONTH,9,@Hoy),  32000.00, 'Cobertura amplia adicional.'),

(@V7, 'AXAHN-2025-00280',  'AXA Seguros Honduras',    'AMPLIA',
 DATEADD(MONTH,-4,@Hoy), DATEADD(MONTH,8,@Hoy),  38500.00, 'Camión de carga. Incluye mercadería.'),

(@V8, 'SEGUHN-2025-00220', 'Seguros Atlántida S.A.',  'RC',
 DATEADD(MONTH,-2,@Hoy), DATEADD(MONTH,10,@Hoy), 14500.00, 'Vehículo con kilometraje alto, solo RC.'),
(@V8, 'SEGUHN-2024-00102', 'Seguros Atlántida S.A.',  'RC',
 DATEADD(MONTH,-14,@Hoy), DATEADD(MONTH,-2,@Hoy), 13800.00, 'Póliza histórica anterior');

INSERT INTO dbo.polizas_seguros
    (id_empresa, id_vehiculo, no_poliza, aseguradora, tipo_cobertura,
     fecha_inicio, fecha_fin, prima_total, moneda, observaciones,
     eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa, p.id_vehiculo, p.no_poliza, p.aseguradora, p.tipo_cobertura,
    p.fecha_inicio, p.fecha_fin, p.prima_total, 'HNL', p.observaciones,
    0, 'seed', SYSUTCDATETIME()
FROM @PolizasExtra p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.polizas_seguros ps
    WHERE ps.id_empresa=@IdEmpresa AND ps.id_vehiculo=p.id_vehiculo
      AND ps.no_poliza=p.no_poliza AND ps.eliminado=0
);

PRINT 'Pólizas de seguros extra insertadas: ' + CAST(@@ROWCOUNT AS NVARCHAR);

-- ===========================================================================
-- RESUMEN FINAL
-- ===========================================================================
PRINT '';
PRINT '=== RESUMEN EXTRA - Empresa Demo (id=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';

SELECT 'Vehículos'                  AS Tabla, COUNT(*) AS Registros FROM dbo.vehiculos             WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Tipos de vehículo',                   COUNT(*)              FROM dbo.tipos_vehiculo        WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Talleres',                            COUNT(*)              FROM dbo.talleres              WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Personas',                            COUNT(*)              FROM dbo.personas              WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Rutas',                               COUNT(*)              FROM dbo.rutas                 WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Categorías Repuesto',                 COUNT(*)              FROM dbo.categorias_repuesto   WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Gastos Repuestos (sin llantas)',      COUNT(*)              FROM dbo.gastos_repuestos gr
    JOIN dbo.categorias_repuesto cr ON cr.id_categoria_repuesto = gr.id_categoria_repuesto
    WHERE gr.id_empresa=@IdEmpresa AND gr.eliminado=0 AND cr.es_llanta=0
UNION ALL
SELECT 'Gastos Llantas',                      COUNT(*)              FROM dbo.gastos_repuestos gr
    JOIN dbo.categorias_repuesto cr ON cr.id_categoria_repuesto = gr.id_categoria_repuesto
    WHERE gr.id_empresa=@IdEmpresa AND gr.eliminado=0 AND cr.es_llanta=1
UNION ALL
SELECT 'Órdenes Mantenimiento',               COUNT(*)              FROM dbo.ordenes_mantenimiento WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Pólizas Seguros',                     COUNT(*)              FROM dbo.polizas_seguros       WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Odómetro Diario',                     COUNT(*)              FROM dbo.odometro_diario       WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Cargas Combustible',                  COUNT(*)              FROM dbo.cargas_combustible    WHERE id_empresa=@IdEmpresa AND eliminado=0
UNION ALL
SELECT 'Salarios Diarios',                    COUNT(*)              FROM dbo.salarios_diarios      WHERE id_empresa=@IdEmpresa AND eliminado=0
ORDER BY Tabla;
