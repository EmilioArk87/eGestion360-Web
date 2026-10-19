-- ============================================================
-- Script   : 038_identidad_global.sql
-- Proposito: Paso F1.5 del plan de la Fase 1 (ADR-003, ADR-008, ADR-013, ADR-019 D19/D22/D23/D24, ADR-023): identidad global con membresias.
--            SOLO AGREGA estructura; no copia ni cambia ningun dato (la migracion de dbo.Users es el script 039). Crea 9 tablas:
--              Plano de control, esquema saas, SIN RLS (se leen antes de conocer el tenant, D19):
--                * saas.cuentas                   la cuenta de acceso global: una por ser humano (correo, clave BCrypt, estado, operador).
--                * saas.cuenta_alias              nombres de usuario que siguen entrando durante la transicion y correos secundarios que ya no.
--                * saas.membresias                cuenta <-> tenant, con estado y la persona del tenant que la representa.
--                * saas.cuenta_intentos           intentos fallidos y bloqueo temporal.
--                * saas.cuenta_mfa                secreto TOTP protegido por la aplicacion (nunca en claro) y anti-repeticion del codigo.
--                * saas.cuenta_mfa_recuperacion   codigos de recuperacion de MFA (solo su hash).
--                * saas.invitaciones              invitaciones a un tenant (solo el hash del token).
--                * saas.llaves_proteccion_datos   almacen de llaves de ASP.NET Core Data Protection (FriendlyName y Xml de DataProtectionKey).
--              Tabla de TENANT, esquema dbo, CON RLS:
--                * dbo.accesos_empresa            membresia <-> empresa <-> rol. Entra a la politica seg.pol_tenant con las mismas funciones que
--                                                 las demas tablas de tenant: 1 filtro y 4 bloqueos (199 -> 204 predicados, 40 -> 41 tablas).
--            Convenciones: auditoria completa (creado_por, fecha_creacion, modificado_por, fecha_modificacion) en las 5 tablas que edita una
--            persona (cuentas, cuenta_alias, membresias, accesos_empresa, cuenta_mfa); token_concurrencia ROWVERSION en las mismas;
--            las tablas tecnicas (intentos, recuperacion, llaves) y la invitacion (de un solo uso) llevan solo lo que su uso pide.
--            Claves compuestas con id_tenant primero (ADR-021): UX_membresias_tenant_id_membresia es la clave que referencia accesos_empresa.
--            Borrado en cascada solo en las tablas satelite de la cuenta (alias, intentos, mfa, recuperacion); membresias e invitaciones no.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-15
-- BD       : eBD_SPD
-- Requiere : 025 a 037 aplicados. La politica seg.pol_tenant puede estar ENCENDIDA o APAGADA: este script la deja como la encontro.
--            La version publicada de la aplicacion no necesita cambiar antes: ignora las tablas nuevas (EF solo mapea lo que conoce).
-- Rollback : ver la seccion ROLLBACK al final. Solo vale mientras las 9 tablas esten vacias (primero se revierte el 039).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta si:
--        a) faltan los esquemas saas o seg, saas.tenants con sus 4 tenants, id_tenant en las 41 tablas, las claves unicas (id_tenant, ...)
--           de empresas, personas y empresa_roles, o hay menos de 78 claves compuestas FKT_ (scripts 025 a 035);
--        b) la politica seg.pol_tenant no existe, o no tiene exactamente 199 predicados (aun sin accesos_empresa) o 204 (ya con ellos),
--           o alguna otra politica toca a dbo.accesos_empresa;
--        c) alguna de las 9 tablas existe a medias (unas si y otras no), no es una tabla, o ya existe con columnas distintas de las esperadas.
--   2. CAMBIO (una sola transaccion): crea las tablas, sus restricciones e indices y agrega dbo.accesos_empresa a la politica.
--   3. POSTCHECK dentro de la misma transaccion: 83 columnas con su tipo y nulabilidad, 60 restricciones e indices, 13 claves foraneas
--      confiables, 5 predicados nuevos con fn_tenant_actual (204 en total) y la politica en el mismo estado (encendida o apagada) en que estaba.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, lo dice y no hace nada.
--   * No lee ni escribe filas de ninguna tabla de tenant: funciona igual con la politica encendida y sin contexto de sesion.
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
IF OBJECT_ID(N'tempdb..#precheck_038') IS NOT NULL DROP TABLE #precheck_038;
IF OBJECT_ID(N'tempdb..#columnas_038') IS NOT NULL DROP TABLE #columnas_038;
IF OBJECT_ID(N'tempdb..#objetos_038') IS NOT NULL DROP TABLE #objetos_038;
IF OBJECT_ID(N'tempdb..#reales_038') IS NOT NULL DROP TABLE #reales_038;
IF OBJECT_ID(N'tempdb..#posterior_038') IS NOT NULL DROP TABLE #posterior_038;

-- Las 83 columnas esperadas de las 9 tablas: tabla, columna, tipo (con largo) y si admite nulo.
CREATE TABLE #columnas_038 (
    tabla   SYSNAME     COLLATE DATABASE_DEFAULT NOT NULL,
    columna SYSNAME     COLLATE DATABASE_DEFAULT NOT NULL,
    tipo    VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL,
    nulable BIT         NOT NULL,
    PRIMARY KEY (tabla, columna)
);

