-- ============================================================
-- Script   : 043_psicologia_modulo_permisos.sql
-- Proposito: Modulo de Psicologia, paso 2 de 3 (catalogos). Plan "Plan modulo Psicologia" (2026-11-15).
--              * Da de alta el modulo 'psicologia' en dbo.modulos (catalogo global, D65: textos con tilde escritos con NCHAR).
--              * Crea su derecho de uso 'modulo.psicologia' en saas.derechos (ADR-010, ADR-013) y lo agrega a la version abierta del
--                plan 'interno', que por regla del 040 tiene todos los modulos de dbo.modulos (verificar_despues_f17.sql lo exige).
--                No cambia lo que ve nadie: un modulo solo se usa si el plan lo concede Y la empresa lo activa (empresa_modulos), y
--                ninguna empresa lo tiene activado. El plan 'psicologia' y la cuenta de la empresa "Centro de Atencion Psicologica y
--                Avance Educativo" se dan de alta en el script 044.
--              * Agrega al catalogo saas.permisos los 19 permisos del modulo (pacientes, jornadas, atenciones, documentos, catalogos de pruebas y tipos de constancia, constancias y profesionales) (modulo.recurso.accion, ambito 'modulo', ADR-029), con
--                la casilla legada que los concede en el sistema anterior: ver, crear, editar o eliminar.
--              * Crea sus 3 roles plantilla (D64), con la misma regla del script 041: administrador = todos los permisos del modulo,
--                operador = ver, crear y editar (todo menos anular), consulta = solo ver.
--            No toca dbo.rol_permisos ni dbo.empresa_rol_permisos: ningun rol existente cambia.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-15
-- BD       : eBD_SPD
-- Requiere : 040, 041 y 042 aplicados (saas.derechos, saas.permisos, saas.roles_plantilla y el esquema psi).
-- Rollback : ver la seccion ROLLBACK al final (las claves foraneas lo detienen si un plan, una empresa o un rol ya usan el modulo).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos;
--        b) el modulo, el derecho, un permiso o una plantilla ya existen con datos distintos de los esperados, o existe un permiso
--           psicologia.* que no esta en este catalogo.
--   2. CAMBIO (una sola transaccion): modulo, derecho, derecho en el plan interno, permisos, plantillas y permisos de las
--      plantillas. Solo inserta lo que falta.
--   3. POSTCHECK dentro de la misma transaccion: modulo activo, derecho, el plan interno con todos los modulos de dbo.modulos,
--      ningun otro plan con el derecho nuevo por obra de este script, 19 permisos con su ambito y casilla legada y 3 plantillas
--      con 19, 17 y 6 permisos. Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * ASCII puro: los textos con tilde se escriben con NCHAR(n).
--   * Solo toca tablas del plano de control y catalogos globales (sin RLS): no depende del estado de la politica.
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
IF OBJECT_ID(N'tempdb..#precheck_043') IS NOT NULL DROP TABLE #precheck_043;
IF OBJECT_ID(N'tempdb..#catalogo_043') IS NOT NULL DROP TABLE #catalogo_043;

