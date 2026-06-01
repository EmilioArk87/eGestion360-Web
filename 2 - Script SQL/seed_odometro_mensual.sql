-- =============================================================================
-- Seed: Odómetro Diario mensual - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual
-- Cobertura: un registro diario por cada vehículo activo (lun-sáb)
-- Idempotente: usa índice único (id_empresa, id_vehiculo, fecha) + NOT EXISTS
-- KMs alineados con seed_combustible_mensual.sql para mantener consistencia.
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
-- Configuración por vehículo (km/día y km_inicial al 2026-01-01)
-- Mismas constantes que seed_combustible_mensual.sql -> datos consistentes
-- ===========================================================================
DECLARE @ConfigVehiculos TABLE (
    id_vehiculo     INT,
    placa           NVARCHAR(20),
    km_diario       DECIMAL(18,2),
    km_inicial_ene  DECIMAL(18,2),
    id_conductor    INT NULL,
    id_ruta         INT NULL
);

-- Conductor por defecto (primero disponible)
DECLARE @ConductorDefault INT;
SELECT TOP 1 @ConductorDefault = id_persona
FROM dbo.personas
WHERE id_empresa = @IdEmpresa AND cargo = 'CONDUCTOR' AND eliminado = 0
ORDER BY id_persona;

-- Ruta por defecto (primera disponible, opcional)
DECLARE @RutaDefault INT = NULL;
SELECT TOP 1 @RutaDefault = id_ruta
FROM dbo.rutas
WHERE id_empresa = @IdEmpresa AND eliminado = 0
ORDER BY id_ruta;

INSERT INTO @ConfigVehiculos (id_vehiculo, placa, km_diario, km_inicial_ene, id_conductor, id_ruta)
SELECT
    v.id_vehiculo,
    v.placa,
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
    @ConductorDefault,
    @RutaDefault
FROM dbo.vehiculos v
WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1;

-- ===========================================================================
-- Generar registro diario por vehículo (lunes a sábado)
-- km_inicial(día) = km_inicial_ene + diasDesdeInicio * km_diario
-- km_final(día)   = km_inicial(día) + km_diario + pequeña variación
-- ===========================================================================
DECLARE @Fecha DATE = @FechaInicio;
DECLARE @TotalInsertados INT = 0;
DECLARE @Inserts INT;

WHILE @Fecha <= @Hoy
BEGIN
    -- Domingo = 1 (con DATEFIRST default 7). Más seguro usar DATENAME.
    IF DATENAME(WEEKDAY, @Fecha) <> 'Sunday' AND DATENAME(WEEKDAY, @Fecha) <> 'domingo'
    BEGIN
        DECLARE @DiasDesdeInicio INT = DATEDIFF(DAY, @FechaInicio, @Fecha);

        INSERT INTO dbo.odometro_diario
            (id_empresa, id_vehiculo, fecha, km_inicial, km_final,
             id_ruta, id_conductor, observaciones,
             eliminado, creado_por, fecha_creacion)
        SELECT
            @IdEmpresa,
            c.id_vehiculo,
            @Fecha,
            c.km_inicial_ene + @DiasDesdeInicio * c.km_diario AS km_inicial,
            c.km_inicial_ene + @DiasDesdeInicio * c.km_diario
                + c.km_diario
                + (((c.id_vehiculo + @DiasDesdeInicio) % 7) - 3) * 2.5 AS km_final,
            c.id_ruta,
            c.id_conductor,
            NULL,
            0, 'seed', SYSUTCDATETIME()
        FROM @ConfigVehiculos c
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.odometro_diario od
            WHERE od.id_empresa = @IdEmpresa
              AND od.id_vehiculo = c.id_vehiculo
              AND od.fecha = @Fecha
              AND od.eliminado = 0
        );

        SET @Inserts = @@ROWCOUNT;
        SET @TotalInsertados = @TotalInsertados + @Inserts;
    END

    SET @Fecha = DATEADD(DAY, 1, @Fecha);
END

PRINT '';
PRINT 'Total registros de odómetro insertados (esta ejecución): ' + CAST(@TotalInsertados AS NVARCHAR);

-- ===========================================================================
-- RESUMEN: registros por mes
-- ===========================================================================
PRINT '';
PRINT '=== RESUMEN MENSUAL - Odómetro Diario (id_empresa=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';

SELECT
    FORMAT(fecha, 'yyyy-MM')      AS Mes,
    COUNT(*)                      AS Registros,
    COUNT(DISTINCT id_vehiculo)   AS Vehiculos,
    SUM(km_recorridos)            AS KM_Totales,
    AVG(km_recorridos)            AS KM_Promedio_Dia
FROM dbo.odometro_diario
WHERE id_empresa = @IdEmpresa AND eliminado = 0
  AND fecha >= @FechaInicio AND fecha <= @Hoy
GROUP BY FORMAT(fecha, 'yyyy-MM')
ORDER BY Mes;

PRINT '';
PRINT '=== TOTAL POR VEHÍCULO (período Ene-' + FORMAT(@Hoy, 'MMM yyyy') + ') ===';

SELECT
    v.placa                            AS Placa,
    COUNT(od.id_odometro_diario)       AS Dias,
    MIN(od.km_inicial)                 AS KM_Inicial,
    MAX(od.km_final)                   AS KM_Final,
    SUM(od.km_recorridos)              AS KM_Recorridos
FROM dbo.vehiculos v
LEFT JOIN dbo.odometro_diario od
    ON od.id_vehiculo = v.id_vehiculo
   AND od.eliminado = 0
   AND od.fecha >= @FechaInicio AND od.fecha <= @Hoy
WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
GROUP BY v.placa
ORDER BY v.placa;
