-- =============================================================================
-- Seed: Salarios Diarios mensual - Empresa Demo (id_empresa = 2)
-- Período: enero 2026 hasta el mes actual
-- Cobertura: lun-sáb, un conductor asignado por vehículo (round-robin).
--           Además: 2 cobradores rotando, 1 supervisor y 1 mecánico (lun-vie).
-- Idempotente: UNIQUE (id_empresa, id_vehiculo, id_persona, fecha) + NOT EXISTS
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
-- Mapeo vehículo -> conductor (round-robin sobre conductores disponibles)
-- ===========================================================================
DECLARE @Conductores TABLE (rn INT IDENTITY(1,1), id_persona INT, tarifa DECIMAL(18,2));
INSERT INTO @Conductores (id_persona, tarifa)
SELECT id_persona, tarifa_diaria
FROM dbo.personas
WHERE id_empresa = @IdEmpresa AND cargo = 'CONDUCTOR' AND eliminado = 0
ORDER BY id_persona;

DECLARE @NumConductores INT = (SELECT COUNT(*) FROM @Conductores);
IF @NumConductores = 0
BEGIN
    RAISERROR('No hay conductores en la empresa Demo. Ejecute seed_demo_flota_extra.sql primero.', 16, 1);
    RETURN;
END

DECLARE @VehConductor TABLE (id_vehiculo INT, id_persona INT, tarifa DECIMAL(18,2));
INSERT INTO @VehConductor (id_vehiculo, id_persona, tarifa)
SELECT
    v.id_vehiculo,
    c.id_persona,
    c.tarifa
FROM (
    SELECT id_vehiculo, ROW_NUMBER() OVER (ORDER BY id_vehiculo) AS rn
    FROM dbo.vehiculos
    WHERE id_empresa = @IdEmpresa AND eliminado = 0 AND activo = 1
) v
JOIN @Conductores c ON c.rn = ((v.rn - 1) % @NumConductores) + 1;

-- Cobradores (2): rotan diariamente entre todos los vehículos
DECLARE @Cobradores TABLE (rn INT IDENTITY(1,1), id_persona INT, tarifa DECIMAL(18,2));
INSERT INTO @Cobradores (id_persona, tarifa)
SELECT id_persona, tarifa_diaria
FROM dbo.personas
WHERE id_empresa = @IdEmpresa AND cargo = 'COBRADOR' AND eliminado = 0
ORDER BY id_persona;
DECLARE @NumCobradores INT = (SELECT COUNT(*) FROM @Cobradores);

-- Supervisor (1) y Mecánico (1): se asignan a un vehículo "ancla" (el primero)
DECLARE @IdSupervisor INT, @TarifaSupervisor DECIMAL(18,2);
SELECT TOP 1 @IdSupervisor = id_persona, @TarifaSupervisor = tarifa_diaria
FROM dbo.personas WHERE id_empresa = @IdEmpresa AND cargo = 'SUPERVISOR' AND eliminado = 0
ORDER BY id_persona;

DECLARE @IdMecanico INT, @TarifaMecanico DECIMAL(18,2);
SELECT TOP 1 @IdMecanico = id_persona, @TarifaMecanico = tarifa_diaria
FROM dbo.personas WHERE id_empresa = @IdEmpresa AND cargo = 'MECANICO' AND eliminado = 0
ORDER BY id_persona;

DECLARE @VehAncla INT;
SELECT TOP 1 @VehAncla = id_vehiculo
FROM dbo.vehiculos WHERE id_empresa = @IdEmpresa AND eliminado = 0 AND activo = 1
ORDER BY id_vehiculo;

-- ===========================================================================
-- Loop diario lunes-sábado
-- ===========================================================================
DECLARE @Fecha DATE = @FechaInicio;
DECLARE @TotalInsertados INT = 0;
DECLARE @Inserts INT;
DECLARE @DiaSem NVARCHAR(15);
DECLARE @DiasDesde INT;
DECLARE @IdxCob INT;

