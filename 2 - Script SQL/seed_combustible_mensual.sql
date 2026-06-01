-- =============================================================================
-- Seed: Cargas de Combustible mensuales - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual (mayo 2026)
-- Cobertura: ~5-6 cargas por mes por cada vehículo activo (cada ~5-7 días)
-- Idempotente: usa no_factura único 'CB-YYYYMM-VEH-NN' + NOT EXISTS
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @IdEmpresa INT = 2;
DECLARE @Hoy DATE = CAST(GETDATE() AS DATE);
DECLARE @FechaInicio DATE = '2026-01-01';

-- Validar empresa
IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE id_empresa = @IdEmpresa)
BEGIN
    RAISERROR('No existe la empresa Demo (id_empresa = 2). Cree la empresa primero.', 16, 1);
    RETURN;
END

-- Validar vehículos
IF NOT EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND eliminado = 0)
BEGIN
    RAISERROR('No hay vehículos para la empresa Demo. Ejecute primero seed_demo_flota.sql', 16, 1);
    RETURN;
END

-- ===========================================================================
-- Configuración por vehículo (cantidad típica, proveedor habitual, conductor)
-- ===========================================================================
DECLARE @ConfigVehiculos TABLE (
    id_vehiculo     INT,
    placa           NVARCHAR(20),
    galones_base    DECIMAL(18,2),  -- galones promedio por carga
    galones_var     DECIMAL(18,2),  -- variación máxima
    km_diario       DECIMAL(18,2),  -- km por día (para calcular odómetro)
    km_inicial_ene  DECIMAL(18,2),  -- km al inicio de enero 2026
    proveedor       NVARCHAR(150),
    id_conductor    INT NULL
);

-- Tomar conductor por defecto (primero disponible)
DECLARE @ConductorDefault INT;
SELECT TOP 1 @ConductorDefault = id_persona
FROM dbo.personas
WHERE id_empresa = @IdEmpresa AND cargo = 'CONDUCTOR' AND eliminado = 0
ORDER BY id_persona;

INSERT INTO @ConfigVehiculos (id_vehiculo, placa, galones_base, galones_var, km_diario, km_inicial_ene, proveedor, id_conductor)
SELECT
    v.id_vehiculo,
    v.placa,
    CASE
        WHEN v.placa = 'HND-001' THEN 32.00
        WHEN v.placa = 'HND-002' THEN 45.00
        WHEN v.placa = 'HND-003' THEN 58.00
        WHEN v.placa = 'HND-004' THEN 40.00
        WHEN v.placa = 'HND-005' THEN 16.00
        WHEN v.placa = 'HND-006' THEN 18.00
        WHEN v.placa = 'HND-007' THEN 28.00
        WHEN v.placa = 'HND-008' THEN 50.00
        ELSE 30.00
    END,
    CASE
        WHEN v.placa IN ('HND-005','HND-006') THEN 3.00
        ELSE 5.00
    END,
    CASE
        WHEN v.placa = 'HND-001' THEN 180.00
        WHEN v.placa = 'HND-002' THEN 340.00
        WHEN v.placa = 'HND-003' THEN 240.00
        WHEN v.placa = 'HND-004' THEN 290.00
        WHEN v.placa = 'HND-005' THEN 120.00
        WHEN v.placa = 'HND-006' THEN 140.00
        WHEN v.placa = 'HND-007' THEN 200.00
        WHEN v.placa = 'HND-008' THEN 220.00
        ELSE 180.00
    END,
    CASE
        WHEN v.placa = 'HND-001' THEN 70000
        WHEN v.placa = 'HND-002' THEN 50000
        WHEN v.placa = 'HND-003' THEN 110000
        WHEN v.placa = 'HND-004' THEN 28000
        WHEN v.placa = 'HND-005' THEN 12000
        WHEN v.placa = 'HND-006' THEN 45000
        WHEN v.placa = 'HND-007' THEN 88000
        WHEN v.placa = 'HND-008' THEN 132000
        ELSE 50000
    END,
    CASE
        WHEN v.placa IN ('HND-001','HND-005') THEN 'Uno Petrol Centro'
        WHEN v.placa = 'HND-002' THEN 'Texaco Mayaland'
        WHEN v.placa = 'HND-003' THEN 'Puma Energy SPS'
        WHEN v.placa = 'HND-004' THEN 'Uno Petrol Choluteca'
        WHEN v.placa = 'HND-006' THEN 'Dippsa Boulevard'
        WHEN v.placa = 'HND-007' THEN 'Texaco Toncontín'
        WHEN v.placa = 'HND-008' THEN 'Puma Energy Centro'
        ELSE 'Uno Petrol Centro'
    END,
    @ConductorDefault
FROM dbo.vehiculos v
WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1;

-- ===========================================================================
-- Generar cargas mes por mes, día 4, 10, 16, 22, 28 (5 cargas/mes/vehículo)
-- Detenerse en @Hoy si el día cae en el mes actual
-- ===========================================================================
DECLARE @MesIter DATE = @FechaInicio;
DECLARE @FinMes DATE;
DECLARE @TotalInsertadas INT = 0;

