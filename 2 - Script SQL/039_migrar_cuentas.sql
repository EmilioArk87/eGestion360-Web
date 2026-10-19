-- ============================================================
-- Script   : 039_migrar_cuentas.sql
-- Proposito: Paso F1.5 del plan de la Fase 1 (ADR-003, ADR-019 D19/D23/D24, D44/D45): copia las 8 filas de dbo.Users a la identidad global
--            (saas.cuentas, saas.cuenta_alias, saas.membresias y dbo.accesos_empresa, creadas por el script 038) con reglas EXPLICITAS
--            por nombre de usuario. NO modifica ni borra dbo.Users (se retira en F1.6). Los hashes BCrypt se copian tal cual: el script
--            nunca imprime ni registra un hash ni un correo.
--            Reglas (Username -> Id; EmpresaId; EmpresaRolId; PersonaId):
--              admin 1; -; -; 102          \
--              egaray 4; 1; 2; 102          > D45: la misma persona (Emilio) -> UNA sola cuenta (correo y clave de admin, operador de la
--              egaray.transgar 10; 4; 5; 104 /  plataforma, estado activa). Entran como alias 'egaray' y 'egaray.transgar' (tipo usuario, pueden
--                                              iniciar sesion) y sus correos como alias tipo correo que NO inician sesion. Membresia activa
--                                              en SIP (persona 102; acceso a la empresa 1 con el rol 2) y en TRANSGAR (persona 104; acceso a
--                                              la empresa 4 con el rol 5).
--              cliente_demo 2; 2; 1; 103    cuenta propia activa; membresia activa en DEMO (persona 103); acceso a la empresa 2, rol 1.
--              nnolasco 5, Moran23113 6     D24: sin persona ni empresa -> cuenta INACTIVA, sin membresia.
--              transgar.admin 7, transgar.flota 8
--                                           D24: sin persona -> cuenta INACTIVA, membresia SUSPENDIDA en TRANSGAR (sin persona) y su acceso
--                                           a la empresa 4 (roles 5 y 6) guardado INACTIVO, para que el operador pueda reactivarlo.
--            Los tenants se buscan por saas.tenants.codigo (SIP, DEMO, TRANSGAR), nunca por id fijo.
--            Cifras (el PRECHECK compara con dbo.Users y el POSTCHECK con lo copiado):
--              8 usuarios de origen -> 6 cuentas (2 activas: Emilio y cliente_demo; 4 inactivas; 1 operador de la plataforma)
--              4 alias (2 de usuario que inician sesion + 2 de correo que no)
--              5 membresias (3 activas: Emilio en SIP y en TRANSGAR, cliente_demo en DEMO; 2 suspendidas)
--              5 accesos por empresa (3 activos + 2 inactivos): SIP 1, DEMO 1, TRANSGAR 3
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-15
-- BD       : eBD_SPD
-- Requiere : 038 aplicado (y por tanto 025 a 037). La politica seg.pol_tenant puede estar ENCENDIDA o APAGADA: como las personas, los
--            roles y los accesos son tablas de tenant, el script fija el contexto de sesion id_tenant de cada tenant (sin solo lectura)
--            solo mientras lee o escribe sus filas y lo restaura al terminar, tambien si falla. Aplicar con el sitio en mantenimiento y
--            justo antes de publicar la version de F1.5: las claves cambiadas o los usuarios creados en dbo.Users despues de este script
--            no pasan a las cuentas.
-- Rollback : ver la seccion ROLLBACK al final. Solo vale mientras la aplicacion de F1.5 no haya escrito en las cuentas.
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta si:
--        a) falta el 038 (las 9 tablas y los 5 predicados de accesos_empresa);
--        b) dbo.Users no tiene EXACTAMENTE las 8 filas esperadas (Id, Username, EmpresaId, EmpresaRolId, PersonaId, Role), o hay otras;
--        c) una clave no es un hash BCrypt de 60 caracteres, un correo esta vacio o repetido, o un usuario de cuenta pasa de 50 caracteres;
--        d) SIP, DEMO o TRANSGAR no existen activos, o una empresa, persona o rol de las reglas no es del tenant esperado;
--        e) la migracion esta a medias (hay entre 1 y 5 de las 6 cuentas) o un correo, usuario o alias ya lo usa otra cuenta.
--   2. CAMBIO (una sola transaccion): inserta solo lo que falta (cuentas, alias, membresias y accesos) y deja el contexto como estaba.
--   3. POSTCHECK dentro de la misma transaccion: las cifras de arriba, las claves copiadas sin cambio, cada rol de acceso de la empresa de
--      su acceso y dbo.Users intacta (mismas filas y misma suma de control). Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta migrado, solo comprueba que esta completo, lo dice y no cambia nada (no pisa claves ni estados que
--     la aplicacion haya cambiado despues).
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
IF OBJECT_ID(N'tempdb..#precheck_039') IS NOT NULL DROP TABLE #precheck_039;
IF OBJECT_ID(N'tempdb..#users_039') IS NOT NULL DROP TABLE #users_039;
IF OBJECT_ID(N'tempdb..#cuentas_039') IS NOT NULL DROP TABLE #cuentas_039;
IF OBJECT_ID(N'tempdb..#alias_039') IS NOT NULL DROP TABLE #alias_039;
IF OBJECT_ID(N'tempdb..#membresias_039') IS NOT NULL DROP TABLE #membresias_039;
IF OBJECT_ID(N'tempdb..#accesos_039') IS NOT NULL DROP TABLE #accesos_039;
IF OBJECT_ID(N'tempdb..#tenants_039') IS NOT NULL DROP TABLE #tenants_039;

