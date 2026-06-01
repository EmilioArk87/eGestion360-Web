-- =============================================================================
-- Seed: Gastos de Llantas - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual
-- Cobertura: ~1 compra de llantas cada 2 meses por vehículo (meses pares: día 12)
--           + emergencias ocasionales en marzo (día 5) para algunos vehículos
-- Idempotente: usa no_factura único 'LL-YYYYMM-VEH-SS' + NOT EXISTS
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

-- Categoría de llantas (es_llanta=1). Tomar la primera.
DECLARE @CatLlanta INT;
SELECT TOP 1 @CatLlanta = id_categoria_repuesto
FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND eliminado = 0 AND es_llanta = 1
ORDER BY id_categoria_repuesto;

IF @CatLlanta IS NULL
BEGIN
    RAISERROR('No existe categoría es_llanta=1 para la empresa Demo.', 16, 1);
    RETURN;
END

-- ===========================================================================
-- Plantillas (rotan por vehículo para variedad)
-- ===========================================================================
DECLARE @Plantillas TABLE (
    sec INT, dia INT,
    descripcion NVARCHAR(250),
    cantidad DECIMAL(12,3),
    precio_base DECIMAL(18,2),
    proveedor NVARCHAR(150)
);

INSERT INTO @Plantillas VALUES
(1, 12, 'Llantas 295/80R22.5 reemplazo (par)',          2.000,  9800.00, 'Llantas del Norte'),
(2, 12, 'Llantas 11R22.5 reemplazo (par)',              2.000,  9200.00, 'Llanteria Express'),
(3, 12, 'Llanta delantera 285/75R24.5 (unidad)',        1.000,  5400.00, 'Goodyear Distribuidor'),
(4, 5,  'Reparación pinchazo + parche industrial',      1.000,   450.00, 'Vulcanizadora La Esquina'),
(5, 5,  'Alineación y balanceo (4 ruedas)',             1.000,   780.00, 'Servicio Llantero Demo');

-- ===========================================================================
-- Loop mensual: solo meses pares (Feb, Abr, Jun...) para llantas nuevas (sec 1-3)
-- Las emergencias (sec 4-5) en marzo y mayo
-- ===========================================================================
DECLARE @MesIter DATE = @FechaInicio;
DECLARE @MesAnio NVARCHAR(6);
DECLARE @MesNum INT;
DECLARE @TotalInsertados INT = 0;

WHILE @MesIter <= @Hoy
BEGIN
    SET @MesAnio = FORMAT(@MesIter, 'yyyyMM');
    SET @MesNum  = MONTH(@MesIter);

    INSERT INTO dbo.gastos_repuestos
        (id_empresa, id_vehiculo, id_categoria_repuesto, fecha, no_factura, proveedor,
         descripcion, cantidad, precio_unitario, moneda, km_odometro, observaciones,
         eliminado, creado_por, fecha_creacion)
    SELECT
        @IdEmpresa,
        v.id_vehiculo,
        @CatLlanta,
        DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), p.dia) AS fecha,
        'LL-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(p.sec AS NVARCHAR), 2) AS no_factura,
        p.proveedor,
        p.descripcion,
        p.cantidad,
        p.precio_base + ((v.id_vehiculo + p.sec) % 4) * 25.00 AS precio_unitario,
        'HNL',
        NULL,
        NULL,
        0, 'seed', SYSUTCDATETIME()
    FROM dbo.vehiculos v
    CROSS JOIN @Plantillas p
    WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
      AND DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), p.dia) <= @Hoy
      -- Llantas nuevas (sec 1-3): solo una plantilla por vehículo, en meses pares,
      -- rotando entre las 3 según id_vehiculo
      AND (
            (p.sec BETWEEN 1 AND 3 AND @MesNum % 2 = 0 AND p.sec = ((v.id_vehiculo % 3) + 1))
            -- Emergencias (sec 4-5): vehículos seleccionados en marzo y mayo
         OR (p.sec = 4 AND @MesNum = 3 AND v.id_vehiculo % 2 = 0)
         OR (p.sec = 5 AND @MesNum = 5 AND v.id_vehiculo % 3 = 0)
      )
      AND NOT EXISTS (
          SELECT 1 FROM dbo.gastos_repuestos gr
          WHERE gr.id_empresa = @IdEmpresa
            AND gr.no_factura = 'LL-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(p.sec AS NVARCHAR), 2)
            AND gr.eliminado = 0
      );

    SET @TotalInsertados = @TotalInsertados + @@ROWCOUNT;
    SET @MesIter = DATEADD(MONTH, 1, @MesIter);
END

PRINT '';
PRINT 'Total gastos llantas insertados (esta ejecución): ' + CAST(@TotalInsertados AS NVARCHAR);

PRINT '';
PRINT '=== RESUMEN MENSUAL - Gastos Llantas (id_empresa=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';
SELECT
    FORMAT(gr.fecha, 'yyyy-MM') AS Mes,
    COUNT(*)                    AS Registros,
    COUNT(DISTINCT gr.id_vehiculo) AS Vehiculos,
    SUM(gr.subtotal)            AS Costo_Total_HNL
FROM dbo.gastos_repuestos gr
WHERE gr.id_empresa = @IdEmpresa AND gr.eliminado = 0
  AND gr.id_categoria_repuesto = @CatLlanta
  AND gr.fecha >= @FechaInicio AND gr.fecha <= @Hoy
GROUP BY FORMAT(gr.fecha, 'yyyy-MM')
ORDER BY Mes;