INSERT INTO #columnas_038 (tabla, columna, tipo, nulable) VALUES
    (N'saas.cuentas', N'id_cuenta', 'int', 0),
    (N'saas.cuentas', N'uid_publico', 'uniqueidentifier', 0),
    (N'saas.cuentas', N'correo', 'nvarchar(256)', 0),
    (N'saas.cuentas', N'nombre_usuario', 'nvarchar(50)', 1),
    (N'saas.cuentas', N'hash_clave', 'nvarchar(255)', 0),
    (N'saas.cuentas', N'debe_cambiar_clave', 'bit', 0),
    (N'saas.cuentas', N'estado', 'varchar(12)', 0),
    (N'saas.cuentas', N'es_operador', 'bit', 0),
    (N'saas.cuentas', N'fecha_ultimo_acceso', 'datetime2', 1),
    (N'saas.cuentas', N'id_user_legado', 'int', 1),
    (N'saas.cuentas', N'creado_por', 'nvarchar(100)', 0),
    (N'saas.cuentas', N'fecha_creacion', 'datetime2', 0),
    (N'saas.cuentas', N'modificado_por', 'nvarchar(100)', 1),
    (N'saas.cuentas', N'fecha_modificacion', 'datetime2', 1),
    (N'saas.cuentas', N'token_concurrencia', 'timestamp', 0),
    (N'saas.cuenta_alias', N'id_alias', 'int', 0),
    (N'saas.cuenta_alias', N'id_cuenta', 'int', 0),
    (N'saas.cuenta_alias', N'alias', 'nvarchar(256)', 0),
    (N'saas.cuenta_alias', N'tipo', 'varchar(10)', 0),
    (N'saas.cuenta_alias', N'puede_iniciar_sesion', 'bit', 0),
    (N'saas.cuenta_alias', N'id_user_legado', 'int', 1),
    (N'saas.cuenta_alias', N'creado_por', 'nvarchar(100)', 0),
    (N'saas.cuenta_alias', N'fecha_creacion', 'datetime2', 0),
    (N'saas.cuenta_alias', N'modificado_por', 'nvarchar(100)', 1),
    (N'saas.cuenta_alias', N'fecha_modificacion', 'datetime2', 1),
    (N'saas.cuenta_alias', N'token_concurrencia', 'timestamp', 0),
    (N'saas.membresias', N'id_membresia', 'int', 0),
    (N'saas.membresias', N'id_cuenta', 'int', 0),
    (N'saas.membresias', N'id_tenant', 'int', 0),
    (N'saas.membresias', N'id_persona', 'int', 1),
    (N'saas.membresias', N'estado', 'varchar(12)', 0),
    (N'saas.membresias', N'fecha_estado', 'datetime2', 0),
    (N'saas.membresias', N'creado_por', 'nvarchar(100)', 0),
    (N'saas.membresias', N'fecha_creacion', 'datetime2', 0),
    (N'saas.membresias', N'modificado_por', 'nvarchar(100)', 1),
    (N'saas.membresias', N'fecha_modificacion', 'datetime2', 1),
    (N'saas.membresias', N'token_concurrencia', 'timestamp', 0),
    (N'dbo.accesos_empresa', N'id_acceso_empresa', 'int', 0),
    (N'dbo.accesos_empresa', N'id_tenant', 'int', 0),
    (N'dbo.accesos_empresa', N'id_membresia', 'int', 0),
    (N'dbo.accesos_empresa', N'id_empresa', 'int', 0),
    (N'dbo.accesos_empresa', N'id_rol', 'int', 1),
    (N'dbo.accesos_empresa', N'rol_legado', 'varchar(20)', 0),
    (N'dbo.accesos_empresa', N'es_principal', 'bit', 0),
    (N'dbo.accesos_empresa', N'activo', 'bit', 0),
    (N'dbo.accesos_empresa', N'creado_por', 'nvarchar(100)', 0),
    (N'dbo.accesos_empresa', N'fecha_creacion', 'datetime2', 0),
    (N'dbo.accesos_empresa', N'modificado_por', 'nvarchar(100)', 1),
    (N'dbo.accesos_empresa', N'fecha_modificacion', 'datetime2', 1),
    (N'dbo.accesos_empresa', N'token_concurrencia', 'timestamp', 0),
    (N'saas.cuenta_intentos', N'id_cuenta', 'int', 0),
    (N'saas.cuenta_intentos', N'intentos_fallidos', 'int', 0),
    (N'saas.cuenta_intentos', N'bloqueada_hasta', 'datetime2', 1),
    (N'saas.cuenta_intentos', N'ultimo_fallo', 'datetime2', 1),
    (N'saas.cuenta_mfa', N'id_cuenta', 'int', 0),
    (N'saas.cuenta_mfa', N'secreto_protegido', 'nvarchar(500)', 0),
    (N'saas.cuenta_mfa', N'confirmada', 'bit', 0),
    (N'saas.cuenta_mfa', N'fecha_confirmacion', 'datetime2', 1),
    (N'saas.cuenta_mfa', N'ultimo_paso_usado', 'bigint', 1),
    (N'saas.cuenta_mfa', N'creado_por', 'nvarchar(100)', 0),
    (N'saas.cuenta_mfa', N'fecha_creacion', 'datetime2', 0),
    (N'saas.cuenta_mfa', N'modificado_por', 'nvarchar(100)', 1),
    (N'saas.cuenta_mfa', N'fecha_modificacion', 'datetime2', 1),
    (N'saas.cuenta_mfa', N'token_concurrencia', 'timestamp', 0),
    (N'saas.cuenta_mfa_recuperacion', N'id', 'int', 0),
    (N'saas.cuenta_mfa_recuperacion', N'id_cuenta', 'int', 0),
    (N'saas.cuenta_mfa_recuperacion', N'hash_codigo', 'char(64)', 0),
    (N'saas.cuenta_mfa_recuperacion', N'usado_en', 'datetime2', 1),
    (N'saas.cuenta_mfa_recuperacion', N'fecha_creacion', 'datetime2', 0),
    (N'saas.invitaciones', N'id_invitacion', 'int', 0),
    (N'saas.invitaciones', N'id_tenant', 'int', 0),
    (N'saas.invitaciones', N'correo', 'nvarchar(256)', 0),
    (N'saas.invitaciones', N'id_empresa', 'int', 1),
    (N'saas.invitaciones', N'id_rol', 'int', 1),
    (N'saas.invitaciones', N'token_hash', 'char(64)', 0),
    (N'saas.invitaciones', N'vence_en', 'datetime2', 0),
    (N'saas.invitaciones', N'usada_en', 'datetime2', 1),
    (N'saas.invitaciones', N'id_cuenta', 'int', 1),
    (N'saas.invitaciones', N'creada_por', 'nvarchar(100)', 0),
    (N'saas.invitaciones', N'fecha_creacion', 'datetime2', 0),
    (N'saas.llaves_proteccion_datos', N'id_llave', 'int', 0),
    (N'saas.llaves_proteccion_datos', N'nombre_amistoso', 'nvarchar(200)', 1),
    (N'saas.llaves_proteccion_datos', N'xml', 'nvarchar(max)', 1);

