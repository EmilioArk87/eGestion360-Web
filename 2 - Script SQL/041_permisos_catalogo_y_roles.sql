-- ============================================================
-- Script   : 041_permisos_catalogo_y_roles.sql
-- Proposito: Paso F1.6a del plan de la Fase 1 (permisos por accion; ADR-003, ADR-008 y ADR-029; decisiones D57 a D65):
--              * Crea el CATALOGO de permisos con la forma modulo.recurso.accion (saas.permisos, plano de control, SIN RLS, ADR-013)
--                y lo siembra con los permisos que cubren las pantallas de hoy. Cada permiso lleva su ambito: 'plataforma' (solo el
--                operador de la plataforma), 'empresa' (administracion de la empresa) o 'modulo' (sujeto al plan y a la activacion del
--                modulo). accion_legada dice a cual de las 4 casillas actuales (ver, crear, editar, eliminar) corresponde un permiso
--                de modulo: la migracion de abajo y la comprobacion de equivalencia se apoyan en ella.
--              * Crea los ROLES PLANTILLA (saas.roles_plantilla y saas.roles_plantilla_permisos, D64): tres por modulo con permisos
--                (Administrador = todos los permisos del modulo, Operador = ver, crear y editar, Consulta = solo ver). F1.9 los copia
--                al crear un tenant. Una clave compuesta obliga a que un permiso de plantilla sea del modulo de la plantilla.
--              * Crea dbo.rol_permisos (TABLA DE TENANT: id_tenant, clave compuesta al rol y politica RLS): los permisos de cada rol de
--                empresa. Un rol nunca recibe permisos de ambito 'plataforma' (los tiene el operador por su cuenta, D60).
--              * MIGRA las filas de dbo.empresa_rol_permisos: cada casilla marcada (ver, crear, editar, eliminar) de un modulo concede
--                todos los permisos de ese modulo cuya accion_legada es esa casilla. NADIE GANA NI PIERDE PERMISOS al migrar. La tabla
--                vieja no se toca (se conserva hasta F1.10, expandir y contraer).
--              * Corrige las tildes danadas de dbo.modulos (D65): el texto de catalogos, facturacion y bancos se guardo mal
--                (por ejemplo la "A" con tilde seguida de un signo en lugar de la "o" con acento). Solo cambia nombre y descripcion de
--                las filas que tengan el dano; los codigos no cambian.
--            No cambia lo que ve ni lo que puede hacer ningun usuario: la aplicacion publicada ignora las tablas nuevas.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-03
-- BD       : eBD_SPD
-- Requiere : 025 a 036 aplicados (esquema saas, id_tenant obligatorio, politica seg.pol_tenant, UX_empresa_roles_tenant_id_rol).
--            038 y 040 pueden estar aplicados o no: la politica se acepta con 199, 204 o 209 predicados segun cuales tablas esten.
-- Rollback : ver la seccion ROLLBACK al final (quita lo que crea este script; se detiene si alguien ya edito los permisos de un rol).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos (esquema saas, saas.tenants, dbo.modulos, dbo.empresa_roles y su clave UX_empresa_roles_tenant_id_rol,
--           dbo.empresa_rol_permisos, la funcion seg.fn_tenant_actual o la politica seg.pol_tenant con los predicados esperados);
--        b) los objetos nuevos existen A MEDIAS (algunas de las 4 tablas y sus 5 predicados si y otros no) o una tabla existe con
--           columnas distintas de las esperadas;
--        c) el catalogo nombra un modulo de ambito 'modulo' que no esta en dbo.modulos, o un modulo de dbo.modulos tiene un codigo que
--           no sirve para formar un permiso;
--        d) una fila de empresa_rol_permisos apunta a un rol de otro tenant.
--      Calcula, tenant por tenant (con la politica RLS ENCENDIDA dbo.empresa_rol_permisos no devuelve filas a una sesion sin tenant),
--      los permisos que debe tener cada rol tras la migracion; el POSTCHECK los exige.
--   2. CAMBIO (una sola transaccion): tablas, restricciones, indices, predicados RLS, catalogo, plantillas, migracion y textos de modulos.
--      Solo inserta lo que falta; no pisa nada que el operador ya haya editado.
--   3. POSTCHECK dentro de la misma transaccion: tablas y columnas, restricciones confiables, 5 predicados nuevos, catalogo completo,
--      plantillas con los permisos que dicta su regla, cada permiso esperado de la migracion y ningun texto de modulo danado.
--      Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los demas bloques exigen).
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
IF OBJECT_ID(N'tempdb..#precheck_041') IS NOT NULL DROP TABLE #precheck_041;
IF OBJECT_ID(N'tempdb..#catalogo_041') IS NOT NULL DROP TABLE #catalogo_041;
IF OBJECT_ID(N'tempdb..#columnas_041') IS NOT NULL DROP TABLE #columnas_041;
IF OBJECT_ID(N'tempdb..#esperado_041') IS NOT NULL DROP TABLE #esperado_041;
IF OBJECT_ID(N'tempdb..#plantillas_nuevas_041') IS NOT NULL DROP TABLE #plantillas_nuevas_041;

