-- =============================================================================
-- Seed: Órdenes de Mantenimiento mensual - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual
-- Cobertura: 1 orden/mes por vehículo (día 20). Tipo rota mes-vehículo:
--           PREVENTIVO (default), CORRECTIVO (algunos meses), REVISION (esporádica)
-- Idempotente: UNIQUE (id_empresa, no_factura) + NOT EXISTS
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @IdEmpresa INT = 2;
DECLARE @Hoy DATE = CAST(GETDATE() AS DATE);
DECLARE @FechaInicio DATE = '2026-01-01';

IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE id_empresa = @IdEmpresa)
BEGIN
    RAISERROR('No existe la empresa Demo (id_empresa = 2).', 16, 1);
    RETURN;
END

-- Talleres disponibles para round-robin
DECLARE @Talleres TABLE (rn INT IDENTITY(1,1), id_taller INT);
INSERT INTO @Talleres (id_taller)
SELECT id_taller FROM dbo.talleres
WHERE id_empresa = @IdEmpresa AND eliminado = 0
ORDER BY id_taller;

DECLARE @NumTalleres INT = (SELECT COUNT(*) FROM @Talleres);
IF @NumTalleres = 0
BEGIN
    RAISERROR('No hay talleres en empresa Demo. Cree al menos uno primero.', 16, 1);
    RETURN;
END

-- ===========================================================================
-- Loop mensual
-- ===========================================================================
DECLARE @MesIter DATE = @FechaInicio;
DECLARE @MesAnio NVARCHAR(6);
DECLARE @MesNum  INT;
DECLARE @Fecha   DATE;
DECLARE @TotalInsertados INT = 0;

WHILE @MesIter <= @Hoy
BEGIN
    SET @MesAnio = FORMAT(@MesIter, 'yyyyMM');
    SET @MesNum  = MONTH(@MesIter);
    SET @Fecha   = DATEFROMPARTS(YEAR(@MesIter), @MesNum, 20);

    IF @Fecha <= @Hoy
    BEGIN
        INSERT INTO dbo.ordenes_mantenimiento
            (id_empresa, id_vehiculo, id_taller, fecha, no_factura, tipo_mantenimiento,
             descripcion, monto_mano_obra, monto_repuestos, monto_otros, moneda,
             km_odometro, observaciones,
             eliminado, creado_por, fecha_creacion)
        SELECT
            @IdEmpresa,
            v.id_vehiculo,
            t.id_taller,
            @Fecha,
            'MTO-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3) AS no_factura,
            CASE
                WHEN (v.id_vehiculo + @MesNum) % 6 = 0 THEN 'REVISION'
                WHEN (v.id_vehiculo + @MesNum) % 3 = 0 THEN 'CORRECTIVO'
                ELSE 'PREVENTIVO'
            END AS tipo_mantenimiento,
            CASE
                WHEN (v.id_vehiculo + @MesNum) % 6 = 0 THEN 'Revisión técnica mensual y chequeo general'
                WHEN (v.id_vehiculo + @MesNum) % 3 = 0 THEN 'Reparación correctiva - intervención por falla reportada'
                ELSE 'Mantenimiento preventivo programado - cambio de fluidos y revisión'
            END,
            -- Mano de obra
            CASE
                WHEN (v.id_vehiculo + @MesNum) % 6 = 0 THEN 600.00
                WHEN (v.id_vehiculo + @MesNum) % 3 = 0 THEN 2400.00 + ((v.id_vehiculo * 17 + @MesNum) % 5) * 80.00
                ELSE 1200.00 + ((v.id_vehiculo * 11 + @MesNum) % 4) * 50.00
            END AS monto_mano_obra,
            -- Repuestos
            CASE
                WHEN (v.id_vehiculo + @MesNum) % 6 = 0 THEN 0.00
                WHEN (v.id_vehiculo + @MesNum) % 3 = 0 THEN 3800.00 + ((v.id_vehiculo * 13 + @MesNum) % 7) * 120.00
                ELSE 1850.00 + ((v.id_vehiculo * 7 + @MesNum) % 5) * 90.00
            END AS monto_repuestos,
            -- Otros
            CASE
                WHEN (v.id_vehiculo + @MesNum) % 6 = 0 THEN 150.00
                WHEN (v.id_vehiculo + @MesNum) % 3 = 0 THEN 350.00
                ELSE 200.00
            END AS monto_otros,
            'HNL',
            NULL,
            NULL,
            0, 'seed', SYSUTCDATETIME()
        FROM dbo.vehiculos v
        CROSS JOIN @Talleres t
        WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
          -- Round-robin de taller por vehículo y mes
          AND t.rn = ((v.id_vehiculo + @MesNum - 1) % @NumTalleres) + 1
          AND NOT EXISTS (
              SELECT 1 FROM dbo.ordenes_mantenimiento om
              WHERE om.id_empresa = @IdEmpresa
                AND om.no_factura = 'MTO-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3)
                AND om.eliminado = 0
          );

        SET @TotalInsertados = @TotalInsertados + @@ROWCOUNT;
    END

    SET @MesIter = DATEADD(MONTH, 1, @MesIter);
END

PRINT '';
PRINT 'Total órdenes mantenimiento insertadas (esta ejecución): ' + CAST(@TotalInsertados AS NVARCHAR);

PRINT '';
PRINT '=== RESUMEN MENSUAL - Mantenimiento (id_empresa=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';
SELECT
    FORMAT(fecha, 'yyyy-MM') AS Mes,
    COUNT(*) AS Ordenes,
    SUM(CASE WHEN tipo_mantenimiento='PREVENTIVO' THEN 1 ELSE 0 END) AS Preventivos,
    SUM(CASE WHEN tipo_mantenimiento='CORRECTIVO' THEN 1 ELSE 0 END) AS Correctivos,
    SUM(CASE WHEN tipo_mantenimiento='REVISION'   THEN 1 ELSE 0 END) AS Revisiones,
    SUM(total) AS Costo_Total_HNL
FROM dbo.ordenes_mantenimiento
WHERE id_empresa = @IdEmpresa AND eliminado = 0
  AND fecha >= @FechaInicio AND fecha <= @Hoy
GROUP BY FORMAT(fecha, 'yyyy-MM')
ORDER BY Mes;