-- Los 8 usuarios que tiene que haber hoy en dbo.Users (medidos en produccion el 2026-10-15).
CREATE TABLE #users_039 (
    id               INT           NOT NULL PRIMARY KEY,
    username         NVARCHAR(100) COLLATE DATABASE_DEFAULT NOT NULL,
    id_empresa       INT           NULL,
    id_rol           INT           NULL,
    id_persona       INT           NULL,
    rol              NVARCHAR(40)  COLLATE DATABASE_DEFAULT NOT NULL,
    activo_requerido BIT           NULL
);
INSERT INTO #users_039 (id, username, id_empresa, id_rol, id_persona, rol, activo_requerido) VALUES
    (1,  N'admin',           NULL, NULL, 102,  N'admin',        1),
    (2,  N'cliente_demo',    2,    1,    103,  N'empresa_user', 1),
    (4,  N'egaray',          1,    2,    102,  N'empresa_user', 1),
    (5,  N'nnolasco',        NULL, NULL, NULL, N'empresa_user', NULL),
    (6,  N'Moran23113',      NULL, NULL, NULL, N'empresa_user', NULL),
    (7,  N'transgar.admin',  4,    5,    NULL, N'empresa_user', NULL),
    (8,  N'transgar.flota',  4,    6,    NULL, N'empresa_user', NULL),
    (10, N'egaray.transgar', 4,    5,    104,  N'empresa_user', 1);

-- Cuentas por crear: el usuario de origen (correo, clave y nombre de usuario salen de su fila de Users).
CREATE TABLE #cuentas_039 (
    id_user     INT         NOT NULL PRIMARY KEY,
    estado      VARCHAR(12) COLLATE DATABASE_DEFAULT NOT NULL,
    es_operador BIT         NOT NULL
);
INSERT INTO #cuentas_039 (id_user, estado, es_operador) VALUES
    (1, 'activa', 1), (2, 'activa', 0), (5, 'inactiva', 0), (6, 'inactiva', 0), (7, 'inactiva', 0), (8, 'inactiva', 0);

-- Alias de la cuenta de Emilio: el nombre de usuario (inicia sesion) y el correo (no inicia) de egaray y de egaray.transgar.
CREATE TABLE #alias_039 (
    id_user_alias  INT           NOT NULL,
    id_user_cuenta INT           NOT NULL,
    tipo           VARCHAR(10)   COLLATE DATABASE_DEFAULT NOT NULL,
    puede          BIT           NOT NULL,
    alias          NVARCHAR(256) COLLATE DATABASE_DEFAULT NULL,
    PRIMARY KEY (id_user_alias, tipo)
);
INSERT INTO #alias_039 (id_user_alias, id_user_cuenta, tipo, puede) VALUES
    (4, 1, 'usuario', 1), (4, 1, 'correo', 0), (10, 1, 'usuario', 1), (10, 1, 'correo', 0);

