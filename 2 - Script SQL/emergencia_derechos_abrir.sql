-- ============================================================
-- Script   : emergencia_derechos_abrir.sql
-- Proposito: EMERGENCIA. Abre los derechos de uso de TODOS los tenants (script 040, F1.7). Usar si un dato mal sembrado o editado (un plan
--            al que le falta un modulo, un tenant suspendido por error, un plan sin asignar) deja a alguien sin acceso a lo que ya usaba.
--            Hace tres cosas, todas sin borrar nada:
--              1. Deja a todos los tenants en estado 'activo' (el motivo anterior queda dentro de motivo_estado).
--              2. Al plan 'interno' (el que lleva todos los modulos y limites sin tope) le AGREGA los derechos que le falten: un derecho
--                 modulo.<codigo> por cada fila de dbo.modulos, limite.usuarios y limite.empresas, y le quita el tope a sus limites.
--              3. Deja a todos los tenants con el plan 'interno' vigente: CIERRA la asignacion vigente de cada uno (vigente_hasta) y abre
--                 una nueva; el historial queda en saas.tenant_planes. Los tenants que ya estaban en 'interno' no cambian.
--            No toca modo_suspension ni datos de dbo. Para volver al reparto normal, reasignar los planes a mano (hay una fila cerrada por
--            cada asignacion anterior) y suspender de nuevo los tenants que correspondan.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-15
-- BD       : eBD_SPD
-- Ejecucion: un solo lote, sin PRECHECK ni dependencias, pensado para correrse de inmediato desde el panel del hosting. Es idempotente:
--            si todo ya esta abierto, no hace nada. Si el script 040 no esta aplicado (no existen los planes), solo hace el paso 1.
--            Por ser una EMERGENCIA no espera la aprobacion previa de /alerta-bd: se avisa a quien corresponda despues, y solo se corre
--            si Emilio lo pide en el chat.
-- ============================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;

DECLARE @ahora DATETIME2 = SYSUTCDATETIME();
DECLARE @abiertos INT = 0, @derechosNuevos INT = 0, @enlacesNuevos INT = 0, @topesQuitados INT = 0, @cerradas INT = 0, @abiertas INT = 0;
DECLARE @idPlan INT, @idVersion INT;
DECLARE @hay040 BIT = CASE WHEN OBJECT_ID(N'saas.planes', N'U') IS NOT NULL AND OBJECT_ID(N'saas.plan_versiones', N'U') IS NOT NULL
                            AND OBJECT_ID(N'saas.derechos', N'U') IS NOT NULL AND OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NOT NULL
                            AND OBJECT_ID(N'saas.tenant_planes', N'U') IS NOT NULL THEN 1 ELSE 0 END;

-- 1. Todos los tenants activos
BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE saas.tenants
    SET estado = 'activo',
        fecha_estado = @ahora,
        motivo_estado = LEFT(CONCAT(N'emergencia_derechos_abrir.sql (estaba ', estado, N'). Motivo anterior: ', COALESCE(motivo_estado, N'-')), 300),
        modificado_por = N'emergencia_derechos_abrir',
        fecha_modificacion = @ahora
    WHERE estado <> 'activo';
    SET @abiertos = @@ROWCOUNT;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- 2 y 3. Plan interno completo y asignado a todos los tenants (solo si el script 040 esta aplicado)
IF @hay040 = 0
    PRINT N'El script 040 no esta aplicado (no existen las tablas de planes): solo se abrio el estado de los tenants.';