-- El catalogo de permisos (fuente unica: Services/Permisos/CatalogoDePermisos.cs; una prueba compara los dos).
CREATE TABLE #catalogo_041 (
    orden          INT           NOT NULL PRIMARY KEY,
    modulo         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    recurso        VARCHAR(40)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion         VARCHAR(30)   COLLATE DATABASE_DEFAULT NOT NULL,
    ambito         VARCHAR(12)   COLLATE DATABASE_DEFAULT NOT NULL,
    accion_legada  VARCHAR(10)   COLLATE DATABASE_DEFAULT NULL,
    descripcion    NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL
);
INSERT INTO #catalogo_041 (orden, modulo, recurso, accion, ambito, accion_legada, descripcion) VALUES
    (1, 'plataforma', 'soporte', 'ver', 'plataforma', NULL, N'Ver el panel de soporte y la lista de tenants'),
    (2, 'plataforma', 'soporte', 'entrar', 'plataforma', NULL, N'Entrar a un tenant o a una empresa para darle soporte'),
    (3, 'plataforma', 'empresas', 'ver', 'plataforma', NULL, N'Ver el directorio de empresas'),
    (4, 'plataforma', 'empresas', 'crear', 'plataforma', NULL, N'Crear empresas'),
    (5, 'plataforma', 'empresas', 'editar', 'plataforma', NULL, N'Editar los datos de una empresa'),
    (6, 'plataforma', 'empresas', 'eliminar', 'plataforma', NULL, N'Eliminar empresas'),
    (7, 'plataforma', 'modulos', 'ver', 'plataforma', NULL, N'Ver el cat' + NCHAR(225) + N'logo de m' + NCHAR(243) + N'dulos y los m' + NCHAR(243) + N'dulos activados por empresa'),
    (8, 'plataforma', 'modulos', 'editar', 'plataforma', NULL, N'Activar y desactivar m' + NCHAR(243) + N'dulos en el cat' + NCHAR(225) + N'logo y por empresa'),
    (9, 'plataforma', 'usuarios', 'ver', 'plataforma', NULL, N'Ver el directorio de usuarios de todo el sistema'),
    (10, 'plataforma', 'usuarios', 'restablecer_clave', 'plataforma', NULL, N'Restablecer la clave de un usuario'),
    (11, 'plataforma', 'personas', 'ver', 'plataforma', NULL, N'Ver las personas del sistema y su historial'),
    (12, 'plataforma', 'personas', 'editar', 'plataforma', NULL, N'Editar los datos personales de una persona'),
    (13, 'plataforma', 'outbox', 'ver', 'plataforma', NULL, N'Ver los eventos pendientes y fallidos de la cola de eventos'),
    (14, 'plataforma', 'outbox', 'gestionar', 'plataforma', NULL, N'Reintentar o marcar como procesados los eventos de la cola'),
    (15, 'plataforma', 'correo', 'ver', 'plataforma', NULL, N'Ver la configuraci' + NCHAR(243) + N'n del correo de notificaciones'),
    (16, 'plataforma', 'correo', 'editar', 'plataforma', NULL, N'Guardar, probar, activar y borrar perfiles de correo'),
    (17, 'plataforma', 'diagnostico', 'ver', 'plataforma', NULL, N'Abrir las p' + NCHAR(225) + N'ginas de diagn' + NCHAR(243) + N'stico del sistema'),
    (18, 'administracion', 'usuarios', 'ver', 'empresa', NULL, N'Ver los usuarios de la empresa'),
    (19, 'administracion', 'usuarios', 'crear', 'empresa', NULL, N'Crear usuarios de la empresa'),
    (20, 'administracion', 'usuarios', 'editar', 'empresa', NULL, N'Editar, activar y desactivar usuarios de la empresa y vincularlos a una persona'),
    (21, 'administracion', 'roles', 'ver', 'empresa', NULL, N'Ver los roles de la empresa y sus permisos'),
    (22, 'administracion', 'roles', 'crear', 'empresa', NULL, N'Crear roles de la empresa'),
    (23, 'administracion', 'roles', 'editar', 'empresa', NULL, N'Editar roles, activarlos o desactivarlos y asignarles permisos'),
    (24, 'administracion', 'invitaciones', 'ver', 'empresa', NULL, N'Ver las invitaciones por correo de la empresa'),
    (25, 'administracion', 'invitaciones', 'crear', 'empresa', NULL, N'Invitar por correo a una persona y reenviar invitaciones'),
    (26, 'administracion', 'invitaciones', 'anular', 'empresa', NULL, N'Anular invitaciones pendientes'),
    (27, 'flota', 'vehiculos', 'ver', 'modulo', 'ver', N'Ver los veh' + NCHAR(237) + N'culos'),
    (28, 'flota', 'vehiculos', 'crear', 'modulo', 'crear', N'Crear veh' + NCHAR(237) + N'culos'),
    (29, 'flota', 'vehiculos', 'editar', 'modulo', 'editar', N'Editar veh' + NCHAR(237) + N'culos'),
    (30, 'flota', 'vehiculos', 'eliminar', 'modulo', 'eliminar', N'Eliminar veh' + NCHAR(237) + N'culos'),
    (31, 'flota', 'personas', 'ver', 'modulo', 'ver', N'Ver el personal de la flota y su historial'),
    (32, 'flota', 'personas', 'crear', 'modulo', 'crear', N'Registrar personal de la flota'),
    (33, 'flota', 'personas', 'editar', 'modulo', 'editar', N'Editar personal de la flota'),
    (34, 'flota', 'catalogos', 'ver', 'modulo', 'ver', N'Ver los cat' + NCHAR(225) + N'logos de flota: tipos de veh' + NCHAR(237) + N'culo, rutas, talleres, cargos y categor' + NCHAR(237) + N'as de repuesto'),
    (35, 'flota', 'catalogos', 'crear', 'modulo', 'crear', N'Crear elementos de los cat' + NCHAR(225) + N'logos de flota'),
    (36, 'flota', 'catalogos', 'editar', 'modulo', 'editar', N'Editar elementos de los cat' + NCHAR(225) + N'logos de flota'),
    (37, 'flota', 'mantenimiento', 'ver', 'modulo', 'ver', N'Ver las ' + NCHAR(243) + N'rdenes de mantenimiento'),
    (38, 'flota', 'mantenimiento', 'crear', 'modulo', 'crear', N'Registrar ' + NCHAR(243) + N'rdenes de mantenimiento'),
    (39, 'flota', 'repuestos', 'ver', 'modulo', 'ver', N'Ver los gastos en repuestos'),
    (40, 'flota', 'repuestos', 'crear', 'modulo', 'crear', N'Registrar gastos en repuestos'),
    (41, 'flota', 'seguros', 'ver', 'modulo', 'ver', N'Ver las p' + NCHAR(243) + N'lizas de seguro'),
    (42, 'flota', 'seguros', 'crear', 'modulo', 'crear', N'Registrar p' + NCHAR(243) + N'lizas de seguro'),
    (43, 'flota', 'combustible', 'ver', 'modulo', 'ver', N'Ver las cargas de combustible'),
    (44, 'flota', 'combustible', 'crear', 'modulo', 'crear', N'Registrar cargas de combustible'),
    (45, 'flota', 'peajes', 'ver', 'modulo', 'ver', N'Ver los peajes'),
    (46, 'flota', 'peajes', 'crear', 'modulo', 'crear', N'Registrar peajes'),
    (47, 'flota', 'peajes', 'editar', 'modulo', 'editar', N'Editar peajes'),
    (48, 'flota', 'salarios', 'ver', 'modulo', 'ver', N'Ver los salarios diarios del personal'),
    (49, 'flota', 'salarios', 'crear', 'modulo', 'crear', N'Registrar salarios diarios'),
    (50, 'flota', 'salidas', 'ver', 'modulo', 'ver', N'Ver el control de salidas y entradas de veh' + NCHAR(237) + N'culos'),
    (51, 'flota', 'salidas', 'crear', 'modulo', 'crear', N'Registrar la salida de un veh' + NCHAR(237) + N'culo'),
    (52, 'flota', 'salidas', 'editar', 'modulo', 'editar', N'Registrar la entrada de un veh' + NCHAR(237) + N'culo'),
    (53, 'flota', 'salidas', 'anular', 'modulo', 'eliminar', N'Anular un registro de salida'),
    (54, 'flota', 'kpi', 'ver', 'modulo', 'ver', N'Ver el tablero y los gr' + NCHAR(225) + N'ficos de indicadores de flota'),
    (55, 'catalogos', 'clientes', 'ver', 'modulo', 'ver', N'Ver los clientes'),
    (56, 'catalogos', 'clientes', 'crear', 'modulo', 'crear', N'Crear clientes'),
    (57, 'catalogos', 'clientes', 'editar', 'modulo', 'editar', N'Editar clientes'),
    (58, 'catalogos', 'clientes', 'eliminar', 'modulo', 'eliminar', N'Eliminar clientes'),
    (59, 'catalogos', 'tasas_cambio', 'ver', 'modulo', 'ver', N'Ver las tasas de cambio, su hist' + NCHAR(243) + N'rico, su bit' + NCHAR(225) + N'cora y sus tendencias'),
    (60, 'catalogos', 'tasas_cambio', 'crear', 'modulo', 'crear', N'Registrar una tasa de cambio propia de la empresa'),
    (61, 'catalogos', 'tasas_cambio', 'aprobar', 'modulo', NULL, N'Aprobar o rechazar tasas de cambio'),
    (62, 'catalogos', 'tasas_cambio', 'configurar', 'modulo', NULL, N'Configurar el proceso de la tasa oficial y ejecutarlo'),
    (63, 'facturacion', 'factura', 'ver', 'modulo', 'ver', N'Ver las facturas'),
    (64, 'facturacion', 'factura', 'crear', 'modulo', 'crear', N'Emitir facturas'),
    (65, 'facturacion', 'factura', 'anular', 'modulo', 'eliminar', N'Anular facturas'),
    (66, 'facturacion', 'nota', 'ver', 'modulo', 'ver', N'Ver las notas de cr' + NCHAR(233) + N'dito y de d' + NCHAR(233) + N'bito'),
    (67, 'facturacion', 'nota', 'crear', 'modulo', 'crear', N'Emitir notas de cr' + NCHAR(233) + N'dito y de d' + NCHAR(233) + N'bito'),
    (68, 'facturacion', 'pago', 'ver', 'modulo', 'ver', N'Ver los pagos recibidos'),
    (69, 'facturacion', 'pago', 'crear', 'modulo', 'crear', N'Registrar pagos recibidos'),
    (70, 'facturacion', 'pago', 'aplicar', 'modulo', 'editar', N'Aplicar un pago a facturas'),
    (71, 'facturacion', 'pago', 'anular', 'modulo', 'eliminar', N'Anular pagos'),
    (72, 'facturacion', 'cxc', 'ver', 'modulo', 'ver', N'Ver las cuentas por cobrar');

