-- ============================================================
-- Archivo  : verificar_despues_041.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de aplicar el script 041 (F1.6a: catalogo de permisos, roles
--            plantilla y permisos de los roles). Comprueba:
--              * las 4 tablas nuevas (saas.permisos, saas.roles_plantilla, saas.roles_plantilla_permisos y dbo.rol_permisos) con sus
--                columnas, claves unicas, restricciones CHECK y claves foraneas, todas habilitadas y confiables;
--              * el catalogo sembrado (valores sembrados: 72 permisos; 17 de plataforma, 9 de administracion, 28 de flota, 8 de catalogos
--                y 10 de facturacion; 44 con casilla legada) y que ningun permiso tiene un ambito que no corresponde a su modulo;
--              * las 9 plantillas de rol (tres por modulo) y sus permisos (valores sembrados: 102);
--              * dbo.rol_permisos: sus 5 predicados en seg.pol_tenant (1 filtro y 4 bloqueos, todos con seg.fn_tenant_actual), que ninguna
--                fila es de ambito plataforma, que una conexion SIN tenant ve 0 filas aunque la tabla tenga filas reales (prueba
--                concluyente de la RLS) y que dbo.empresa_rol_permisos NO se toco;
--              * que ningun texto de dbo.modulos conserva el dano de tildes (D65).
--            La migracion fila por fila de los roles (que nadie gane ni pierda permisos) la comprueba el POSTCHECK del script 041, que lee
--            tenant por tenant; desde una conexion sin tenant esta verificacion solo puede contar filas reales.
--            Antes de aplicar 041 da FALLA. Las filas de "valores sembrados" comprueban lo que sembro 041: si el operador edita el catalogo o
--            los roles despues, dejaran de coincidir y es lo esperado (ajustar el numero que corresponda y anotarlo).
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME_F16.md). No escribe nada.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

