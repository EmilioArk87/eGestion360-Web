-- ============================================================
-- Archivo  : verificar_despues_f15.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de los scripts 038 y 039 (F1.5: identidad global con membresias) con la politica seg.pol_tenant ENCENDIDA (estado
--            actual de produccion). Comprueba:
--              * las 9 tablas de la identidad global (8 del esquema saas y dbo.accesos_empresa): que existen, sus 83 columnas con tipo y
--                nulabilidad, ninguna columna no prevista, las 60 restricciones e indices y las 13 claves foraneas nuevas (confiables);
--              * las cifras de la base: 57 tablas en dbo, 9 en saas, 165 claves foraneas (152 + 13), 81 claves compuestas FKT_ en dbo,
--                44 claves hacia saas.tenants y 42 columnas id_tenant en dbo;
--              * la politica ENCENDIDA con 204 predicados sobre 41 tablas (40 de filtro y 164 de bloqueo), dbo.accesos_empresa con su filtro
--                y sus 4 bloqueos de fn_tenant_actual, ninguna tabla de saas en la politica y domain_events fuera;
--              * que esta conexion NO fija contexto de sesion: sin contexto, la politica no deja ver ninguna fila de accesos_empresa;
--              * dbo.Users intacta (8 filas) y lo migrado por el 039: 6 cuentas (2 activas, 4 inactivas, 1 operador), 4 alias,
--                5 membresias (3 activas, 2 suspendidas) y 5 accesos por empresa; las claves copiadas iguales a las de dbo.Users;
--            Es el estado EXACTO de este momento: despues de 038 solamente da FALLA en los datos (no hay cuentas) y antes de 038 da FALLA en casi todo.
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME_F15.md). No escribe nada; las filas de las tablas de tenant se cuentan
--            con sys.partitions, que no pasa por la RLS.
-- Nota     : las listas de columnas y restricciones son las mismas del PRECHECK/POSTCHECK de 038_identidad_global.sql; una prueba
--            (ScriptsIdentidadGlobalTests) vigila que no se desvien.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