-- Las 60 restricciones e indices esperados. Tipo: PK, UQ (restriccion unica), C (CHECK), F (clave foranea), D (valor por defecto),
-- UI (indice unico) e IX (indice no unico).
CREATE TABLE #objetos_038 (
    tabla  SYSNAME     COLLATE DATABASE_DEFAULT NOT NULL,
    nombre SYSNAME     COLLATE DATABASE_DEFAULT NOT NULL,
    tipo   VARCHAR(2)  COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, nombre)
);

INSERT INTO #objetos_038 (tabla, nombre, tipo) VALUES
    (N'saas.cuentas', N'PK_cuentas', 'PK'),
    (N'saas.cuentas', N'UX_cuentas_uid_publico', 'UQ'),
    (N'saas.cuentas', N'UX_cuentas_correo', 'UI'),
    (N'saas.cuentas', N'UX_cuentas_nombre_usuario', 'UI'),
    (N'saas.cuentas', N'UX_cuentas_id_user_legado', 'UI'),
    (N'saas.cuentas', N'CK_cuentas_estado', 'C'),
    (N'saas.cuentas', N'DF_cuentas_uid_publico', 'D'),
    (N'saas.cuentas', N'DF_cuentas_debe_cambiar_clave', 'D'),
    (N'saas.cuentas', N'DF_cuentas_es_operador', 'D'),
    (N'saas.cuentas', N'DF_cuentas_fecha_creacion', 'D'),
    (N'saas.cuenta_alias', N'PK_cuenta_alias', 'PK'),
    (N'saas.cuenta_alias', N'UX_cuenta_alias_alias', 'UI'),
    (N'saas.cuenta_alias', N'UX_cuenta_alias_user_legado_tipo', 'UI'),
    (N'saas.cuenta_alias', N'IX_cuenta_alias_cuenta', 'IX'),
    (N'saas.cuenta_alias', N'CK_cuenta_alias_tipo', 'C'),
    (N'saas.cuenta_alias', N'FK_cuenta_alias_cuentas', 'F'),
    (N'saas.cuenta_alias', N'DF_cuenta_alias_puede_iniciar_sesion', 'D'),
    (N'saas.cuenta_alias', N'DF_cuenta_alias_fecha_creacion', 'D'),
    (N'saas.membresias', N'PK_membresias', 'PK'),
    (N'saas.membresias', N'UX_membresias_cuenta_tenant', 'UI'),
    (N'saas.membresias', N'UX_membresias_tenant_id_membresia', 'UI'),
    (N'saas.membresias', N'IX_membresias_persona', 'IX'),
    (N'saas.membresias', N'CK_membresias_estado', 'C'),
    (N'saas.membresias', N'FK_membresias_cuentas', 'F'),
    (N'saas.membresias', N'FK_membresias_tenants', 'F'),
    (N'saas.membresias', N'FKT_membresias_personas_id_persona', 'F'),
    (N'saas.membresias', N'DF_membresias_fecha_estado', 'D'),
    (N'saas.membresias', N'DF_membresias_fecha_creacion', 'D'),
    (N'dbo.accesos_empresa', N'PK_accesos_empresa', 'PK'),
    (N'dbo.accesos_empresa', N'UX_accesos_empresa_membresia_empresa', 'UI'),
    (N'dbo.accesos_empresa', N'UX_accesos_empresa_principal', 'UI'),
    (N'dbo.accesos_empresa', N'IX_accesos_empresa_tenant', 'IX'),
    (N'dbo.accesos_empresa', N'CK_accesos_empresa_rol_legado', 'C'),
    (N'dbo.accesos_empresa', N'FK_accesos_empresa_tenants', 'F'),
    (N'dbo.accesos_empresa', N'FKT_accesos_empresa_membresias_id_membresia', 'F'),
    (N'dbo.accesos_empresa', N'FKT_accesos_empresa_empresas_id_empresa', 'F'),
    (N'dbo.accesos_empresa', N'FKT_accesos_empresa_empresa_roles_id_rol', 'F'),
    (N'dbo.accesos_empresa', N'DF_accesos_empresa_es_principal', 'D'),
    (N'dbo.accesos_empresa', N'DF_accesos_empresa_activo', 'D'),
    (N'dbo.accesos_empresa', N'DF_accesos_empresa_fecha_creacion', 'D'),
    (N'saas.cuenta_intentos', N'PK_cuenta_intentos', 'PK'),
    (N'saas.cuenta_intentos', N'FK_cuenta_intentos_cuentas', 'F'),
    (N'saas.cuenta_intentos', N'DF_cuenta_intentos_intentos_fallidos', 'D'),
    (N'saas.cuenta_mfa', N'PK_cuenta_mfa', 'PK'),
    (N'saas.cuenta_mfa', N'CK_cuenta_mfa_confirmacion', 'C'),
    (N'saas.cuenta_mfa', N'FK_cuenta_mfa_cuentas', 'F'),
    (N'saas.cuenta_mfa', N'DF_cuenta_mfa_confirmada', 'D'),
    (N'saas.cuenta_mfa', N'DF_cuenta_mfa_fecha_creacion', 'D'),
    (N'saas.cuenta_mfa_recuperacion', N'PK_cuenta_mfa_recuperacion', 'PK'),
    (N'saas.cuenta_mfa_recuperacion', N'UX_cuenta_mfa_recuperacion_cuenta_codigo', 'UI'),
    (N'saas.cuenta_mfa_recuperacion', N'FK_cuenta_mfa_recuperacion_cuentas', 'F'),
    (N'saas.cuenta_mfa_recuperacion', N'DF_cuenta_mfa_recuperacion_fecha_creacion', 'D'),
    (N'saas.invitaciones', N'PK_invitaciones', 'PK'),
    (N'saas.invitaciones', N'UX_invitaciones_token_hash', 'UI'),
    (N'saas.invitaciones', N'IX_invitaciones_tenant', 'IX'),
    (N'saas.invitaciones', N'IX_invitaciones_correo', 'IX'),
    (N'saas.invitaciones', N'FK_invitaciones_tenants', 'F'),
    (N'saas.invitaciones', N'FK_invitaciones_cuentas', 'F'),
    (N'saas.invitaciones', N'DF_invitaciones_fecha_creacion', 'D'),
    (N'saas.llaves_proteccion_datos', N'PK_llaves_proteccion_datos', 'PK');

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT, @existentes INT, @conTabla INT, @predAcceso INT;

