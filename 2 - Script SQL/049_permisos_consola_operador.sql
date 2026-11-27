-- ============================================================
-- Script   : 049_permisos_consola_operador.sql
-- Proposito: Paso F1.9a (consola del operador, ADR-032, decisiones D77 a D84). Agrega al catalogo saas.permisos los 6 permisos de
--            plataforma de la consola:
--              * plataforma.suscriptores.ver     (orden 94): la lista de suscriptores con su estado y su plan (F1.9a).
--              * plataforma.suscriptores.crear   (orden 95): dar de alta un suscriptor (F1.9b).
--              * plataforma.suscriptores.estado  (orden 96): suspender, reactivar y cancelar (F1.9a).
--              * plataforma.suscriptores.plan    (orden 97): cambiar el plan (F1.9a).
--              * plataforma.accesos.ver          (orden 98): las cuentas y accesos de cada suscriptor (F1.9d).
--              * plataforma.accesos.editar       (orden 99): suspender, reactivar y conceder accesos (F1.9d).
--            Son de ambito plataforma: los recibe solo el operador, por la regla del evaluador (todo el catalogo, D60 y D66). Ningun
--            rol de empresa puede tenerlos. No toca dbo.rol_permisos ni las plantillas: ningun rol cambia.
--            Van los 6 en un solo script para no repetir la alerta de BD en cada entrega; los de F1.9b y F1.9d no abren nada hasta que
--            sus pantallas existan.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-26
-- BD       : eBD_SPD
-- Requiere : 041 aplicado (saas.permisos) y 046 aplicado (ordenes 92 y 93).
-- Rollback : ver la seccion ROLLBACK al final.
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos;
--        b) uno de los 6 permisos ya existe con otro modulo, ambito o casilla legada;
--        c) un orden del 94 al 99 lo usa otro permiso.
--   2. CAMBIO (una sola transaccion): inserta los permisos que falten.
--   3. POSTCHECK dentro de la misma transaccion: los 6 permisos activos, de plataforma y sin casilla legada, y solo esos 6 de los
--      recursos suscriptores y accesos. Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * ASCII puro.
--   * Solo toca el catalogo saas.permisos (plano de control, sin RLS): no depende del estado de la politica.
--   * Se aplica ANTES de publicar el codigo de F1.9a: sin estas filas las pantallas nuevas no las abre nadie (el evaluador lee el
--     catalogo de la base). Al reves no pasa nada: el codigo anterior ignora los permisos nuevos y la pantalla de roles no ofrece
--     permisos de plataforma.
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
IF OBJECT_ID(N'tempdb..#precheck_049') IS NOT NULL DROP TABLE #precheck_049;
IF OBJECT_ID(N'tempdb..#catalogo_049') IS NOT NULL DROP TABLE #catalogo_049;

-- Los permisos de la consola (fuente unica: Services/Permisos/CatalogoDePermisos.cs; una prueba compara los dos).
-- El orden sigue al del script 046 (93 permisos).
CREATE TABLE #catalogo_049 (
    orden          INT           NOT NULL PRIMARY KEY,
    modulo         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    recurso        VARCHAR(40)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    ambito         VARCHAR(12)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion_legada  VARCHAR(10)   COLLATE DATABASE_DEFAULT NULL,
    descripcion    NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL
);
INSERT INTO #catalogo_049 (orden, modulo, recurso, accion, ambito, accion_legada, descripcion) VALUES
    (94, 'plataforma', 'suscriptores', 'ver', 'plataforma', NULL, N'Ver los suscriptores de la plataforma, su estado y su plan'),
    (95, 'plataforma', 'suscriptores', 'crear', 'plataforma', NULL, N'Dar de alta un suscriptor'),
    (96, 'plataforma', 'suscriptores', 'estado', 'plataforma', NULL, N'Suspender, reactivar y cancelar un suscriptor'),
    (97, 'plataforma', 'suscriptores', 'plan', 'plataforma', NULL, N'Cambiar el plan de un suscriptor'),
    (98, 'plataforma', 'accesos', 'ver', 'plataforma', NULL, N'Ver las cuentas y los accesos de cada suscriptor'),
    (99, 'plataforma', 'accesos', 'editar', 'plataforma', NULL, N'Suspender, reactivar y conceder accesos a un suscriptor');