-- Membresias: cuenta (por el usuario de origen) y tenant (por su codigo).
CREATE TABLE #membresias_039 (
    id_user_cuenta INT         NOT NULL,
    codigo_tenant  VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL,
    id_persona     INT         NULL,
    estado         VARCHAR(12) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (id_user_cuenta, codigo_tenant)
);
INSERT INTO #membresias_039 (id_user_cuenta, codigo_tenant, id_persona, estado) VALUES
    (1, 'SIP',      102,  'activa'),
    (1, 'TRANSGAR', 104,  'activa'),
    (2, 'DEMO',     103,  'activa'),
    (7, 'TRANSGAR', NULL, 'suspendida'),
    (8, 'TRANSGAR', NULL, 'suspendida');

-- Accesos por empresa: la empresa, el rol y el rol de texto salen de la fila de Users del usuario de origen.
CREATE TABLE #accesos_039 (
    id_user_origen INT         NOT NULL PRIMARY KEY,
    id_user_cuenta INT         NOT NULL,
    codigo_tenant  VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL,
    activo         BIT         NOT NULL
);
INSERT INTO #accesos_039 (id_user_origen, id_user_cuenta, codigo_tenant, activo) VALUES
    (4, 1, 'SIP', 1), (10, 1, 'TRANSGAR', 1), (2, 2, 'DEMO', 1), (7, 7, 'TRANSGAR', 0), (8, 8, 'TRANSGAR', 0);

CREATE TABLE #tenants_039 (codigo VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY, id_tenant INT NOT NULL);

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT, @yaMigradas INT;
DECLARE @t INT, @cod VARCHAR(30);

-- a) el 038 esta aplicado; sin el no se puede seguir leyendo
SELECT @n = COUNT(*) FROM (VALUES (N'saas.cuentas'), (N'saas.cuenta_alias'), (N'saas.membresias'), (N'saas.cuenta_intentos'), (N'saas.cuenta_mfa'),
                                  (N'saas.cuenta_mfa_recuperacion'), (N'saas.invitaciones'), (N'saas.llaves_proteccion_datos'), (N'dbo.accesos_empresa')) v (t)
WHERE OBJECT_ID(v.t, N'U') IS NOT NULL;
IF @n <> 9
    THROW 50039, N'PRECHECK 039 no paso, no se cambio nada: faltan tablas del script 038 (se esperaban 9 y hay una cifra menor). Aplique primero el 038.', 1;

SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa');
IF @n <> 5
    THROW 50039, N'PRECHECK 039 no paso, no se cambio nada: dbo.accesos_empresa no esta en la politica seg.pol_tenant (script 038).', 1;

-- b) dbo.Users: exactamente las 8 filas esperadas, con sus caracteristicas
SELECT @n = COUNT(*) FROM dbo.Users;
IF @n <> 8 SET @errores += CONCAT(N' dbo.Users tiene ', @n, N' filas y el script espera exactamente 8.');

SELECT @errores += N' El usuario ' + x.username + N' (Id ' + CAST(x.id AS NVARCHAR(10)) + N') no existe con ese nombre, empresa, rol, persona, rol de texto o estado.'
FROM #users_039 x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Users u
                  WHERE u.Id = x.id
                    AND u.Username COLLATE Latin1_General_BIN2 = x.username COLLATE Latin1_General_BIN2
                    AND ISNULL(u.EmpresaId, -1) = ISNULL(x.id_empresa, -1)
                    AND ISNULL(u.EmpresaRolId, -1) = ISNULL(x.id_rol, -1)
                    AND ISNULL(u.PersonaId, -1) = ISNULL(x.id_persona, -1)
                    AND u.Role COLLATE Latin1_General_BIN2 = x.rol COLLATE Latin1_General_BIN2
                    AND (x.activo_requerido IS NULL OR u.IsActive = x.activo_requerido));