-- a) lo que dejaron los scripts 025 a 037
IF SCHEMA_ID(N'saas') IS NULL SET @errores += N' Falta el esquema saas (script 025).';
IF SCHEMA_ID(N'seg') IS NULL SET @errores += N' Falta el esquema seg (script 025).';
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL
    SET @errores += N' Falta saas.tenants (script 025).';
ELSE
BEGIN
    EXEC sys.sp_executesql N'SELECT @m = COUNT(*) FROM saas.tenants WHERE codigo IN (''SIP'', ''DEMO'', ''IISE'', ''TRANSGAR'');',
        N'@m INT OUTPUT', @m = @n OUTPUT;
    IF @n <> 4 SET @errores += CONCAT(N' saas.tenants tiene ', @n, N' de los 4 tenants de D18 (SIP, DEMO, IISE, TRANSGAR).');
END

-- 41 tablas con id_tenant antes de este script; 42 despues (se suma dbo.accesos_empresa), para que repetirlo no falle.
SELECT @n = COUNT(*) FROM sys.columns c JOIN sys.tables tb ON tb.object_id = c.object_id AND tb.schema_id = SCHEMA_ID(N'dbo') WHERE c.name = N'id_tenant';
IF @n <> 41 + CASE WHEN OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NULL THEN 0 ELSE 1 END
    SET @errores += CONCAT(N' Hay ', @n, N' tablas de dbo con id_tenant y se esperaban 41, mas accesos_empresa si ya existe (scripts 025 a 030).');

SELECT @n = COUNT(*) FROM sys.columns c JOIN sys.tables tb ON tb.object_id = c.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
WHERE c.name = N'id_tenant' AND c.is_nullable = 1;
IF @n <> 2 SET @errores += CONCAT(N' Hay ', @n, N' tablas con id_tenant nulable y se esperaban 2, las mixtas (scripts 031 a 035).');

SELECT @errores += N' Falta la clave unica ' + v.nombre + N' (scripts 031 y 032).'
FROM (VALUES (N'empresas', N'UX_empresas_tenant_id_empresa'), (N'personas', N'UX_personas_tenant_id_persona'),
             (N'empresa_roles', N'UX_empresa_roles_tenant_id_rol')) v (tabla, nombre)
WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.nombre AND i.object_id = OBJECT_ID(N'dbo.' + v.tabla) AND i.is_unique = 1);

SELECT @n = COUNT(*) FROM sys.foreign_keys WHERE name LIKE N'FKT[_]%' AND schema_id = SCHEMA_ID(N'dbo');
IF @n < 78 SET @errores += CONCAT(N' Hay ', @n, N' claves compuestas FKT_ en dbo y se esperaban al menos 78 (scripts 031 a 035).');

-- b) la politica: 199 predicados (sin accesos_empresa) o 204 (con ellos); ninguna otra politica sobre la tabla nueva
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';
ELSE
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    SELECT @predAcceso = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa');

    IF @predAcceso = 0 AND @n <> 199
        SET @errores += CONCAT(N' La politica seg.pol_tenant tiene ', @n, N' predicados y se esperaban 199 (accesos_empresa aun no esta en ella).');
    ELSE IF @predAcceso = 5 AND @n <> 204
        SET @errores += CONCAT(N' La politica seg.pol_tenant tiene ', @n, N' predicados y se esperaban 204 (accesos_empresa ya esta en ella).');
    ELSE IF @predAcceso NOT IN (0, 5)
        SET @errores += CONCAT(N' La politica tiene ', @predAcceso, N' predicados sobre accesos_empresa y se esperaban 0 o 5.');
END

SELECT @errores += N' ' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND p.target_object_id = OBJECT_ID(N'dbo.accesos_empresa');