-- Columnas esperadas de cada tabla nueva.
CREATE TABLE #columnas_041 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    columna  SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, columna)
);
INSERT INTO #columnas_041 (tabla, columna) VALUES
    (N'saas.permisos', N'id_permiso'), (N'saas.permisos', N'codigo'), (N'saas.permisos', N'modulo'), (N'saas.permisos', N'recurso'),
    (N'saas.permisos', N'accion'), (N'saas.permisos', N'ambito'), (N'saas.permisos', N'accion_legada'), (N'saas.permisos', N'descripcion'),
    (N'saas.permisos', N'orden'), (N'saas.permisos', N'activo'), (N'saas.permisos', N'creado_por'), (N'saas.permisos', N'fecha_creacion'),
    (N'saas.permisos', N'modificado_por'), (N'saas.permisos', N'fecha_modificacion'),
    (N'saas.roles_plantilla', N'id_rol_plantilla'), (N'saas.roles_plantilla', N'codigo'), (N'saas.roles_plantilla', N'modulo'),
    (N'saas.roles_plantilla', N'nombre'), (N'saas.roles_plantilla', N'descripcion'), (N'saas.roles_plantilla', N'orden'),
    (N'saas.roles_plantilla', N'activo'), (N'saas.roles_plantilla', N'creado_por'), (N'saas.roles_plantilla', N'fecha_creacion'),
    (N'saas.roles_plantilla', N'modificado_por'), (N'saas.roles_plantilla', N'fecha_modificacion'),
    (N'saas.roles_plantilla_permisos', N'id_rol_plantilla'), (N'saas.roles_plantilla_permisos', N'id_permiso'),
    (N'saas.roles_plantilla_permisos', N'modulo'), (N'saas.roles_plantilla_permisos', N'creado_por'),
    (N'saas.roles_plantilla_permisos', N'fecha_creacion'),
    (N'dbo.rol_permisos', N'id_tenant'), (N'dbo.rol_permisos', N'id_rol'), (N'dbo.rol_permisos', N'id_permiso'),
    (N'dbo.rol_permisos', N'ambito_permiso'), (N'dbo.rol_permisos', N'creado_por'), (N'dbo.rol_permisos', N'fecha_creacion'),
    (N'dbo.rol_permisos', N'modificado_por'), (N'dbo.rol_permisos', N'fecha_modificacion');

-- Lo que cada rol debe tener tras la migracion (se calcula tenant por tenant, ver el PRECHECK).
CREATE TABLE #esperado_041 (
    id_tenant  INT         NOT NULL,
    id_rol     INT         NOT NULL,
    codigo     VARCHAR(80) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (id_tenant, id_rol, codigo)
);

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT;
DECLARE @t INT;
DECLARE @predAcceso INT, @predRol INT;

-- a) scripts previos
IF SCHEMA_ID(N'saas') IS NULL SET @errores += N' Falta el esquema saas (script 025).';
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
IF OBJECT_ID(N'dbo.modulos', N'U') IS NULL SET @errores += N' Falta la tabla dbo.modulos.';
IF OBJECT_ID(N'dbo.empresa_roles', N'U') IS NULL SET @errores += N' Falta la tabla dbo.empresa_roles.';
IF OBJECT_ID(N'dbo.empresa_rol_permisos', N'U') IS NULL SET @errores += N' Falta la tabla dbo.empresa_rol_permisos.';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresa_roles_tenant_id_rol' AND object_id = OBJECT_ID(N'dbo.empresa_roles') AND is_unique = 1)
    SET @errores += N' Falta la clave UX_empresa_roles_tenant_id_rol de dbo.empresa_roles (scripts 031 a 035).';