ELSE
BEGIN
    SELECT @idPlan = id_plan FROM saas.planes WHERE codigo = 'interno';
    SELECT TOP (1) @idVersion = id_plan_version FROM saas.plan_versiones WHERE id_plan = @idPlan AND vigente_hasta IS NULL ORDER BY numero DESC;

    IF @idPlan IS NULL OR @idVersion IS NULL
        PRINT N'No existe el plan interno con una version abierta: solo se abrio el estado de los tenants. Crear el plan interno a mano.';
    ELSE
    BEGIN
      BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE saas.planes SET activo = 1, modificado_por = N'emergencia_derechos_abrir', fecha_modificacion = @ahora
        WHERE id_plan = @idPlan AND activo = 0;

        -- Derechos que falten (solo se agregan): un modulo.<codigo> por cada modulo y los dos limites.
        INSERT INTO saas.derechos (codigo, tipo, descripcion, creado_por)
        SELECT x.codigo, x.tipo, x.descripcion, N'emergencia_derechos_abrir'
        FROM (SELECT 'modulo.' + CAST(m.codigo AS VARCHAR(50)) AS codigo, 'modulo' AS tipo, N'Acceso al modulo ' + m.codigo AS descripcion
              FROM dbo.modulos m
              UNION ALL SELECT 'limite.usuarios', 'limite', N'Maximo de usuarios activos del tenant (sin valor: sin tope)'
              UNION ALL SELECT 'limite.empresas', 'limite', N'Maximo de empresas del tenant (sin valor: sin tope)') x
        WHERE NOT EXISTS (SELECT 1 FROM saas.derechos d WHERE d.codigo = x.codigo);
        SET @derechosNuevos = @@ROWCOUNT;

        -- Todos los modulos y los dos limites quedan en la version del plan interno (los limites sin tope).
        INSERT INTO saas.plan_version_derechos (id_plan_version, id_derecho, tipo_derecho, valor_limite, creado_por)
        SELECT @idVersion, d.id_derecho, d.tipo, NULL, N'emergencia_derechos_abrir'
        FROM saas.derechos d
        WHERE (d.tipo = 'modulo' OR d.codigo IN ('limite.usuarios', 'limite.empresas'))
          AND NOT EXISTS (SELECT 1 FROM saas.plan_version_derechos l WHERE l.id_plan_version = @idVersion AND l.id_derecho = d.id_derecho);
        SET @enlacesNuevos = @@ROWCOUNT;

        UPDATE saas.plan_version_derechos
        SET valor_limite = NULL, modificado_por = N'emergencia_derechos_abrir', fecha_modificacion = @ahora
        WHERE id_plan_version = @idVersion AND valor_limite IS NOT NULL;
        SET @topesQuitados = @@ROWCOUNT;

        -- Cierra (no borra) la asignacion vigente de los tenants que no estan en el plan interno, y abre la nueva.
        UPDATE saas.tenant_planes
        SET vigente_hasta = @ahora, modificado_por = N'emergencia_derechos_abrir', fecha_modificacion = @ahora
        WHERE vigente_hasta IS NULL AND id_plan_version <> @idVersion;
        SET @cerradas = @@ROWCOUNT;

        INSERT INTO saas.tenant_planes (id_tenant, id_plan_version, vigente_desde, motivo, creado_por)
        SELECT t.id_tenant, @idVersion, @ahora, N'emergencia_derechos_abrir.sql', N'emergencia_derechos_abrir'
        FROM saas.tenants t
        WHERE NOT EXISTS (SELECT 1 FROM saas.tenant_planes tp WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL);
        SET @abiertas = @@ROWCOUNT;

        COMMIT TRANSACTION;
      END TRY
      BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
      END CATCH;
    END
END

IF @abiertos = 0 AND @derechosNuevos = 0 AND @enlacesNuevos = 0 AND @topesQuitados = 0 AND @cerradas = 0 AND @abiertas = 0
    PRINT N'Nada que hacer: todos los tenants ya estaban activos' + CASE WHEN @hay040 = 1 THEN N' y con el plan interno completo.' ELSE N'.' END;
ELSE
    PRINT CONCAT(N'Derechos ABIERTOS: ', @abiertos, N' tenant(s) reactivados; ', @derechosNuevos, N' derecho(s) y ', @enlacesNuevos,
                 N' derecho(s) de version agregados al plan interno; ', @topesQuitados, N' tope(s) quitado(s); ', @cerradas,
                 N' asignacion(es) cerradas y ', @abiertas, N' abiertas con el plan interno. Avisar a Emilio y revisar por que fallo el dato.');

-- (No se nombra modo_suspension: si el script 040 no esta aplicado la columna no existe y el lote no compilaria.)
IF @hay040 = 0
    SELECT t.id_tenant, t.codigo, t.estado FROM saas.tenants t ORDER BY t.id_tenant;
ELSE
    SELECT t.id_tenant, t.codigo, t.estado,
           (SELECT TOP (1) p.codigo FROM saas.tenant_planes tp
            JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
            JOIN saas.planes p ON p.id_plan = v.id_plan
            WHERE tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL) AS codigo_plan
    FROM saas.tenants t
    ORDER BY t.id_tenant;
