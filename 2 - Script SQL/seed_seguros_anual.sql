-- =============================================================================
-- Seed: Pólizas de Seguros 2026 - Empresa Demo (id_empresa = 2)
-- Período de vigencia: 2026-01-01 a 2026-12-31 (anual)
-- Cobertura: una póliza por cada vehículo activo
-- Idempotente: UNIQUE (id_empresa, no_poliza, id_vehiculo) + NOT EXISTS
-- Convive con pólizas existentes (usa prefijo 'SEED-2026-' para evitar choques)
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @IdEmpresa INT = 2;
DECLARE @FechaInicio DATE = '2026-01-01';
DECLARE @FechaFin    DATE = '2026-12-31';

IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE id_empresa = @IdEmpresa)
BEGIN
    RAISERROR('No existe la empresa Demo (id_empresa = 2).', 16, 1);
    RETURN;
END

-- Catálogo simple de aseguradoras y tipos para rotar
DECLARE @Aseguradoras TABLE (rn INT IDENTITY(1,1), nombre NVARCHAR(150), tipo VARCHAR(30), prima DECIMAL(18,2));
INSERT INTO @Aseguradoras (nombre, tipo, prima) VALUES
('Seguros Atlántida',  'AMPLIA',    24500.00),
('AIG Honduras',       'AMPLIA',    26800.00),
('AXA Seguros HN',     'LIMITADA',  18300.00),
('Mapfre Honduras',    'RC',        12500.00),
('Pan-American Life',  'AMPLIA',    25200.00);

DECLARE @NumAseg INT = (SELECT COUNT(*) FROM @Aseguradoras);

INSERT INTO dbo.polizas_seguros
    (id_empresa, id_vehiculo, no_poliza, aseguradora, tipo_cobertura,
     fecha_inicio, fecha_fin, prima_total, moneda, observaciones,
     eliminado, creado_por, fecha_creacion)
SELECT
    @IdEmpresa,
    v.id_vehiculo,
    'SEED-2026-' + RIGHT('0000' + CAST(v.id_vehiculo AS NVARCHAR), 4) AS no_poliza,
    a.nombre,
    a.tipo,
    @FechaInicio,
    @FechaFin,
    a.prima + ((v.id_vehiculo % 5) * 350.00) AS prima_total,
    'HNL',
    NULL,
    0, 'seed', SYSUTCDATETIME()
FROM dbo.vehiculos v
CROSS JOIN @Aseguradoras a
WHERE v.id_empresa = @IdEmpresa AND v.eliminado = 0 AND v.activo = 1
  AND a.rn = ((v.id_vehiculo - 1) % @NumAseg) + 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.polizas_seguros p
      WHERE p.id_empresa = @IdEmpresa
        AND p.id_vehiculo = v.id_vehiculo
        AND p.no_poliza = 'SEED-2026-' + RIGHT('0000' + CAST(v.id_vehiculo AS NVARCHAR), 4)
        AND p.eliminado = 0
  );

PRINT '';
PRINT 'Total pólizas insertadas (esta ejecución): ' + CAST(@@ROWCOUNT AS NVARCHAR);

PRINT '';
PRINT '=== PÓLIZAS 2026 - Empresa Demo (id=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';
SELECT
    v.placa,
    p.no_poliza,
    p.aseguradora,
    p.tipo_cobertura,
    p.fecha_inicio,
    p.fecha_fin,
    p.prima_total,
    p.costo_diario
FROM dbo.polizas_seguros p
JOIN dbo.vehiculos v ON v.id_vehiculo = p.id_vehiculo
WHERE p.id_empresa = @IdEmpresa AND p.eliminado = 0
  AND p.no_poliza LIKE 'SEED-2026-%'
ORDER BY v.placa;

-- Reparto mensual estimado (costo_diario * días del mes) para 2026
PRINT '';
PRINT '=== COSTO MENSUAL ESTIMADO 2026 (sólo pólizas SEED-2026) ===';
;WITH Meses AS (
    SELECT 1 AS m UNION ALL SELECT m+1 FROM Meses WHERE m < 12
)
SELECT
    FORMAT(DATEFROMPARTS(2026, m, 1), 'yyyy-MM') AS Mes,
    SUM(p.costo_diario * DAY(EOMONTH(DATEFROMPARTS(2026, m, 1)))) AS Costo_Mes_HNL
FROM Meses
CROSS JOIN dbo.polizas_seguros p
WHERE p.id_empresa = @IdEmpresa AND p.eliminado = 0
  AND p.no_poliza LIKE 'SEED-2026-%'
GROUP BY m
ORDER BY m;