-- c) las 9 tablas: ninguna a medias, ninguna que no sea tabla; si ya existen, con las columnas esperadas
SELECT @existentes = COUNT(DISTINCT k.tabla) FROM #columnas_038 k WHERE OBJECT_ID(k.tabla, N'U') IS NOT NULL;
SELECT @conTabla = COUNT(DISTINCT k.tabla) FROM #columnas_038 k WHERE OBJECT_ID(k.tabla) IS NOT NULL;
IF @conTabla <> @existentes
    SELECT @errores += N' ' + k.tabla + N' existe pero no es una tabla.' FROM (SELECT DISTINCT tabla FROM #columnas_038) k
    WHERE OBJECT_ID(k.tabla) IS NOT NULL AND OBJECT_ID(k.tabla, N'U') IS NULL;
IF @existentes NOT IN (0, 9)
    SELECT @errores += N' La tabla ' + k.tabla + N' no existe y otras 8 si: el script se aplico a medias, revise antes de seguir.'
    FROM (SELECT DISTINCT tabla FROM #columnas_038) k WHERE OBJECT_ID(k.tabla, N'U') IS NULL;

IF @existentes = 9
BEGIN
    SELECT (SCHEMA_NAME(o.schema_id) + N'.' + o.name) COLLATE DATABASE_DEFAULT AS tabla, c.name COLLATE DATABASE_DEFAULT AS columna,
           CONCAT(TYPE_NAME(c.user_type_id),
                  CASE WHEN TYPE_NAME(c.user_type_id) IN (N'varchar', N'char', N'varbinary', N'binary')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS NVARCHAR(10)) END, N')')
                       WHEN TYPE_NAME(c.user_type_id) IN (N'nvarchar', N'nchar')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS NVARCHAR(10)) END, N')')
                       ELSE N'' END) COLLATE DATABASE_DEFAULT AS tipo,
           c.is_nullable AS nulable
    INTO #reales_038
    FROM sys.columns c JOIN sys.objects o ON o.object_id = c.object_id AND o.type = N'U'
    WHERE (SCHEMA_NAME(o.schema_id) + N'.' + o.name) COLLATE DATABASE_DEFAULT IN (SELECT DISTINCT tabla FROM #columnas_038);

    SELECT @errores += N' Falta o difiere la columna ' + x.tabla + N'.' + x.columna + N'.'
    FROM (SELECT tabla, columna, tipo, nulable FROM #columnas_038
          EXCEPT SELECT tabla, columna, CAST(tipo AS VARCHAR(30)), nulable FROM #reales_038) x;

    SELECT @errores += N' ' + x.tabla + N' tiene una columna no prevista: ' + x.columna + N'.'
    FROM (SELECT tabla, columna FROM #reales_038 EXCEPT SELECT tabla, columna FROM #columnas_038) x;
END

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 038 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50038, @mensaje, 1;
END

CREATE TABLE #precheck_038 (ok BIT NOT NULL);
INSERT INTO #precheck_038 (ok) VALUES (1);

DECLARE @textoPolitica NVARCHAR(100) = CASE WHEN (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) = 1
                                            THEN N'ENCENDIDA' ELSE N'APAGADA' END;
PRINT CONCAT(N'PRECHECK 038 correcto. Tablas: ', CASE WHEN @existentes = 9 THEN N'las 9 ya existen' ELSE N'9 por crear' END,
             N'; politica seg.pol_tenant ', @textoPolitica, N' con ', @n, N' predicados.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_038') IS NULL
    THROW 50038, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @creadas INT = 0, @malas INT, @msg NVARCHAR(2048);
DECLARE @estabaEncendida BIT = (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'));
DECLARE @fkAntes INT = (SELECT COUNT(*) FROM sys.foreign_keys);
DECLARE @fkManifiestoAntes INT = (SELECT COUNT(*) FROM #objetos_038 k
                                  WHERE k.tipo = N'F' AND EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name COLLATE DATABASE_DEFAULT = k.nombre AND f.parent_object_id = OBJECT_ID(k.tabla)));
DECLARE @predAntes INT = (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant'));

BEGIN TRY
    BEGIN TRANSACTION;

    -- saas.cuentas: la cuenta de acceso global
    IF OBJECT_ID(N'saas.cuentas', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.cuentas (
            id_cuenta           INT              IDENTITY(1,1) NOT NULL,
            uid_publico         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_cuentas_uid_publico DEFAULT NEWSEQUENTIALID(),
            correo              NVARCHAR(256)    NOT NULL,
            nombre_usuario      NVARCHAR(50)     NULL,
            hash_clave          NVARCHAR(255)    NOT NULL,
            debe_cambiar_clave  BIT              NOT NULL CONSTRAINT DF_cuentas_debe_cambiar_clave DEFAULT 0,
            estado              VARCHAR(12)      NOT NULL,
            es_operador         BIT              NOT NULL CONSTRAINT DF_cuentas_es_operador DEFAULT 0,
            fecha_ultimo_acceso DATETIME2        NULL,
            id_user_legado      INT              NULL,
            creado_por          NVARCHAR(100)    NOT NULL,
            fecha_creacion      DATETIME2        NOT NULL CONSTRAINT DF_cuentas_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por      NVARCHAR(100)    NULL,
            fecha_modificacion  DATETIME2        NULL,
            token_concurrencia  ROWVERSION       NOT NULL,
            CONSTRAINT PK_cuentas PRIMARY KEY CLUSTERED (id_cuenta),
            CONSTRAINT UX_cuentas_uid_publico UNIQUE (uid_publico),
            CONSTRAINT CK_cuentas_estado CHECK (estado IN ('activa', 'inactiva', 'invitada'))
        );
        CREATE UNIQUE INDEX UX_cuentas_correo ON saas.cuentas (correo);
        CREATE UNIQUE INDEX UX_cuentas_nombre_usuario ON saas.cuentas (nombre_usuario) WHERE nombre_usuario IS NOT NULL;
        -- Rastro de dbo.Users.Id de donde salio la cuenta (script 039); se retira junto con Users en F1.6.
        CREATE UNIQUE INDEX UX_cuentas_id_user_legado ON saas.cuentas (id_user_legado) WHERE id_user_legado IS NOT NULL;
        SET @creadas += 1;
    END

    -- saas.cuenta_alias: nombres de usuario de transicion y correos secundarios
    IF OBJECT_ID(N'saas.cuenta_alias', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.cuenta_alias (
            id_alias             INT           IDENTITY(1,1) NOT NULL,
            id_cuenta            INT           NOT NULL,
            alias                NVARCHAR(256) NOT NULL,
            tipo                 VARCHAR(10)   NOT NULL,
            puede_iniciar_sesion BIT           NOT NULL CONSTRAINT DF_cuenta_alias_puede_iniciar_sesion DEFAULT 1,
            id_user_legado       INT           NULL,
            creado_por           NVARCHAR(100) NOT NULL,
            fecha_creacion       DATETIME2     NOT NULL CONSTRAINT DF_cuenta_alias_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por       NVARCHAR(100) NULL,
            fecha_modificacion   DATETIME2     NULL,
            token_concurrencia   ROWVERSION    NOT NULL,
            CONSTRAINT PK_cuenta_alias PRIMARY KEY CLUSTERED (id_alias),
            CONSTRAINT CK_cuenta_alias_tipo CHECK (tipo IN ('usuario', 'correo')),
            CONSTRAINT FK_cuenta_alias_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX UX_cuenta_alias_alias ON saas.cuenta_alias (alias);
        -- Rastro de dbo.Users.Id del que salio el alias (script 039): a lo mas un alias de usuario y uno de correo por usuario legado.
        CREATE UNIQUE INDEX UX_cuenta_alias_user_legado_tipo ON saas.cuenta_alias (id_user_legado, tipo) WHERE id_user_legado IS NOT NULL;
        CREATE INDEX IX_cuenta_alias_cuenta ON saas.cuenta_alias (id_cuenta);
        SET @creadas += 1;
    END

    -- saas.membresias: cuenta <-> tenant
    IF OBJECT_ID(N'saas.membresias', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.membresias (
            id_membresia       INT           IDENTITY(1,1) NOT NULL,
            id_cuenta          INT           NOT NULL,
            id_tenant          INT           NOT NULL,
            id_persona         INT           NULL,
            estado             VARCHAR(12)   NOT NULL,
            fecha_estado       DATETIME2     NOT NULL CONSTRAINT DF_membresias_fecha_estado DEFAULT SYSUTCDATETIME(),
            creado_por         NVARCHAR(100) NOT NULL,
            fecha_creacion     DATETIME2     NOT NULL CONSTRAINT DF_membresias_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por     NVARCHAR(100) NULL,
            fecha_modificacion DATETIME2     NULL,
            token_concurrencia ROWVERSION    NOT NULL,
            CONSTRAINT PK_membresias PRIMARY KEY CLUSTERED (id_membresia),
            CONSTRAINT CK_membresias_estado CHECK (estado IN ('invitada', 'activa', 'suspendida')),
            CONSTRAINT FK_membresias_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta),
            CONSTRAINT FK_membresias_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            -- La persona tiene que ser del mismo tenant (ADR-021). Con id_persona nulo la clave no se evalua.
            CONSTRAINT FKT_membresias_personas_id_persona FOREIGN KEY (id_tenant, id_persona) REFERENCES dbo.personas (id_tenant, id_persona)
        );
        CREATE UNIQUE INDEX UX_membresias_cuenta_tenant ON saas.membresias (id_cuenta, id_tenant);
        -- Clave que referencia dbo.accesos_empresa con id_tenant primero (ADR-021).
        CREATE UNIQUE INDEX UX_membresias_tenant_id_membresia ON saas.membresias (id_tenant, id_membresia);
        CREATE INDEX IX_membresias_persona ON saas.membresias (id_tenant, id_persona) WHERE id_persona IS NOT NULL;
        SET @creadas += 1;
    END

    -- dbo.accesos_empresa: membresia <-> empresa <-> rol. TABLA DE TENANT: lleva id_tenant y entra a la politica RLS.
    IF OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.accesos_empresa (
            id_acceso_empresa  INT           IDENTITY(1,1) NOT NULL,
            id_tenant          INT           NOT NULL,
            id_membresia       INT           NOT NULL,
            id_empresa         INT           NOT NULL,
            id_rol             INT           NULL,
            rol_legado         VARCHAR(20)   NOT NULL,
            es_principal       BIT           NOT NULL CONSTRAINT DF_accesos_empresa_es_principal DEFAULT 0,
            activo             BIT           NOT NULL CONSTRAINT DF_accesos_empresa_activo DEFAULT 1,
            creado_por         NVARCHAR(100) NOT NULL,
            fecha_creacion     DATETIME2     NOT NULL CONSTRAINT DF_accesos_empresa_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por     NVARCHAR(100) NULL,
            fecha_modificacion DATETIME2     NULL,
            token_concurrencia ROWVERSION    NOT NULL,
            CONSTRAINT PK_accesos_empresa PRIMARY KEY CLUSTERED (id_acceso_empresa),
            -- rol_legado es el rol de texto de Users.Role; se retira en F1.6 cuando id_rol sea obligatorio.
            CONSTRAINT CK_accesos_empresa_rol_legado CHECK (rol_legado IN ('empresa_admin', 'empresa_user')),
            CONSTRAINT FK_accesos_empresa_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_accesos_empresa_membresias_id_membresia FOREIGN KEY (id_tenant, id_membresia) REFERENCES saas.membresias (id_tenant, id_membresia),
            CONSTRAINT FKT_accesos_empresa_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT FKT_accesos_empresa_empresa_roles_id_rol FOREIGN KEY (id_tenant, id_rol) REFERENCES dbo.empresa_roles (id_tenant, id_rol)
        );
        CREATE UNIQUE INDEX UX_accesos_empresa_membresia_empresa ON dbo.accesos_empresa (id_membresia, id_empresa);
        -- Como mucho una empresa principal (la que se abre al iniciar sesion) por membresia.
        CREATE UNIQUE INDEX UX_accesos_empresa_principal ON dbo.accesos_empresa (id_membresia) WHERE es_principal = 1;
        CREATE INDEX IX_accesos_empresa_tenant ON dbo.accesos_empresa (id_tenant);
        SET @creadas += 1;
    END

    -- saas.cuenta_intentos: intentos fallidos y bloqueo temporal
    IF OBJECT_ID(N'saas.cuenta_intentos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.cuenta_intentos (
            id_cuenta         INT       NOT NULL,
            intentos_fallidos INT       NOT NULL CONSTRAINT DF_cuenta_intentos_intentos_fallidos DEFAULT 0,
            bloqueada_hasta   DATETIME2 NULL,
            ultimo_fallo      DATETIME2 NULL,
            CONSTRAINT PK_cuenta_intentos PRIMARY KEY CLUSTERED (id_cuenta),
            CONSTRAINT FK_cuenta_intentos_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta) ON DELETE CASCADE
        );
        SET @creadas += 1;
    END

    -- saas.cuenta_mfa: secreto TOTP (lo protege la aplicacion con Data Protection; nunca se guarda en claro)
    IF OBJECT_ID(N'saas.cuenta_mfa', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.cuenta_mfa (
            id_cuenta          INT           NOT NULL,
            secreto_protegido  NVARCHAR(500) NOT NULL,
            confirmada         BIT           NOT NULL CONSTRAINT DF_cuenta_mfa_confirmada DEFAULT 0,
            fecha_confirmacion DATETIME2     NULL,
            ultimo_paso_usado  BIGINT        NULL,
            creado_por         NVARCHAR(100) NOT NULL,
            fecha_creacion     DATETIME2     NOT NULL CONSTRAINT DF_cuenta_mfa_fecha_creacion DEFAULT SYSUTCDATETIME(),
            modificado_por     NVARCHAR(100) NULL,
            fecha_modificacion DATETIME2     NULL,
            token_concurrencia ROWVERSION    NOT NULL,
            CONSTRAINT PK_cuenta_mfa PRIMARY KEY CLUSTERED (id_cuenta),
            CONSTRAINT CK_cuenta_mfa_confirmacion CHECK (confirmada = 0 OR fecha_confirmacion IS NOT NULL),
            CONSTRAINT FK_cuenta_mfa_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta) ON DELETE CASCADE
        );
        SET @creadas += 1;
    END

    -- saas.cuenta_mfa_recuperacion: codigos de recuperacion (solo el hash SHA-256 en hexadecimal)
    IF OBJECT_ID(N'saas.cuenta_mfa_recuperacion', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.cuenta_mfa_recuperacion (
            id             INT       IDENTITY(1,1) NOT NULL,
            id_cuenta      INT       NOT NULL,
            hash_codigo    CHAR(64)  NOT NULL,
            usado_en       DATETIME2 NULL,
            fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_cuenta_mfa_recuperacion_fecha_creacion DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_cuenta_mfa_recuperacion PRIMARY KEY CLUSTERED (id),
            CONSTRAINT FK_cuenta_mfa_recuperacion_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX UX_cuenta_mfa_recuperacion_cuenta_codigo ON saas.cuenta_mfa_recuperacion (id_cuenta, hash_codigo);
        SET @creadas += 1;
    END

    -- saas.invitaciones: invitaciones a un tenant (solo el hash del token)
    IF OBJECT_ID(N'saas.invitaciones', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.invitaciones (
            id_invitacion  INT           IDENTITY(1,1) NOT NULL,
            id_tenant      INT           NOT NULL,
            correo         NVARCHAR(256) NOT NULL,
            id_empresa     INT           NULL,
            id_rol         INT           NULL,
            token_hash     CHAR(64)      NOT NULL,
            vence_en       DATETIME2     NOT NULL,
            usada_en       DATETIME2     NULL,
            id_cuenta      INT           NULL,
            creada_por     NVARCHAR(100) NOT NULL,
            fecha_creacion DATETIME2     NOT NULL CONSTRAINT DF_invitaciones_fecha_creacion DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_invitaciones PRIMARY KEY CLUSTERED (id_invitacion),
            CONSTRAINT FK_invitaciones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FK_invitaciones_cuentas FOREIGN KEY (id_cuenta) REFERENCES saas.cuentas (id_cuenta)
        );
        CREATE UNIQUE INDEX UX_invitaciones_token_hash ON saas.invitaciones (token_hash);
        CREATE INDEX IX_invitaciones_tenant ON saas.invitaciones (id_tenant);
        CREATE INDEX IX_invitaciones_correo ON saas.invitaciones (correo);
        SET @creadas += 1;
    END

    -- saas.llaves_proteccion_datos: almacen de llaves de ASP.NET Core Data Protection (el modelo de EF mapea FriendlyName y Xml)
    IF OBJECT_ID(N'saas.llaves_proteccion_datos', N'U') IS NULL
    BEGIN
        CREATE TABLE saas.llaves_proteccion_datos (
            id_llave        INT           IDENTITY(1,1) NOT NULL,
            nombre_amistoso NVARCHAR(200) NULL,
            [xml]           NVARCHAR(MAX) NULL,
            CONSTRAINT PK_llaves_proteccion_datos PRIMARY KEY CLUSTERED (id_llave)
        );
        SET @creadas += 1;
    END

    -- Politica: accesos_empresa entra con las mismas funciones que las demas tablas de tenant (1 filtro y 4 bloqueos).
    -- ALTER SECURITY POLICY ... ADD no cambia el estado (encendida o apagada) de la politica.
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.accesos_empresa,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.accesos_empresa AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.accesos_empresa AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.accesos_empresa BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON dbo.accesos_empresa BEFORE DELETE;';
        SET @creadas += 1;
    END

    -- POSTCHECK: si algo no cuadra, se revierte todo.
    SELECT (SCHEMA_NAME(o.schema_id) + N'.' + o.name) COLLATE DATABASE_DEFAULT AS tabla, c.name COLLATE DATABASE_DEFAULT AS columna,
           CONCAT(TYPE_NAME(c.user_type_id),
                  CASE WHEN TYPE_NAME(c.user_type_id) IN (N'varchar', N'char', N'varbinary', N'binary')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS NVARCHAR(10)) END, N')')
                       WHEN TYPE_NAME(c.user_type_id) IN (N'nvarchar', N'nchar')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS NVARCHAR(10)) END, N')')
                       ELSE N'' END) COLLATE DATABASE_DEFAULT AS tipo,
           c.is_nullable AS nulable
    INTO #posterior_038
    FROM sys.columns c JOIN sys.objects o ON o.object_id = c.object_id AND o.type = N'U'
    WHERE (SCHEMA_NAME(o.schema_id) + N'.' + o.name) COLLATE DATABASE_DEFAULT IN (SELECT DISTINCT tabla FROM #columnas_038);

    SELECT @malas = COUNT(*) FROM (SELECT tabla, columna, tipo, nulable FROM #columnas_038
                                   EXCEPT SELECT tabla, columna, CAST(tipo AS VARCHAR(30)), nulable FROM #posterior_038) x;
    IF @malas > 0 THROW 50038, N'POSTCHECK: alguna columna falta o difiere de lo esperado. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM (SELECT tabla, columna FROM #posterior_038 EXCEPT SELECT tabla, columna FROM #columnas_038) x;
    IF @malas > 0 THROW 50038, N'POSTCHECK: alguna tabla tiene una columna no prevista. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM #objetos_038 k
    WHERE NOT (
        (k.tipo IN (N'PK', N'UQ', N'C', N'F', N'D')
            AND EXISTS (SELECT 1 FROM sys.objects x WHERE x.name COLLATE DATABASE_DEFAULT = k.nombre AND x.parent_object_id = OBJECT_ID(k.tabla) AND x.type COLLATE DATABASE_DEFAULT = k.tipo))
        OR (k.tipo = N'UI' AND EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name COLLATE DATABASE_DEFAULT = k.nombre AND i.object_id = OBJECT_ID(k.tabla) AND i.is_unique = 1))
        OR (k.tipo = N'IX' AND EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name COLLATE DATABASE_DEFAULT = k.nombre AND i.object_id = OBJECT_ID(k.tabla) AND i.is_unique = 0)));
    IF @malas > 0 THROW 50038, N'POSTCHECK: falta alguna de las 60 restricciones o indices esperados. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM #objetos_038 k JOIN sys.foreign_keys f ON f.name COLLATE DATABASE_DEFAULT = k.nombre AND f.parent_object_id = OBJECT_ID(k.tabla)
    WHERE k.tipo = N'F' AND (f.is_disabled = 1 OR f.is_not_trusted = 1);
    IF @malas > 0 THROW 50038, N'POSTCHECK: alguna clave foranea nueva quedo deshabilitada o sin confianza. Se revierte.', 1;

    -- Las 13 claves foraneas nuevas son las unicas que cambian el total de la base (152 antes del 038, 165 despues).
    SELECT @malas = COUNT(*) FROM sys.foreign_keys;
    IF @malas <> @fkAntes + 13 - @fkManifiestoAntes
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: la base tiene ', @malas, N' claves foraneas y se esperaban ', @fkAntes + 13 - @fkManifiestoAntes, N'. Se revierte.');
        THROW 50038, @msg, 1;
    END

    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa')
        AND predicate_definition LIKE N'%fn_tenant_actual%';
    IF @malas <> 5 THROW 50038, N'POSTCHECK: accesos_empresa debe tener 5 predicados con fn_tenant_actual. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa') AND predicate_type_desc = N'FILTER';
    IF @malas <> 1 THROW 50038, N'POSTCHECK: accesos_empresa debe tener un solo filtro. Se revierte.', 1;

    SELECT @malas = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    IF @malas <> 204
    BEGIN
        SET @msg = CONCAT(N'POSTCHECK: la politica tiene ', @malas, N' predicados y se esperaban 204. Se revierte.');
        THROW 50038, @msg, 1;
    END

    IF (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) <> @estabaEncendida
        THROW 50038, N'POSTCHECK: la politica cambio de estado (encendida/apagada). Se revierte.', 1;

    -- Solo cuentan las de este script (y tenants): el 040 puede haber agregado sus 5 tablas antes o despues.
    SELECT @malas = COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'saas') AND type = N'U'
        AND name IN (N'tenants', N'cuentas', N'cuenta_alias', N'membresias', N'cuenta_intentos', N'cuenta_mfa', N'cuenta_mfa_recuperacion', N'invitaciones', N'llaves_proteccion_datos');
    IF @malas <> 9 THROW 50038, N'POSTCHECK: el esquema saas debe quedar con 9 tablas (tenants y las 8 nuevas). Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @creadas = 0
        PRINT N'Nada que hacer: las 9 tablas y los predicados de accesos_empresa ya estaban aplicados.';
    ELSE
        PRINT CONCAT(N'CAMBIO 038 aplicado: ', @creadas, N' cambio(s) (tablas creadas y/o predicados agregados). Politica ',
                     CASE WHEN @estabaEncendida = 1 THEN N'ENCENDIDA' ELSE N'APAGADA' END, N' como estaba; predicados: ', @predAntes, N' -> 204.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado: las 9 tablas con sus filas reales (sys.partitions no pasa por la RLS) y los predicados de la politica
SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name AS tabla,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) AS filas,
       (SELECT COUNT(*) FROM sys.security_predicates x WHERE x.object_id = OBJECT_ID(N'seg.pol_tenant') AND x.target_object_id = t.object_id) AS predicados_rls
FROM sys.tables t
WHERE (t.schema_id = SCHEMA_ID(N'saas') AND t.name <> N'tenants') OR (t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'accesos_empresa')
ORDER BY t.schema_id DESC, t.name;
SELECT p.name AS politica, p.is_enabled AS encendida, (SELECT COUNT(*) FROM sys.security_predicates x WHERE x.object_id = p.object_id) AS predicados
FROM sys.security_policies p WHERE p.name = N'pol_tenant';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita dbo.accesos_empresa de la politica (sus 5 predicados) y borra las 9 tablas. Solo vale mientras estan VACIAS: si el
--   script 039 ya copio las cuentas, se revierte primero el 039. El estado de la politica (encendida o apagada) no cambia.
--   La guarda de abajo aborta si alguna tabla tiene filas (cuentas nuevas, claves de MFA, invitaciones o llaves de Data
--   Protection se perderian). sys.partitions no pasa por la RLS, asi que cuenta las filas reales de accesos_empresa.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id
               WHERE p.index_id IN (0, 1) AND p.rows > 0
                 AND ((t.schema_id = SCHEMA_ID(N'saas') AND t.name IN (N'cuentas', N'cuenta_alias', N'membresias', N'cuenta_intentos', N'cuenta_mfa',
                                                                      N'cuenta_mfa_recuperacion', N'invitaciones', N'llaves_proteccion_datos'))
                      OR (t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'accesos_empresa')))
        THROW 50038, N'ROLLBACK 038 abortado: alguna de las 9 tablas tiene filas. Revierta primero el 039 y verifique que no haya datos nuevos.', 1;

    IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa'))
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    DROP FILTER PREDICATE ON dbo.accesos_empresa,
    DROP BLOCK PREDICATE ON dbo.accesos_empresa AFTER INSERT,
    DROP BLOCK PREDICATE ON dbo.accesos_empresa AFTER UPDATE,
    DROP BLOCK PREDICATE ON dbo.accesos_empresa BEFORE UPDATE,
    DROP BLOCK PREDICATE ON dbo.accesos_empresa BEFORE DELETE;';

    IF OBJECT_ID(N'dbo.accesos_empresa', N'U') IS NOT NULL DROP TABLE dbo.accesos_empresa;
    IF OBJECT_ID(N'saas.invitaciones', N'U') IS NOT NULL DROP TABLE saas.invitaciones;
    IF OBJECT_ID(N'saas.cuenta_mfa_recuperacion', N'U') IS NOT NULL DROP TABLE saas.cuenta_mfa_recuperacion;
    IF OBJECT_ID(N'saas.cuenta_mfa', N'U') IS NOT NULL DROP TABLE saas.cuenta_mfa;
    IF OBJECT_ID(N'saas.cuenta_intentos', N'U') IS NOT NULL DROP TABLE saas.cuenta_intentos;
    IF OBJECT_ID(N'saas.membresias', N'U') IS NOT NULL DROP TABLE saas.membresias;
    IF OBJECT_ID(N'saas.cuenta_alias', N'U') IS NOT NULL DROP TABLE saas.cuenta_alias;
    IF OBJECT_ID(N'saas.cuentas', N'U') IS NOT NULL DROP TABLE saas.cuentas;
    IF OBJECT_ID(N'saas.llaves_proteccion_datos', N'U') IS NOT NULL DROP TABLE saas.llaves_proteccion_datos;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
