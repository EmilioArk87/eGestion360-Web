-- =============================================================================
-- Seed: Gastos de Repuestos mensual - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual
-- Cobertura: 3 gastos/mes por vehículo (días 7, 17, 24) en categorías no-llanta
-- Idempotente: usa no_factura único 'RP-YYYYMM-VEH-SS' + NOT EXISTS
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

-- ===========================================================================
-- Catálogo de gastos típicos (rota por mes y vehículo)
-- 5 plantillas que se rotan -> simula variedad de repuestos
-- ===========================================================================
DECLARE @Plantillas TABLE (
    sec INT, dia INT,
    nombre_categoria NVARCHAR(60),
    descripcion NVARCHAR(250),
    cantidad DECIMAL(12,3),
    precio_base DECIMAL(18,2),
    proveedor NVARCHAR(150)
);

INSERT INTO @Plantillas VALUES
(1, 7,  'Aceites y Fluidos', 'Cambio de aceite motor 15W-40 + filtro',  4.000,   320.00, 'Lubricantes del Sur'),
(2, 17, 'Filtros',           'Filtro de aire + filtro de combustible',  1.000,   480.00, 'AutoPartes Centro'),
(3, 24, 'Frenos',            'Pastillas de freno delanteras (juego)',   1.000,  1450.00, 'Frenos y Servicios HN'),
(4, 24, 'Motor',              'Bujías de encendido (set 4)',            1.000,   980.00, 'AutoPartes Centro'),
(5, 17, 'Sistema Eléctrico',  'Batería 12V 100Ah reemplazo',            1.000,  3200.00, 'Baterías La Industrial');

-- Resolver id_categoria por nombre (ignora duplicados con tilde corrupta)
-- Toma el id_categoria_repuesto más bajo activo por nombre
DECLARE @CatMap TABLE (nombre_categoria NVARCHAR(60), id_categoria INT);
INSERT INTO @CatMap (nombre_categoria, id_categoria)
SELECT p.nombre_categoria, MIN(c.id_categoria_repuesto)
FROM @Plantillas p
JOIN dbo.categorias_repuesto c
  ON c.id_empresa = @IdEmpresa
 AND c.eliminado = 0
 AND c.es_llanta = 0
 AND (c.nombre = p.nombre_categoria OR c.nombre LIKE LEFT(p.nombre_categoria,4)+'%')
GROUP BY p.nombre_categoria;

-- Fallback: si no se mapea alguna, usar la primera categoría no-llanta
DECLARE @CatDefault INT;
SELECT TOP 1 @CatDefault = id_categoria_repuesto
FROM dbo.categorias_repuesto
WHERE id_empresa = @IdEmpresa AND eliminado = 0 AND es_llanta = 0
ORDER BY id_categoria_repuesto;

-- ===========================================================================
-- Loop mensual
-- ===========================================================================
DECLARE @MesIter DATE = @FechaInicio;
DECLARE @MesAnio NVARCHAR(6);
DECLARE @TotalInsertados INT = 0;

WHILE @MesIter <= @Hoy
BEGIN
    SET @MesAnio = FORMAT(@MesIter, 'yyyyMM');

    INSERT INTO dbo.gastos_repuestos
        (id_empresa, id_vehiculo, id_categoria_repuesto, fecha, no_factura, proveedor,
         descripcion, cantidad, precio_unitario, moneda, km_odometro, observaciones,
         eliminado, creado_por, fecha_creacion)
    SELECT
        @IdEmpresa,
        v.id_vehiculo,
        COALESCE(cm.id_categoria, @CatDefault) AS id_categoria_repuesto,
        DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), p.dia) AS fecha,
        'RP-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(p.sec AS NVARCHAR), 2) AS no_factura,
        p.proveedor,
        p.descripcion,
        p.cantidad,
        p.precio_base + ((v.id_vehiculo + p.sec) % 5) * 12.50 AS precio_unitario,
        'HNL',
        NULL,
        NULL,
        0, 'seed', SYSUTCDATETIME()
    FROM dbo.vehiculos v
    CROSS JOIN @Plantillas p
    LEFT JOIN @CatMap cm ON cm.nombre_categoria = p.nombre_categoria
    WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
      AND DATEFROMPARTS(YEAR(@MesIter), MONTH(@MesIter), p.dia) <= @Hoy
      -- Solo 3 gastos al mes por vehículo: sec en {1,2,3} todos los meses,
      -- + plantillas 4 y 5 en meses pares para variar
      AND (p.sec <= 3 OR (p.sec >= 4 AND MONTH(@MesIter) % 2 = 0))
      AND NOT EXISTS (
          SELECT 1 FROM dbo.gastos_repuestos gr
          WHERE gr.id_empresa = @IdEmpresa
            AND gr.no_factura = 'RP-' + @MesAnio + '-' + RIGHT('00' + CAST(v.id_vehiculo AS NVARCHAR), 3) + '-' + RIGHT('0' + CAST(p.sec AS NVARCHAR), 2)
            AND gr.eliminado = 0
      );

    SET @TotalInsertados = @TotalInsertados + @@ROWCOUNT;
    SET @MesIter = DATEADD(MONTH, 1, @MesIter);
END

PRINT '';
PRINT 'Total gastos repuestos insertados (esta ejecución): ' + CAST(@TotalInsertados AS NVARCHAR);

PRINT '';
PRINT '=== RESUMEN MENSUAL - Gastos Repuestos (id_empresa=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';
SELECT
    FORMAT(gr.fecha, 'yyyy-MM') AS Mes,
    COUNT(*)                    AS Registros,
    COUNT(DISTINCT gr.id_vehiculo) AS Vehiculos,
    SUM(gr.subtotal)            AS Costo_Total_HNL
FROM dbo.gastos_repuestos gr
JOIN dbo.categorias_repuesto c ON c.id_categoria_repuesto = gr.id_categoria_repuesto
WHERE gr.id_empresa = @IdEmpresa AND gr.eliminado = 0 AND c.es_llanta = 0
  AND gr.fecha >= @FechaInicio AND gr.fecha <= @Hoy
GROUP BY FORMAT(gr.fecha, 'yyyy-MM')
ORDER BY Mes;