WHILE @MesIter <= @Hoy
BEGIN
    SET @FinMes = EOMONTH(@MesIter);

    -- Días de carga dentro del mes
    DECLARE @Dias TABLE (dia INT, secuencia INT);
    DELETE FROM @Dias;
    INSERT INTO @Dias VALUES (4,1),(10,2),(16,3),(22,4),(28,5);

    DECLARE @MesAnio NVARCHAR(6) = FORMAT(@MesIter, 'yyyyMM');

    -- Precio base del mes (varía mensualmente para simular cambios de mercado)
    DECLARE @PrecioMes DECIMAL(18,2) =
        CASE FORMAT(@MesIter, 'yyyyMM')
            WHEN '202601' THEN 103.50
            WHEN '202602' THEN 104.25
            WHEN '202603' THEN 105.75
            WHEN '202604' THEN 107.00
            WHEN '202605' THEN 106.50
            WHEN '202606' THEN 108.00
            WHEN '202607' THEN 109.25
            WHEN '202608' THEN 110.00
            WHEN '202609' THEN 109.50
            WHEN '202610' THEN 108.75
            WHEN '202611' THEN 107.50
            WHEN '202612' THEN 106.00
            ELSE 105.50
        END;

    -- Insertar una carga por (vehículo, día) si la fecha no supera @Hoy
    INSERT INTO dbo.cargas_combustible
        (id_empresa, id_vehiculo, fecha, hora, no_factura, proveedor, tipo_combustible, unidad_medida,
         cantidad, precio_unitario, moneda, km_odometro, id_conductor, observaciones,
         eliminado, creado_por, fecha_creacion)
    SELECT
        @IdEmpresa,
        c.id_vehiculo,
        DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), d.dia) AS fecha,
        CAST(DATEADD(MINUTE, (c.id_vehiculo * 7 + d.dia * 13) % 180, '06:00') AS TIME) AS hora,
        'CB-' + @MesAnio + '-' + RIGHT('00' + CAST(c.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(d.secuencia AS NVARCHAR), 2) AS no_factura,
        c.proveedor,
        'DIESEL',
        'GAL',
        c.galones_base + ((d.dia + c.id_vehiculo) % 6) * (c.galones_var / 5.0) AS cantidad,
        @PrecioMes + (((d.dia + c.id_vehiculo) % 5) - 2) * 0.50 AS precio_unitario,
        'HNL',
        -- Odómetro acumulado: km_inicial_enero + (días transcurridos desde 2026-01-01) * km_diario
        c.km_inicial_ene + DATEDIFF(DAY, @FechaInicio, DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), d.dia)) * c.km_diario,
        c.id_conductor,
        NULL,
        0, 'seed', SYSUTCDATETIME()
    FROM @ConfigVehiculos c
    CROSS JOIN @Dias d
    WHERE DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), d.dia) <= @Hoy
      AND NOT EXISTS (
          SELECT 1 FROM dbo.cargas_combustible cc
          WHERE cc.id_empresa = @IdEmpresa
            AND cc.id_vehiculo = c.id_vehiculo
            AND cc.no_factura = 'CB-' + @MesAnio + '-' + RIGHT('00' + CAST(c.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(d.secuencia AS NVARCHAR), 2)
            AND cc.eliminado = 0
      );

    SET @TotalInsertadas = @TotalInsertadas + @@ROWCOUNT;

    PRINT 'Mes ' + FORMAT(@MesIter, 'yyyy-MM') + ' procesado. Acumulado: ' + CAST(@TotalInsertadas AS NVARCHAR);

    SET @MesIter = DATEADD(MONTH, 1, @MesIter);
END

PRINT '';
PRINT 'Total cargas insertadas (esta ejecución): ' + CAST(@TotalInsertadas AS NVARCHAR);

-- ===========================================================================
-- RESUMEN: cargas por mes y vehículo
-- ===========================================================================
PRINT '';
PRINT '=== RESUMEN MENSUAL - Empresa Demo (id=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';

SELECT
    FORMAT(fecha, 'yyyy-MM') AS Mes,
    COUNT(*)                 AS Cargas,
    COUNT(DISTINCT id_vehiculo) AS Vehiculos,
    SUM(cantidad)            AS Galones,
    SUM(total)               AS Costo_Total_HNL
FROM dbo.cargas_combustible
WHERE id_empresa = @IdEmpresa AND eliminado = 0
  AND fecha >= @FechaInicio AND fecha <= @Hoy
GROUP BY FORMAT(fecha, 'yyyy-MM')
ORDER BY Mes;

PRINT '';
PRINT '=== TOTAL POR VEHÍCULO (período Ene-' + FORMAT(@Hoy, 'MMM yyyy') + ') ===';

SELECT
    v.placa                  AS Placa,
    COUNT(cc.id_carga_combustible) AS Cargas,
    SUM(cc.cantidad)         AS Galones,
    SUM(cc.total)            AS Costo_HNL
FROM dbo.vehiculos v
LEFT JOIN dbo.cargas_combustible cc
    ON cc.id_vehiculo = v.id_vehiculo
   AND cc.eliminado = 0
   AND cc.fecha >= @FechaInicio AND cc.fecha <= @Hoy
WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
GROUP BY v.placa
ORDER BY v.placa;