WHILE @Fecha <= @Hoy
BEGIN
    SET @DiaSem = DATENAME(WEEKDAY, @Fecha);
    SET @DiasDesde = DATEDIFF(DAY, @FechaInicio, @Fecha);

    IF @DiaSem NOT IN ('Sunday','domingo')
    BEGIN
        -- 1) Conductores (lun-sáb): un salario por vehículo con su conductor asignado
        INSERT INTO dbo.salarios_diarios
            (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones,
             eliminado, creado_por, fecha_creacion)
        SELECT
            @IdEmpresa, vc.id_vehiculo, vc.id_persona, @Fecha, 'CONDUCTOR', vc.tarifa, 'HNL', NULL,
            0, 'seed', SYSUTCDATETIME()
        FROM @VehConductor vc
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.salarios_diarios s
            WHERE s.id_empresa=@IdEmpresa AND s.id_vehiculo=vc.id_vehiculo
              AND s.id_persona=vc.id_persona AND s.fecha=@Fecha AND s.eliminado=0
        );
        SET @Inserts = @@ROWCOUNT;
        SET @TotalInsertados = @TotalInsertados + @Inserts;

        -- 2) Cobradores (lun-sáb): alternan diariamente. Cada cobrador a la mitad de los vehículos.
        IF @NumCobradores > 0
        BEGIN
            INSERT INTO dbo.salarios_diarios
                (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones,
                 eliminado, creado_por, fecha_creacion)
            SELECT
                @IdEmpresa, vc.id_vehiculo, cb.id_persona, @Fecha, 'COBRADOR', cb.tarifa, 'HNL', NULL,
                0, 'seed', SYSUTCDATETIME()
            FROM (
                SELECT id_vehiculo, ROW_NUMBER() OVER (ORDER BY id_vehiculo) AS rn
                FROM @VehConductor
            ) vc
            JOIN @Cobradores cb ON cb.rn = ((vc.rn - 1 + @DiasDesde) % @NumCobradores) + 1
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.salarios_diarios s
                WHERE s.id_empresa=@IdEmpresa AND s.id_vehiculo=vc.id_vehiculo
                  AND s.id_persona=cb.id_persona AND s.fecha=@Fecha AND s.eliminado=0
            );
            SET @Inserts = @@ROWCOUNT;
            SET @TotalInsertados = @TotalInsertados + @Inserts;
        END

        -- 3) Supervisor y Mecánico (lun-vie) anclados al primer vehículo
        IF @DiaSem NOT IN ('Saturday','sábado','sabado')
        BEGIN
            IF @IdSupervisor IS NOT NULL
            BEGIN
                INSERT INTO dbo.salarios_diarios
                    (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones,
                     eliminado, creado_por, fecha_creacion)
                SELECT
                    @IdEmpresa, @VehAncla, @IdSupervisor, @Fecha, 'SUPERVISOR', @TarifaSupervisor, 'HNL', NULL,
                    0, 'seed', SYSUTCDATETIME()
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.salarios_diarios s
                    WHERE s.id_empresa=@IdEmpresa AND s.id_vehiculo=@VehAncla
                      AND s.id_persona=@IdSupervisor AND s.fecha=@Fecha AND s.eliminado=0
                );
                SET @Inserts = @@ROWCOUNT;
                SET @TotalInsertados = @TotalInsertados + @Inserts;
            END

            IF @IdMecanico IS NOT NULL
            BEGIN
                INSERT INTO dbo.salarios_diarios
                    (id_empresa, id_vehiculo, id_persona, fecha, cargo, monto, moneda, observaciones,
                     eliminado, creado_por, fecha_creacion)
                SELECT
                    @IdEmpresa, @VehAncla, @IdMecanico, @Fecha, 'MECANICO', @TarifaMecanico, 'HNL', NULL,
                    0, 'seed', SYSUTCDATETIME()
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.salarios_diarios s
                    WHERE s.id_empresa=@IdEmpresa AND s.id_vehiculo=@VehAncla
                      AND s.id_persona=@IdMecanico AND s.fecha=@Fecha AND s.eliminado=0
                );
                SET @Inserts = @@ROWCOUNT;
                SET @TotalInsertados = @TotalInsertados + @Inserts;
            END
        END
    END

    SET @Fecha = DATEADD(DAY, 1, @Fecha);
END

PRINT '';
PRINT 'Total salarios insertados (esta ejecución): ' + CAST(@TotalInsertados AS NVARCHAR);

PRINT '';
PRINT '=== RESUMEN MENSUAL - Salarios Diarios (id_empresa=' + CAST(@IdEmpresa AS NVARCHAR) + ') ===';
SELECT
    FORMAT(fecha, 'yyyy-MM') AS Mes,
    COUNT(*)                 AS Registros,
    SUM(CASE WHEN cargo='CONDUCTOR'  THEN 1 ELSE 0 END) AS Conductores,
    SUM(CASE WHEN cargo='COBRADOR'   THEN 1 ELSE 0 END) AS Cobradores,
    SUM(CASE WHEN cargo='SUPERVISOR' THEN 1 ELSE 0 END) AS Supervisores,
    SUM(CASE WHEN cargo='MECANICO'   THEN 1 ELSE 0 END) AS Mecanicos,
    SUM(monto)               AS Costo_Total_HNL
FROM dbo.salarios_diarios
WHERE id_empresa = @IdEmpresa AND eliminado = 0
  AND fecha >= @FechaInicio AND fecha <= @Hoy
GROUP BY FORMAT(fecha, 'yyyy-MM')
ORDER BY Mes;
