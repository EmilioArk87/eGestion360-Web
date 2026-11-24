-- ============================================================
-- Script   : 046_permisos_auditoria.sql
-- Proposito: Paso F1.8c (pantallas de auditoria, ADR-030 decision D74). Agrega al catalogo saas.permisos los 2 permisos de solo
--            lectura de las pantallas de auditoria:
--              * administracion.auditoria.ver (ambito empresa, orden 92): "Auditoria" de la empresa (dbo.auditoria_seguridad).
--                Por decision de Emilio del 2026-11-21 la ve SOLO el administrador de la empresa: lo recibe por la regla del
--                evaluador (D60) y la pantalla de roles no lo ofrece para un rol (CatalogoDePermisos.SoloDelAdministrador).
--              * plataforma.auditoria.ver (ambito plataforma, orden 93): "Auditoria de la plataforma" (saas.auditoria_plataforma).
--                El operador lo recibe por la regla del evaluador (todo el catalogo, D60 y D66).
--            No toca dbo.rol_permisos, saas.roles_plantilla ni saas.roles_plantilla_permisos: ningun rol ni ninguna plantilla cambia
--            (las plantillas son de modulo y estos permisos no lo son).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-22
-- BD       : eBD_SPD
-- Requiere : 041 aplicado (saas.permisos) y 045 aplicado (las tablas de auditoria que leen las pantallas).
-- Rollback : ver la seccion ROLLBACK al final (la clave foranea de dbo.rol_permisos lo detiene si un rol ya tiene alguno).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos;
--        b) uno de los dos permisos ya existe con otro modulo, ambito o casilla legada;
--        c) el orden 92 o 93 lo usa otro permiso.
--   2. CAMBIO (una sola transaccion): inserta los permisos que falten.
--   3. POSTCHECK dentro de la misma transaccion: los 2 permisos activos con su ambito y sin casilla legada, y solo esos 2 permisos de
--      auditoria. Si algo no cuadra, se revierte todo. (Que ningun rol tenga administracion.auditoria.ver no se puede contar desde aqui:
--      dbo.rol_permisos tiene RLS y esta conexion no tiene tenant. Un permiso recien creado no lo tiene nadie, la pantalla de roles no lo
--      ofrece y el evaluador no lo concede a un rol aunque alguien lo pusiera por SQL.)
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * ASCII puro: los textos con tilde se escriben con NCHAR(n).
--   * Solo toca el catalogo saas.permisos (plano de control, sin RLS): no depende del estado de la politica.
--   * Se aplica ANTES de publicar el codigo de F1.8c: sin estas filas las pantallas nuevas no las abre nadie (el evaluador lee el
--     catalogo de la base). Al reves no pasa nada: el codigo anterior ignora los permisos nuevos.
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
IF OBJECT_ID(N'tempdb..#precheck_046') IS NOT NULL DROP TABLE #precheck_046;
IF OBJECT_ID(N'tempdb..#catalogo_046') IS NOT NULL DROP TABLE #catalogo_046;

-- Los permisos de las pantallas de auditoria (fuente unica: Services/Permisos/CatalogoDePermisos.cs; una prueba compara los dos).
-- El orden sigue al del script 043 (91 permisos).
CREATE TABLE #catalogo_046 (
    orden          INT           NOT NULL PRIMARY KEY,
    modulo         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    recurso        VARCHAR(40)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    ambito         VARCHAR(12)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion_legada  VARCHAR(10)   COLLATE DATABASE_DEFAULT NULL,
    descripcion    NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL
);
INSERT INTO #catalogo_046 (orden, modulo, recurso, accion, ambito, accion_legada, descripcion) VALUES
    (92, 'administracion', 'auditoria', 'ver', 'empresa', NULL, N'Ver y exportar la auditor' + NCHAR(237) + N'a de seguridad de la empresa'),
    (93, 'plataforma', 'auditoria', 'ver', 'plataforma', NULL, N'Ver y exportar la auditor' + NCHAR(237) + N'a de las cuentas y del operador de toda la plataforma');

DECLARE @errores NVARCHAR(MAX) = N'';