DECLARE @hay_tablas BIT = CASE WHEN OBJECT_ID(N'saas.permisos', N'U') IS NOT NULL AND OBJECT_ID(N'saas.roles_plantilla', N'U') IS NOT NULL
                                AND OBJECT_ID(N'saas.roles_plantilla_permisos', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.rol_permisos', N'U') IS NOT NULL
                               THEN 1 ELSE 0 END;

DECLARE @permisos INT, @unicos INT, @plataforma INT, @administracion INT, @flota INT, @catalogos INT, @facturacion INT,
        @ambito_plat INT, @ambito_emp INT, @ambito_mod INT, @con_casilla INT, @ambito_mal INT, @sin_modulo NVARCHAR(400),
        @plantillas INT, @plantillas_ok INT, @plantilla_permisos INT, @plantilla_modulo_mal INT,
        @rol_filas_reales BIGINT, @rol_filas_visibles INT, @legado_reales BIGINT,
        @pred_rol INT, @pred_rol_filtro INT, @pred_rol_fn INT, @modulos_danados NVARCHAR(400);

IF @hay_tablas = 1
BEGIN
    SELECT @permisos = COUNT(*), @unicos = COUNT(DISTINCT codigo),
           @plataforma = SUM(CASE WHEN modulo = 'plataforma' THEN 1 ELSE 0 END),
           @administracion = SUM(CASE WHEN modulo = 'administracion' THEN 1 ELSE 0 END),
           @flota = SUM(CASE WHEN modulo = 'flota' THEN 1 ELSE 0 END),
           @catalogos = SUM(CASE WHEN modulo = 'catalogos' THEN 1 ELSE 0 END),
           @facturacion = SUM(CASE WHEN modulo = 'facturacion' THEN 1 ELSE 0 END),
           @ambito_plat = SUM(CASE WHEN ambito = 'plataforma' THEN 1 ELSE 0 END),
           @ambito_emp = SUM(CASE WHEN ambito = 'empresa' THEN 1 ELSE 0 END),
           @ambito_mod = SUM(CASE WHEN ambito = 'modulo' THEN 1 ELSE 0 END),
           @con_casilla = SUM(CASE WHEN accion_legada IS NOT NULL THEN 1 ELSE 0 END),
           @ambito_mal = SUM(CASE WHEN (modulo = 'plataforma' AND ambito <> 'plataforma') OR (modulo = 'administracion' AND ambito <> 'empresa')
                                    OR (modulo NOT IN ('plataforma', 'administracion') AND ambito <> 'modulo') THEN 1 ELSE 0 END)
    FROM saas.permisos;

    -- Todo permiso de ambito modulo es de un modulo que existe en dbo.modulos.
    SELECT @sin_modulo = COALESCE(LEFT(STRING_AGG(CAST(x.modulo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY x.modulo), 400), N'')
    FROM (SELECT DISTINCT p.modulo FROM saas.permisos p
          WHERE p.ambito = 'modulo' AND NOT EXISTS (SELECT 1 FROM dbo.modulos m WHERE CAST(m.codigo AS VARCHAR(50)) = p.modulo)) x;

    SELECT @plantillas = COUNT(*) FROM saas.roles_plantilla;
    SELECT @plantillas_ok = COUNT(*)
    FROM (SELECT DISTINCT modulo FROM saas.permisos WHERE ambito = 'modulo') m
    CROSS JOIN (VALUES ('administrador'), ('operador'), ('consulta')) k (clave)
    WHERE EXISTS (SELECT 1 FROM saas.roles_plantilla r WHERE r.codigo = m.modulo + '_' + k.clave AND r.modulo = m.modulo);
    SELECT @plantilla_permisos = COUNT(*) FROM saas.roles_plantilla_permisos;
    SELECT @plantilla_modulo_mal = COUNT(*)
    FROM saas.roles_plantilla_permisos l
    JOIN saas.roles_plantilla r ON r.id_rol_plantilla = l.id_rol_plantilla
    JOIN saas.permisos p ON p.id_permiso = l.id_permiso
    WHERE r.modulo <> p.modulo OR p.ambito <> 'modulo';

    SELECT @rol_filas_reales = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.rol_permisos') AND p.index_id IN (0, 1);
    SELECT @rol_filas_visibles = COUNT(*) FROM dbo.rol_permisos;
    SELECT @legado_reales = SUM(p.rows) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(N'dbo.empresa_rol_permisos') AND p.index_id IN (0, 1);

    SELECT @pred_rol = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos');
    SELECT @pred_rol_filtro = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos') AND predicate_type_desc = N'FILTER';
    SELECT @pred_rol_fn = COUNT(*) FROM sys.security_predicates
    WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'dbo.rol_permisos') AND predicate_definition LIKE N'%fn_tenant_actual%';
END;

SELECT @modulos_danados = COALESCE(LEFT(STRING_AGG(CAST(m.codigo AS NVARCHAR(MAX)), N',') WITHIN GROUP (ORDER BY m.codigo), 400), N'')
FROM dbo.modulos m
WHERE m.nombre COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%' OR m.descripcion COLLATE Latin1_General_100_BIN2 LIKE N'%' + NCHAR(195) + N'%';

WITH
-- Columnas esperadas: tabla, columna y definicion 'tipo|bytes|nulo' (nvarchar(100) = 200 bytes).
col_esp AS (
    SELECT v.tabla, v.columna, v.def
    FROM (VALUES
        (N'saas.permisos', N'id_permiso', N'int|4|0'), (N'saas.permisos', N'codigo', N'varchar|80|0'), (N'saas.permisos', N'modulo', N'varchar|30|0'),
        (N'saas.permisos', N'recurso', N'varchar|40|0'), (N'saas.permisos', N'accion', N'varchar|30|0'), (N'saas.permisos', N'ambito', N'varchar|12|0'),
        (N'saas.permisos', N'accion_legada', N'varchar|10|1'), (N'saas.permisos', N'descripcion', N'nvarchar|400|0'), (N'saas.permisos', N'orden', N'int|4|0'),
        (N'saas.permisos', N'activo', N'bit|1|0'), (N'saas.permisos', N'creado_por', N'nvarchar|200|0'), (N'saas.permisos', N'fecha_creacion', N'datetime2|8|0'),
        (N'saas.permisos', N'modificado_por', N'nvarchar|200|1'), (N'saas.permisos', N'fecha_modificacion', N'datetime2|8|1'),
        (N'saas.roles_plantilla', N'id_rol_plantilla', N'int|4|0'), (N'saas.roles_plantilla', N'codigo', N'varchar|40|0'),
        (N'saas.roles_plantilla', N'modulo', N'varchar|30|0'), (N'saas.roles_plantilla', N'nombre', N'nvarchar|100|0'),
        (N'saas.roles_plantilla', N'descripcion', N'nvarchar|400|1'), (N'saas.roles_plantilla', N'orden', N'int|4|0'), (N'saas.roles_plantilla', N'activo', N'bit|1|0'),
        (N'saas.roles_plantilla', N'creado_por', N'nvarchar|200|0'), (N'saas.roles_plantilla', N'fecha_creacion', N'datetime2|8|0'),
        (N'saas.roles_plantilla', N'modificado_por', N'nvarchar|200|1'), (N'saas.roles_plantilla', N'fecha_modificacion', N'datetime2|8|1'),
        (N'saas.roles_plantilla_permisos', N'id_rol_plantilla', N'int|4|0'), (N'saas.roles_plantilla_permisos', N'id_permiso', N'int|4|0'),
        (N'saas.roles_plantilla_permisos', N'modulo', N'varchar|30|0'), (N'saas.roles_plantilla_permisos', N'creado_por', N'nvarchar|200|0'),
        (N'saas.roles_plantilla_permisos', N'fecha_creacion', N'datetime2|8|0'),
        (N'dbo.rol_permisos', N'id_tenant', N'int|4|0'), (N'dbo.rol_permisos', N'id_rol', N'int|4|0'), (N'dbo.rol_permisos', N'id_permiso', N'int|4|0'),
        (N'dbo.rol_permisos', N'ambito_permiso', N'varchar|12|0'), (N'dbo.rol_permisos', N'creado_por', N'nvarchar|200|0'),
        (N'dbo.rol_permisos', N'fecha_creacion', N'datetime2|8|0'), (N'dbo.rol_permisos', N'modificado_por', N'nvarchar|200|1'),
        (N'dbo.rol_permisos', N'fecha_modificacion', N'datetime2|8|1')
    ) v (tabla, columna, def)
),
col_real AS (
    SELECT (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AS tabla, c.name AS columna, CONCAT(TYPE_NAME(c.user_type_id), N'|', c.max_length, N'|', c.is_nullable) AS def
    FROM sys.objects o
    JOIN sys.columns c ON c.object_id = o.object_id
    WHERE o.type = 'U' AND o.name IN (N'permisos', N'roles_plantilla', N'roles_plantilla_permisos', N'rol_permisos')
      AND SCHEMA_NAME(o.schema_id) IN (N'saas', N'dbo')
),
col_dif AS (
    SELECT COALESCE(e.tabla, r.tabla) AS tabla, COALESCE(e.columna, r.columna) AS columna
    FROM col_esp e
    FULL OUTER JOIN col_real r ON r.tabla = e.tabla AND r.columna = e.columna
    WHERE e.def IS NULL OR r.def IS NULL OR e.def <> r.def
),
chk AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'CK_permisos_ambito'), (N'CK_permisos_codigo'), (N'CK_permisos_ambito_modulo'), (N'CK_permisos_accion_legada'),
                 (N'CK_roles_plantilla_codigo'), (N'CK_roles_plantilla_modulo'), (N'CK_rol_permisos_ambito')) v (nombre)
),
fkn AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'FK_roles_plantilla_permisos_plantilla'), (N'FK_roles_plantilla_permisos_permisos'), (N'FK_rol_permisos_tenants'),
                 (N'FKT_rol_permisos_empresa_roles_id_rol'), (N'FK_rol_permisos_permisos')) v (nombre)
),
uqn AS (
    SELECT v.nombre, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name = v.nombre AND i.object_id = OBJECT_ID(v.tabla) AND i.is_unique = 1) THEN 1 ELSE 0 END AS ok
    FROM (VALUES (N'saas.permisos', N'UX_permisos_codigo'), (N'saas.permisos', N'UX_permisos_id_modulo'), (N'saas.permisos', N'UX_permisos_id_ambito'),
                 (N'saas.roles_plantilla', N'UX_roles_plantilla_codigo'), (N'saas.roles_plantilla', N'UX_roles_plantilla_id_modulo')) v (tabla, nombre)
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM (VALUES
        -- 1xx: las tablas nuevas y sus restricciones
        (110, N'Tablas nuevas: saas.permisos, saas.roles_plantilla, saas.roles_plantilla_permisos y dbo.rol_permisos', N'1',
              CAST(@hay_tablas AS NVARCHAR(400))),
        (120, N'Columnas de las 4 tablas nuevas (nombre, tipo, longitud y nulabilidad): diferencias', N'0',
              CASE WHEN @hay_tablas = 1 THEN CAST((SELECT COUNT(*) FROM col_dif) AS NVARCHAR(400)) ELSE N'no existen' END),
        (130, N'Restricciones CHECK de las tablas nuevas (7), habilitadas y confiables', N'7 de 7',
              CONCAT((SELECT SUM(ok) FROM chk), N' de ', (SELECT COUNT(*) FROM chk))),
        (140, N'Claves foraneas de las tablas nuevas (5), habilitadas y confiables', N'5 de 5',
              CONCAT((SELECT SUM(ok) FROM fkn), N' de ', (SELECT COUNT(*) FROM fkn))),
        (150, N'Claves unicas de apoyo y de codigo (5)', N'5 de 5', CONCAT((SELECT SUM(ok) FROM uqn), N' de ', (SELECT COUNT(*) FROM uqn))),
        -- 2xx: el catalogo (valores sembrados)
        (210, N'Permisos en el catalogo (valores sembrados)', N'72', COALESCE(CAST(@permisos AS NVARCHAR(400)), N'no existe')),
        (220, N'Codigos de permiso repetidos', N'0', COALESCE(CAST(@permisos - @unicos AS NVARCHAR(400)), N'no existe')),
        (230, N'Permisos por modulo: plataforma / administracion / flota / catalogos / facturacion (valores sembrados)', N'17 / 9 / 28 / 8 / 10',
              CASE WHEN @permisos IS NULL THEN N'no existe' ELSE CONCAT(@plataforma, N' / ', @administracion, N' / ', @flota, N' / ', @catalogos, N' / ', @facturacion) END),
        (240, N'Permisos por ambito: plataforma / empresa / modulo (valores sembrados)', N'17 / 9 / 46',
              CASE WHEN @permisos IS NULL THEN N'no existe' ELSE CONCAT(@ambito_plat, N' / ', @ambito_emp, N' / ', @ambito_mod) END),
        (250, N'Permisos con casilla legada, para migrar los roles (valores sembrados)', N'44', COALESCE(CAST(@con_casilla AS NVARCHAR(400)), N'no existe')),
        (260, N'Permisos cuyo ambito no corresponde a su modulo', N'0', COALESCE(CAST(@ambito_mal AS NVARCHAR(400)), N'no existe')),
        (270, N'Modulos con permisos de ambito modulo que no estan en dbo.modulos', N'ninguno', CASE WHEN @permisos IS NULL THEN N'no existe' WHEN LEN(@sin_modulo) = 0 THEN N'ninguno' ELSE @sin_modulo END),
        -- 3xx: las plantillas de rol (valores sembrados)
        (310, N'Plantillas de rol: tres por modulo con permisos (flota, catalogos y facturacion)', N'9', COALESCE(CAST(@plantillas AS NVARCHAR(400)), N'no existe')),
        (320, N'Plantillas administrador, operador y consulta de cada modulo, todas presentes', N'9', COALESCE(CAST(@plantillas_ok AS NVARCHAR(400)), N'no existe')),
        (330, N'Permisos de plantilla en total (administrador = todos; operador = ver, crear y editar; consulta = ver)', N'102',
              COALESCE(CAST(@plantilla_permisos AS NVARCHAR(400)), N'no existe')),
        (340, N'Permisos de plantilla de otro modulo o de ambito que no es modulo', N'0', COALESCE(CAST(@plantilla_modulo_mal AS NVARCHAR(400)), N'no existe')),
        -- 4xx: permisos de los roles y RLS
        (410, N'Permisos migrados a roles: filas reales de dbo.rol_permisos (valores sembrados; sys.partitions no pasa por la RLS)', N'143',
              COALESCE(CAST(@rol_filas_reales AS NVARCHAR(400)), N'no existe')),
        (420, N'dbo.empresa_rol_permisos NO se toco: filas reales (valores sembrados)', N'7', COALESCE(CAST(@legado_reales AS NVARCHAR(400)), N'no existe')),
        (430, N'Predicados de dbo.rol_permisos en seg.pol_tenant (1 filtro y 4 bloqueos)', N'5', COALESCE(CAST(@pred_rol AS NVARCHAR(400)), N'no existe')),
        (440, N'Predicados de filtro de dbo.rol_permisos', N'1', COALESCE(CAST(@pred_rol_filtro AS NVARCHAR(400)), N'no existe')),
        (450, N'Predicados de dbo.rol_permisos que usan seg.fn_tenant_actual', N'5', COALESCE(CAST(@pred_rol_fn AS NVARCHAR(400)), N'no existe')),
        (460, N'RLS: dbo.rol_permisos devuelve 0 filas a una conexion sin tenant', N'0', COALESCE(CAST(@rol_filas_visibles AS NVARCHAR(400)), N'no existe')),
        (470, N'Cobertura: rol_permisos tiene filas reales y la politica las oculta (prueba concluyente)', N'1',
              CASE WHEN @rol_filas_reales > 0 AND @rol_filas_visibles = 0 THEN N'1' ELSE N'0' END),
        -- 5xx: textos de dbo.modulos (D65)
        (510, N'Modulos de dbo.modulos con el texto danado (la "A" con tilde seguida de otro signo)', N'ninguno', CASE WHEN LEN(@modulos_danados) = 0 THEN N'ninguno' ELSE @modulos_danados END)
    ) v (orden, comprobacion, esperado, obtenido)
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