SELECT @errores += N' Hay un usuario sin regla en este script: Id ' + CAST(u.Id AS NVARCHAR(10)) + N' (' + u.Username + N').'
FROM dbo.Users u WHERE u.Id NOT IN (SELECT id FROM #users_039);

-- c) claves, correos y nombres de usuario
SELECT @errores += N' La clave del usuario ' + u.Username + N' no es un hash BCrypt de 60 caracteres.'
FROM dbo.Users u
WHERE u.Id IN (SELECT id FROM #users_039) AND NOT (LEN(u.Password) = 60 AND u.Password LIKE N'$2_$__$%');

SELECT @errores += N' El usuario ' + u.Username + N' no tiene un correo valido (vacio o de mas de 256 caracteres).'
FROM dbo.Users u
WHERE u.Id IN (SELECT id FROM #users_039) AND (LEN(LTRIM(RTRIM(u.Email))) = 0 OR LEN(u.Email) > 256);

SELECT @errores += N' El correo de ' + STRING_AGG(u.Username, N', ') + N' esta repetido (sin distinguir mayusculas).'
FROM dbo.Users u WHERE u.Id IN (SELECT id FROM #users_039)
GROUP BY u.Email HAVING COUNT(*) > 1;

SELECT @errores += N' El nombre de usuario ' + u.Username + N' pasa de 50 caracteres o esta vacio.'
FROM dbo.Users u
WHERE u.Id IN (SELECT id_user FROM #cuentas_039) AND (LEN(u.Username) > 50 OR LEN(LTRIM(RTRIM(u.Username))) = 0);

-- d) los tenants de las reglas existen y estan activos
INSERT INTO #tenants_039 (codigo, id_tenant)
SELECT t.codigo, t.id_tenant FROM saas.tenants t WHERE t.codigo IN (SELECT DISTINCT codigo_tenant FROM #membresias_039) AND t.estado = N'activo';
SELECT @n = COUNT(*) FROM #tenants_039;
IF @n <> 3 SET @errores += CONCAT(N' Se esperaban los 3 tenants SIP, DEMO y TRANSGAR activos en saas.tenants y hay ', @n, N'.');

-- Las empresas, personas y roles son de tablas de tenant: se leen con el contexto de cada tenant (la RLS puede estar encendida).
DECLARE @ctxOriginal SQL_VARIANT = SESSION_CONTEXT(N'id_tenant');
DECLARE @contextoSePuedeFijar BIT = 1;
BEGIN TRY
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;
END TRY
BEGIN CATCH
    SET @contextoSePuedeFijar = 0;
    SET @errores += N' El contexto de sesion id_tenant de esta conexion es de solo lectura: ejecute el script con una conexion de administracion.';
END CATCH

IF @contextoSePuedeFijar = 1
BEGIN
    SET @t = 0;
    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @t = id_tenant, @cod = codigo FROM #tenants_039 WHERE id_tenant > @t ORDER BY id_tenant;
        IF @@ROWCOUNT = 0 BREAK;

        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;

        SELECT @errores += N' La persona ' + CAST(m.id_persona AS NVARCHAR(10)) + N' no existe, esta eliminada o no es del tenant ' + m.codigo_tenant + N'.'
        FROM #membresias_039 m
        WHERE m.codigo_tenant = @cod AND m.id_persona IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.personas p WHERE p.id_persona = m.id_persona AND p.id_tenant = @t AND p.eliminado = 0);

        SELECT @errores += N' La empresa ' + CAST(u.EmpresaId AS NVARCHAR(10)) + N' (usuario ' + u.Username + N') no es del tenant ' + a.codigo_tenant + N' o esta eliminada.'
        FROM #accesos_039 a JOIN dbo.Users u ON u.Id = a.id_user_origen
        WHERE a.codigo_tenant = @cod
          AND NOT EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.id_empresa = u.EmpresaId AND e.id_tenant = @t AND e.eliminado = 0);

        SELECT @errores += N' El rol ' + CAST(u.EmpresaRolId AS NVARCHAR(10)) + N' (usuario ' + u.Username + N') no existe en la empresa ' + CAST(u.EmpresaId AS NVARCHAR(10)) + N' del tenant ' + a.codigo_tenant + N'.'
        FROM #accesos_039 a JOIN dbo.Users u ON u.Id = a.id_user_origen
        WHERE a.codigo_tenant = @cod
          AND NOT EXISTS (SELECT 1 FROM dbo.empresa_roles r WHERE r.id_rol = u.EmpresaRolId AND r.id_empresa = u.EmpresaId AND r.id_tenant = @t);
    END

    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;
END

-- e) migracion a medias o choques con cuentas que no son estas
UPDATE a SET a.alias = CASE a.tipo WHEN 'usuario' THEN u.Username ELSE u.Email END
FROM #alias_039 a JOIN dbo.Users u ON u.Id = a.id_user_alias;

SELECT @yaMigradas = COUNT(*) FROM saas.cuentas c JOIN #cuentas_039 r ON r.id_user = c.id_user_legado;
IF @yaMigradas NOT IN (0, 6)
    SET @errores += CONCAT(N' La migracion esta a medias: hay ', @yaMigradas, N' de las 6 cuentas. Revise antes de seguir.');

SELECT @errores += N' El correo o el nombre de usuario de ' + u.Username + N' ya lo usa otra cuenta.'
FROM #cuentas_039 r JOIN dbo.Users u ON u.Id = r.id_user
WHERE NOT EXISTS (SELECT 1 FROM saas.cuentas c WHERE c.id_user_legado = r.id_user)
  AND (EXISTS (SELECT 1 FROM saas.cuentas c WHERE c.correo = u.Email OR c.nombre_usuario = u.Username)
       OR EXISTS (SELECT 1 FROM saas.cuenta_alias x WHERE x.alias = u.Email OR x.alias = u.Username));

SELECT @errores += N' El alias ' + CASE a.tipo WHEN 'usuario' THEN N'de usuario ' ELSE N'de correo ' END + N'del usuario Id ' + CAST(a.id_user_alias AS NVARCHAR(10)) + N' ya lo usa otra cuenta.'
FROM #alias_039 a
WHERE NOT EXISTS (SELECT 1 FROM saas.cuenta_alias x JOIN saas.cuentas c ON c.id_cuenta = x.id_cuenta
                  WHERE x.alias = a.alias AND c.id_user_legado = a.id_user_cuenta)
  AND (EXISTS (SELECT 1 FROM saas.cuenta_alias x WHERE x.alias = a.alias)
       OR EXISTS (SELECT 1 FROM saas.cuentas c WHERE (c.correo = a.alias OR c.nombre_usuario = a.alias) AND ISNULL(c.id_user_legado, -1) <> a.id_user_cuenta));

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 039 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50039, @mensaje, 1;
END

CREATE TABLE #precheck_039 (ok BIT NOT NULL, users_filas INT NOT NULL, users_suma INT NULL, ya_migradas INT NOT NULL);
INSERT INTO #precheck_039 (ok, users_filas, users_suma, ya_migradas)
SELECT 1, COUNT(*), CHECKSUM_AGG(CHECKSUM(*)), @yaMigradas FROM dbo.Users;

PRINT CONCAT(N'PRECHECK 039 correcto: 8 usuarios de origen verificados; cuentas ya migradas: ', @yaMigradas, N' de 6.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_039') IS NULL
    THROW 50039, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @ctxOriginal SQL_VARIANT = SESSION_CONTEXT(N'id_tenant');
DECLARE @cuentasNuevas INT = 0, @aliasNuevos INT = 0, @membresiasNuevas INT = 0, @accesosNuevos INT = 0, @n INT;
DECLARE @malas INT, @msg NVARCHAR(2048);
DECLARE @t INT, @cod VARCHAR(30);
DECLARE @accesosTotales INT = 0, @accesosActivos INT = 0, @accesosRolMalo INT = 0, @accesosSip INT = 0, @accesosDemo INT = 0, @accesosTransgar INT = 0;
DECLARE @usersFilas INT, @usersSuma INT;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Cuentas (6): correo, clave y nombre de usuario de la fila de Users; el resto, de la regla.
    INSERT INTO saas.cuentas (correo, nombre_usuario, hash_clave, debe_cambiar_clave, estado, es_operador, id_user_legado, creado_por)
    SELECT u.Email, u.Username, u.Password, u.RequirePasswordChange, r.estado, r.es_operador, u.Id, N'script:039'
    FROM #cuentas_039 r JOIN dbo.Users u ON u.Id = r.id_user
    WHERE NOT EXISTS (SELECT 1 FROM saas.cuentas c WHERE c.id_user_legado = u.Id)
    ORDER BY u.Id;
    SET @cuentasNuevas = @@ROWCOUNT;

    -- 2) Alias (4): usuario (inicia sesion) y correo (no inicia) de egaray y de egaray.transgar, colgados de la cuenta de Emilio.
    INSERT INTO saas.cuenta_alias (id_cuenta, alias, tipo, puede_iniciar_sesion, id_user_legado, creado_por)
    SELECT c.id_cuenta, a.alias, a.tipo, a.puede, a.id_user_alias, N'script:039'
    FROM #alias_039 a JOIN saas.cuentas c ON c.id_user_legado = a.id_user_cuenta
    WHERE NOT EXISTS (SELECT 1 FROM saas.cuenta_alias x WHERE x.alias = a.alias)
    ORDER BY a.id_user_alias, a.tipo;
    SET @aliasNuevos = @@ROWCOUNT;

    -- 3) Membresias (5): activas las de Emilio y cliente_demo; suspendidas las de transgar.admin y transgar.flota (D24).
    INSERT INTO saas.membresias (id_cuenta, id_tenant, id_persona, estado, creado_por)
    SELECT c.id_cuenta, t.id_tenant, m.id_persona, m.estado, N'script:039'
    FROM #membresias_039 m
    JOIN saas.cuentas c ON c.id_user_legado = m.id_user_cuenta
    JOIN #tenants_039 t ON t.codigo = m.codigo_tenant
    WHERE NOT EXISTS (SELECT 1 FROM saas.membresias x WHERE x.id_cuenta = c.id_cuenta AND x.id_tenant = t.id_tenant)
    ORDER BY m.id_user_cuenta, t.id_tenant;
    SET @membresiasNuevas = @@ROWCOUNT;

    -- 4) Accesos por empresa (5): tabla de tenant, con RLS. Cada tenant se escribe y se lee con su propio contexto de sesion.
    SET @t = 0;
    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @t = id_tenant, @cod = codigo FROM #tenants_039 WHERE id_tenant > @t ORDER BY id_tenant;
        IF @@ROWCOUNT = 0 BREAK;

        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;

        INSERT INTO dbo.accesos_empresa (id_tenant, id_membresia, id_empresa, id_rol, rol_legado, es_principal, activo, creado_por)
        SELECT m.id_tenant, m.id_membresia, u.EmpresaId, u.EmpresaRolId, u.Role, 1, a.activo, N'script:039'
        FROM #accesos_039 a
        JOIN dbo.Users u ON u.Id = a.id_user_origen
        JOIN saas.cuentas c ON c.id_user_legado = a.id_user_cuenta
        JOIN saas.membresias m ON m.id_cuenta = c.id_cuenta AND m.id_tenant = @t
        WHERE a.codigo_tenant = @cod
          AND NOT EXISTS (SELECT 1 FROM dbo.accesos_empresa x WHERE x.id_membresia = m.id_membresia AND x.id_empresa = u.EmpresaId)
        ORDER BY a.id_user_origen;
        SET @accesosNuevos += @@ROWCOUNT;

        -- Lo que quedo de los migrados en este tenant (total, activos y roles que no son de la empresa del acceso). Se filtra por
        -- id_tenant aunque la politica este encendida: con la politica apagada la base no filtra y las cuentas se contarian de mas.
        SELECT @n = COUNT(*) FROM dbo.accesos_empresa x
        JOIN saas.membresias m ON m.id_membresia = x.id_membresia JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
        WHERE x.id_tenant = @t AND c.id_user_legado IN (SELECT id_user FROM #cuentas_039) AND x.creado_por = N'script:039';
        SET @accesosTotales += @n;
        SET @accesosSip += CASE WHEN @cod = 'SIP' THEN @n ELSE 0 END;
        SET @accesosDemo += CASE WHEN @cod = 'DEMO' THEN @n ELSE 0 END;
        SET @accesosTransgar += CASE WHEN @cod = 'TRANSGAR' THEN @n ELSE 0 END;

        SELECT @n = COUNT(*) FROM dbo.accesos_empresa x
        JOIN saas.membresias m ON m.id_membresia = x.id_membresia JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
        WHERE x.id_tenant = @t AND c.id_user_legado IN (SELECT id_user FROM #cuentas_039) AND x.creado_por = N'script:039' AND x.activo = 1;
        SET @accesosActivos += @n;

        SELECT @n = COUNT(*) FROM dbo.accesos_empresa x
        JOIN saas.membresias m ON m.id_membresia = x.id_membresia JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
        WHERE x.id_tenant = @t AND c.id_user_legado IN (SELECT id_user FROM #cuentas_039) AND x.creado_por = N'script:039'
          AND (x.id_rol IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.empresa_roles r WHERE r.id_tenant = x.id_tenant AND r.id_rol = x.id_rol AND r.id_empresa = x.id_empresa));
        SET @accesosRolMalo += @n;
    END

    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    -- Siempre: esta todo lo migrado (6 cuentas, 4 alias, 5 membresias, 5 accesos).
    SELECT @malas = COUNT(*) FROM saas.cuentas WHERE id_user_legado IN (SELECT id_user FROM #cuentas_039);
    IF @malas <> 6 THROW 50039, N'POSTCHECK: deben quedar 6 cuentas migradas. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM saas.cuenta_alias x JOIN saas.cuentas c ON c.id_cuenta = x.id_cuenta
    WHERE x.alias IN (SELECT alias FROM #alias_039) AND c.id_user_legado IN (SELECT id_user_cuenta FROM #alias_039);
    IF @malas <> 4 THROW 50039, N'POSTCHECK: deben quedar 4 alias en la cuenta de Emilio. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM saas.membresias m JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
    WHERE c.id_user_legado IN (SELECT id_user FROM #cuentas_039);
    IF @malas <> 5 THROW 50039, N'POSTCHECK: deben quedar 5 membresias migradas. Se revierte.', 1;

    IF @accesosTotales <> 5 OR @accesosSip <> 1 OR @accesosDemo <> 1 OR @accesosTransgar <> 3
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: accesos por empresa ', @accesosTotales, N' (SIP ', @accesosSip, N', DEMO ', @accesosDemo, N', TRANSGAR ', @accesosTransgar,
                          N') y se esperaban 5 (SIP 1, DEMO 1, TRANSGAR 3). Se revierte.');
        THROW 50039, @msg, 1;
    END
    IF @accesosRolMalo > 0 THROW 50039, N'POSTCHECK: algun acceso tiene un rol que no es de su empresa. Se revierte.', 1;

    -- Solo si este corrida creo algo: los valores copiados son los de la regla (una segunda corrida no pisa lo que la aplicacion cambie).
    IF @cuentasNuevas + @aliasNuevos + @membresiasNuevas + @accesosNuevos > 0
    BEGIN
        SELECT @malas = COUNT(*) FROM saas.cuentas c JOIN #cuentas_039 r ON r.id_user = c.id_user_legado JOIN dbo.Users u ON u.Id = r.id_user
        WHERE c.estado <> r.estado OR c.es_operador <> r.es_operador
           OR c.hash_clave COLLATE Latin1_General_BIN2 <> u.Password COLLATE Latin1_General_BIN2
           OR c.correo <> u.Email OR c.nombre_usuario COLLATE Latin1_General_BIN2 <> u.Username COLLATE Latin1_General_BIN2
           OR c.debe_cambiar_clave <> u.RequirePasswordChange;
        IF @malas > 0 THROW 50039, N'POSTCHECK: alguna cuenta no quedo con el correo, usuario, clave o estado de la regla. Se revierte.', 1;

        SELECT @malas = COUNT(*) FROM saas.cuentas WHERE id_user_legado IN (SELECT id_user FROM #cuentas_039) AND estado = N'activa';
        IF @malas <> 2 THROW 50039, N'POSTCHECK: deben ser 2 cuentas activas. Se revierte.', 1;
        SELECT @malas = COUNT(*) FROM saas.cuentas WHERE id_user_legado IN (SELECT id_user FROM #cuentas_039) AND es_operador = 1;
        IF @malas <> 1 THROW 50039, N'POSTCHECK: debe ser 1 operador de la plataforma. Se revierte.', 1;

        SELECT @malas = COUNT(*) FROM saas.cuenta_alias x JOIN #alias_039 a ON a.alias = x.alias
        WHERE x.tipo <> a.tipo OR x.puede_iniciar_sesion <> a.puede OR x.id_user_legado <> a.id_user_alias;
        IF @malas > 0 THROW 50039, N'POSTCHECK: algun alias no quedo con el tipo o el permiso de la regla. Se revierte.', 1;

        SELECT @malas = COUNT(*) FROM saas.membresias m JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
        JOIN #cuentas_039 r ON r.id_user = c.id_user_legado
        JOIN #membresias_039 d ON d.id_user_cuenta = r.id_user
        JOIN #tenants_039 t ON t.codigo = d.codigo_tenant AND t.id_tenant = m.id_tenant
        WHERE m.estado <> d.estado OR ISNULL(m.id_persona, -1) <> ISNULL(d.id_persona, -1);
        IF @malas > 0 THROW 50039, N'POSTCHECK: alguna membresia no quedo con el estado o la persona de la regla. Se revierte.', 1;

        SELECT @malas = COUNT(*) FROM saas.membresias m JOIN saas.cuentas c ON c.id_cuenta = m.id_cuenta
        WHERE c.id_user_legado IN (SELECT id_user FROM #cuentas_039) AND m.estado = N'activa';
        IF @malas <> 3 THROW 50039, N'POSTCHECK: deben ser 3 membresias activas. Se revierte.', 1;

        IF @accesosActivos <> 3 THROW 50039, N'POSTCHECK: deben ser 3 accesos activos y 2 inactivos. Se revierte.', 1;
    END

    -- dbo.Users no se toco: las mismas filas y la misma suma de control.
    SELECT @usersFilas = COUNT(*), @usersSuma = CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.Users;
    IF NOT EXISTS (SELECT 1 FROM #precheck_039 WHERE users_filas = @usersFilas AND ISNULL(users_suma, 0) = ISNULL(@usersSuma, 0))
        THROW 50039, N'POSTCHECK: dbo.Users cambio durante el script. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @cuentasNuevas + @aliasNuevos + @membresiasNuevas + @accesosNuevos = 0
        PRINT N'Nada que hacer: las 6 cuentas, 4 alias, 5 membresias y 5 accesos ya estaban migrados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 039 aplicado: ', @cuentasNuevas, N' cuenta(s), ', @aliasNuevos, N' alias, ', @membresiasNuevas, N' membresia(s) y ',
                     @accesosNuevos, N' acceso(s) por empresa nuevos. dbo.Users sin cambios (', @usersFilas, N' filas).');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    -- El contexto de sesion no es transaccional: se devuelve a como estaba aunque el CAMBIO haya fallado.
    BEGIN TRY
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;
    END TRY
    BEGIN CATCH
        SET @msg = NULL;
    END CATCH;
    THROW;
END CATCH;
GO

-- Resultado: las cuentas migradas con su estado y lo que cuelga de cada una (sin claves ni correos)
SELECT c.id_user_legado, c.nombre_usuario, c.estado, c.es_operador,
       (SELECT COUNT(*) FROM saas.cuenta_alias a WHERE a.id_cuenta = c.id_cuenta) AS alias,
       (SELECT COUNT(*) FROM saas.membresias m WHERE m.id_cuenta = c.id_cuenta) AS membresias,
       (SELECT COUNT(*) FROM saas.membresias m WHERE m.id_cuenta = c.id_cuenta AND m.estado = N'activa') AS membresias_activas
FROM saas.cuentas c
WHERE c.id_user_legado IN (SELECT id_user FROM #cuentas_039)
ORDER BY c.id_user_legado;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Borra solo lo que creo este script (creado_por = 'script:039'): accesos, membresias, alias y cuentas, en ese orden. No toca
--   dbo.Users ni las tablas del 038. La guarda aborta si la aplicacion de F1.5 ya uso alguna cuenta (ultimo acceso, cambios, MFA,
--   intentos fallidos, codigos de recuperacion o invitaciones): en ese caso borrarla perderia datos que dbo.Users no tiene.
--   Los accesos son una tabla de tenant: se borran con el contexto de cada tenant (sin contexto la RLS no los alcanza).
-- ------------------------------------------------------------
/*
DECLARE @ctxOriginal SQL_VARIANT = SESSION_CONTEXT(N'id_tenant');
DECLARE @t INT = 0;
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM saas.cuentas c
               WHERE c.creado_por = N'script:039'
                 AND (c.modificado_por IS NOT NULL OR c.fecha_ultimo_acceso IS NOT NULL
                      OR EXISTS (SELECT 1 FROM saas.cuenta_mfa x WHERE x.id_cuenta = c.id_cuenta)
                      OR EXISTS (SELECT 1 FROM saas.cuenta_intentos x WHERE x.id_cuenta = c.id_cuenta)
                      OR EXISTS (SELECT 1 FROM saas.cuenta_mfa_recuperacion x WHERE x.id_cuenta = c.id_cuenta)
                      OR EXISTS (SELECT 1 FROM saas.invitaciones x WHERE x.id_cuenta = c.id_cuenta)))
        THROW 50039, N'ROLLBACK 039 abortado: la aplicacion ya uso o cambio alguna cuenta migrada (se perderian datos).', 1;

    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @t = id_tenant FROM saas.tenants WHERE id_tenant > @t ORDER BY id_tenant;
        IF @@ROWCOUNT = 0 BREAK;
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
        DELETE FROM dbo.accesos_empresa WHERE creado_por = N'script:039';
    END
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;

    DELETE FROM saas.membresias WHERE creado_por = N'script:039';
    DELETE FROM saas.cuenta_alias WHERE creado_por = N'script:039';
    DELETE FROM saas.cuentas WHERE creado_por = N'script:039';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @ctxOriginal;
    THROW;
END CATCH;
*/
