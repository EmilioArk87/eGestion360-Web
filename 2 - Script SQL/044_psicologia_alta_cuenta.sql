-- ============================================================
-- Script   : 044_psicologia_alta_cuenta.sql
-- Proposito: Modulo de Psicologia, paso 3 (alta de la cuenta). Plan "Plan modulo Psicologia" (2026-11-15), decision D1:
--            la empresa "Centro de Atencion Psicologica y Avance Educativo" trabaja en una CUENTA (tenant) NUEVA.
--              * Crea el plan 'psicologia' (saas.planes) con su version 1, vigente desde 2026-11-15, y sus derechos:
--                  modulo.psicologia                    acceso al modulo
--                  limite.empresas  = 1                 tope PROVISIONAL (como los topes del 040)
--                  limite.usuarios  = 10                tope PROVISIONAL
--              * Crea la cuenta CAPAE en saas.tenants (tipo cliente, estado activo; modo de suspension por defecto).
--              * Le asigna la version 1 del plan 'psicologia' (saas.tenant_planes).
--            No crea la empresa, ni roles, ni usuarios: se dan de alta desde la aplicacion (Empresas > Nueva con la cuenta CAPAE,
--            Administracion > Modulos por empresa, Roles y permisos, Invitaciones), como cualquier otra cuenta.
--            Solo toca el plano de control (esquema saas, sin RLS): ninguna otra cuenta ni ningun otro plan cambia.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-15
-- BD       : eBD_SPD
-- Requiere : 025, 040 y 043 aplicados (saas.tenants con modo_suspension, planes y derechos, derecho modulo.psicologia).
-- Rollback : ver la seccion ROLLBACK al final (las claves foraneas lo detienen si la cuenta ya tiene empresas o membresias).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos o los derechos modulo.psicologia, limite.empresas o limite.usuarios;
--        b) la cuenta CAPAE existe con otro tipo, cancelada o con un plan vigente distinto de 'psicologia';
--        c) otra cuenta ya se llama igual (evita duplicarla con otro codigo);
--        d) el plan 'psicologia' existe sin su version 1 o con derechos distintos de los esperados (alguien lo edito).
--   2. CAMBIO (una sola transaccion): plan, version, derechos de la version, cuenta y asignacion. Solo inserta lo que falta.
--   3. POSTCHECK dentro de la misma transaccion: la cuenta activa con un solo plan vigente (psicologia v1), la version con sus
--      3 derechos y sus topes, y las asignaciones de las demas cuentas intactas. Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * ASCII puro: los textos con tilde se escriben con NCHAR(n).
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_044') IS NOT NULL DROP TABLE #precheck_044;
IF OBJECT_ID(N'tempdb..#derechos_044') IS NOT NULL DROP TABLE #derechos_044;

-- Derechos de la version 1 del plan psicologia (valor_limite NULL en el modulo).
CREATE TABLE #derechos_044 (
    codigo_derecho  VARCHAR(60) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    valor_limite    INT         NULL
);
INSERT INTO #derechos_044 (codigo_derecho, valor_limite) VALUES
    ('modulo.psicologia', NULL),
    ('limite.empresas',   1),
    ('limite.usuarios',   10);

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @nombreCuenta NVARCHAR(200) = N'Centro de Atenci' + NCHAR(243) + N'n Psicol' + NCHAR(243) + N'gica y Avance Educativo';
DECLARE @idTenant INT, @idPlan INT, @idVersion INT;

