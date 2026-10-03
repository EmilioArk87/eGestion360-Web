-- AVISO (F6, 2026-10-02): ESQUEMA ANTERIOR A LA 019
-- Este script lee o escribe las columnas viejas de dbo.personas (id_empresa, documento,
-- tipo_documento, cargo, tarifa_diaria, moneda_tarifa, fecha_ingreso, fecha_baja).
-- El script 019_personas_retirar_columnas.sql las elimina: DESPUES DE LA 019 ESTE SCRIPT
-- YA NO FUNCIONA. Es historico o de datos de ejemplo; no lo ejecute contra una BD con la 019.
-- Ver "Scripts que asumen el esquema anterior a la 019" en 1 - Documetacion/INDICE_SCRIPTS_SQL.md.
-- ----------------------------------------------------------------------------------------
-- ============================================================
-- Script   : 013_datos_demo_control_salidas.sql
-- Proposito: Datos de demostracion de salidas y entradas de garita para la empresa Demo
--            (14 al 27 de septiembre de 2026), para mostrar Control de Salidas y el KPI.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-27
-- BD       : eBD_SPD
-- Requiere : 012_control_salidas_entradas.sql; empresa 'Demo' con sus vehiculos,
--            conductores (personas con cargo CONDUCTOR) y rutas.
-- Rollback : DELETE FROM dbo.control_salidas WHERE creado_por = 'demo_seed';
-- ============================================================
-- Contenido:
--   * Lunes a sabado: vehiculos urbanos (DEM-*) 2 viajes/dia (35-75 km c/u);
--     vehiculos de carretera (HND-*) 1 viaje/dia (180-360 km).
--   * Domingo 20: 4 vehiculos, 1 viaje.
--   * Domingo 27 (hoy): 3 viajes cerrados por la manana y 3 vehiculos FUERA ahora.
--   * 1 salida ANULADA (registro duplicado) el 22.
-- El odometro arranca en la ultima lectura conocida de cada vehiculo y es continuo:
-- cada salida = entrada anterior. Horas en hora local (igual que la app, DateTime.Now);
-- fecha_creacion en UTC (+6 h), igual que la app.
-- Valores deterministas (CHECKSUM): ejecutar dos veces da los mismos datos.
-- Idempotente: si ya existen filas 'demo_seed' no hace nada.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.control_salidas', 'U') IS NULL
BEGIN
    RAISERROR('Falta dbo.control_salidas. Ejecute 012_control_salidas_entradas.sql antes. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @id_empresa INT = (SELECT id_empresa FROM dbo.empresas WHERE razon_social = 'Demo');
IF @id_empresa IS NULL
BEGIN
    RAISERROR('No existe la empresa Demo. Abortar.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.control_salidas WHERE id_empresa = @id_empresa AND creado_por <> 'demo_seed')
BEGIN
    RAISERROR('La empresa Demo ya tiene movimientos reales en control_salidas; los datos demo romperian la continuidad del odometro. Abortar.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.personas WHERE id_empresa = @id_empresa AND cargo = 'CONDUCTOR' AND activo = 1 AND eliminado = 0)
BEGIN
    RAISERROR('La empresa Demo no tiene conductores activos. Abortar.', 16, 1);
    RETURN;
END;
GO

-- ------------------------------------------------------------
-- CAMBIO: [VERDE / AGREGA] filas demo en dbo.control_salidas
-- ------------------------------------------------------------
DECLARE @id_empresa INT = (SELECT id_empresa FROM dbo.empresas WHERE razon_social = 'Demo');

IF EXISTS (SELECT 1 FROM dbo.control_salidas WHERE creado_por = 'demo_seed')
BEGIN
    PRINT 'Datos demo ya cargados; no se hace nada.';
    RETURN;
END;

BEGIN TRANSACTION;

WITH dias AS (
    SELECT CAST('2026-09-14' AS DATE) AS dia
    UNION ALL
    SELECT DATEADD(DAY, 1, dia) FROM dias WHERE dia < '2026-09-27'
),
veh AS (
    SELECT v.id_vehiculo,
           v.placa,
           v.id_ruta,
           r.nombre AS ruta,
           ROW_NUMBER() OVER (ORDER BY v.placa) AS n,
           CASE WHEN v.placa LIKE 'HND-%' THEN 1 ELSE 0 END AS carretera,
           -- ultima lectura conocida: la mayor entre km_inicial, odometro_diario y control_salidas
           (SELECT MAX(x.km) FROM (VALUES
                (v.km_inicial),
                ((SELECT MAX(o.km_final) FROM dbo.odometro_diario o WHERE o.id_vehiculo = v.id_vehiculo AND o.eliminado = 0)),
                ((SELECT MAX(ISNULL(c.odometro_entrada, c.odometro_salida)) FROM dbo.control_salidas c WHERE c.id_vehiculo = v.id_vehiculo AND c.eliminado = 0))
            ) x(km)) AS km_base
    FROM dbo.vehiculos v
    LEFT JOIN dbo.rutas r ON r.id_ruta = v.id_ruta
    WHERE v.id_empresa = @id_empresa AND v.activo = 1 AND v.eliminado = 0
),
conductores AS (
    SELECT p.id_persona,
           ROW_NUMBER() OVER (ORDER BY p.id_persona) - 1 AS k,
           COUNT(*) OVER () AS total
    FROM dbo.personas p
    WHERE p.id_empresa = @id_empresa AND p.cargo = 'CONDUCTOR' AND p.activo = 1 AND p.eliminado = 0
),
turnos AS (
    SELECT 1 AS t UNION ALL SELECT 2
),
plan_viajes AS (
    SELECT veh.*, d.dia, tu.t,
           DATEDIFF(DAY, '2026-09-14', d.dia) AS nd,
           CASE WHEN d.dia = '2026-09-27' AND veh.n IN (2, 5, 8) THEN 1 ELSE 0 END AS abierto,
           ABS(CHECKSUM(veh.id_vehiculo, d.dia, tu.t) % 1000) AS h1,
           ABS(CHECKSUM(d.dia, tu.t, veh.id_vehiculo, 'x') % 1000) AS h2
    FROM veh
    CROSS JOIN dias d
    CROSS JOIN turnos tu
    WHERE
        -- lunes a sabado (1900-01-01 fue lunes: residuo 6 = domingo)
        (DATEDIFF(DAY, '19000101', d.dia) % 7 <> 6 AND (tu.t = 1 OR veh.carretera = 0))
        -- domingo 20: guardia minima
        OR (d.dia = '2026-09-20' AND tu.t = 1 AND veh.n IN (1, 3, 5, 8))
        -- domingo 27 (hoy): 3 cerrados en la manana + 3 fuera ahora
        -- (los cerrados son urbanos: un viaje de carretera cerraria despues de las 14:00)
        OR (d.dia = '2026-09-27' AND tu.t = 1 AND veh.n IN (1, 2, 3, 4, 5, 8))
),
con_km AS (
    SELECT pv.*,
           CAST(CASE WHEN pv.abierto = 1 THEN 0
                     WHEN pv.carretera = 1 THEN 180 + pv.h1 % 181 + (pv.h2 % 10) / 10.0
                     ELSE 35 + pv.h1 % 41 + (pv.h2 % 10) / 10.0 END AS DECIMAL(12,2)) AS km,
           CASE WHEN pv.abierto = 1
                    THEN DATEADD(MINUTE, 12 * 60 + 40 + pv.h1 % 100, CAST(pv.dia AS DATETIME2))
                WHEN pv.carretera = 1
                    THEN DATEADD(MINUTE, 5 * 60 + 15 + pv.h1 % 45, CAST(pv.dia AS DATETIME2))
                WHEN pv.t = 1
                    THEN DATEADD(MINUTE, 6 * 60 + pv.h1 % 50, CAST(pv.dia AS DATETIME2))
                ELSE DATEADD(MINUTE, 13 * 60 + pv.h1 % 45, CAST(pv.dia AS DATETIME2)) END AS salida,
           CASE WHEN pv.carretera = 1 THEN 540 + pv.h2 % 180
                WHEN pv.t = 1 THEN 240 + pv.h2 % 90
                ELSE 210 + pv.h2 % 100 END AS minutos_fuera
    FROM plan_viajes pv
),
con_odo AS (
    SELECT ck.*,
           ck.km_base + ISNULL(SUM(ck.km) OVER (PARTITION BY ck.id_vehiculo ORDER BY ck.dia, ck.t
                                                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS odo_salida
    FROM con_km ck
)
INSERT INTO dbo.control_salidas (
    id_empresa, id_vehiculo, id_conductor, id_ruta, destino,
    fecha_hora_salida, odometro_salida, observaciones_salida,
    fecha_hora_entrada, odometro_entrada, observaciones_entrada,
    estado, activo, eliminado, creado_por, fecha_creacion, modificado_por, fecha_modificacion)
SELECT
    @id_empresa,
    co.id_vehiculo,
    c.id_persona,
    co.id_ruta,
    CASE WHEN co.carretera = 1
         THEN CHOOSE(1 + co.h2 % 5, N'San Pedro Sula', N'La Ceiba', N'Choluteca', N'Comayagua', N'Puerto Cortés')
         ELSE co.ruta END,
    co.salida,
    co.odo_salida,
    NULL,
    CASE WHEN co.abierto = 1 THEN NULL ELSE DATEADD(MINUTE, co.minutos_fuera, co.salida) END,
    CASE WHEN co.abierto = 1 THEN NULL ELSE co.odo_salida + co.km END,
    CASE WHEN co.abierto = 0 AND co.h1 % 17 = 0 THEN N'Llanta delantera baja, revisar en taller' ELSE NULL END,
    CASE WHEN co.abierto = 1 THEN 'ABIERTO' ELSE 'CERRADO' END,
    1,
    0,
    'demo_seed',
    DATEADD(HOUR, 6, co.salida),
    CASE WHEN co.abierto = 1 THEN NULL ELSE 'demo_seed' END,
    CASE WHEN co.abierto = 1 THEN NULL ELSE DATEADD(HOUR, 6, DATEADD(MINUTE, co.minutos_fuera, co.salida)) END
FROM con_odo co
JOIN conductores c ON c.k = (co.n + co.nd) % c.total
OPTION (MAXRECURSION 30);

-- Una salida ANULADA: registro duplicado de DEM-0044 el martes 22 por la manana
INSERT INTO dbo.control_salidas (
    id_empresa, id_vehiculo, id_conductor, id_ruta, destino,
    fecha_hora_salida, odometro_salida, observaciones_salida,
    estado, activo, eliminado, creado_por, fecha_creacion, modificado_por, fecha_modificacion)
SELECT TOP 1
    c.id_empresa, c.id_vehiculo, c.id_conductor, c.id_ruta, c.destino,
    DATEADD(MINUTE, -3, c.fecha_hora_salida), c.odometro_salida,
    N'Salida registrada dos veces por error; se anula',
    'ANULADO', 1, 0, 'demo_seed',
    DATEADD(HOUR, 6, DATEADD(MINUTE, -3, c.fecha_hora_salida)),
    'demo_seed', DATEADD(HOUR, 6, c.fecha_hora_salida)
FROM dbo.control_salidas c
JOIN dbo.vehiculos v ON v.id_vehiculo = c.id_vehiculo
WHERE c.creado_por = 'demo_seed' AND v.placa = 'DEM-0044'
  AND c.fecha_hora_salida >= '2026-09-22' AND c.fecha_hora_salida < '2026-09-23'
ORDER BY c.fecha_hora_salida;

COMMIT TRANSACTION;
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
-- ------------------------------------------------------------
SELECT estado, COUNT(*) AS viajes, SUM(km_recorridos) AS km,
       MIN(fecha_hora_salida) AS primera_salida, MAX(fecha_hora_salida) AS ultima_salida
FROM dbo.control_salidas
WHERE creado_por = 'demo_seed'
GROUP BY estado;

-- Continuidad del odometro: debe devolver 0 filas
SELECT c.id_control_salida, v.placa, c.odometro_salida,
       LAG(c.odometro_entrada) OVER (PARTITION BY c.id_vehiculo ORDER BY c.fecha_hora_salida) AS entrada_anterior
INTO #continuidad
FROM dbo.control_salidas c
JOIN dbo.vehiculos v ON v.id_vehiculo = c.id_vehiculo
WHERE c.creado_por = 'demo_seed' AND c.estado <> 'ANULADO';

SELECT * FROM #continuidad WHERE entrada_anterior IS NOT NULL AND entrada_anterior <> odometro_salida;
DROP TABLE #continuidad;
GO

-- ------------------------------------------------------------
-- ROLLBACK (ejecutar manualmente en caso de revertir el cambio)
-- ------------------------------------------------------------
/*
DELETE FROM dbo.control_salidas WHERE creado_por = 'demo_seed';
*/