-- Los permisos del modulo (fuente unica: Services/Permisos/CatalogoDePermisos.cs; una prueba compara los dos).
-- El orden sigue al del script 041 (72 permisos).
CREATE TABLE #catalogo_043 (
    orden          INT           NOT NULL PRIMARY KEY,
    modulo         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    recurso        VARCHAR(40)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    ambito         VARCHAR(12)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion_legada  VARCHAR(10)   COLLATE DATABASE_DEFAULT NULL,
    descripcion    NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL
);
INSERT INTO #catalogo_043 (orden, modulo, recurso, accion, ambito, accion_legada, descripcion) VALUES
    (73, 'psicologia', 'pacientes', 'ver', 'modulo', 'ver', N'Ver los pacientes y su expediente'),
    (74, 'psicologia', 'pacientes', 'crear', 'modulo', 'crear', N'Registrar pacientes'),
    (75, 'psicologia', 'pacientes', 'editar', 'modulo', 'editar', N'Editar los datos de un paciente'),
    (76, 'psicologia', 'pacientes', 'importar', 'modulo', 'crear', N'Importar pacientes desde un archivo de Excel'),
    (77, 'psicologia', 'jornadas', 'ver', 'modulo', 'ver', N'Ver las jornadas y brigadas de atenci' + NCHAR(243) + N'n'),
    (78, 'psicologia', 'jornadas', 'crear', 'modulo', 'crear', N'Crear jornadas y brigadas'),
    (79, 'psicologia', 'jornadas', 'editar', 'modulo', 'editar', N'Editar, cerrar o anular jornadas'),
    (80, 'psicologia', 'atenciones', 'ver', 'modulo', 'ver', N'Ver el historial de atenciones'),
    (81, 'psicologia', 'atenciones', 'registrar', 'modulo', 'crear', N'Registrar y completar atenciones'),
    (82, 'psicologia', 'atenciones', 'cerrar', 'modulo', 'editar', N'Cerrar una atenci' + NCHAR(243) + N'n para que ya no se edite'),
    (83, 'psicologia', 'atenciones', 'anular', 'modulo', 'eliminar', N'Anular una atenci' + NCHAR(243) + N'n'),
    (84, 'psicologia', 'documentos', 'imprimir', 'modulo', 'crear', N'Imprimir los documentos cl' + NCHAR(237) + N'nicos'),
    (85, 'psicologia', 'catalogos', 'ver', 'modulo', 'ver', N'Ver las pruebas psicol' + NCHAR(243) + N'gicas y los tipos de constancia'),
    (86, 'psicologia', 'catalogos', 'editar', 'modulo', 'editar', N'Crear y editar pruebas psicol' + NCHAR(243) + N'gicas y tipos de constancia'),
    (87, 'psicologia', 'constancias', 'ver', 'modulo', 'ver', N'Ver las constancias extendidas'),
    (88, 'psicologia', 'constancias', 'emitir', 'modulo', 'crear', N'Emitir constancias de examen psicol' + NCHAR(243) + N'gico'),
    (89, 'psicologia', 'constancias', 'anular', 'modulo', 'eliminar', N'Anular una constancia'),
    (90, 'psicologia', 'profesionales', 'ver', 'modulo', 'ver', N'Ver los profesionales del centro'),
    (91, 'psicologia', 'profesionales', 'editar', 'modulo', 'editar', N'Registrar y editar los profesionales del centro');

DECLARE @errores NVARCHAR(MAX) = N'';

-- a) scripts previos
IF OBJECT_ID(N'dbo.modulos', N'U') IS NULL SET @errores += N' Falta la tabla dbo.modulos.';
IF OBJECT_ID(N'saas.derechos', N'U') IS NULL SET @errores += N' Falta la tabla saas.derechos (script 040).';
IF OBJECT_ID(N'saas.plan_version_derechos', N'U') IS NULL SET @errores += N' Falta la tabla saas.plan_version_derechos (script 040).';
IF OBJECT_ID(N'saas.permisos', N'U') IS NULL SET @errores += N' Falta la tabla saas.permisos (script 041).';
IF OBJECT_ID(N'saas.roles_plantilla', N'U') IS NULL SET @errores += N' Falta la tabla saas.roles_plantilla (script 041).';
IF OBJECT_ID(N'saas.roles_plantilla_permisos', N'U') IS NULL SET @errores += N' Falta la tabla saas.roles_plantilla_permisos (script 041).';
IF OBJECT_ID(N'psi.pacientes', N'U') IS NULL SET @errores += N' Falta el esquema psi (script 042).';
IF OBJECT_ID(N'saas.planes', N'U') IS NOT NULL AND OBJECT_ID(N'saas.plan_versiones', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM saas.planes p JOIN saas.plan_versiones v ON v.id_plan = p.id_plan
                   WHERE p.codigo = 'interno' AND v.vigente_hasta IS NULL)
    SET @errores += N' Falta el plan interno con una version abierta (script 040).';
IF OBJECT_ID(N'dbo.modulos', N'U') IS NOT NULL AND ISNULL(COLUMNPROPERTY(OBJECT_ID(N'dbo.modulos'), N'id_modulo', 'IsIdentity'), 0) <> 1
    SET @errores += N' dbo.modulos.id_modulo no es IDENTITY (lo era en F0_Catalogos_Transversales.sql); revisar a mano.';