-- a) scripts previos
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
ELSE IF COL_LENGTH(N'saas.tenants', N'modo_suspension') IS NULL SET @errores += N' Falta saas.tenants.modo_suspension (script 040).';
IF OBJECT_ID(N'saas.planes', N'U') IS NULL SET @errores += N' Falta la tabla saas.planes (script 040).';
IF OBJECT_ID(N'saas.plan_versiones', N'U') IS NULL SET @errores += N' Falta la tabla saas.plan_versiones (script 040).';
IF OBJECT_ID(N'saas.derechos', N'U') IS NULL SET @errores += N' Falta la tabla saas.derechos (script 040).';
IF OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NULL SET @errores += N' Falta la tabla saas.plan_version_derechos (script 040).';
IF OBJECT_ID(N'saas.tenant_planes', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenant_planes (script 040).';

IF LEN(@errores) = 0
BEGIN
    SELECT @errores += N' Falta el derecho ' + d.codigo_derecho + CASE WHEN d.codigo_derecho = 'modulo.psicologia' THEN N' (script 043).' ELSE N' (script 040).' END
    FROM #derechos_044 d
    WHERE NOT EXISTS (SELECT 1 FROM saas.derechos x WHERE x.codigo = d.codigo_derecho);

    -- b) la cuenta, si ya existe, es la esperada
    SELECT @idTenant = id_tenant FROM saas.tenants WHERE codigo = 'CAPAE';
    IF @idTenant IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM saas.tenants WHERE id_tenant = @idTenant AND (tipo <> 'cliente' OR estado = 'cancelado'))
            SET @errores += N' La cuenta CAPAE existe con otro tipo o esta cancelada; revisar a mano.';
        IF EXISTS (SELECT 1 FROM saas.tenant_planes tp
                   JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
                   JOIN saas.planes p ON p.id_plan = v.id_plan
                   WHERE tp.id_tenant = @idTenant AND tp.vigente_hasta IS NULL AND p.codigo <> 'psicologia')
            SET @errores += N' La cuenta CAPAE ya tiene vigente un plan distinto de psicologia; revisar a mano.';
    END

    -- c) ninguna otra cuenta con el mismo nombre
    IF EXISTS (SELECT 1 FROM saas.tenants WHERE nombre = @nombreCuenta AND codigo <> 'CAPAE')
        SET @errores += N' Ya existe otra cuenta con el nombre de la empresa (con otro codigo); revisar a mano.';

    -- d) el plan, si ya existe, tiene su version 1 con exactamente los derechos esperados
    SELECT @idPlan = id_plan FROM saas.planes WHERE codigo = 'psicologia';
    IF @idPlan IS NOT NULL
    BEGIN
        SELECT @idVersion = id_plan_version FROM saas.plan_versiones WHERE id_plan = @idPlan AND numero = 1;
        IF @idVersion IS NULL
            SET @errores += N' El plan psicologia existe sin su version 1; revisar a mano.';
        ELSE
        BEGIN
            SELECT @errores += N' La version 1 del plan psicologia no tiene el derecho ' + d.codigo_derecho + N' con el valor esperado.'
            FROM #derechos_044 d
            WHERE NOT EXISTS (SELECT 1 FROM saas.plan_version_derechos v JOIN saas.derechos x ON x.id_derecho = v.id_derecho
                              WHERE v.id_plan_version = @idVersion AND x.codigo = d.codigo_derecho
                                AND ISNULL(v.valor_limite, -1) = ISNULL(d.valor_limite, -1));
            SELECT @errores += N' La version 1 del plan psicologia tiene un derecho no previsto: ' + x.codigo + N'.'
            FROM saas.plan_version_derechos v JOIN saas.derechos x ON x.id_derecho = v.id_derecho
            WHERE v.id_plan_version = @idVersion
              AND NOT EXISTS (SELECT 1 FROM #derechos_044 d WHERE d.codigo_derecho = x.codigo);
        END
    END
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 044 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50044, @mensaje, 1;
END

CREATE TABLE #precheck_044 (ok BIT NOT NULL);
INSERT INTO #precheck_044 (ok) VALUES (1);

PRINT CONCAT(N'PRECHECK 044 correcto. Cuenta CAPAE ', CASE WHEN @idTenant IS NULL THEN N'por crear' ELSE CONCAT(N'ya existe (id ', @idTenant, N')') END,
             N'; plan psicologia ', CASE WHEN @idPlan IS NULL THEN N'por crear' ELSE N'ya existe' END, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_044') IS NULL
    THROW 50044, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @nombreCuenta NVARCHAR(200) = N'Centro de Atenci' + NCHAR(243) + N'n Psicol' + NCHAR(243) + N'gica y Avance Educativo';
DECLARE @planesNuevos INT = 0, @versionesNuevas INT = 0, @derechosNuevos INT = 0, @cuentasNuevas INT = 0, @asignacionesNuevas INT = 0;
DECLARE @idTenant INT, @idPlan INT, @idVersion INT;
DECLARE @malas INT;
-- Huella de las asignaciones de las demas cuentas, para comprobar que no cambian.
DECLARE @huellaAntes INT = (SELECT CHECKSUM_AGG(CHECKSUM(id_tenant_plan, id_tenant, id_plan_version, vigente_desde, vigente_hasta))
                            FROM saas.tenant_planes
                            WHERE id_tenant NOT IN (SELECT id_tenant FROM saas.tenants WHERE codigo = 'CAPAE'));
DECLARE @asignacionesOtrasAntes INT = (SELECT COUNT(*) FROM saas.tenant_planes
                                       WHERE id_tenant NOT IN (SELECT id_tenant FROM saas.tenants WHERE codigo = 'CAPAE'));

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Plan y version 1
    INSERT INTO saas.planes (codigo, nombre, descripcion, creado_por)
    SELECT 'psicologia', N'Psicolog' + NCHAR(237) + N'a',
           N'Plan del Centro de Atencion Psicologica y Avance Educativo: modulo de psicologia con topes de usuarios y empresas. Topes provisionales.',
           N'script:044'
    WHERE NOT EXISTS (SELECT 1 FROM saas.planes WHERE codigo = 'psicologia');
    SET @planesNuevos = @@ROWCOUNT;
    SELECT @idPlan = id_plan FROM saas.planes WHERE codigo = 'psicologia';

    IF NOT EXISTS (SELECT 1 FROM saas.plan_versiones WHERE id_plan = @idPlan)
    BEGIN
        INSERT INTO saas.plan_versiones (id_plan, numero, vigente_desde, creado_por)
        VALUES (@idPlan, 1, CAST('20261115' AS DATE), N'script:044');
        SET @versionesNuevas = 1;
        SET @idVersion = SCOPE_IDENTITY();

        -- Los derechos solo se siembran en la version recien creada (una version existente no se edita: el PRECHECK la exige igual).
        INSERT INTO saas.plan_version_derechos (id_plan_version, id_derecho, tipo_derecho, valor_limite, creado_por)
        SELECT @idVersion, x.id_derecho, x.tipo, d.valor_limite, N'script:044'
        FROM #derechos_044 d
        JOIN saas.derechos x ON x.codigo = d.codigo_derecho;
        SET @derechosNuevos = @@ROWCOUNT;
    END
    ELSE
        SELECT @idVersion = id_plan_version FROM saas.plan_versiones WHERE id_plan = @idPlan AND numero = 1;

    -- 2. Cuenta (tipo cliente, activa; modo_suspension toma su valor por defecto del 040)
    INSERT INTO saas.tenants (codigo, nombre, tipo, estado, motivo_estado, creado_por)
    SELECT 'CAPAE', @nombreCuenta, 'cliente', 'activo', N'Alta para el modulo de Psicologia (script 044)', N'script:044'
    WHERE NOT EXISTS (SELECT 1 FROM saas.tenants WHERE codigo = 'CAPAE');
    SET @cuentasNuevas = @@ROWCOUNT;
    SELECT @idTenant = id_tenant FROM saas.tenants WHERE codigo = 'CAPAE';

    -- 3. Asignacion del plan, si la cuenta no tiene uno vigente
    INSERT INTO saas.tenant_planes (id_tenant, id_plan_version, vigente_desde, motivo, creado_por)
    SELECT @idTenant, @idVersion, SYSUTCDATETIME(), N'script:044', N'script:044'
    WHERE NOT EXISTS (SELECT 1 FROM saas.tenant_planes WHERE id_tenant = @idTenant AND vigente_hasta IS NULL);
    SET @asignacionesNuevas = @@ROWCOUNT;

    -- 4. POSTCHECK: si algo no cuadra, se revierte todo.
    IF NOT EXISTS (SELECT 1 FROM saas.tenants WHERE id_tenant = @idTenant AND codigo = 'CAPAE' AND nombre = @nombreCuenta
                   AND tipo = 'cliente' AND estado = 'activo')
        THROW 50044, N'POSTCHECK: la cuenta CAPAE no quedo activa con su nombre y tipo cliente. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM saas.tenant_planes WHERE id_tenant = @idTenant AND vigente_hasta IS NULL) <> 1
       OR NOT EXISTS (SELECT 1 FROM saas.tenant_planes WHERE id_tenant = @idTenant AND vigente_hasta IS NULL AND id_plan_version = @idVersion)
        THROW 50044, N'POSTCHECK: la cuenta CAPAE no tiene un solo plan vigente (psicologia v1). Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM #derechos_044 d
    WHERE NOT EXISTS (SELECT 1 FROM saas.plan_version_derechos v JOIN saas.derechos x ON x.id_derecho = v.id_derecho
                      WHERE v.id_plan_version = @idVersion AND x.codigo = d.codigo_derecho
                        AND ISNULL(v.valor_limite, -1) = ISNULL(d.valor_limite, -1));
    IF @malas > 0 OR (SELECT COUNT(*) FROM saas.plan_version_derechos WHERE id_plan_version = @idVersion) <> 3
        THROW 50044, N'POSTCHECK: la version 1 del plan psicologia no tiene exactamente sus 3 derechos. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM saas.tenant_planes WHERE id_tenant <> @idTenant) <> @asignacionesOtrasAntes
       OR ISNULL((SELECT CHECKSUM_AGG(CHECKSUM(id_tenant_plan, id_tenant, id_plan_version, vigente_desde, vigente_hasta))
                  FROM saas.tenant_planes WHERE id_tenant <> @idTenant), 0) <> ISNULL(@huellaAntes, 0)
        THROW 50044, N'POSTCHECK: cambio la asignacion de plan de otra cuenta. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @planesNuevos + @versionesNuevas + @derechosNuevos + @cuentasNuevas + @asignacionesNuevas = 0
        PRINT N'Nada que hacer: el script 044 ya estaba aplicado.';
    ELSE
        PRINT CONCAT(N'CAMBIO 044 aplicado: cuenta CAPAE (id ', @idTenant, N') ', CASE WHEN @cuentasNuevas = 1 THEN N'creada' ELSE N'ya existia' END,
                     N'; plan psicologia ', CASE WHEN @planesNuevos = 1 THEN N'creado' ELSE N'ya existia' END,
                     N' (version 1 con ', (@derechosNuevos), N' derecho(s) nuevos); asignaciones nuevas: ', @asignacionesNuevas,
                     N'. Siguiente paso: crear la empresa desde la aplicacion con la cuenta CAPAE.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resumen (solo lectura; plano de control, sin RLS)
SELECT t.id_tenant, t.codigo, t.nombre, t.tipo, t.estado, t.modo_suspension, p.codigo AS plan_vigente, v.numero AS version_plan,
       (SELECT STRING_AGG(CONCAT(d.codigo, CASE WHEN x.valor_limite IS NULL THEN N'' ELSE CONCAT(N'=', x.valor_limite) END), N', ')
        FROM saas.plan_version_derechos x JOIN saas.derechos d ON d.id_derecho = x.id_derecho
        WHERE x.id_plan_version = v.id_plan_version) AS derechos
FROM saas.tenants t
LEFT JOIN saas.tenant_planes tp ON tp.id_tenant = t.id_tenant AND tp.vigente_hasta IS NULL
LEFT JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version
LEFT JOIN saas.planes p ON p.id_plan = v.id_plan
WHERE t.codigo = 'CAPAE';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Las claves foraneas lo detienen (y se revierte todo) si la cuenta ya tiene empresas, membresias o invitaciones.
--   El plan solo se borra si ninguna otra cuenta lo tiene asignado.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @idTenant INT = (SELECT id_tenant FROM saas.tenants WHERE codigo = 'CAPAE');
DECLARE @idPlan INT = (SELECT id_plan FROM saas.planes WHERE codigo = 'psicologia');

DELETE FROM saas.tenant_planes WHERE id_tenant = @idTenant;
DELETE FROM saas.tenants WHERE id_tenant = @idTenant;

IF NOT EXISTS (SELECT 1 FROM saas.tenant_planes tp JOIN saas.plan_versiones v ON v.id_plan_version = tp.id_plan_version WHERE v.id_plan = @idPlan)
BEGIN
    DELETE x FROM saas.plan_version_derechos x JOIN saas.plan_versiones v ON v.id_plan_version = x.id_plan_version WHERE v.id_plan = @idPlan;
    DELETE FROM saas.plan_versiones WHERE id_plan = @idPlan;
    DELETE FROM saas.planes WHERE id_plan = @idPlan;
END

COMMIT TRANSACTION;
PRINT N'ROLLBACK 044 aplicado: cuenta CAPAE y plan psicologia retirados.';
*/