WITH
-- Las 9 tablas de F1.5: orden, esquema, nombre y numero de columnas esperadas.
t9 (n, esquema, nombre, completo, ncols) AS (
    SELECT v.n, v.esquema, v.nombre, v.esquema + N'.' + v.nombre, v.ncols
    FROM (VALUES
        (1, N'saas', N'cuentas', 15),
        (2, N'saas', N'cuenta_alias', 11),
        (3, N'saas', N'membresias', 11),
        (4, N'dbo', N'accesos_empresa', 13),
        (5, N'saas', N'cuenta_intentos', 4),
        (6, N'saas', N'cuenta_mfa', 10),
        (7, N'saas', N'cuenta_mfa_recuperacion', 5),
        (8, N'saas', N'invitaciones', 11),
        (9, N'saas', N'llaves_proteccion_datos', 3)
    ) v (n, esquema, nombre, ncols)
),
-- Las 83 columnas esperadas: tabla, columna, tipo (con largo) y si admite nulo.
cols (tabla, columna, tipo, nulable) AS (
    SELECT v.tabla, v.columna, v.tipo, v.nulable
    FROM (VALUES
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
        (N'saas.llaves_proteccion_datos', N'xml', 'nvarchar(max)', 1)
    ) v (tabla, columna, tipo, nulable)
),
-- Las 60 restricciones e indices esperados. Tipo: PK, UQ (restriccion unica), C (CHECK), F (clave foranea), D (valor por defecto),
-- UI (indice unico) e IX (indice no unico).
objs (tabla, nombre, tipo) AS (
    SELECT v.tabla, v.nombre, v.tipo
    FROM (VALUES
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
        (N'saas.llaves_proteccion_datos', N'PK_llaves_proteccion_datos', 'PK')
    ) v (tabla, nombre, tipo)
),
-- Columnas reales de las 9 tablas: se busca por OBJECT_ID de cada tabla (no recorre todo el catalogo).
real_cols AS (
    SELECT t.completo AS tabla, c.name AS columna,
           CONCAT(TYPE_NAME(c.user_type_id),
                  CASE WHEN TYPE_NAME(c.user_type_id) IN (N'varchar', N'char', N'varbinary', N'binary')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS NVARCHAR(10)) END, N')')
                       WHEN TYPE_NAME(c.user_type_id) IN (N'nvarchar', N'nchar')
                            THEN CONCAT(N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS NVARCHAR(10)) END, N')')
                       ELSE N'' END) AS tipo,
           c.is_nullable AS nulable
    FROM t9 t JOIN sys.columns c ON c.object_id = OBJECT_ID(t.completo)
),
-- Por tabla: existe, columnas buenas (tipo y nulabilidad), esperadas y columnas de mas.
pt AS (
    SELECT t.n, t.completo, t.ncols,
           CASE WHEN OBJECT_ID(t.completo, N'U') IS NULL THEN 0 ELSE 1 END AS existe,
           (SELECT COUNT(*) FROM cols k WHERE k.tabla = t.completo
                AND EXISTS (SELECT 1 FROM real_cols r WHERE r.tabla = k.tabla AND r.columna = k.columna AND r.tipo = k.tipo AND r.nulable = k.nulable)) AS buenas,
           (SELECT COUNT(*) FROM cols k WHERE k.tabla = t.completo) AS esperadas,
           (SELECT COUNT(*) FROM real_cols r WHERE r.tabla = t.completo
                AND NOT EXISTS (SELECT 1 FROM cols k WHERE k.tabla = r.tabla AND k.columna = r.columna)) AS de_mas,
           COALESCE((SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(t.completo) AND p.index_id IN (0, 1)), 0) AS filas_reales
    FROM t9 t
),
oc AS (
    SELECT k.tabla, k.nombre, k.tipo,
           CASE WHEN (k.tipo IN (N'PK', N'UQ', N'C', N'F', N'D')
                      AND EXISTS (SELECT 1 FROM sys.objects x WHERE x.name = k.nombre AND x.parent_object_id = OBJECT_ID(k.tabla) AND x.type = k.tipo))
                  OR (k.tipo = N'UI' AND EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = k.nombre AND i.object_id = OBJECT_ID(k.tabla) AND i.is_unique = 1))
                  OR (k.tipo = N'IX' AND EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = k.nombre AND i.object_id = OBJECT_ID(k.tabla) AND i.is_unique = 0))
                THEN 1 ELSE 0 END AS ok
    FROM objs k
),
fkn AS (
    SELECT f.name, f.is_disabled, f.is_not_trusted
    FROM objs k JOIN sys.foreign_keys f ON f.name = k.nombre AND f.parent_object_id = OBJECT_ID(k.tabla)
    WHERE k.tipo = N'F'
),
c AS (
    SELECT (SELECT COUNT(*) FROM sys.schemas WHERE name = N'saas') AS esq_saas,
           (SELECT COUNT(*) FROM sys.schemas WHERE name = N'seg') AS esq_seg,
           (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'saas')) AS tablas_saas, (SELECT CASE WHEN OBJECT_ID(N'saas.planes', N'U') IS NOT NULL THEN 1 ELSE 0 END) AS con040, (SELECT CASE WHEN OBJECT_ID(N'dbo.rol_permisos', N'U') IS NOT NULL THEN 1 ELSE 0 END) AS con041,
           (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) AS tablas_dbo,
           (SELECT SUM(existe) FROM pt) AS nuevas_existen,
           (SELECT SUM(de_mas) FROM pt) AS cols_de_mas,
           (SELECT SUM(ok) FROM oc) AS obj_ok,
           (SELECT COUNT(*) FROM oc) AS obj_n,
           (SELECT LEFT(STRING_AGG(CASE WHEN ok = 0 THEN CAST(nombre AS NVARCHAR(MAX)) END, N', ') WITHIN GROUP (ORDER BY nombre), 300) FROM oc) AS obj_mal,
           (SELECT COUNT(*) FROM fkn WHERE is_disabled = 0 AND is_not_trusted = 0) AS fk_buenas,
           (SELECT COUNT(*) FROM fkn) AS fk_n,
           (SELECT COUNT(*) FROM sys.foreign_keys) AS fks,
           (SELECT COUNT(*) FROM sys.foreign_keys WHERE name LIKE N'FKT[_]%' AND schema_id = SCHEMA_ID(N'dbo')) AS fkt_dbo,
           (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'saas.tenants')) AS fks_tenants,
           (SELECT COUNT(*) FROM sys.columns col INNER JOIN sys.tables tb ON tb.object_id = col.object_id AND tb.schema_id = SCHEMA_ID(N'dbo')
            WHERE col.name = N'id_tenant') AS cols_id_tenant_dbo,
           (SELECT COUNT(*) FROM sys.columns col WHERE col.object_id = OBJECT_ID(N'dbo.accesos_empresa') AND col.name = N'id_tenant' AND col.is_nullable = 0) AS acc_tenant_obligatorio,
           (SELECT COUNT(*) FROM sys.security_policies) AS politicas,
           (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_existe,
           (SELECT MAX(CAST(is_enabled AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_encendida,
           (SELECT MAX(CAST(is_schema_bound AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS pol_schemabinding,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS predicados,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND predicate_type_desc = N'FILTER') AS pred_filtro,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND predicate_type_desc = N'BLOCK') AS pred_bloqueo,
           (SELECT COUNT(DISTINCT target_object_id) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS pred_tablas,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')
                                                         AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa')) AS pred_accesos,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')
                                                         AND target_object_id = OBJECT_ID(N'dbo.accesos_empresa')
                                                         AND predicate_definition LIKE N'%fn_tenant_actual%') AS pred_accesos_fn,
           (SELECT COUNT(*) FROM sys.security_predicates p JOIN sys.tables tb ON tb.object_id = p.target_object_id
            WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND tb.schema_id = SCHEMA_ID(N'saas')) AS pred_saas,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')
                                                         AND target_object_id = OBJECT_ID(N'dbo.domain_events')) AS pred_domain_events,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg') AND parent_object_id = 0
                                              AND name IN (N'fn_tenant_actual', N'fn_mixta_lectura', N'fn_mixta_escritura')) AS funciones_seg,
           (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'seg') AND parent_object_id = 0) AS obj_seg,
           CASE WHEN SESSION_CONTEXT(N'id_tenant') IS NULL THEN N'sin contexto'
                ELSE CONCAT(N'con contexto id_tenant=', CAST(SESSION_CONTEXT(N'id_tenant') AS NVARCHAR(50))) END AS contexto,
           (SELECT COUNT(*) FROM dbo.accesos_empresa) AS acc_visibles,
           (SELECT COUNT(*) FROM dbo.empresas) AS emp_visibles,
           COALESCE((SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.empresas') AND p.index_id IN (0, 1)), 0) AS emp_reales,
           (SELECT COUNT(*) FROM dbo.Users) AS users_filas,
           (SELECT SUM(filas_reales) FROM pt) AS filas_nuevas
),
-- Lo migrado por el script 039 (las reglas por Id de dbo.Users son las del propio script: 1 admin, 2 cliente_demo, 5 nnolasco, 6 Moran23113,
-- 7 transgar.admin, 8 transgar.flota; los alias son de egaray 4 y egaray.transgar 10, colgados de la cuenta del Id 1).
d AS (
    SELECT COUNT(*) AS n_cuentas,
           ISNULL(SUM(CASE WHEN cu.estado = N'activa' THEN 1 ELSE 0 END), 0) AS activas,
           ISNULL(SUM(CASE WHEN cu.estado = N'inactiva' THEN 1 ELSE 0 END), 0) AS inactivas,
           ISNULL(SUM(CASE WHEN cu.es_operador = 1 THEN 1 ELSE 0 END), 0) AS operadores,
           ISNULL(SUM(CASE WHEN cu.hash_clave = u.Password THEN 1 ELSE 0 END), 0) AS claves_iguales,
           ISNULL(SUM(CASE WHEN LEN(cu.hash_clave) = 60 AND cu.hash_clave LIKE N'$2_$__$%' THEN 1 ELSE 0 END), 0) AS claves_bcrypt,
           ISNULL(SUM(CASE WHEN cu.debe_cambiar_clave = u.RequirePasswordChange THEN 1 ELSE 0 END), 0) AS cambio_clave_igual,
           (SELECT COUNT(*) FROM saas.cuenta_alias a JOIN saas.cuentas x ON x.id_cuenta = a.id_cuenta WHERE x.id_user_legado = 1) AS alias_emilio,
           (SELECT COUNT(*) FROM saas.cuenta_alias a JOIN saas.cuentas x ON x.id_cuenta = a.id_cuenta
            WHERE x.id_user_legado = 1 AND a.tipo = N'usuario' AND a.puede_iniciar_sesion = 1) AS alias_usuario,
           (SELECT COUNT(*) FROM saas.cuenta_alias a JOIN saas.cuentas x ON x.id_cuenta = a.id_cuenta
            WHERE x.id_user_legado = 1 AND a.tipo = N'correo' AND a.puede_iniciar_sesion = 0) AS alias_correo,
           (SELECT COUNT(*) FROM saas.membresias m JOIN saas.cuentas x ON x.id_cuenta = m.id_cuenta
            WHERE x.id_user_legado IN (1, 2, 5, 6, 7, 8)) AS membresias,
           (SELECT COUNT(*) FROM saas.membresias m JOIN saas.cuentas x ON x.id_cuenta = m.id_cuenta
            WHERE x.id_user_legado IN (1, 2, 5, 6, 7, 8) AND m.estado = N'activa') AS mem_activas,
           (SELECT COUNT(*) FROM saas.membresias m JOIN saas.cuentas x ON x.id_cuenta = m.id_cuenta
            WHERE x.id_user_legado IN (1, 2, 5, 6, 7, 8) AND m.estado = N'suspendida') AS mem_suspendidas,
           (SELECT COUNT(*) FROM saas.membresias m JOIN saas.cuentas x ON x.id_cuenta = m.id_cuenta JOIN saas.tenants t ON t.id_tenant = m.id_tenant
            WHERE (t.codigo = N'SIP' AND x.id_user_legado = 1 AND m.estado = N'activa' AND m.id_persona = 102)
               OR (t.codigo = N'TRANSGAR' AND x.id_user_legado = 1 AND m.estado = N'activa' AND m.id_persona = 104)
               OR (t.codigo = N'DEMO' AND x.id_user_legado = 2 AND m.estado = N'activa' AND m.id_persona = 103)
               OR (t.codigo = N'TRANSGAR' AND x.id_user_legado IN (7, 8) AND m.estado = N'suspendida' AND m.id_persona IS NULL)) AS mem_regla,
           (SELECT COUNT(*) FROM saas.membresias m JOIN saas.cuentas x ON x.id_cuenta = m.id_cuenta
            WHERE x.id_user_legado IN (1, 2, 5, 6, 7, 8) AND x.estado = N'inactiva' AND m.estado = N'activa') AS inactivas_con_membresia_activa,
           (SELECT COUNT(*) FROM saas.tenants WHERE (codigo = N'SIP' AND tipo = N'interno' AND estado = N'activo')
                                                  OR (codigo = N'DEMO' AND tipo = N'demo' AND estado = N'activo')
                                                  OR (codigo = N'IISE' AND tipo = N'cliente' AND estado = N'suspendido')
                                                  OR (codigo = N'TRANSGAR' AND tipo = N'cliente' AND estado = N'activo')) AS tenants_ok,
           (SELECT COUNT(*) FROM saas.cuenta_mfa m
            WHERE m.confirmada = 1 AND (SELECT COUNT(*) FROM saas.cuenta_mfa_recuperacion r WHERE r.id_cuenta = m.id_cuenta) <> 10) AS mfa_sin_codigos,
           (SELECT p.filas_reales FROM pt p WHERE p.completo = N'dbo.accesos_empresa') AS accesos_reales
    FROM saas.cuentas cu JOIN dbo.Users u ON u.Id = cu.id_user_legado
    WHERE cu.id_user_legado IN (1, 2, 5, 6, 7, 8)
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM c
    CROSS JOIN d
    CROSS APPLY (VALUES
        (110, N'Esquema saas existe',                                                    N'1',  CAST(c.esq_saas AS NVARCHAR(400))),
        (120, N'Esquema seg existe',                                                     N'1',  CAST(c.esq_seg AS NVARCHAR(400))),
        (130, N'Tablas del esquema saas (tenants y las 8 nuevas; +5 si ya esta el 040; +3 si ya esta el 041)',  CAST(9 + 5 * c.con040 + 3 * c.con041 AS NVARCHAR(400)), CAST(c.tablas_saas AS NVARCHAR(400))),
        (140, N'Tablas del esquema dbo (las 56 anteriores y accesos_empresa; +1 si ya esta el 041)', CAST(57 + c.con041 AS NVARCHAR(400)), CAST(c.tablas_dbo AS NVARCHAR(400))),
        (150, N'Las 9 tablas de la identidad global existen',                            N'9',  CAST(c.nuevas_existen AS NVARCHAR(400))),
        (170, N'Columnas no previstas en las 9 tablas',                                  N'0',  CAST(c.cols_de_mas AS NVARCHAR(400))),
        (180, N'Restricciones e indices esperados (PK, UQ, CHECK, FK, DEFAULT, indices)', N'60 de 60',
              CONCAT(c.obj_ok, N' de ', c.obj_n, CASE WHEN c.obj_ok < c.obj_n THEN CONCAT(N' (faltan: ', c.obj_mal, N')') ELSE N'' END)),
        (190, N'Claves foraneas nuevas habilitadas y confiables',                        N'13 de 13', CONCAT(c.fk_buenas, N' de ', c.fk_n)),
        (200, N'Claves foraneas en total (152 + 13; +5 si ya esta el 040; +5 si ya esta el 041)', CAST(165 + 5 * c.con040 + 5 * c.con041 AS NVARCHAR(400)), CAST(c.fks AS NVARCHAR(400))),
        (210, N'Claves compuestas FKT_ en dbo (78 + 3 de accesos_empresa; +1 si ya esta el 041)', CAST(81 + c.con041 AS NVARCHAR(400)), CAST(c.fkt_dbo AS NVARCHAR(400))),
        (220, N'Claves foraneas hacia saas.tenants (41 + membresias, invitaciones y accesos_empresa; +1 si ya esta el 040; +1 si ya esta el 041)', CAST(44 + c.con040 + c.con041 AS NVARCHAR(400)), CAST(c.fks_tenants AS NVARCHAR(400))),
        (230, N'Columnas id_tenant en el esquema dbo (41 + accesos_empresa; +1 si ya esta el 041)', CAST(42 + c.con041 AS NVARCHAR(400)), CAST(c.cols_id_tenant_dbo AS NVARCHAR(400))),
        (240, N'dbo.accesos_empresa.id_tenant es obligatorio',                           N'1',  CAST(c.acc_tenant_obligatorio AS NVARCHAR(400))),
        (300, N'Politicas de seguridad en la base',                                      N'1',  CAST(c.politicas AS NVARCHAR(400))),
        (310, N'Politica seg.pol_tenant existe',                                         N'1',  CAST(c.pol_existe AS NVARCHAR(400))),
        (320, N'Politica ENCENDIDA (is_enabled = 1)',                                    N'1',  COALESCE(CAST(c.pol_encendida AS NVARCHAR(400)), N'no existe')),
        (330, N'Politica con SCHEMABINDING',                                             N'1',  COALESCE(CAST(c.pol_schemabinding AS NVARCHAR(400)), N'no existe')),
        (340, N'Predicados de la politica (199 + 5 de accesos_empresa; +5 si ya esta el 041)', CAST(204 + 5 * c.con041 AS NVARCHAR(400)), CAST(c.predicados AS NVARCHAR(400))),
        (350, N'Predicados de filtro (37 tablas de tenant, 2 mixtas y accesos_empresa; +1 si ya esta el 041)', CAST(40 + c.con041 AS NVARCHAR(400)), CAST(c.pred_filtro AS NVARCHAR(400))),
        (360, N'Predicados de bloqueo (4 por cada una de las 41 tablas; +4 si ya esta el 041)', CAST(164 + 4 * c.con041 AS NVARCHAR(400)), CAST(c.pred_bloqueo AS NVARCHAR(400))),
        (370, N'Tablas cubiertas por la politica (todas menos domain_events; +1 si ya esta el 041)', CAST(41 + c.con041 AS NVARCHAR(400)), CAST(c.pred_tablas AS NVARCHAR(400))),
        (380, N'Predicados sobre dbo.accesos_empresa (1 filtro y 4 bloqueos)',           N'5',  CAST(c.pred_accesos AS NVARCHAR(400))),
        (385, N'Predicados de accesos_empresa que usan seg.fn_tenant_actual',            N'5',  CAST(c.pred_accesos_fn AS NVARCHAR(400))),
        (390, N'Predicados sobre tablas del esquema saas (el plano de control no lleva RLS)', N'0', CAST(c.pred_saas AS NVARCHAR(400))),
        (400, N'Predicados sobre domain_events (queda fuera de la politica)',            N'0',  CAST(c.pred_domain_events AS NVARCHAR(400))),
        (410, N'Funciones seg.fn_tenant_actual, fn_mixta_lectura y fn_mixta_escritura',  N'3',  CAST(c.funciones_seg AS NVARCHAR(400))),
        (420, N'Objetos propios del esquema seg (3 funciones y la politica)',            N'4',  CAST(c.obj_seg AS NVARCHAR(400))),
        (500, N'Contexto de sesion id_tenant de esta conexion (el ejecutor no lo fija)', N'sin contexto', c.contexto),
        (510, N'RLS: dbo.accesos_empresa devuelve 0 filas sin contexto',                 N'0',  CAST(c.acc_visibles AS NVARCHAR(400))),
        (530, N'Directorio empresas sigue legible: devuelve todas sus filas',            CAST(c.emp_reales AS NVARCHAR(400)), CAST(c.emp_visibles AS NVARCHAR(400)))
        ,(520, N'Cobertura: accesos_empresa tiene filas reales y la politica las oculta (prueba concluyente)', N'1',
              CASE WHEN d.accesos_reales > 0 AND c.acc_visibles = 0 THEN N'1' ELSE N'0' END)
        ,(600, N'dbo.Users intacta (no se modifica ni se borra hasta F1.6)', N'8', CAST(c.users_filas AS NVARCHAR(400)))
        ,(610, N'Cuentas migradas desde dbo.Users (6 de 8 usuarios; egaray y egaray.transgar son alias de la cuenta de Emilio)', N'6', CAST(d.n_cuentas AS NVARCHAR(400)))
        ,(620, N'Cuentas activas (Emilio y cliente_demo)', N'2', CAST(d.activas AS NVARCHAR(400)))
        ,(630, N'Cuentas inactivas (D24: nnolasco, Moran23113, transgar.admin y transgar.flota)', N'4', CAST(d.inactivas AS NVARCHAR(400)))
        ,(640, N'Operadores de la plataforma (D23: solo la cuenta de Emilio)', N'1', CAST(d.operadores AS NVARCHAR(400)))
        ,(650, N'Claves copiadas igual que en dbo.Users (deja de valer cuando la aplicacion de F1.5 cambie una clave)', N'6', CAST(d.claves_iguales AS NVARCHAR(400)))
        ,(655, N'Claves que son un hash BCrypt de 60 caracteres', N'6', CAST(d.claves_bcrypt AS NVARCHAR(400)))
        ,(660, N'Marca de cambio de clave igual que en dbo.Users', N'6', CAST(d.cambio_clave_igual AS NVARCHAR(400)))
        ,(670, N'Alias de la cuenta de Emilio (egaray, egaray.transgar y sus dos correos)', N'4', CAST(d.alias_emilio AS NVARCHAR(400)))
        ,(680, N'Alias de usuario que inician sesion', N'2', CAST(d.alias_usuario AS NVARCHAR(400)))
        ,(690, N'Alias de correo que NO inician sesion', N'2', CAST(d.alias_correo AS NVARCHAR(400)))
        ,(700, N'Membresias migradas', N'5', CAST(d.membresias AS NVARCHAR(400)))
        ,(710, N'Membresias activas (Emilio en SIP y en TRANSGAR, cliente_demo en DEMO)', N'3', CAST(d.mem_activas AS NVARCHAR(400)))
        ,(720, N'Membresias suspendidas (transgar.admin y transgar.flota)', N'2', CAST(d.mem_suspendidas AS NVARCHAR(400)))
        ,(730, N'Membresias con el tenant, estado y persona de la regla (SIP 102, TRANSGAR 104, DEMO 103)', N'5', CAST(d.mem_regla AS NVARCHAR(400)))
        ,(740, N'Cuentas inactivas con una membresia activa', N'0', CAST(d.inactivas_con_membresia_activa AS NVARCHAR(400)))
        ,(750, N'Accesos por empresa (filas reales; sys.partitions no pasa por la RLS)', N'5', CAST(d.accesos_reales AS NVARCHAR(400)))
        ,(760, N'Cuentas con MFA confirmado que no tienen sus 10 codigos de recuperacion (usados o no)', N'0', CAST(d.mfa_sin_codigos AS NVARCHAR(400)))
        ,(770, N'Tenants del script 025 intactos (SIP, DEMO, IISE suspendido y TRANSGAR)', N'4', CAST(d.tenants_ok AS NVARCHAR(400)))
    ) v (orden, comprobacion, esperado, obtenido)
    UNION ALL
    SELECT 160 + p.n,
           CONCAT(N'Columnas de ', p.completo, N' (tipo y nulabilidad)'),
           CONCAT(p.ncols, N' de ', p.ncols),
           CASE WHEN p.existe = 0 THEN N'no existe' ELSE CONCAT(p.buenas, N' de ', p.esperadas) END
    FROM pt p
),
f AS (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN esperado = obtenido THEN 1 ELSE 0 END AS ok
    FROM r
)
SELECT z.comprobacion, z.esperado, z.obtenido, z.estado
FROM (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN ok = 1 THEN N'OK' ELSE N'FALLA' END AS estado
    FROM f
    UNION ALL
    SELECT 2147483647, N'RESUMEN', N'0 FALLA', CONCAT(SUM(ok), N' OK, ', COUNT(*) - SUM(ok), N' FALLA'),
           CASE WHEN SUM(ok) = COUNT(*) THEN N'OK' ELSE N'FALLA' END
    FROM f
) z
ORDER BY z.orden;