-- b) lo que ya exista coincide con lo esperado (solo si las tablas estan)
IF LEN(@errores) = 0
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'psicologia' AND activo = 0)
        SET @errores += N' El modulo psicologia existe en dbo.modulos pero esta inactivo; revisar a mano.';

    IF EXISTS (SELECT 1 FROM saas.derechos WHERE codigo = 'modulo.psicologia' AND tipo <> 'modulo')
        SET @errores += N' El derecho modulo.psicologia existe con un tipo distinto de modulo.';

    SELECT @errores += N' El permiso ' + p.codigo + N' existe con otro ambito o casilla legada.'
    FROM saas.permisos p
    JOIN #catalogo_043 c ON p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion
    WHERE p.ambito <> c.ambito OR ISNULL(p.accion_legada, '') <> ISNULL(c.accion_legada, '');

    SELECT @errores += N' El permiso ' + p.codigo + N' es del modulo psicologia pero no esta en este catalogo.'
    FROM saas.permisos p
    WHERE p.modulo = 'psicologia'
      AND NOT EXISTS (SELECT 1 FROM #catalogo_043 c WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion);

    SELECT @errores += N' La plantilla ' + r.codigo + N' existe pero no es del modulo psicologia.'
    FROM saas.roles_plantilla r
    WHERE r.codigo IN ('psicologia_administrador', 'psicologia_operador', 'psicologia_consulta') AND r.modulo <> 'psicologia';
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 043 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50043, @mensaje, 1;
END

CREATE TABLE #precheck_043 (ok BIT NOT NULL);
INSERT INTO #precheck_043 (ok) VALUES (1);

DECLARE @moduloExiste INT = (SELECT COUNT(*) FROM dbo.modulos WHERE codigo = 'psicologia');
DECLARE @permisosExisten INT = (SELECT COUNT(*) FROM saas.permisos WHERE modulo = 'psicologia');
PRINT CONCAT(N'PRECHECK 043 correcto. Modulo ', CASE WHEN @moduloExiste > 0 THEN N'ya existe' ELSE N'por crear' END,
             N'; permisos de psicologia ya en el catalogo: ', @permisosExisten, N' de 19.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_043') IS NULL
    THROW 50043, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @modulosNuevos INT = 0, @derechosNuevos INT = 0, @enlacesInternoNuevos INT = 0, @permisosNuevos INT = 0;
DECLARE @plantillasNuevas INT = 0, @plantillaPermisosNuevos INT = 0;
-- Enlaces del derecho con planes que no son el interno (por ejemplo, el plan psicologia del 044): este script no los toca.
DECLARE @enlacesOtrosAntes INT = (SELECT COUNT(*) FROM saas.plan_version_derechos v
                                  JOIN saas.derechos d ON d.id_derecho = v.id_derecho
                                  JOIN saas.plan_versiones pv ON pv.id_plan_version = v.id_plan_version
                                  JOIN saas.planes p ON p.id_plan = pv.id_plan
                                  WHERE d.codigo = 'modulo.psicologia' AND p.codigo <> 'interno');
DECLARE @malas INT;

IF OBJECT_ID(N'tempdb..#plantillas_nuevas_043') IS NOT NULL DROP TABLE #plantillas_nuevas_043;
CREATE TABLE #plantillas_nuevas_043 (id_rol_plantilla INT NOT NULL PRIMARY KEY, codigo VARCHAR(40) COLLATE DATABASE_DEFAULT NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Modulo (catalogo global; id_modulo es IDENTITY, lo comprueba el PRECHECK). Va al final del orden del menu.
    IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'psicologia')
    BEGIN
        INSERT INTO dbo.modulos (codigo, nombre, descripcion, icono, orden, activo)
        SELECT 'psicologia', N'Psicolog' + NCHAR(237) + N'a',
               N'Pacientes, jornadas, historial cl' + NCHAR(237) + N'nico e impresi' + NCHAR(243) + N'n de documentos',
               'fa-brain', ISNULL(MAX(orden), 0) + 1, 1
        FROM dbo.modulos WITH (UPDLOCK, HOLDLOCK);
        SET @modulosNuevos = @@ROWCOUNT;
    END

    -- 2. Derecho de uso del modulo (mismo texto que siembra el 040 para los demas modulos).
    INSERT INTO saas.derechos (codigo, tipo, descripcion, creado_por)
    SELECT 'modulo.psicologia', 'modulo', N'Acceso al modulo psicologia', N'script:043'
    WHERE NOT EXISTS (SELECT 1 FROM saas.derechos WHERE codigo = 'modulo.psicologia');
    SET @derechosNuevos = @@ROWCOUNT;

    -- El plan interno tiene todos los modulos (regla del 040): se agrega a sus versiones abiertas. Sin efecto visible: ninguna
    -- empresa tiene el modulo activado en empresa_modulos.
    INSERT INTO saas.plan_version_derechos (id_plan_version, id_derecho, tipo_derecho, valor_limite, creado_por)
    SELECT pv.id_plan_version, d.id_derecho, d.tipo, NULL, N'script:043'
    FROM saas.planes p
    JOIN saas.plan_versiones pv ON pv.id_plan = p.id_plan AND pv.vigente_hasta IS NULL
    CROSS JOIN saas.derechos d
    WHERE p.codigo = 'interno' AND d.codigo = 'modulo.psicologia'
      AND NOT EXISTS (SELECT 1 FROM saas.plan_version_derechos x WHERE x.id_plan_version = pv.id_plan_version AND x.id_derecho = d.id_derecho);
    SET @enlacesInternoNuevos = @@ROWCOUNT;

    -- 3. Permisos del modulo: solo se inserta lo que falta; no se toca nada que el operador ya haya editado.
    INSERT INTO saas.permisos (codigo, modulo, recurso, accion, ambito, accion_legada, descripcion, orden, creado_por)
    SELECT c.modulo + '.' + c.recurso + '.' + c.accion, c.modulo, c.recurso, c.accion, c.ambito, c.accion_legada, c.descripcion, c.orden, N'script:043'
    FROM #catalogo_043 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion)
    ORDER BY c.orden;
    SET @permisosNuevos = @@ROWCOUNT;

    -- 4. Tres roles plantilla del modulo (D64), con la regla del script 041.
    INSERT INTO saas.roles_plantilla (codigo, modulo, nombre, descripcion, orden, creado_por)
    OUTPUT inserted.id_rol_plantilla, inserted.codigo INTO #plantillas_nuevas_043 (id_rol_plantilla, codigo)
    SELECT 'psicologia_' + k.clave, 'psicologia', k.nombre, k.descripcion + N' psicologia.', 730 + k.orden, N'script:043'
    FROM (VALUES ('administrador', N'Administrador', N'Todos los permisos del m' + NCHAR(243) + N'dulo', 1),
                 ('operador',      N'Operador',      N'Ver, crear y editar en el m' + NCHAR(243) + N'dulo, sin eliminar ni anular', 2),
                 ('consulta',      N'Consulta',      N'Solo consulta en el m' + NCHAR(243) + N'dulo', 3)) k (clave, nombre, descripcion, orden)
    WHERE NOT EXISTS (SELECT 1 FROM saas.roles_plantilla r WHERE r.codigo = 'psicologia_' + k.clave);
    SET @plantillasNuevas = @@ROWCOUNT;

    -- Permisos de las plantillas recien creadas (las que ya existian no se tocan).
    INSERT INTO saas.roles_plantilla_permisos (id_rol_plantilla, id_permiso, modulo, creado_por)
    SELECT rp.id_rol_plantilla, p.id_permiso, p.modulo, N'script:043'
    FROM #plantillas_nuevas_043 pn
    JOIN saas.roles_plantilla rp ON rp.id_rol_plantilla = pn.id_rol_plantilla
    JOIN saas.permisos p ON p.modulo = rp.modulo AND p.ambito = 'modulo'
    WHERE (rp.codigo = rp.modulo + '_administrador')
       OR (rp.codigo = rp.modulo + '_operador' AND p.accion_legada IN ('ver', 'crear', 'editar'))
       OR (rp.codigo = rp.modulo + '_consulta' AND p.accion_legada = 'ver');
    SET @plantillaPermisosNuevos = @@ROWCOUNT;

    -- 5. POSTCHECK: si algo no cuadra, se revierte todo.
    IF NOT EXISTS (SELECT 1 FROM dbo.modulos WHERE codigo = 'psicologia' AND activo = 1)
        THROW 50043, N'POSTCHECK: el modulo psicologia no quedo activo en dbo.modulos. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM dbo.modulos WHERE codigo = 'psicologia') <> 1
        THROW 50043, N'POSTCHECK: hay mas de un modulo psicologia. Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM saas.derechos WHERE codigo = 'modulo.psicologia' AND tipo = 'modulo')
        THROW 50043, N'POSTCHECK: falta el derecho modulo.psicologia. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM #catalogo_043 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p
                      WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion AND p.modulo = c.modulo
                        AND p.ambito = c.ambito AND ISNULL(p.accion_legada, '') = ISNULL(c.accion_legada, ''));
    IF @malas > 0 OR (SELECT COUNT(*) FROM saas.permisos WHERE modulo = 'psicologia') <> 19
        THROW 50043, N'POSTCHECK: el catalogo no tiene exactamente los 19 permisos de psicologia. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM (VALUES ('psicologia_administrador', 19), ('psicologia_operador', 17), ('psicologia_consulta', 6)) v (codigo, esperados)
    WHERE (SELECT COUNT(*) FROM saas.roles_plantilla r JOIN saas.roles_plantilla_permisos rpp ON rpp.id_rol_plantilla = r.id_rol_plantilla
           WHERE r.codigo = v.codigo AND r.modulo = 'psicologia') <> v.esperados;
    IF @malas > 0
        THROW 50043, N'POSTCHECK: alguna plantilla de psicologia no tiene sus permisos (19, 17 y 6). Se revierte.', 1;

    -- El plan interno (versiones abiertas) tiene todos los modulos de dbo.modulos, psicologia incluido.
    SELECT @malas = COUNT(*)
    FROM saas.planes p
    JOIN saas.plan_versiones pv ON pv.id_plan = p.id_plan AND pv.vigente_hasta IS NULL
    CROSS JOIN dbo.modulos m
    WHERE p.codigo = 'interno'
      AND NOT EXISTS (SELECT 1 FROM saas.plan_version_derechos x JOIN saas.derechos d ON d.id_derecho = x.id_derecho
                      WHERE x.id_plan_version = pv.id_plan_version AND d.codigo = 'modulo.' + CAST(m.codigo AS VARCHAR(50)));
    IF @malas > 0
        THROW 50043, N'POSTCHECK: el plan interno no tiene todos los modulos de dbo.modulos. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM saas.plan_version_derechos v
        JOIN saas.derechos d ON d.id_derecho = v.id_derecho
        JOIN saas.plan_versiones pv ON pv.id_plan_version = v.id_plan_version
        JOIN saas.planes p ON p.id_plan = pv.id_plan
        WHERE d.codigo = 'modulo.psicologia' AND p.codigo <> 'interno') <> @enlacesOtrosAntes
        THROW 50043, N'POSTCHECK: un plan distinto del interno cambio su acceso a psicologia. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @modulosNuevos + @derechosNuevos + @enlacesInternoNuevos + @permisosNuevos + @plantillasNuevas + @plantillaPermisosNuevos = 0
        PRINT N'Nada que hacer: el script 043 ya estaba aplicado.';
    ELSE
        PRINT CONCAT(N'CAMBIO 043 aplicado: ', @modulosNuevos, N' modulo(s), ', @derechosNuevos, N' derecho(s) (en ', @enlacesInternoNuevos,
                     N' version(es) del plan interno), ', @permisosNuevos, N' permiso(s), ', @plantillasNuevas, N' plantilla(s) con ',
                     @plantillaPermisosNuevos, N' permiso(s). Ningun rol cambio y ninguna empresa tiene el modulo activado.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resumen (solo lectura)
SELECT m.id_modulo, m.codigo, m.nombre, m.orden, m.activo,
       (SELECT COUNT(*) FROM saas.permisos p WHERE p.modulo = m.codigo) AS permisos,
       (SELECT COUNT(*) FROM saas.roles_plantilla r WHERE r.modulo = m.codigo) AS plantillas,
       (SELECT COUNT(*) FROM saas.plan_version_derechos v JOIN saas.derechos d ON d.id_derecho = v.id_derecho
        WHERE d.codigo = 'modulo.' + m.codigo) AS planes_con_el_modulo
FROM dbo.modulos m
WHERE m.codigo = 'psicologia';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita el derecho del plan interno. Las claves foraneas lo detienen (y se revierte todo) si un rol de empresa ya tiene un
--   permiso de psicologia (FK_rol_permisos_permisos), si otro plan tiene el derecho (FK_plan_version_derechos_derechos; por
--   ejemplo, el plan psicologia: revertir antes el 044) o si una empresa activo el modulo.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE x FROM saas.plan_version_derechos x
JOIN saas.derechos d ON d.id_derecho = x.id_derecho
JOIN saas.plan_versiones pv ON pv.id_plan_version = x.id_plan_version
JOIN saas.planes p ON p.id_plan = pv.id_plan
WHERE d.codigo = 'modulo.psicologia' AND p.codigo = 'interno';

DELETE rpp FROM saas.roles_plantilla_permisos rpp
JOIN saas.roles_plantilla r ON r.id_rol_plantilla = rpp.id_rol_plantilla
WHERE r.modulo = 'psicologia';

DELETE FROM saas.roles_plantilla WHERE modulo = 'psicologia';
DELETE FROM saas.permisos WHERE modulo = 'psicologia';
DELETE FROM saas.derechos WHERE codigo = 'modulo.psicologia';
DELETE FROM dbo.modulos WHERE codigo = 'psicologia';

COMMIT TRANSACTION;
PRINT N'ROLLBACK 043 aplicado: modulo, derecho, permisos y plantillas de psicologia retirados.';
*/