IF OBJECT_ID(N'seg.fn_tenant_actual') IS NULL SET @errores += N' Falta la funcion seg.fn_tenant_actual (script 036).';
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';
ELSE
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    SELECT @predAcceso = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa');
    SELECT @predRol = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos');

    IF @predAcceso NOT IN (0, 5)
        SET @errores += CONCAT(N' La politica tiene ', @predAcceso, N' predicados sobre accesos_empresa y se esperaban 0 o 5.');
    IF @predRol NOT IN (0, 5)
        SET @errores += CONCAT(N' La politica tiene ', @predRol, N' predicados sobre rol_permisos y se esperaban 0 o 5.');
    ELSE IF @predAcceso IN (0, 5) AND @n <> 199 + @predAcceso + @predRol
        SET @errores += CONCAT(N' La politica seg.pol_tenant tiene ', @n, N' predicados y se esperaban ', 199 + @predAcceso + @predRol,
                               N' (199 del script 036; +5 con accesos_empresa; +5 con rol_permisos).');
END

SELECT @errores += N' ' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND p.target_object_id = OBJECT_ID(N'dbo.rol_permisos');

-- b) estado de los objetos nuevos: ninguno (primera vez) o todos (ya aplicado); a medias, no
DECLARE @tablasExisten INT = (SELECT COUNT(*) FROM (VALUES (N'saas.permisos'), (N'saas.roles_plantilla'), (N'saas.roles_plantilla_permisos'),
                                                           (N'dbo.rol_permisos')) v (t)
                              WHERE OBJECT_ID(v.t, N'U') IS NOT NULL);
DECLARE @objetosExisten INT = @tablasExisten + CASE WHEN COALESCE(@predRol, 0) = 5 THEN 1 ELSE 0 END;

IF @objetosExisten NOT IN (0, 5)
    SET @errores += CONCAT(N' Los objetos de este script existen a medias (', @objetosExisten, N' de 5: ', @tablasExisten,
                           N' de 4 tablas y ', CASE WHEN COALESCE(@predRol, 0) = 5 THEN N'con' ELSE N'sin' END,
                           N' predicados RLS de rol_permisos). Revisar a mano antes de seguir.');

-- b) las tablas que ya existan tienen exactamente las columnas esperadas
SELECT @errores += N' La tabla ' + x.tabla + N' no tiene la columna ' + x.columna + N'.'
FROM #columnas_041 x
WHERE OBJECT_ID(x.tabla, N'U') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);