-- a) scripts previos
IF OBJECT_ID(N'saas.permisos', N'U') IS NULL SET @errores += N' Falta la tabla saas.permisos (script 041).';
IF OBJECT_ID(N'dbo.rol_permisos', N'U') IS NULL SET @errores += N' Falta la tabla dbo.rol_permisos (script 041).';
IF OBJECT_ID(N'dbo.auditoria_seguridad', N'U') IS NULL SET @errores += N' Falta la tabla dbo.auditoria_seguridad (script 045).';
IF OBJECT_ID(N'saas.auditoria_plataforma', N'U') IS NULL SET @errores += N' Falta la tabla saas.auditoria_plataforma (script 045).';

-- b) y c) lo que ya exista coincide con lo esperado (solo si las tablas estan)
IF LEN(@errores) = 0
BEGIN
    SELECT @errores += N' El permiso ' + p.codigo + N' existe con otro modulo, ambito o casilla legada.'
    FROM saas.permisos p
    JOIN #catalogo_046 c ON p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion
    WHERE p.modulo <> c.modulo OR p.ambito <> c.ambito OR p.accion_legada IS NOT NULL;

    SELECT @errores += N' El orden ' + CAST(c.orden AS NVARCHAR(10)) + N' ya lo usa el permiso ' + p.codigo + N'.'
    FROM saas.permisos p
    JOIN #catalogo_046 c ON p.orden = c.orden
    WHERE p.codigo <> c.modulo + '.' + c.recurso + '.' + c.accion;
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 046 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50046, @mensaje, 1;
END

CREATE TABLE #precheck_046 (ok BIT NOT NULL);
INSERT INTO #precheck_046 (ok) VALUES (1);

DECLARE @existen INT = (SELECT COUNT(*) FROM saas.permisos p JOIN #catalogo_046 c ON p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion); DECLARE @totalPermisos INT = (SELECT COUNT(*) FROM saas.permisos);
PRINT CONCAT(N'PRECHECK 046 correcto. Permisos de auditoria ya en el catalogo: ', @existen, N' de 2; permisos en total: ',
             @totalPermisos, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_046') IS NULL
    THROW 50046, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @permisosNuevos INT = 0;
DECLARE @malas INT;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Permisos: solo se inserta lo que falta; no se toca nada que el operador ya haya editado.
    INSERT INTO saas.permisos (codigo, modulo, recurso, accion, ambito, accion_legada, descripcion, orden, creado_por)
    SELECT c.modulo + '.' + c.recurso + '.' + c.accion, c.modulo, c.recurso, c.accion, c.ambito, c.accion_legada, c.descripcion, c.orden, N'script:046'
    FROM #catalogo_046 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion)
    ORDER BY c.orden;
    SET @permisosNuevos = @@ROWCOUNT;

    -- 2. POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT @malas = COUNT(*)
    FROM #catalogo_046 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p
                      WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion AND p.modulo = c.modulo AND p.recurso = c.recurso
                        AND p.accion = c.accion AND p.ambito = c.ambito AND p.accion_legada IS NULL AND p.activo = 1);
    IF @malas > 0
        THROW 50046, N'POSTCHECK: falta alguno de los 2 permisos de auditoria, o quedo inactivo o con otro ambito. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM saas.permisos WHERE recurso = 'auditoria') <> 2
        THROW 50046, N'POSTCHECK: el catalogo no tiene exactamente 2 permisos de auditoria. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @permisosNuevos = 0
        PRINT N'Nada que hacer: el script 046 ya estaba aplicado.';
    ELSE
        PRINT CONCAT(N'CAMBIO 046 aplicado: ', @permisosNuevos, N' permiso(s) de auditoria. Ningun rol ni ninguna plantilla cambio.');
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
WHERE p.recurso = 'auditoria'
ORDER BY p.orden;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita los 2 permisos. La clave foranea de dbo.rol_permisos lo detiene (y se revierte todo) si un rol ya tiene alguno.
--   Publicar antes el codigo anterior a F1.8c: sin los permisos, las pantallas de auditoria no las abre nadie.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM saas.permisos WHERE codigo IN ('administracion.auditoria.ver', 'plataforma.auditoria.ver');

COMMIT TRANSACTION;
PRINT N'ROLLBACK 046 aplicado: permisos de auditoria retirados.';
*/