DECLARE @errores NVARCHAR(MAX) = N'';

-- a) scripts previos
IF OBJECT_ID(N'saas.permisos', N'U') IS NULL SET @errores += N' Falta la tabla saas.permisos (script 041).';

IF LEN(@errores) = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM saas.permisos WHERE codigo = 'plataforma.auditoria.ver' AND orden = 93)
        SET @errores += N' Falta el permiso plataforma.auditoria.ver con orden 93 (script 046).';

    -- b) y c) lo que ya exista coincide con lo esperado
    SELECT @errores += N' El permiso ' + p.codigo + N' existe con otro modulo, ambito o casilla legada.'
    FROM saas.permisos p
    JOIN #catalogo_049 c ON p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion
    WHERE p.modulo <> c.modulo OR p.ambito <> c.ambito OR p.accion_legada IS NOT NULL;

    SELECT @errores += N' El orden ' + CAST(c.orden AS NVARCHAR(10)) + N' ya lo usa el permiso ' + p.codigo + N'.'
    FROM saas.permisos p
    JOIN #catalogo_049 c ON p.orden = c.orden
    WHERE p.codigo <> c.modulo + '.' + c.recurso + '.' + c.accion;
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 049 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50049, @mensaje, 1;
END

CREATE TABLE #precheck_049 (ok BIT NOT NULL);
INSERT INTO #precheck_049 (ok) VALUES (1);

DECLARE @existen INT = (SELECT COUNT(*) FROM saas.permisos p JOIN #catalogo_049 c ON p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion); DECLARE @totalPermisos INT = (SELECT COUNT(*) FROM saas.permisos);
PRINT CONCAT(N'PRECHECK 049 correcto. Permisos de la consola ya en el catalogo: ', @existen, N' de 6; permisos en total: ',
             @totalPermisos, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_049') IS NULL
    THROW 50049, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @permisosNuevos INT = 0;
DECLARE @malas INT;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Permisos: solo se inserta lo que falta; no se toca nada que el operador ya haya editado.
    INSERT INTO saas.permisos (codigo, modulo, recurso, accion, ambito, accion_legada, descripcion, orden, creado_por)
    SELECT c.modulo + '.' + c.recurso + '.' + c.accion, c.modulo, c.recurso, c.accion, c.ambito, c.accion_legada, c.descripcion, c.orden, N'script:049'
    FROM #catalogo_049 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion)
    ORDER BY c.orden;
    SET @permisosNuevos = @@ROWCOUNT;

    -- 2. POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*)
    FROM #catalogo_049 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p
                      WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion AND p.modulo = c.modulo AND p.recurso = c.recurso
                        AND p.accion = c.accion AND p.ambito = c.ambito AND p.accion_legada IS NULL AND p.activo = 1);
    IF @malas > 0
        THROW 50049, N'POSTCHECK: falta alguno de los 6 permisos de la consola, o quedo inactivo o con otro ambito. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM saas.permisos WHERE recurso IN ('suscriptores', 'accesos')) <> 6
        THROW 50049, N'POSTCHECK: el catalogo no tiene exactamente 6 permisos de suscriptores y accesos. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @permisosNuevos = 0
        PRINT N'Nada que hacer: el script 049 ya estaba aplicado.';
    ELSE
        PRINT CONCAT(N'CAMBIO 049 aplicado: ', @permisosNuevos, N' permiso(s) de la consola del operador. Ningun rol ni ninguna plantilla cambio.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resumen (solo lectura)
SELECT p.id_permiso, p.codigo, p.ambito, p.orden, p.activo, p.creado_por,
       (SELECT COUNT(*) FROM saas.permisos) AS permisos_en_total
FROM saas.permisos p
WHERE p.recurso IN ('suscriptores', 'accesos')
ORDER BY p.orden;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita los 6 permisos. Publicar antes el codigo anterior a F1.9a: sin los permisos, las pantallas de la consola no las abre nadie.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM saas.permisos
WHERE codigo IN ('plataforma.suscriptores.ver', 'plataforma.suscriptores.crear', 'plataforma.suscriptores.estado',
                 'plataforma.suscriptores.plan', 'plataforma.accesos.ver', 'plataforma.accesos.editar');

COMMIT TRANSACTION;
PRINT N'ROLLBACK 049 aplicado: permisos de la consola retirados.';
*/