SELECT @errores += N' La tabla ' + SCHEMA_NAME(o.schema_id) + N'.' + o.name + N' tiene una columna no prevista: ' + c.name + N'.'
FROM sys.objects o
JOIN sys.columns c ON c.object_id = o.object_id
WHERE o.type = 'U'
  AND (SCHEMA_NAME(o.schema_id) + N'.' + o.name) IN (SELECT DISTINCT tabla FROM #columnas_041)
  AND NOT EXISTS (SELECT 1 FROM #columnas_041 x WHERE x.tabla = (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AND x.columna = c.name);

-- c) el catalogo es coherente con dbo.modulos
IF OBJECT_ID(N'dbo.modulos', N'U') IS NOT NULL
BEGIN
    SELECT @errores += N' El catalogo nombra el modulo ' + c.modulo + N', que no esta en dbo.modulos.'
    FROM (SELECT DISTINCT modulo FROM #catalogo_041 WHERE ambito = 'modulo') c
    WHERE NOT EXISTS (SELECT 1 FROM dbo.modulos m WHERE CAST(m.codigo AS VARCHAR(50)) = c.modulo);

    SELECT @errores += N' El codigo de modulo ' + m.codigo + N' no sirve para formar un permiso (solo a-z, 0-9 y guion bajo).'
    FROM dbo.modulos m
    WHERE m.codigo COLLATE Latin1_General_100_BIN2 LIKE N'%[^a-z0-9_]%' OR LEN(m.codigo) = 0;
END

-- Las comprobaciones de datos solo corren si lo anterior esta en orden.
IF LEN(@errores) = 0
BEGIN
    -- d) filas de empresa_rol_permisos de un rol de otro tenant (no deberia existir: la clave compuesta FKT_ lo impide). Los permisos
    --    que cada rol debe tener tras la migracion se calculan tenant por tenant. Un rol que YA tiene filas en rol_permisos (porque este
    --    script ya corrio y alguien las edito) no se vuelve a migrar: no se pisa una edicion.
    DECLARE @sinMigrar NVARCHAR(200) = CASE WHEN OBJECT_ID(N'dbo.rol_permisos', N'U') IS NULL THEN N''
                                            ELSE N' AND NOT EXISTS (SELECT 1 FROM dbo.rol_permisos x WHERE x.id_rol = erp.id_rol)' END;
    DECLARE @consultaEsperado NVARCHAR(MAX) =
        N'INSERT INTO #esperado_041 (id_tenant, id_rol, codigo)
          SELECT DISTINCT erp.id_tenant, erp.id_rol, c.modulo + ''.'' + c.recurso + ''.'' + c.accion
          FROM dbo.empresa_rol_permisos erp
          JOIN dbo.modulos m ON m.id_modulo = erp.id_modulo
          JOIN #catalogo_041 c ON c.ambito = ''modulo'' AND c.modulo = CAST(m.codigo AS VARCHAR(50)) COLLATE DATABASE_DEFAULT
          WHERE erp.id_tenant = @t
            AND ((c.accion_legada = ''ver''      AND erp.puede_ver = 1)
              OR (c.accion_legada = ''crear''    AND erp.puede_crear = 1)
              OR (c.accion_legada = ''editar''   AND erp.puede_editar = 1)
              OR (c.accion_legada = ''eliminar'' AND erp.puede_eliminar = 1))' + @sinMigrar + N';';
    BEGIN TRY
        DECLARE cur_tenants CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants ORDER BY id_tenant;
        OPEN cur_tenants;
        FETCH NEXT FROM cur_tenants INTO @t;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
            EXEC sys.sp_executesql @consultaEsperado, N'@t INT', @t = @t;
            FETCH NEXT FROM cur_tenants INTO @t;
        END
        CLOSE cur_tenants;
        DEALLOCATE cur_tenants;
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    END TRY
    BEGIN CATCH
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        THROW;
    END CATCH
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 041 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50041, @mensaje, 1;
END

CREATE TABLE #precheck_041 (ok BIT NOT NULL);
INSERT INTO #precheck_041 (ok) VALUES (1);

DECLARE @filasEsperadas INT = (SELECT COUNT(*) FROM #esperado_041);
DECLARE @catalogoN INT = (SELECT COUNT(*) FROM #catalogo_041);
PRINT CONCAT(N'PRECHECK 041 correcto. Objetos nuevos ya existentes: ', @objetosExisten, N' de 5; permisos en el catalogo: ', @catalogoN,
             N'; permisos que deben tener los roles tras la migracion: ', @filasEsperadas, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_041') IS NULL
    THROW 50041, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @porCrear INT = (CASE WHEN OBJECT_ID(N'saas.permisos', N'U') IS NULL THEN 1 ELSE 0 END)
                      + (CASE WHEN OBJECT_ID(N'saas.roles_plantilla', N'U') IS NULL THEN 1 ELSE 0 END)
                      + (CASE WHEN OBJECT_ID(N'saas.roles_plantilla_permisos', N'U') IS NULL THEN 1 ELSE 0 END)
                      + (CASE WHEN OBJECT_ID(N'dbo.rol_permisos', N'U') IS NULL THEN 1 ELSE 0 END);
DECLARE @permisosNuevos INT = 0, @plantillasNuevas INT = 0, @plantillaPermisosNuevos INT = 0, @migrados INT = 0, @textosCorregidos INT = 0;
DECLARE @predicadosNuevos INT = 0;
DECLARE @malas INT;
DECLARE @t INT;
DECLARE @detalle NVARCHAR(1000);
DECLARE @filas INT;

CREATE TABLE #plantillas_nuevas_041 (id_rol_plantilla INT NOT NULL PRIMARY KEY, codigo VARCHAR(40) COLLATE DATABASE_DEFAULT NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Catalogo global de permisos (esquema saas, sin RLS)
    IF OBJECT_ID(N'saas.permisos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.permisos (
            id_permiso          INT           IDENTITY(1,1) NOT NULL,
            codigo              VARCHAR(80)   NOT NULL,
            modulo              VARCHAR(30)   NOT NULL,
            recurso             VARCHAR(40)   NOT NULL,
            accion              VARCHAR(30)   NOT NULL,
            ambito              VARCHAR(12)   NOT NULL,
            accion_legada       VARCHAR(10)   NULL,
            descripcion         NVARCHAR(200) NOT NULL,
            orden               INT           NOT NULL,
            activo              BIT           NOT NULL CONSTRAINT DF_permisos_activo DEFAULT 1,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_permisos_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_permisos PRIMARY KEY CLUSTERED (id_permiso),
            CONSTRAINT UX_permisos_codigo UNIQUE (codigo),
            -- Claves de apoyo para las claves foraneas compuestas de roles_plantilla_permisos (mismo modulo) y rol_permisos (ambito).
            CONSTRAINT UX_permisos_id_modulo UNIQUE (id_permiso, modulo),
            CONSTRAINT UX_permisos_id_ambito UNIQUE (id_permiso, ambito),
            CONSTRAINT CK_permisos_ambito CHECK (ambito IN ('plataforma', 'empresa', 'modulo')),
            -- El codigo es modulo.recurso.accion y solo lleva a-z, 0-9, _ y punto.
            CONSTRAINT CK_permisos_codigo CHECK (LEN(modulo) > 0 AND LEN(recurso) > 0 AND LEN(accion) > 0
                                                 AND codigo = modulo + '.' + recurso + '.' + accion
                                                 AND codigo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_.]%'),
            -- plataforma <-> ambito plataforma; administracion <-> ambito empresa; cualquier otro modulo <-> ambito modulo.
            CONSTRAINT CK_permisos_ambito_modulo CHECK ((modulo = 'plataforma' AND ambito = 'plataforma')
                                                        OR (modulo = 'administracion' AND ambito = 'empresa')
                                                        OR (modulo NOT IN ('plataforma', 'administracion') AND ambito = 'modulo')),
            -- La casilla legada solo existe en los permisos de modulo.
            CONSTRAINT CK_permisos_accion_legada CHECK (accion_legada IS NULL
                                                        OR (ambito = 'modulo' AND accion_legada IN ('ver', 'crear', 'editar', 'eliminar')))
        );
    END

    IF OBJECT_ID(N'saas.roles_plantilla', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.roles_plantilla (
            id_rol_plantilla    INT           IDENTITY(1,1) NOT NULL,
            codigo              VARCHAR(40)   NOT NULL,
            modulo              VARCHAR(30)   NOT NULL,
            nombre              NVARCHAR(50)  NOT NULL,
            descripcion         NVARCHAR(200) NULL,
            orden               INT           NOT NULL,
            activo              BIT           NOT NULL CONSTRAINT DF_roles_plantilla_activo DEFAULT 1,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_roles_plantilla_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_roles_plantilla PRIMARY KEY CLUSTERED (id_rol_plantilla),
            CONSTRAINT UX_roles_plantilla_codigo UNIQUE (codigo),
            CONSTRAINT UX_roles_plantilla_id_modulo UNIQUE (id_rol_plantilla, modulo),
            CONSTRAINT CK_roles_plantilla_codigo CHECK (LEN(codigo) > 0 AND codigo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^a-z0-9_]%'),
            -- Las plantillas son de modulos con permisos de ambito modulo, no de plataforma ni de administracion.
            CONSTRAINT CK_roles_plantilla_modulo CHECK (modulo NOT IN ('plataforma', 'administracion'))
        );
    END

    IF OBJECT_ID(N'saas.roles_plantilla_permisos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.roles_plantilla_permisos (
            id_rol_plantilla  INT           NOT NULL,
            id_permiso        INT           NOT NULL,
            modulo            VARCHAR(30)   NOT NULL,
            creado_por        NVARCHAR(100) NOT NULL,
            fecha_creacion    DATETIME2     NOT NULL CONSTRAINT DF_roles_plantilla_permisos_fecha_creacion DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_roles_plantilla_permisos PRIMARY KEY CLUSTERED (id_rol_plantilla, id_permiso),
            -- Las dos claves compuestas llevan el modulo: un permiso de plantilla es del modulo de la plantilla, y la base lo exige.
            CONSTRAINT FK_roles_plantilla_permisos_plantilla FOREIGN KEY (id_rol_plantilla, modulo)
                REFERENCES saas.roles_plantilla (id_rol_plantilla, modulo) ON DELETE CASCADE,
            CONSTRAINT FK_roles_plantilla_permisos_permisos FOREIGN KEY (id_permiso, modulo)
                REFERENCES saas.permisos (id_permiso, modulo)
        );
        CREATE INDEX IX_roles_plantilla_permisos_permiso ON saas.roles_plantilla_permisos (id_permiso);
    END

    -- 2. Permisos de cada rol de empresa. TABLA DE TENANT: lleva id_tenant y entra a la politica RLS.
    IF OBJECT_ID(N'dbo.rol_permisos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rol_permisos (
            id_tenant           INT           NOT NULL,
            id_rol              INT           NOT NULL,
            id_permiso          INT           NOT NULL,
            ambito_permiso      VARCHAR(12)   NOT NULL,
            creado_por          NVARCHAR(100) NOT NULL,
            fecha_creacion      DATETIME2     NOT NULL CONSTRAINT DF_rol_permisos_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100) NULL,
            fecha_modificacion  DATETIME2     NULL,
            CONSTRAINT PK_rol_permisos PRIMARY KEY CLUSTERED (id_rol, id_permiso),
            -- Un rol de empresa nunca recibe permisos de plataforma (D60).
            CONSTRAINT CK_rol_permisos_ambito CHECK (ambito_permiso IN ('empresa', 'modulo')),
            CONSTRAINT FK_rol_permisos_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_rol_permisos_empresa_roles_id_rol FOREIGN KEY (id_tenant, id_rol) REFERENCES dbo.empresa_roles (id_tenant, id_rol),
            -- (id_permiso, ambito_permiso) apunta a (id_permiso, ambito): ambito_permiso no puede mentir sobre el permiso.
            CONSTRAINT FK_rol_permisos_permisos FOREIGN KEY (id_permiso, ambito_permiso) REFERENCES saas.permisos (id_permiso, ambito)
        );
        CREATE INDEX IX_rol_permisos_tenant ON dbo.rol_permisos (id_tenant);
        CREATE INDEX IX_rol_permisos_permiso ON dbo.rol_permisos (id_permiso);
    END

    -- Politica: rol_permisos entra con las mismas funciones que las demas tablas de tenant (1 filtro y 4 bloqueos).
    -- ALTER SECURITY POLICY ... ADD no cambia el estado (encendida o apagada) de la politica.
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rol_permisos,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rol_permisos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rol_permisos AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rol_permisos BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.rol_permisos BEFORE DELETE;';
        SET @predicadosNuevos = 5;
    END

    -- 3. Datos iniciales: solo se inserta lo que falta. No se toca nada que el operador ya haya editado.
    INSERT INTO saas.permisos (codigo, modulo, recurso, accion, ambito, accion_legada, descripcion, orden, creado_por)
    SELECT c.modulo + '.' + c.recurso + '.' + c.accion, c.modulo, c.recurso, c.accion, c.ambito, c.accion_legada, c.descripcion, c.orden, N'script:041'
    FROM #catalogo_041 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion)
    ORDER BY c.orden;
    SET @permisosNuevos = @@ROWCOUNT;

    -- Tres roles plantilla por modulo con permisos de ambito modulo (D64): administrador, operador y consulta.
    INSERT INTO saas.roles_plantilla (codigo, modulo, nombre, descripcion, orden, creado_por)
    OUTPUT inserted.id_rol_plantilla, inserted.codigo INTO #plantillas_nuevas_041 (id_rol_plantilla, codigo)
    SELECT m.modulo + '_' + k.clave, m.modulo, k.nombre,
           k.descripcion + N' ' + m.modulo + N'.',
           m.orden_modulo * 10 + k.orden, N'script:041'
    FROM (SELECT modulo, MIN(orden) AS orden_modulo FROM #catalogo_041 WHERE ambito = 'modulo' GROUP BY modulo) m
    CROSS JOIN (VALUES ('administrador', N'Administrador', N'Todos los permisos del m' + NCHAR(243) + N'dulo', 1),
                       ('operador',      N'Operador',      N'Ver, crear y editar en el m' + NCHAR(243) + N'dulo, sin eliminar ni anular', 2),
                       ('consulta',      N'Consulta',      N'Solo consulta en el m' + NCHAR(243) + N'dulo', 3)) k (clave, nombre, descripcion, orden)
    WHERE NOT EXISTS (SELECT 1 FROM saas.roles_plantilla r WHERE r.codigo = m.modulo + '_' + k.clave);
    SET @plantillasNuevas = @@ROWCOUNT;

    -- Permisos de las plantillas recien creadas (las que ya existian no se tocan).
    INSERT INTO saas.roles_plantilla_permisos (id_rol_plantilla, id_permiso, modulo, creado_por)
    SELECT rp.id_rol_plantilla, p.id_permiso, p.modulo, N'script:041'
    FROM #plantillas_nuevas_041 pn
    JOIN saas.roles_plantilla rp ON rp.id_rol_plantilla = pn.id_rol_plantilla
    JOIN saas.permisos p ON p.modulo = rp.modulo AND p.ambito = 'modulo'
    WHERE (rp.codigo = rp.modulo + '_administrador')
       OR (rp.codigo = rp.modulo + '_operador' AND p.accion_legada IN ('ver', 'crear', 'editar'))
       OR (rp.codigo = rp.modulo + '_consulta' AND p.accion_legada = 'ver');
    SET @plantillaPermisosNuevos = @@ROWCOUNT;

    -- 4. Migracion de empresa_rol_permisos -> rol_permisos, tenant por tenant (la politica RLS exige el tenant de la sesion para leer y
    --    para escribir). Solo se inserta lo que falta.
    DECLARE cur_tenants CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants ORDER BY id_tenant;
    OPEN cur_tenants;
    FETCH NEXT FROM cur_tenants INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
        EXEC sys.sp_executesql
            N'INSERT INTO dbo.rol_permisos (id_tenant, id_rol, id_permiso, ambito_permiso, creado_por)
              SELECT e.id_tenant, e.id_rol, p.id_permiso, p.ambito, N''script:041''
              FROM #esperado_041 e
              JOIN saas.permisos p ON p.codigo = e.codigo
              WHERE e.id_tenant = @t
                AND NOT EXISTS (SELECT 1 FROM dbo.rol_permisos x WHERE x.id_rol = e.id_rol AND x.id_permiso = p.id_permiso);
              SET @n = @@ROWCOUNT;',
            N'@t INT, @n INT OUTPUT', @t = @t, @n = @filas OUTPUT;
        SET @migrados += @filas;
        FETCH NEXT FROM cur_tenants INTO @t;
    END
    CLOSE cur_tenants;
    DEALLOCATE cur_tenants;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;

    -- 5. Textos danados de dbo.modulos (D65): solo las filas cuyo texto tiene el dano (la "A" con tilde seguida de otro signo).
    UPDATE m SET m.nombre = t.nombre
    FROM dbo.modulos m
    JOIN (VALUES (N'catalogos',   N'Cat' + NCHAR(225) + N'logos'),
                 (N'facturacion', N'Facturaci' + NCHAR(243) + N'n')) t (codigo, nombre) ON t.codigo = m.codigo
    WHERE m.nombre COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%';
    SET @textosCorregidos += @@ROWCOUNT;

    UPDATE m SET m.descripcion = t.descripcion
    FROM dbo.modulos m
    JOIN (VALUES (N'catalogos',   N'Clientes, proveedores, productos, impuestos, formas y condiciones de pago'),
                 (N'facturacion', N'Emisi' + NCHAR(243) + N'n de facturas contado/cr' + NCHAR(233) + N'dito, notas de cr' + NCHAR(233) + N'dito/d' + NCHAR(233) + N'bito, CxC'),
                 (N'bancos',      N'Cuentas bancarias, dep' + NCHAR(243) + N'sitos, cheques y conciliaci' + NCHAR(243) + N'n')) t (codigo, descripcion) ON t.codigo = m.codigo
    WHERE m.descripcion COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%';
    SET @textosCorregidos += @@ROWCOUNT;

    -- 6. POSTCHECK: si algo no cuadra, se revierte todo.
    -- 6a. tablas con exactamente sus columnas
    SELECT @malas = COUNT(*)
    FROM #columnas_041 x
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: falta alguna columna en las tablas nuevas. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM sys.columns c
    JOIN sys.objects o ON o.object_id = c.object_id AND o.type = 'U'
    WHERE (SCHEMA_NAME(o.schema_id) + N'.' + o.name) IN (SELECT DISTINCT tabla FROM #columnas_041)
      AND NOT EXISTS (SELECT 1 FROM #columnas_041 x WHERE x.tabla = (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AND x.columna = c.name);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: alguna tabla nueva tiene columnas no previstas. Se revierte.', 1;

    -- 6b. restricciones y claves existen y son confiables (no estan deshabilitadas ni sin validar)
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'CK_permisos_ambito'), (N'CK_permisos_codigo'), (N'CK_permisos_ambito_modulo'), (N'CK_permisos_accion_legada'),
                 (N'CK_roles_plantilla_codigo'), (N'CK_roles_plantilla_modulo'), (N'CK_rol_permisos_ambito')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: falta alguna restriccion CHECK o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM (VALUES (N'FK_roles_plantilla_permisos_plantilla'), (N'FK_roles_plantilla_permisos_permisos'), (N'FK_rol_permisos_tenants'),
                 (N'FKT_rol_permisos_empresa_roles_id_rol'), (N'FK_rol_permisos_permisos')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: falta alguna clave foranea o no es confiable. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM (VALUES (N'saas.permisos', N'UX_permisos_codigo'), (N'saas.permisos', N'UX_permisos_id_modulo'), (N'saas.permisos', N'UX_permisos_id_ambito'),
                 (N'saas.roles_plantilla', N'UX_roles_plantilla_codigo'), (N'saas.roles_plantilla', N'UX_roles_plantilla_id_modulo')) v (tabla, nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.nombre AND i.object_id = OBJECT_ID(v.tabla) AND i.is_unique = 1);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: falta alguna clave unica. Se revierte.', 1;

    -- 6c. politica: rol_permisos con 1 filtro y 4 bloqueos, todos con fn_tenant_actual
    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos');
    IF @malas <> 5 THROW 50041, N'POSTCHECK: rol_permisos debe tener 5 predicados en la politica. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos') AND predicate_type_desc = N'FILTER';
    IF @malas <> 1 THROW 50041, N'POSTCHECK: rol_permisos debe tener un solo filtro. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos')
      AND predicate_definition NOT LIKE N'%fn_tenant_actual%';
    IF @malas > 0 THROW 50041, N'POSTCHECK: algun predicado de rol_permisos no usa seg.fn_tenant_actual. Se revierte.', 1;

    -- 6d. el catalogo esta completo y tal cual
    SELECT @malas = COUNT(*)
    FROM #catalogo_041 c
    WHERE NOT EXISTS (SELECT 1 FROM saas.permisos p
                      WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion AND p.modulo = c.modulo AND p.recurso = c.recurso
                        AND p.accion = c.accion AND p.ambito = c.ambito AND COALESCE(p.accion_legada, '') = COALESCE(c.accion_legada, ''));
    IF @malas > 0
        THROW 50041, N'POSTCHECK: algun permiso del catalogo falta o no coincide con lo previsto. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM saas.permisos p WHERE p.creado_por = N'script:041'
      AND NOT EXISTS (SELECT 1 FROM #catalogo_041 c WHERE p.codigo = c.modulo + '.' + c.recurso + '.' + c.accion);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: hay permisos de este script que no estan en el catalogo previsto. Se revierte.', 1;

    -- 6e. plantillas: tres por modulo con permisos de ambito modulo, cada una con lo que dicta su regla
    SELECT @malas = COUNT(*)
    FROM (SELECT DISTINCT modulo FROM #catalogo_041 WHERE ambito = 'modulo') m
    CROSS JOIN (VALUES ('administrador'), ('operador'), ('consulta')) k (clave)
    WHERE NOT EXISTS (SELECT 1 FROM saas.roles_plantilla r WHERE r.codigo = m.modulo + '_' + k.clave AND r.modulo = m.modulo);
    IF @malas > 0
        THROW 50041, N'POSTCHECK: falta alguna plantilla (tres por modulo). Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM #plantillas_nuevas_041 pn
    JOIN saas.roles_plantilla rp ON rp.id_rol_plantilla = pn.id_rol_plantilla
    WHERE (SELECT COUNT(*) FROM saas.roles_plantilla_permisos l WHERE l.id_rol_plantilla = rp.id_rol_plantilla)
          <> (SELECT COUNT(*) FROM saas.permisos p
              WHERE p.modulo = rp.modulo AND p.ambito = 'modulo'
                AND ((rp.codigo = rp.modulo + '_administrador')
                  OR (rp.codigo = rp.modulo + '_operador' AND p.accion_legada IN ('ver', 'crear', 'editar'))
                  OR (rp.codigo = rp.modulo + '_consulta' AND p.accion_legada = 'ver')));
    IF @malas > 0
        THROW 50041, N'POSTCHECK: alguna plantilla nueva no quedo con los permisos que dicta su regla. Se revierte.', 1;

    -- 6f. NADIE GANA NI PIERDE PERMISOS: cada permiso esperado de la migracion existe en rol_permisos (solo se insertan los esperados,
    --     asi que no puede sobrar ninguno). Se lee tenant por tenant (politica RLS).
    SET @malas = 0;
    DECLARE cur_tenants2 CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants ORDER BY id_tenant;
    OPEN cur_tenants2;
    FETCH NEXT FROM cur_tenants2 INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
        EXEC sys.sp_executesql
            N'SELECT @n = COUNT(*) FROM #esperado_041 e
                           WHERE e.id_tenant = @t
                             AND NOT EXISTS (SELECT 1 FROM dbo.rol_permisos r JOIN saas.permisos p ON p.id_permiso = r.id_permiso
                                             WHERE r.id_rol = e.id_rol AND p.codigo = e.codigo);',
            N'@t INT, @n INT OUTPUT', @t = @t, @n = @filas OUTPUT;
        SET @malas += @filas;
        FETCH NEXT FROM cur_tenants2 INTO @t;
    END
    CLOSE cur_tenants2;
    DEALLOCATE cur_tenants2;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    IF @malas > 0
        THROW 50041, N'POSTCHECK: la migracion de permisos no coincide con lo esperado (algun rol gana o pierde permisos). Se revierte.', 1;

    -- 6g. ningun texto de modulo conserva el dano
    SET @detalle = N'';
    SELECT @detalle += N' ' + m.codigo + N';'
    FROM dbo.modulos m
    WHERE m.nombre COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%'
       OR m.descripcion COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%';
    IF LEN(@detalle) > 0
    BEGIN
        SET @detalle = LEFT(N'POSTCHECK: quedan modulos con texto danado que este script no sabe corregir:' + @detalle + N' Se revierte.', 1000);
        THROW 50041, @detalle, 1;
    END

    COMMIT TRANSACTION;

    IF @porCrear = 0 AND @predicadosNuevos = 0 AND @permisosNuevos = 0 AND @plantillasNuevas = 0 AND @plantillaPermisosNuevos = 0
       AND @migrados = 0 AND @textosCorregidos = 0
        PRINT N'Nada que hacer: tablas, catalogo, plantillas, permisos migrados y textos de modulos ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 041 aplicado: ', @porCrear, N' tabla(s) creadas y ', @predicadosNuevos, N' predicado(s) RLS; ', @permisosNuevos,
                     N' permiso(s) del catalogo, ', @plantillasNuevas, N' plantilla(s) con ', @plantillaPermisosNuevos, N' permiso(s), ', @migrados,
                     N' permiso(s) migrados a roles y ', @textosCorregidos, N' texto(s) de modulos corregidos. dbo.empresa_rol_permisos sin cambios.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    THROW;
END CATCH;
GO

-- Resultado: el catalogo por modulo, las plantillas y los permisos migrados por rol
SELECT p.modulo, p.ambito, COUNT(*) AS permisos,
       SUM(CASE WHEN p.accion_legada IS NOT NULL THEN 1 ELSE 0 END) AS con_casilla_legada
FROM saas.permisos p
GROUP BY p.modulo, p.ambito
ORDER BY MIN(p.orden);

SELECT r.codigo, r.modulo, r.nombre, (SELECT COUNT(*) FROM saas.roles_plantilla_permisos l WHERE l.id_rol_plantilla = r.id_rol_plantilla) AS permisos
FROM saas.roles_plantilla r
ORDER BY r.orden;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita los 5 predicados de dbo.rol_permisos de la politica y borra las 4 tablas nuevas. dbo.empresa_rol_permisos nunca se toco,
--   asi que no se pierde ningun permiso. Se detiene si alguien ya edito los permisos de un rol (hay filas en rol_permisos que no son de
--   este script) o ya agrego permisos o plantillas propias: se perderian. NO devuelve los textos danados de dbo.modulos (era un
--   error, no un dato que se quiera recuperar).
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.rol_permisos', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.rol_permisos') AND p.index_id IN (0, 1) AND p.rows > 0)
    BEGIN
        -- sys.partitions no pasa por la RLS: cuenta las filas reales. Las de este script se vuelven a poder crear migrando de nuevo;
        -- si hay filas de otro origen, se detiene.
        DECLARE @t INT, @otras INT = 0, @f INT;
        DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id_tenant FROM saas.tenants;
        OPEN c; FETCH NEXT FROM c INTO @t;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC sys.sp_set_session_context @key = N'id_tenant', @value = @t;
            EXEC sys.sp_executesql N'SELECT @f = COUNT(*) FROM dbo.rol_permisos WHERE creado_por <> N''script:041'';', N'@f INT OUTPUT', @f = @f OUTPUT;
            SET @otras += @f;
            FETCH NEXT FROM c INTO @t;
        END
        CLOSE c; DEALLOCATE c;
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        IF @otras > 0
            THROW 50041, N'ROLLBACK 041 detenido: hay permisos de roles que no son de este script y se perderian. Revisar primero.', 1;
    END

    IF OBJECT_ID(N'saas.permisos', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM saas.permisos WHERE creado_por <> N'script:041')
        THROW 50041, N'ROLLBACK 041 detenido: hay permisos del catalogo que no son de este script. Revisar primero.', 1;
    IF OBJECT_ID(N'saas.roles_plantilla', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM saas.roles_plantilla WHERE creado_por <> N'script:041')
        THROW 50041, N'ROLLBACK 041 detenido: hay plantillas de rol que no son de este script. Revisar primero.', 1;

    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.rol_permisos,
    DROP BLOCK PREDICATE ON dbo.rol_permisos AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.rol_permisos AFTER UPDATE,
    DROP BLOCK PREDICATE ON dbo.rol_permisos BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.rol_permisos BEFORE DELETE;';

    -- Las filas de este script se borran (se pueden volver a migrar), cada tenant con su contexto.
    IF OBJECT_ID(N'dbo.rol_permisos', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
        DROP TABLE dbo.rol_permisos;
    END
    IF OBJECT_ID(N'saas.roles_plantilla_permisos', N'U') IS NOT NULL DROP TABLE saas.roles_plantilla_permisos;
    IF OBJECT_ID(N'saas.roles_plantilla', N'U') IS NOT NULL DROP TABLE saas.roles_plantilla;
    IF OBJECT_ID(N'saas.permisos', N'U') IS NOT NULL DROP TABLE saas.permisos;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key = N'id_tenant', @value = NULL;
    THROW;
END CATCH;
*/
