-- ============================================================
-- Script   : 042_psicologia_esquema.sql
-- Proposito: Modulo de Psicologia, paso 1 de 3 (estructura). Plan "Plan modulo Psicologia" (2026-11-15), decisiones D1 a D16.
--              * Crea el esquema psi para los datos clinicos (separados de dbo para darles permisos y respaldos aparte).
--              * Crea 12 TABLAS DE TENANT en psi, todas con id_tenant obligatorio, claves foraneas COMPUESTAS por tenant
--                (ADR-021) y 5 predicados cada una en seg.pol_tenant (1 filtro y 4 bloqueos con seg.fn_tenant_actual):
--                  psi.jornadas            brigada o evento de atencion grupal
--                  psi.pacientes           rol de paciente del vinculo persona_empresa (como dbo.empleados)
--                  psi.atenciones          cada evaluacion o consulta: el historial clinico
--                  psi.pruebas             catalogo de pruebas psicologicas de la empresa (D12)
--                  psi.atencion_pruebas    pruebas aplicadas en cada atencion
--                  psi.tipos_constancia    tipos de constancia de examen psicologico, cada uno con su formato (D14)
--                  psi.constancias         constancias extendidas al paciente, con su correlativo (D13)
--                  psi.constancia_pruebas  pruebas que declara cada constancia
--                  psi.profesionales       psicologos y administradores que atienden, firman o son responsables de brigadas (D2, D11)
--                  psi.impresiones        registro de cada documento impreso (una atencion o una constancia)
--                  psi.importaciones       un lote por archivo Excel subido
--                  psi.importacion_filas   cada fila del Excel con su resultado
--                Las claves compuestas dentro de psi llevan tambien id_empresa: una atencion, una constancia, una impresion o una
--                fila de importacion no pueden apuntar a un paciente, una prueba o un tipo de otra empresa del mismo tenant.
--              * Agrega 'paciente' a CK_persona_empresa_tipo (dbo.persona_empresa): el paciente es un vinculo mas de la persona
--                maestra (ADR-004). No cambia ninguna fila existente.
--            No cambia lo que ve ni lo que puede hacer ningun usuario: la aplicacion publicada ignora el esquema psi.
--            Los campos clinicos de psi.atenciones y los datos propios de cada formato de constancia se agregan en un script
--            posterior, cuando se defina su contenido.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-15
-- BD       : eBD_SPD
-- Requiere : 025 a 037 aplicados (saas.tenants, id_tenant obligatorio, claves UX_*_tenant_*, politica seg.pol_tenant).
--            038 y 041 pueden estar aplicados o no: la politica se acepta con 199, 204 o 209 predicados fuera de psi.
-- Rollback : ver la seccion ROLLBACK al final (se detiene si alguna tabla de psi tiene filas).
-- ============================================================
-- Que hace:
--   1. PRECHECK (no cambia nada). Aborta, nombrando el problema, si:
--        a) faltan los scripts previos (saas.tenants, seg.fn_tenant_actual, seg.pol_tenant, las claves unicas por tenant de
--           dbo.empresas, dbo.personas y dbo.persona_empresa, o CK_persona_empresa_tipo);
--        b) la politica no tiene los predicados esperados (199, 204 o 209 fuera de psi; 0 o 60 en psi);
--        c) los objetos nuevos existen A MEDIAS (algunas de las 12 tablas y sus 60 predicados si y otros no), una tabla existe
--           con columnas distintas de las esperadas o el esquema psi tiene una tabla no prevista;
--        d) CK_persona_empresa_tipo no admite los 6 tipos de vinculo de hoy (alguien lo cambio a mano).
--   2. CAMBIO (una sola transaccion): esquema, CHECK de vinculos, tablas, restricciones, indices y predicados RLS.
--   3. POSTCHECK dentro de la misma transaccion: 174 columnas, 29 CHECK y 37 claves foraneas confiables, 20 indices unicos,
--      60 predicados (5 por tabla, todos de seg.fn_tenant_actual) y la politica en el mismo estado (encendida o apagada).
--      Si algo no cuadra, se revierte todo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que el bloque siguiente exige).
--   * Idempotente: si todo ya esta aplicado, no hace nada y lo dice.
--   * Funciona con la politica encendida o apagada y la deja como estaba (ALTER SECURITY POLICY ... ADD no cambia el estado).
--   * ASCII puro.
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
IF OBJECT_ID(N'tempdb..#precheck_042') IS NOT NULL DROP TABLE #precheck_042;
IF OBJECT_ID(N'tempdb..#columnas_042') IS NOT NULL DROP TABLE #columnas_042;

-- Columnas esperadas de cada tabla nueva (174).
CREATE TABLE #columnas_042 (
    tabla    SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    columna  SYSNAME COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (tabla, columna)
);
INSERT INTO #columnas_042 (tabla, columna) VALUES
    (N'psi.jornadas', N'id_jornada'), (N'psi.jornadas', N'id_tenant'), (N'psi.jornadas', N'id_empresa'), (N'psi.jornadas', N'nombre'),
    (N'psi.jornadas', N'lugar'), (N'psi.jornadas', N'institucion'), (N'psi.jornadas', N'fecha_inicio'), (N'psi.jornadas', N'fecha_fin'),
    (N'psi.jornadas', N'id_persona_responsable'), (N'psi.jornadas', N'nombre_responsable'), (N'psi.jornadas', N'estado'),
    (N'psi.jornadas', N'observaciones'), (N'psi.jornadas', N'creado_por'), (N'psi.jornadas', N'fecha_creacion'),
    (N'psi.jornadas', N'modificado_por'), (N'psi.jornadas', N'fecha_modificacion'), (N'psi.jornadas', N'token_concurrencia'),

    (N'psi.pacientes', N'id_paciente'), (N'psi.pacientes', N'id_tenant'), (N'psi.pacientes', N'id_empresa'),
    (N'psi.pacientes', N'id_persona_empresa'), (N'psi.pacientes', N'tipo_vinculo'), (N'psi.pacientes', N'numero_expediente'),
    (N'psi.pacientes', N'fecha_apertura'), (N'psi.pacientes', N'observaciones'), (N'psi.pacientes', N'activo'),
    (N'psi.pacientes', N'eliminado'), (N'psi.pacientes', N'fecha_eliminado'), (N'psi.pacientes', N'creado_por'),
    (N'psi.pacientes', N'fecha_creacion'), (N'psi.pacientes', N'modificado_por'), (N'psi.pacientes', N'fecha_modificacion'),
    (N'psi.pacientes', N'token_concurrencia'),

    (N'psi.atenciones', N'id_atencion'), (N'psi.atenciones', N'id_tenant'), (N'psi.atenciones', N'id_empresa'),
    (N'psi.atenciones', N'id_paciente'), (N'psi.atenciones', N'id_jornada'), (N'psi.atenciones', N'fecha_atencion'),
    (N'psi.atenciones', N'edad_declarada'), (N'psi.atenciones', N'id_persona_profesional'), (N'psi.atenciones', N'nombre_profesional'),
    (N'psi.atenciones', N'estado'), (N'psi.atenciones', N'observaciones'), (N'psi.atenciones', N'fecha_cierre'),
    (N'psi.atenciones', N'cerrado_por'), (N'psi.atenciones', N'motivo_anulacion'), (N'psi.atenciones', N'creado_por'),
    (N'psi.atenciones', N'fecha_creacion'), (N'psi.atenciones', N'modificado_por'), (N'psi.atenciones', N'fecha_modificacion'),
    (N'psi.atenciones', N'token_concurrencia'),

    (N'psi.pruebas', N'id_prueba'), (N'psi.pruebas', N'id_tenant'), (N'psi.pruebas', N'id_empresa'), (N'psi.pruebas', N'codigo'),
    (N'psi.pruebas', N'nombre'), (N'psi.pruebas', N'descripcion'), (N'psi.pruebas', N'activo'), (N'psi.pruebas', N'creado_por'),
    (N'psi.pruebas', N'fecha_creacion'), (N'psi.pruebas', N'modificado_por'), (N'psi.pruebas', N'fecha_modificacion'),
    (N'psi.pruebas', N'token_concurrencia'),

    (N'psi.atencion_pruebas', N'id_atencion_prueba'), (N'psi.atencion_pruebas', N'id_tenant'), (N'psi.atencion_pruebas', N'id_empresa'),
    (N'psi.atencion_pruebas', N'id_atencion'), (N'psi.atencion_pruebas', N'id_prueba'), (N'psi.atencion_pruebas', N'creado_por'),
    (N'psi.atencion_pruebas', N'fecha_creacion'),

    (N'psi.tipos_constancia', N'id_tipo_constancia'), (N'psi.tipos_constancia', N'id_tenant'), (N'psi.tipos_constancia', N'id_empresa'),
    (N'psi.tipos_constancia', N'codigo'), (N'psi.tipos_constancia', N'nombre'), (N'psi.tipos_constancia', N'plantilla'),
    (N'psi.tipos_constancia', N'activo'), (N'psi.tipos_constancia', N'creado_por'), (N'psi.tipos_constancia', N'fecha_creacion'),
    (N'psi.tipos_constancia', N'modificado_por'), (N'psi.tipos_constancia', N'fecha_modificacion'), (N'psi.tipos_constancia', N'token_concurrencia'),

    (N'psi.constancias', N'id_constancia'), (N'psi.constancias', N'id_tenant'), (N'psi.constancias', N'id_empresa'),
    (N'psi.constancias', N'id_paciente'), (N'psi.constancias', N'id_atencion'), (N'psi.constancias', N'id_tipo_constancia'),
    (N'psi.constancias', N'numero_constancia'), (N'psi.constancias', N'fecha_emision'), (N'psi.constancias', N'id_persona_profesional'),
    (N'psi.constancias', N'nombre_profesional'), (N'psi.constancias', N'estado'), (N'psi.constancias', N'motivo_anulacion'),
    (N'psi.constancias', N'fecha_anulacion'), (N'psi.constancias', N'anulada_por'), (N'psi.constancias', N'creado_por'),
    (N'psi.constancias', N'fecha_creacion'), (N'psi.constancias', N'modificado_por'), (N'psi.constancias', N'fecha_modificacion'),
    (N'psi.constancias', N'token_concurrencia'),

    (N'psi.constancia_pruebas', N'id_constancia_prueba'), (N'psi.constancia_pruebas', N'id_tenant'), (N'psi.constancia_pruebas', N'id_empresa'),
    (N'psi.constancia_pruebas', N'id_constancia'), (N'psi.constancia_pruebas', N'id_prueba'), (N'psi.constancia_pruebas', N'creado_por'),
    (N'psi.constancia_pruebas', N'fecha_creacion'),

    (N'psi.profesionales', N'id_profesional'), (N'psi.profesionales', N'id_tenant'), (N'psi.profesionales', N'id_empresa'),
    (N'psi.profesionales', N'id_persona'), (N'psi.profesionales', N'tipo'), (N'psi.profesionales', N'titulo'),
    (N'psi.profesionales', N'nombre_firma'), (N'psi.profesionales', N'numero_colegiacion'), (N'psi.profesionales', N'especialidad'),
    (N'psi.profesionales', N'activo'), (N'psi.profesionales', N'creado_por'), (N'psi.profesionales', N'fecha_creacion'),
    (N'psi.profesionales', N'modificado_por'), (N'psi.profesionales', N'fecha_modificacion'), (N'psi.profesionales', N'token_concurrencia'),

    (N'psi.impresiones', N'id_impresion'), (N'psi.impresiones', N'id_tenant'), (N'psi.impresiones', N'id_empresa'),
    (N'psi.impresiones', N'id_atencion'), (N'psi.impresiones', N'id_constancia'), (N'psi.impresiones', N'id_lote'),
    (N'psi.impresiones', N'plantilla'), (N'psi.impresiones', N'version_plantilla'), (N'psi.impresiones', N'tamano_papel'),
    (N'psi.impresiones', N'creado_por'), (N'psi.impresiones', N'fecha_creacion'),

    (N'psi.importaciones', N'id_importacion'), (N'psi.importaciones', N'id_tenant'), (N'psi.importaciones', N'id_empresa'),
    (N'psi.importaciones', N'id_jornada'), (N'psi.importaciones', N'nombre_archivo'), (N'psi.importaciones', N'hash_sha256'),
    (N'psi.importaciones', N'hoja'), (N'psi.importaciones', N'total_filas'), (N'psi.importaciones', N'filas_nuevas'),
    (N'psi.importaciones', N'filas_reutilizadas'), (N'psi.importaciones', N'filas_rechazadas'), (N'psi.importaciones', N'estado'),
    (N'psi.importaciones', N'fecha_confirmacion'), (N'psi.importaciones', N'confirmado_por'), (N'psi.importaciones', N'creado_por'),
    (N'psi.importaciones', N'fecha_creacion'), (N'psi.importaciones', N'modificado_por'), (N'psi.importaciones', N'fecha_modificacion'),
    (N'psi.importaciones', N'token_concurrencia'),

    (N'psi.importacion_filas', N'id_importacion_fila'), (N'psi.importacion_filas', N'id_tenant'), (N'psi.importacion_filas', N'id_empresa'),
    (N'psi.importacion_filas', N'id_importacion'), (N'psi.importacion_filas', N'numero_fila'), (N'psi.importacion_filas', N'nombre_original'),
    (N'psi.importacion_filas', N'dni_original'), (N'psi.importacion_filas', N'edad_original'), (N'psi.importacion_filas', N'genero_original'),
    (N'psi.importacion_filas', N'primer_nombre'), (N'psi.importacion_filas', N'segundo_nombre'), (N'psi.importacion_filas', N'primer_apellido'),
    (N'psi.importacion_filas', N'segundo_apellido'), (N'psi.importacion_filas', N'resultado'), (N'psi.importacion_filas', N'mensaje'),
    (N'psi.importacion_filas', N'id_paciente'), (N'psi.importacion_filas', N'creado_por'), (N'psi.importacion_filas', N'fecha_creacion'),
    (N'psi.importacion_filas', N'modificado_por'), (N'psi.importacion_filas', N'fecha_modificacion');

DECLARE @errores NVARCHAR(MAX) = N'';
DECLARE @n INT = 0;
DECLARE @nPsi INT = 0;
DECLARE @defCk NVARCHAR(MAX);

-- a) scripts previos
IF OBJECT_ID(N'saas.tenants', N'U') IS NULL SET @errores += N' Falta la tabla saas.tenants (script 025).';
IF OBJECT_ID(N'seg.fn_tenant_actual') IS NULL SET @errores += N' Falta la funcion seg.fn_tenant_actual (script 036).';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_empresas_tenant_id_empresa' AND object_id = OBJECT_ID(N'dbo.empresas') AND is_unique = 1)
    SET @errores += N' Falta la clave UX_empresas_tenant_id_empresa de dbo.empresas (script 031).';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_personas_tenant_id_persona' AND object_id = OBJECT_ID(N'dbo.personas') AND is_unique = 1)
    SET @errores += N' Falta la clave UX_personas_tenant_id_persona de dbo.personas (script 032).';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo'
                 AND object_id = OBJECT_ID(N'dbo.persona_empresa') AND is_unique = 1)
    SET @errores += N' Falta la clave UX_persona_empresa_tenant_id_persona_empresa_id_empresa_tipo_vinculo de dbo.persona_empresa (script 032).';

-- b) politica RLS
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'))
    SET @errores += N' Falta la politica seg.pol_tenant (script 036).';
ELSE
BEGIN
    SELECT @n = COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant');
    SELECT @nPsi = COUNT(*)
    FROM sys.security_predicates p
    JOIN sys.objects o ON o.object_id = p.target_object_id
    WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND o.schema_id = SCHEMA_ID(N'psi');

    IF @nPsi NOT IN (0, 60)
        SET @errores += CONCAT(N' La politica tiene ', @nPsi, N' predicados sobre tablas de psi y se esperaban 0 o 60.');
    IF (@n - @nPsi) NOT IN (199, 204, 209)
        SET @errores += CONCAT(N' La politica seg.pol_tenant tiene ', @n - @nPsi, N' predicados fuera de psi y se esperaban 199, 204 o 209',
                               N' (199 del script 036; +5 con accesos_empresa del 038; +5 con rol_permisos del 041).');
END

SELECT @errores += N' ' + OBJECT_SCHEMA_NAME(p.target_object_id) + N'.' + OBJECT_NAME(p.target_object_id) + N' ya esta en la politica ' + sp.name + N'.'
FROM sys.security_predicates p JOIN sys.security_policies sp ON sp.object_id = p.object_id
WHERE sp.name <> N'pol_tenant' AND OBJECT_SCHEMA_NAME(p.target_object_id) = N'psi';

-- c) estado de los objetos nuevos: ninguno (primera vez) o todos (ya aplicado); a medias, no
DECLARE @tablasExisten INT = (SELECT COUNT(*) FROM (VALUES (N'psi.jornadas'), (N'psi.pacientes'), (N'psi.atenciones'), (N'psi.pruebas'),
                                                           (N'psi.atencion_pruebas'), (N'psi.tipos_constancia'), (N'psi.constancias'),
                                                           (N'psi.constancia_pruebas'), (N'psi.profesionales'), (N'psi.impresiones'),
                                                           (N'psi.importaciones'), (N'psi.importacion_filas')) v (t)
                              WHERE OBJECT_ID(v.t, N'U') IS NOT NULL);
DECLARE @objetosExisten INT = @tablasExisten + CASE WHEN @nPsi = 60 THEN 1 ELSE 0 END;

IF @objetosExisten NOT IN (0, 13)
    SET @errores += CONCAT(N' Los objetos de este script existen a medias (', @objetosExisten, N' de 13: ', @tablasExisten,
                           N' de 12 tablas y ', CASE WHEN @nPsi = 60 THEN N'con' ELSE N'sin' END,
                           N' predicados RLS de psi). Revisar a mano antes de seguir.');

SELECT @errores += N' La tabla ' + x.tabla + N' no tiene la columna ' + x.columna + N'.'
FROM #columnas_042 x
WHERE OBJECT_ID(x.tabla, N'U') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);

SELECT @errores += N' La tabla ' + SCHEMA_NAME(o.schema_id) + N'.' + o.name + N' tiene una columna no prevista: ' + c.name + N'.'
FROM sys.objects o
JOIN sys.columns c ON c.object_id = o.object_id
WHERE o.type = 'U'
  AND (SCHEMA_NAME(o.schema_id) + N'.' + o.name) IN (SELECT DISTINCT tabla FROM #columnas_042)
  AND NOT EXISTS (SELECT 1 FROM #columnas_042 x WHERE x.tabla = (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AND x.columna = c.name);

SELECT @errores += N' El esquema psi tiene una tabla no prevista: ' + o.name + N'.'
FROM sys.objects o
WHERE o.type = 'U' AND o.schema_id = SCHEMA_ID(N'psi')
  AND (SCHEMA_NAME(o.schema_id) + N'.' + o.name) NOT IN (SELECT DISTINCT tabla FROM #columnas_042);

-- d) CK_persona_empresa_tipo admite los 6 tipos de hoy (con o sin 'paciente')
SELECT @defCk = definition FROM sys.check_constraints
WHERE name = N'CK_persona_empresa_tipo' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa');

IF @defCk IS NULL
    SET @errores += N' Falta la restriccion CK_persona_empresa_tipo de dbo.persona_empresa (script 014).';
ELSE
    SELECT @errores += N' CK_persona_empresa_tipo no admite el tipo ' + v.tipo + N' (definicion inesperada; revisar a mano).'
    FROM (VALUES (N'empleado'), (N'cliente'), (N'proveedor'), (N'usuario'), (N'contacto'), (N'otro')) v (tipo)
    WHERE @defCk NOT LIKE N'%''' + v.tipo + N'''%';

IF LEN(@errores) > 0
BEGIN
    DECLARE @mensaje NVARCHAR(2048) = LEFT(N'PRECHECK 042 no paso, no se cambio nada:' + @errores, 2048);
    THROW 50042, @mensaje, 1;
END

CREATE TABLE #precheck_042 (ok BIT NOT NULL);
INSERT INTO #precheck_042 (ok) VALUES (1);

PRINT CONCAT(N'PRECHECK 042 correcto. Objetos nuevos ya existentes: ', @objetosExisten, N' de 13; predicados de la politica: ', @n,
             N' (', @nPsi, N' en psi); CK_persona_empresa_tipo ', CASE WHEN @defCk LIKE N'%''paciente''%' THEN N'ya admite' ELSE N'todavia no admite' END,
             N' el tipo paciente.');
GO

-- ------------------------------------------------------------
-- CAMBIO (una transaccion) + POSTCHECK
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_042') IS NULL
    THROW 50042, N'Falta la marca del PRECHECK: ejecute todo el archivo en una sola sesion. Abortar.', 1;

DECLARE @estabaEncendida BIT = (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg'));
DECLARE @predicadosAntes INT = (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant'));
DECLARE @esquemaNuevo BIT = 0, @ckCambiado BIT = 0;
DECLARE @tablasNuevas INT = 0, @predicadosNuevos INT = 0;
DECLARE @malas INT;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Esquema
    IF SCHEMA_ID(N'psi') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE SCHEMA psi AUTHORIZATION dbo;';
        SET @esquemaNuevo = 1;
    END

    -- 2. El paciente es un vinculo mas de la persona maestra
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_persona_empresa_tipo' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')
                     AND definition LIKE N'%''paciente''%')
    BEGIN
        ALTER TABLE dbo.persona_empresa DROP CONSTRAINT CK_persona_empresa_tipo;
        ALTER TABLE dbo.persona_empresa WITH CHECK ADD CONSTRAINT CK_persona_empresa_tipo
            CHECK (tipo_vinculo IN ('empleado', 'cliente', 'proveedor', 'usuario', 'contacto', 'otro', 'paciente'));
        SET @ckCambiado = 1;
    END

    -- 3. Tablas. Todas son TABLAS DE TENANT: id_tenant obligatorio, claves compuestas por tenant y politica RLS.
    IF OBJECT_ID(N'psi.jornadas', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.jornadas (
            id_jornada              INT IDENTITY(1,1) NOT NULL,
            id_tenant               INT            NOT NULL,
            id_empresa              INT            NOT NULL,
            nombre                  NVARCHAR(150)  NOT NULL,
            lugar                   NVARCHAR(150)  NULL,
            institucion             NVARCHAR(150)  NULL,
            fecha_inicio            DATE           NOT NULL,
            fecha_fin               DATE           NULL,
            id_persona_responsable  INT            NULL,
            nombre_responsable      NVARCHAR(200)  NULL,
            estado                  VARCHAR(10)    NOT NULL CONSTRAINT DF_jornadas_estado DEFAULT ('abierta'),
            observaciones           NVARCHAR(1000) NULL,
            creado_por              NVARCHAR(100)  NOT NULL,
            fecha_creacion          DATETIME2(3)   NOT NULL CONSTRAINT DF_jornadas_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por          NVARCHAR(100)  NULL,
            fecha_modificacion      DATETIME2(3)   NULL,
            token_concurrencia      ROWVERSION     NOT NULL,
            CONSTRAINT PK_jornadas PRIMARY KEY CLUSTERED (id_jornada),
            CONSTRAINT FK_jornadas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_jornadas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT FKT_jornadas_personas_id_persona_responsable FOREIGN KEY (id_tenant, id_persona_responsable)
                REFERENCES dbo.personas (id_tenant, id_persona),
            -- Destino de las claves compuestas de atenciones e importaciones (misma empresa y mismo tenant).
            CONSTRAINT UX_jornadas_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_jornada),
            CONSTRAINT CK_jornadas_estado CHECK (estado IN ('abierta', 'cerrada', 'anulada')),
            CONSTRAINT CK_jornadas_fechas CHECK (fecha_fin IS NULL OR fecha_fin >= fecha_inicio),
            CONSTRAINT CK_jornadas_nombre CHECK (LEN(nombre) > 0)
        );
        CREATE INDEX IX_jornadas_empresa_fecha ON psi.jornadas (id_empresa, fecha_inicio);
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.pacientes', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.pacientes (
            id_paciente         INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            id_persona_empresa  INT            NOT NULL,
            tipo_vinculo        VARCHAR(20)    NOT NULL CONSTRAINT DF_pacientes_tipo DEFAULT ('paciente'),
            numero_expediente   VARCHAR(20)    NOT NULL,
            fecha_apertura      DATE           NOT NULL,
            observaciones       NVARCHAR(1000) NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_pacientes_activo DEFAULT (1),
            eliminado           BIT            NOT NULL CONSTRAINT DF_pacientes_elim DEFAULT (0),
            fecha_eliminado     DATETIME2(3)   NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_pacientes_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_pacientes PRIMARY KEY CLUSTERED (id_paciente),
            CONSTRAINT FK_pacientes_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_pacientes_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            -- Cada paciente cuelga de un vinculo 'paciente' de la misma empresa y del mismo tenant (como dbo.empleados).
            CONSTRAINT FKT_pacientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo
                FOREIGN KEY (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo)
                REFERENCES dbo.persona_empresa (id_tenant, id_persona_empresa, id_empresa, tipo_vinculo),
            CONSTRAINT UX_pacientes_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_paciente),
            -- El numero de expediente (PSI-AAAA-NNNNN, decision D5) no se reutiliza en la empresa, ni el de un paciente eliminado.
            CONSTRAINT UX_pacientes_expediente UNIQUE (id_empresa, numero_expediente),
            CONSTRAINT CK_pacientes_tipo CHECK (tipo_vinculo = 'paciente'),
            CONSTRAINT CK_pacientes_expediente CHECK (LEN(numero_expediente) > 0),
            CONSTRAINT CK_pacientes_eliminado CHECK (eliminado = 0 OR fecha_eliminado IS NOT NULL)
        );
        -- Un vinculo tiene como mucho una ficha de paciente vigente.
        CREATE UNIQUE INDEX UX_pacientes_vinculo ON psi.pacientes (id_persona_empresa) WHERE eliminado = 0;
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.atenciones', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.atenciones (
            id_atencion             INT IDENTITY(1,1) NOT NULL,
            id_tenant               INT            NOT NULL,
            id_empresa              INT            NOT NULL,
            id_paciente             INT            NOT NULL,
            id_jornada              INT            NULL,
            fecha_atencion          DATE           NOT NULL,
            -- Edad que declaro o que trae la fuente al momento de la atencion (decision D3); no se deduce fecha de nacimiento.
            edad_declarada          TINYINT        NULL,
            id_persona_profesional  INT            NULL,
            -- Nombre del profesional tal como sale impreso (se conserva aunque la persona cambie despues).
            nombre_profesional      NVARCHAR(200)  NULL,
            estado                  VARCHAR(10)    NOT NULL CONSTRAINT DF_atenciones_estado DEFAULT ('borrador'),
            observaciones           NVARCHAR(4000) NULL,
            fecha_cierre            DATETIME2(3)   NULL,
            cerrado_por             NVARCHAR(100)  NULL,
            motivo_anulacion        NVARCHAR(300)  NULL,
            creado_por              NVARCHAR(100)  NOT NULL,
            fecha_creacion          DATETIME2(3)   NOT NULL CONSTRAINT DF_atenciones_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por          NVARCHAR(100)  NULL,
            fecha_modificacion      DATETIME2(3)   NULL,
            token_concurrencia      ROWVERSION     NOT NULL,
            CONSTRAINT PK_atenciones PRIMARY KEY CLUSTERED (id_atencion),
            CONSTRAINT FK_atenciones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_atenciones_pacientes_id_empresa_id_paciente FOREIGN KEY (id_tenant, id_empresa, id_paciente)
                REFERENCES psi.pacientes (id_tenant, id_empresa, id_paciente),
            CONSTRAINT FKT_atenciones_jornadas_id_empresa_id_jornada FOREIGN KEY (id_tenant, id_empresa, id_jornada)
                REFERENCES psi.jornadas (id_tenant, id_empresa, id_jornada),
            CONSTRAINT FKT_atenciones_personas_id_persona_profesional FOREIGN KEY (id_tenant, id_persona_profesional)
                REFERENCES dbo.personas (id_tenant, id_persona),
            CONSTRAINT UX_atenciones_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_atencion),
            CONSTRAINT CK_atenciones_estado CHECK (estado IN ('borrador', 'cerrada', 'anulada')),
            CONSTRAINT CK_atenciones_edad CHECK (edad_declarada IS NULL OR edad_declarada <= 120),
            -- Una atencion cerrada ya no se edita (la aplicacion lo hace cumplir); aqui se exige saber quien y cuando la cerro.
            CONSTRAINT CK_atenciones_cierre CHECK (estado <> 'cerrada' OR (fecha_cierre IS NOT NULL AND cerrado_por IS NOT NULL)),
            CONSTRAINT CK_atenciones_anulacion CHECK (estado <> 'anulada' OR motivo_anulacion IS NOT NULL)
        );
        -- Una sola atencion vigente por paciente en cada jornada: reimportar la brigada no la duplica (decision D7).
        CREATE UNIQUE INDEX UX_atenciones_jornada_paciente ON psi.atenciones (id_jornada, id_paciente)
            WHERE id_jornada IS NOT NULL AND estado <> 'anulada';
        CREATE INDEX IX_atenciones_paciente_fecha ON psi.atenciones (id_paciente, fecha_atencion);
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.pruebas', N'U') IS NULL
    BEGIN
        -- Catalogo de pruebas psicologicas de la empresa (decision D12): MMPI-2, 16PF, Raven, Bender...
        CREATE TABLE psi.pruebas (
            id_prueba           INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            codigo              VARCHAR(30)    NOT NULL,
            nombre              NVARCHAR(150)  NOT NULL,
            descripcion         NVARCHAR(500)  NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_pruebas_activo DEFAULT (1),
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_pruebas_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_pruebas PRIMARY KEY CLUSTERED (id_prueba),
            CONSTRAINT FK_pruebas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_pruebas_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT UX_pruebas_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_prueba),
            CONSTRAINT UX_pruebas_codigo UNIQUE (id_empresa, codigo),
            CONSTRAINT CK_pruebas_codigo CHECK (LEN(codigo) > 0),
            CONSTRAINT CK_pruebas_nombre CHECK (LEN(nombre) > 0)
        );
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.atencion_pruebas', N'U') IS NULL
    BEGIN
        -- Pruebas aplicadas en cada atencion (una o varias).
        CREATE TABLE psi.atencion_pruebas (
            id_atencion_prueba  INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            id_atencion         INT            NOT NULL,
            id_prueba           INT            NOT NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_atencion_pruebas_fc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_atencion_pruebas PRIMARY KEY CLUSTERED (id_atencion_prueba),
            CONSTRAINT FK_atencion_pruebas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_atencion_pruebas_atenciones_id_empresa_id_atencion FOREIGN KEY (id_tenant, id_empresa, id_atencion)
                REFERENCES psi.atenciones (id_tenant, id_empresa, id_atencion),
            CONSTRAINT FKT_atencion_pruebas_pruebas_id_empresa_id_prueba FOREIGN KEY (id_tenant, id_empresa, id_prueba)
                REFERENCES psi.pruebas (id_tenant, id_empresa, id_prueba),
            CONSTRAINT UX_atencion_pruebas_atencion_prueba UNIQUE (id_atencion, id_prueba)
        );
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.tipos_constancia', N'U') IS NULL
    BEGIN
        -- Tipos de constancia de examen psicologico (decision D14): cada uno con su propio formato de impresion.
        CREATE TABLE psi.tipos_constancia (
            id_tipo_constancia  INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            codigo              VARCHAR(30)    NOT NULL,
            nombre              NVARCHAR(150)  NOT NULL,
            -- Formato con que se imprime (se define cuando llega cada formato; se imprime sobre papel ya membretado, D15).
            plantilla           VARCHAR(40)    NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_tipos_constancia_activo DEFAULT (1),
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_tipos_constancia_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_tipos_constancia PRIMARY KEY CLUSTERED (id_tipo_constancia),
            CONSTRAINT FK_tipos_constancia_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_tipos_constancia_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT UX_tipos_constancia_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_tipo_constancia),
            CONSTRAINT UX_tipos_constancia_codigo UNIQUE (id_empresa, codigo),
            CONSTRAINT CK_tipos_constancia_codigo CHECK (LEN(codigo) > 0),
            CONSTRAINT CK_tipos_constancia_nombre CHECK (LEN(nombre) > 0)
        );
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.constancias', N'U') IS NULL
    BEGIN
        -- Constancias de examen psicologico extendidas al paciente (decision D13: un correlativo por tipo de constancia, sin reutilizar
        -- numeros: CODIGO-AAAA-NNNNN, donde CODIGO es el del tipo, de hasta 10 caracteres, que no cambia una vez que el tipo tiene constancias).
        CREATE TABLE psi.constancias (
            id_constancia           INT IDENTITY(1,1) NOT NULL,
            id_tenant               INT            NOT NULL,
            id_empresa              INT            NOT NULL,
            id_paciente             INT            NOT NULL,
            -- La atencion (evaluacion) de la que sale, si la hay.
            id_atencion             INT            NULL,
            id_tipo_constancia      INT            NOT NULL,
            numero_constancia       VARCHAR(30)    NOT NULL,
            fecha_emision           DATE           NOT NULL,
            id_persona_profesional  INT            NULL,
            nombre_profesional      NVARCHAR(200)  NULL,
            estado                  VARCHAR(10)    NOT NULL CONSTRAINT DF_constancias_estado DEFAULT ('emitida'),
            motivo_anulacion        NVARCHAR(300)  NULL,
            fecha_anulacion         DATETIME2(3)   NULL,
            anulada_por             NVARCHAR(100)  NULL,
            creado_por              NVARCHAR(100)  NOT NULL,
            fecha_creacion          DATETIME2(3)   NOT NULL CONSTRAINT DF_constancias_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por          NVARCHAR(100)  NULL,
            fecha_modificacion      DATETIME2(3)   NULL,
            token_concurrencia      ROWVERSION     NOT NULL,
            CONSTRAINT PK_constancias PRIMARY KEY CLUSTERED (id_constancia),
            CONSTRAINT FK_constancias_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_constancias_pacientes_id_empresa_id_paciente FOREIGN KEY (id_tenant, id_empresa, id_paciente)
                REFERENCES psi.pacientes (id_tenant, id_empresa, id_paciente),
            CONSTRAINT FKT_constancias_atenciones_id_empresa_id_atencion FOREIGN KEY (id_tenant, id_empresa, id_atencion)
                REFERENCES psi.atenciones (id_tenant, id_empresa, id_atencion),
            CONSTRAINT FKT_constancias_tipos_constancia_id_empresa_id_tipo_constancia FOREIGN KEY (id_tenant, id_empresa, id_tipo_constancia)
                REFERENCES psi.tipos_constancia (id_tenant, id_empresa, id_tipo_constancia),
            CONSTRAINT FKT_constancias_personas_id_persona_profesional FOREIGN KEY (id_tenant, id_persona_profesional)
                REFERENCES dbo.personas (id_tenant, id_persona),
            CONSTRAINT UX_constancias_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_constancia),
            -- El numero no se repite en la empresa, tampoco el de una constancia anulada (el prefijo distingue cada tipo).
            CONSTRAINT UX_constancias_numero UNIQUE (id_empresa, numero_constancia),
            CONSTRAINT CK_constancias_estado CHECK (estado IN ('emitida', 'anulada')),
            CONSTRAINT CK_constancias_numero CHECK (LEN(numero_constancia) > 0),
            CONSTRAINT CK_constancias_anulacion CHECK (estado <> 'anulada'
                                                       OR (motivo_anulacion IS NOT NULL AND fecha_anulacion IS NOT NULL AND anulada_por IS NOT NULL))
        );
        CREATE INDEX IX_constancias_paciente_fecha ON psi.constancias (id_paciente, fecha_emision);
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.constancia_pruebas', N'U') IS NULL
    BEGIN
        -- Pruebas psicologicas que declara cada constancia (una o varias).
        CREATE TABLE psi.constancia_pruebas (
            id_constancia_prueba  INT IDENTITY(1,1) NOT NULL,
            id_tenant             INT            NOT NULL,
            id_empresa            INT            NOT NULL,
            id_constancia         INT            NOT NULL,
            id_prueba             INT            NOT NULL,
            creado_por            NVARCHAR(100)  NOT NULL,
            fecha_creacion        DATETIME2(3)   NOT NULL CONSTRAINT DF_constancia_pruebas_fc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_constancia_pruebas PRIMARY KEY CLUSTERED (id_constancia_prueba),
            CONSTRAINT FK_constancia_pruebas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_constancia_pruebas_constancias_id_empresa_id_constancia FOREIGN KEY (id_tenant, id_empresa, id_constancia)
                REFERENCES psi.constancias (id_tenant, id_empresa, id_constancia),
            CONSTRAINT FKT_constancia_pruebas_pruebas_id_empresa_id_prueba FOREIGN KEY (id_tenant, id_empresa, id_prueba)
                REFERENCES psi.pruebas (id_tenant, id_empresa, id_prueba),
            CONSTRAINT UX_constancia_pruebas_constancia_prueba UNIQUE (id_constancia, id_prueba)
        );
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.profesionales', N'U') IS NULL
    BEGIN
        -- Profesionales del centro (decisiones D2 y D11): psicologos que atienden, firman constancias y son responsables de brigadas, y
        -- administradores que tambien pueden ser responsables. Cada uno es una persona de la cuenta; su usuario del sistema se enlaza
        -- por esa persona (saas.membresias.id_persona), sin columna propia.
        CREATE TABLE psi.profesionales (
            id_profesional      INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            id_persona          INT            NOT NULL,
            tipo                VARCHAR(15)    NOT NULL CONSTRAINT DF_profesionales_tipo DEFAULT ('psicologo'),
            -- Abreviatura que va antes del nombre en lo impreso (Lic., M.Sc., Dr.).
            titulo              NVARCHAR(30)   NULL,
            -- Nombre como sale en las constancias y en la firma.
            nombre_firma        NVARCHAR(200)  NOT NULL,
            numero_colegiacion  VARCHAR(30)    NULL,
            especialidad        NVARCHAR(150)  NULL,
            activo              BIT            NOT NULL CONSTRAINT DF_profesionales_activo DEFAULT (1),
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_profesionales_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_profesionales PRIMARY KEY CLUSTERED (id_profesional),
            CONSTRAINT FK_profesionales_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_profesionales_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT FKT_profesionales_personas_id_persona FOREIGN KEY (id_tenant, id_persona) REFERENCES dbo.personas (id_tenant, id_persona),
            CONSTRAINT UX_profesionales_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_profesional),
            -- Una persona es profesional una sola vez por empresa.
            CONSTRAINT UX_profesionales_persona UNIQUE (id_empresa, id_persona),
            CONSTRAINT CK_profesionales_tipo CHECK (tipo IN ('psicologo', 'administrador')),
            CONSTRAINT CK_profesionales_nombre_firma CHECK (LEN(nombre_firma) > 0)
        );
        -- El numero de colegiacion no se repite en la empresa (los administradores pueden no tenerlo).
        CREATE UNIQUE INDEX UX_profesionales_colegiacion ON psi.profesionales (id_empresa, numero_colegiacion) WHERE numero_colegiacion IS NOT NULL;
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.impresiones', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.impresiones (
            id_impresion        BIGINT IDENTITY(1,1) NOT NULL,
            id_tenant           INT              NOT NULL,
            id_empresa          INT              NOT NULL,
            -- Lo impreso: una atencion (historia) o una constancia; exactamente una de las dos.
            id_atencion         INT              NULL,
            id_constancia       INT              NULL,
            -- Mismo valor para todos los documentos impresos de una vez (por ejemplo, la jornada completa).
            id_lote             UNIQUEIDENTIFIER NULL,
            plantilla           VARCHAR(40)      NOT NULL,
            version_plantilla   VARCHAR(20)      NOT NULL,
            tamano_papel        VARCHAR(15)      NOT NULL,
            creado_por          NVARCHAR(100)    NOT NULL,
            fecha_creacion      DATETIME2(3)     NOT NULL CONSTRAINT DF_impresiones_fc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_impresiones PRIMARY KEY CLUSTERED (id_impresion),
            CONSTRAINT FK_impresiones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_impresiones_atenciones_id_empresa_id_atencion FOREIGN KEY (id_tenant, id_empresa, id_atencion)
                REFERENCES psi.atenciones (id_tenant, id_empresa, id_atencion),
            CONSTRAINT FKT_impresiones_constancias_id_empresa_id_constancia FOREIGN KEY (id_tenant, id_empresa, id_constancia)
                REFERENCES psi.constancias (id_tenant, id_empresa, id_constancia),
            CONSTRAINT CK_impresiones_documento CHECK ((id_atencion IS NOT NULL AND id_constancia IS NULL)
                                                       OR (id_atencion IS NULL AND id_constancia IS NOT NULL)),
            CONSTRAINT CK_impresiones_papel CHECK (tamano_papel IN ('carta', 'oficio', 'media_carta')),
            CONSTRAINT CK_impresiones_plantilla CHECK (LEN(plantilla) > 0 AND LEN(version_plantilla) > 0)
        );
        CREATE INDEX IX_impresiones_atencion ON psi.impresiones (id_atencion, fecha_creacion);
        CREATE INDEX IX_impresiones_constancia ON psi.impresiones (id_constancia, fecha_creacion);
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.importaciones', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.importaciones (
            id_importacion      INT IDENTITY(1,1) NOT NULL,
            id_tenant           INT            NOT NULL,
            id_empresa          INT            NOT NULL,
            id_jornada          INT            NULL,
            nombre_archivo      NVARCHAR(260)  NOT NULL,
            hash_sha256         CHAR(64)       NOT NULL,
            hoja                NVARCHAR(100)  NULL,
            total_filas         INT            NOT NULL CONSTRAINT DF_importaciones_total DEFAULT (0),
            filas_nuevas        INT            NOT NULL CONSTRAINT DF_importaciones_nuevas DEFAULT (0),
            filas_reutilizadas  INT            NOT NULL CONSTRAINT DF_importaciones_reutilizadas DEFAULT (0),
            filas_rechazadas    INT            NOT NULL CONSTRAINT DF_importaciones_rechazadas DEFAULT (0),
            estado              VARCHAR(12)    NOT NULL CONSTRAINT DF_importaciones_estado DEFAULT ('borrador'),
            fecha_confirmacion  DATETIME2(3)   NULL,
            confirmado_por      NVARCHAR(100)  NULL,
            creado_por          NVARCHAR(100)  NOT NULL,
            fecha_creacion      DATETIME2(3)   NOT NULL CONSTRAINT DF_importaciones_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por      NVARCHAR(100)  NULL,
            fecha_modificacion  DATETIME2(3)   NULL,
            token_concurrencia  ROWVERSION     NOT NULL,
            CONSTRAINT PK_importaciones PRIMARY KEY CLUSTERED (id_importacion),
            CONSTRAINT FK_importaciones_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_importaciones_empresas_id_empresa FOREIGN KEY (id_tenant, id_empresa) REFERENCES dbo.empresas (id_tenant, id_empresa),
            CONSTRAINT FKT_importaciones_jornadas_id_empresa_id_jornada FOREIGN KEY (id_tenant, id_empresa, id_jornada)
                REFERENCES psi.jornadas (id_tenant, id_empresa, id_jornada),
            CONSTRAINT UX_importaciones_tenant_empresa_id UNIQUE (id_tenant, id_empresa, id_importacion),
            CONSTRAINT CK_importaciones_estado CHECK (estado IN ('borrador', 'confirmada', 'descartada')),
            -- SHA-256 en hexadecimal en minusculas: 64 caracteres 0-9 y a-f.
            CONSTRAINT CK_importaciones_hash CHECK (LEN(hash_sha256) = 64 AND hash_sha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
            CONSTRAINT CK_importaciones_conteos CHECK (total_filas >= 0 AND filas_nuevas >= 0 AND filas_reutilizadas >= 0 AND filas_rechazadas >= 0
                                                       AND filas_nuevas + filas_reutilizadas + filas_rechazadas <= total_filas),
            CONSTRAINT CK_importaciones_confirmacion CHECK (estado <> 'confirmada'
                                                            OR (fecha_confirmacion IS NOT NULL AND confirmado_por IS NOT NULL AND id_jornada IS NOT NULL))
        );
        -- El mismo archivo no se confirma dos veces en la misma empresa.
        CREATE UNIQUE INDEX UX_importaciones_hash ON psi.importaciones (id_empresa, hash_sha256) WHERE estado = 'confirmada';
        SET @tablasNuevas += 1;
    END

    IF OBJECT_ID(N'psi.importacion_filas', N'U') IS NULL
    BEGIN
        CREATE TABLE psi.importacion_filas (
            id_importacion_fila  INT IDENTITY(1,1) NOT NULL,
            id_tenant            INT            NOT NULL,
            id_empresa           INT            NOT NULL,
            id_importacion       INT            NOT NULL,
            numero_fila          INT            NOT NULL,
            -- Valores tal como venian en el archivo.
            nombre_original      NVARCHAR(300)  NULL,
            dni_original         NVARCHAR(30)   NULL,
            edad_original        NVARCHAR(10)   NULL,
            genero_original      NVARCHAR(10)   NULL,
            -- Reparto propuesto (o corregido en la previsualizacion) de nombres y apellidos, con los largos de dbo.personas.
            primer_nombre        NVARCHAR(50)   NULL,
            segundo_nombre       NVARCHAR(50)   NULL,
            primer_apellido      NVARCHAR(50)   NULL,
            segundo_apellido     NVARCHAR(50)   NULL,
            resultado            VARCHAR(12)    NOT NULL CONSTRAINT DF_importacion_filas_resultado DEFAULT ('pendiente'),
            mensaje              NVARCHAR(500)  NULL,
            id_paciente          INT            NULL,
            creado_por           NVARCHAR(100)  NOT NULL,
            fecha_creacion       DATETIME2(3)   NOT NULL CONSTRAINT DF_importacion_filas_fc DEFAULT (SYSUTCDATETIME()),
            modificado_por       NVARCHAR(100)  NULL,
            fecha_modificacion   DATETIME2(3)   NULL,
            CONSTRAINT PK_importacion_filas PRIMARY KEY CLUSTERED (id_importacion_fila),
            CONSTRAINT FK_importacion_filas_tenants FOREIGN KEY (id_tenant) REFERENCES saas.tenants (id_tenant),
            CONSTRAINT FKT_importacion_filas_importaciones_id_empresa_id_importacion FOREIGN KEY (id_tenant, id_empresa, id_importacion)
                REFERENCES psi.importaciones (id_tenant, id_empresa, id_importacion),
            CONSTRAINT FKT_importacion_filas_pacientes_id_empresa_id_paciente FOREIGN KEY (id_tenant, id_empresa, id_paciente)
                REFERENCES psi.pacientes (id_tenant, id_empresa, id_paciente),
            CONSTRAINT UX_importacion_filas_fila UNIQUE (id_importacion, numero_fila),
            CONSTRAINT CK_importacion_filas_resultado CHECK (resultado IN ('pendiente', 'nueva', 'reutilizada', 'revisar', 'error', 'omitida')),
            CONSTRAINT CK_importacion_filas_numero CHECK (numero_fila > 0)
        );
        SET @tablasNuevas += 1;
    END

    -- 4. Politica: las 12 tablas entran con las mismas funciones que las demas tablas de tenant (1 filtro y 4 bloqueos cada una).
    --    El PRECHECK garantiza que estan todas o ninguna. ALTER SECURITY POLICY ... ADD no cambia el estado de la politica.
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant') AND target_object_id = OBJECT_ID(N'psi.jornadas'))
    BEGIN
        EXEC sys.sp_executesql N'ALTER SECURITY POLICY seg.pol_tenant
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.jornadas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.jornadas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.jornadas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.jornadas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.jornadas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pacientes,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pacientes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pacientes AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pacientes BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pacientes BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atenciones,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atenciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atenciones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atenciones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atenciones BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pruebas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pruebas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pruebas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pruebas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.pruebas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atencion_pruebas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atencion_pruebas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atencion_pruebas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atencion_pruebas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.atencion_pruebas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.tipos_constancia,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.tipos_constancia AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.tipos_constancia AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.tipos_constancia BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.tipos_constancia BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancias,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancias AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancias AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancias BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancias BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancia_pruebas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancia_pruebas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancia_pruebas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancia_pruebas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.constancia_pruebas BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.profesionales,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.profesionales AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.profesionales AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.profesionales BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.profesionales BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.impresiones,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.impresiones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.impresiones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.impresiones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.impresiones BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importaciones,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importaciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importaciones AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importaciones BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importaciones BEFORE DELETE,
    ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importacion_filas,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importacion_filas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importacion_filas AFTER UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importacion_filas BEFORE UPDATE,
    ADD BLOCK PREDICATE seg.fn_tenant_actual(id_tenant) ON psi.importacion_filas BEFORE DELETE;';
        SET @predicadosNuevos = 60;
    END

    -- 5. POSTCHECK: si algo no cuadra, se revierte todo.
    -- 5a. tablas con exactamente sus columnas
    SELECT @malas = COUNT(*)
    FROM #columnas_042 x
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(x.tabla) AND c.name = x.columna);
    IF @malas > 0
        THROW 50042, N'POSTCHECK: falta alguna columna en las tablas nuevas. Se revierte.', 1;

    SELECT @malas = COUNT(*)
    FROM sys.columns c
    JOIN sys.objects o ON o.object_id = c.object_id AND o.type = 'U'
    WHERE (SCHEMA_NAME(o.schema_id) + N'.' + o.name) IN (SELECT DISTINCT tabla FROM #columnas_042)
      AND NOT EXISTS (SELECT 1 FROM #columnas_042 x WHERE x.tabla = (SCHEMA_NAME(o.schema_id) + N'.' + o.name) AND x.columna = c.name);
    IF @malas > 0
        THROW 50042, N'POSTCHECK: alguna tabla nueva tiene columnas no previstas. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM sys.objects WHERE type = 'U' AND schema_id = SCHEMA_ID(N'psi')) <> 12
        THROW 50042, N'POSTCHECK: el esquema psi no tiene exactamente las 12 tablas previstas. Se revierte.', 1;

    -- 5b. id_tenant obligatorio en las 12 tablas
    SELECT @malas = COUNT(*)
    FROM sys.columns c
    JOIN sys.objects o ON o.object_id = c.object_id AND o.type = 'U' AND o.schema_id = SCHEMA_ID(N'psi')
    WHERE c.name = N'id_tenant' AND c.is_nullable = 1;
    IF @malas > 0
        THROW 50042, N'POSTCHECK: alguna tabla de psi tiene id_tenant nulable. Se revierte.', 1;

    -- 5c. restricciones CHECK existen y son confiables (no estan deshabilitadas ni sin validar)
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'CK_persona_empresa_tipo'),
                 (N'CK_jornadas_estado'), (N'CK_jornadas_fechas'), (N'CK_jornadas_nombre'),
                 (N'CK_pacientes_tipo'), (N'CK_pacientes_expediente'), (N'CK_pacientes_eliminado'),
                 (N'CK_atenciones_estado'), (N'CK_atenciones_edad'), (N'CK_atenciones_cierre'), (N'CK_atenciones_anulacion'),
                 (N'CK_pruebas_codigo'), (N'CK_pruebas_nombre'),
                 (N'CK_tipos_constancia_codigo'), (N'CK_tipos_constancia_nombre'),
                 (N'CK_constancias_estado'), (N'CK_constancias_numero'), (N'CK_constancias_anulacion'),
                 (N'CK_profesionales_tipo'), (N'CK_profesionales_nombre_firma'),
                 (N'CK_impresiones_documento'), (N'CK_impresiones_papel'), (N'CK_impresiones_plantilla'),
                 (N'CK_importaciones_estado'), (N'CK_importaciones_hash'), (N'CK_importaciones_conteos'), (N'CK_importaciones_confirmacion'),
                 (N'CK_importacion_filas_resultado'), (N'CK_importacion_filas_numero')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.check_constraints k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50042, N'POSTCHECK: falta alguna restriccion CHECK o no es confiable. Se revierte.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_persona_empresa_tipo' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')
                     AND definition LIKE N'%''paciente''%')
        THROW 50042, N'POSTCHECK: CK_persona_empresa_tipo no admite el tipo paciente. Se revierte.', 1;

    -- 5d. claves foraneas existen y son confiables
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'FK_jornadas_tenants'), (N'FKT_jornadas_empresas_id_empresa'), (N'FKT_jornadas_personas_id_persona_responsable'),
                 (N'FK_pacientes_tenants'), (N'FKT_pacientes_empresas_id_empresa'),
                 (N'FKT_pacientes_persona_empresa_id_persona_empresa_id_empresa_tipo_vinculo'),
                 (N'FK_atenciones_tenants'), (N'FKT_atenciones_pacientes_id_empresa_id_paciente'), (N'FKT_atenciones_jornadas_id_empresa_id_jornada'),
                 (N'FKT_atenciones_personas_id_persona_profesional'),
                 (N'FK_pruebas_tenants'), (N'FKT_pruebas_empresas_id_empresa'),
                 (N'FK_atencion_pruebas_tenants'), (N'FKT_atencion_pruebas_atenciones_id_empresa_id_atencion'),
                 (N'FKT_atencion_pruebas_pruebas_id_empresa_id_prueba'),
                 (N'FK_tipos_constancia_tenants'), (N'FKT_tipos_constancia_empresas_id_empresa'),
                 (N'FK_constancias_tenants'), (N'FKT_constancias_pacientes_id_empresa_id_paciente'), (N'FKT_constancias_atenciones_id_empresa_id_atencion'),
                 (N'FKT_constancias_tipos_constancia_id_empresa_id_tipo_constancia'), (N'FKT_constancias_personas_id_persona_profesional'),
                 (N'FK_constancia_pruebas_tenants'), (N'FKT_constancia_pruebas_constancias_id_empresa_id_constancia'),
                 (N'FKT_constancia_pruebas_pruebas_id_empresa_id_prueba'),
                 (N'FK_profesionales_tenants'), (N'FKT_profesionales_empresas_id_empresa'), (N'FKT_profesionales_personas_id_persona'),
                 (N'FK_impresiones_tenants'), (N'FKT_impresiones_atenciones_id_empresa_id_atencion'), (N'FKT_impresiones_constancias_id_empresa_id_constancia'),
                 (N'FK_importaciones_tenants'), (N'FKT_importaciones_empresas_id_empresa'), (N'FKT_importaciones_jornadas_id_empresa_id_jornada'),
                 (N'FK_importacion_filas_tenants'), (N'FKT_importacion_filas_importaciones_id_empresa_id_importacion'),
                 (N'FKT_importacion_filas_pacientes_id_empresa_id_paciente')) v (nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys k WHERE k.name = v.nombre AND k.is_disabled = 0 AND k.is_not_trusted = 0);
    IF @malas > 0
        THROW 50042, N'POSTCHECK: falta alguna clave foranea o no es confiable. Se revierte.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys k JOIN sys.objects o ON o.object_id = k.parent_object_id WHERE o.schema_id = SCHEMA_ID(N'psi')) <> 37
        THROW 50042, N'POSTCHECK: las tablas de psi no tienen exactamente 37 claves foraneas. Se revierte.', 1;

    -- 5e. indices unicos
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'psi.jornadas', N'UX_jornadas_tenant_empresa_id'),
                 (N'psi.pacientes', N'UX_pacientes_tenant_empresa_id'), (N'psi.pacientes', N'UX_pacientes_expediente'),
                 (N'psi.pacientes', N'UX_pacientes_vinculo'),
                 (N'psi.atenciones', N'UX_atenciones_tenant_empresa_id'), (N'psi.atenciones', N'UX_atenciones_jornada_paciente'),
                 (N'psi.pruebas', N'UX_pruebas_tenant_empresa_id'), (N'psi.pruebas', N'UX_pruebas_codigo'),
                 (N'psi.atencion_pruebas', N'UX_atencion_pruebas_atencion_prueba'),
                 (N'psi.tipos_constancia', N'UX_tipos_constancia_tenant_empresa_id'), (N'psi.tipos_constancia', N'UX_tipos_constancia_codigo'),
                 (N'psi.constancias', N'UX_constancias_tenant_empresa_id'), (N'psi.constancias', N'UX_constancias_numero'),
                 (N'psi.constancia_pruebas', N'UX_constancia_pruebas_constancia_prueba'),
                 (N'psi.profesionales', N'UX_profesionales_tenant_empresa_id'), (N'psi.profesionales', N'UX_profesionales_persona'),
                 (N'psi.profesionales', N'UX_profesionales_colegiacion'),
                 (N'psi.importaciones', N'UX_importaciones_tenant_empresa_id'), (N'psi.importaciones', N'UX_importaciones_hash'),
                 (N'psi.importacion_filas', N'UX_importacion_filas_fila')) v (tabla, nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = OBJECT_ID(v.tabla) AND i.name = v.nombre AND i.is_unique = 1);
    IF @malas > 0
        THROW 50042, N'POSTCHECK: falta algun indice unico. Se revierte.', 1;

    -- 5f. politica: 5 predicados por tabla (1 filtro y 4 bloqueos), todos de seg.fn_tenant_actual, y nada mas cambio
    SELECT @malas = COUNT(*)
    FROM (VALUES (N'psi.jornadas'), (N'psi.pacientes'), (N'psi.atenciones'), (N'psi.pruebas'), (N'psi.atencion_pruebas'),
                 (N'psi.tipos_constancia'), (N'psi.constancias'), (N'psi.constancia_pruebas'), (N'psi.profesionales'),
                 (N'psi.impresiones'), (N'psi.importaciones'), (N'psi.importacion_filas')) v (t)
    WHERE (SELECT COUNT(*) FROM sys.security_predicates p
           WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(v.t)
             AND p.predicate_definition LIKE N'%fn_tenant_actual%') <> 5
       OR (SELECT COUNT(*) FROM sys.security_predicates p
           WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND p.target_object_id = OBJECT_ID(v.t)
             AND p.predicate_type_desc = N'FILTER') <> 1;
    IF @malas > 0
        THROW 50042, N'POSTCHECK: alguna tabla de psi no tiene sus 5 predicados (1 filtro y 4 bloqueos de fn_tenant_actual). Se revierte.', 1;

    IF (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) <> @predicadosAntes + @predicadosNuevos
        THROW 50042, N'POSTCHECK: la politica cambio fuera de las tablas de psi. Se revierte.', 1;

    IF (SELECT is_enabled FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) <> @estabaEncendida
        THROW 50042, N'POSTCHECK: la politica cambio de estado. Se revierte.', 1;

    COMMIT TRANSACTION;

    IF @esquemaNuevo = 0 AND @ckCambiado = 0 AND @tablasNuevas = 0 AND @predicadosNuevos = 0
        PRINT N'Nada que hacer: el script 042 ya estaba aplicado.';
    ELSE
        PRINT CONCAT(N'CAMBIO 042 aplicado: esquema psi ', CASE WHEN @esquemaNuevo = 1 THEN N'creado' ELSE N'ya existia' END,
                     N'; CK_persona_empresa_tipo ', CASE WHEN @ckCambiado = 1 THEN N'admite ahora el tipo paciente' ELSE N'sin cambios' END,
                     N'; tablas nuevas: ', @tablasNuevas, N'; predicados nuevos: ', @predicadosNuevos,
                     N'; politica ', CASE WHEN @estabaEncendida = 1 THEN N'encendida' ELSE N'apagada' END, N' (sin cambio de estado).');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resumen (solo lectura; sys.partitions no pasa por la politica RLS)
SELECT SCHEMA_NAME(o.schema_id) + N'.' + o.name AS tabla,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = o.object_id) AS columnas,
       (SELECT COUNT(*) FROM sys.security_predicates p WHERE p.target_object_id = o.object_id) AS predicados_rls,
       (SELECT SUM(pa.rows) FROM sys.partitions pa WHERE pa.object_id = o.object_id AND pa.index_id IN (0, 1)) AS filas
FROM sys.objects o
WHERE o.type = 'U' AND o.schema_id = SCHEMA_ID(N'psi')
ORDER BY o.name;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Se detiene si alguna tabla de psi tiene filas (cuenta con sys.partitions, que no pasa por la politica RLS).
--   CK_persona_empresa_tipo vuelve a los 6 tipos con WITH CHECK: si queda algun vinculo 'paciente', falla y se revierte todo
--   (primero hay que terminar o borrar esos vinculos con la aplicacion).
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM sys.partitions pa JOIN sys.objects o ON o.object_id = pa.object_id
           WHERE o.schema_id = SCHEMA_ID(N'psi') AND o.type = 'U' AND pa.index_id IN (0, 1) AND pa.rows > 0)
    THROW 50042, N'ROLLBACK 042: alguna tabla de psi tiene filas. No se revierte.', 1;

BEGIN TRANSACTION;

DECLARE @quitar NVARCHAR(MAX) = N'';
SELECT @quitar += CASE WHEN LEN(@quitar) = 0 THEN N'' ELSE N', ' END
                  + N'DROP ' + CASE WHEN p.predicate_type_desc = N'FILTER' THEN N'FILTER' ELSE N'BLOCK' END + N' PREDICATE ON '
                  + QUOTENAME(OBJECT_SCHEMA_NAME(p.target_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(p.target_object_id))
                  -- operation_desc viene como 'AFTER INSERT', 'BEFORE DELETE'...; nulo en los filtros.
                  + CASE WHEN p.operation_desc IS NULL THEN N'' ELSE N' ' + REPLACE(p.operation_desc, N'_', N' ') END
FROM sys.security_predicates p
JOIN sys.objects o ON o.object_id = p.target_object_id
WHERE p.object_id = OBJECT_ID(N'seg.pol_tenant') AND o.schema_id = SCHEMA_ID(N'psi');
IF LEN(@quitar) > 0
BEGIN
    SET @quitar = N'ALTER SECURITY POLICY seg.pol_tenant ' + @quitar + N';';
    EXEC sys.sp_executesql @quitar;
END

IF OBJECT_ID(N'psi.importacion_filas', N'U') IS NOT NULL DROP TABLE psi.importacion_filas;
IF OBJECT_ID(N'psi.importaciones', N'U') IS NOT NULL DROP TABLE psi.importaciones;
IF OBJECT_ID(N'psi.impresiones', N'U') IS NOT NULL DROP TABLE psi.impresiones;
IF OBJECT_ID(N'psi.profesionales', N'U') IS NOT NULL DROP TABLE psi.profesionales;
IF OBJECT_ID(N'psi.constancia_pruebas', N'U') IS NOT NULL DROP TABLE psi.constancia_pruebas;
IF OBJECT_ID(N'psi.constancias', N'U') IS NOT NULL DROP TABLE psi.constancias;
IF OBJECT_ID(N'psi.tipos_constancia', N'U') IS NOT NULL DROP TABLE psi.tipos_constancia;
IF OBJECT_ID(N'psi.atencion_pruebas', N'U') IS NOT NULL DROP TABLE psi.atencion_pruebas;
IF OBJECT_ID(N'psi.pruebas', N'U') IS NOT NULL DROP TABLE psi.pruebas;
IF OBJECT_ID(N'psi.atenciones', N'U') IS NOT NULL DROP TABLE psi.atenciones;
IF OBJECT_ID(N'psi.pacientes', N'U') IS NOT NULL DROP TABLE psi.pacientes;
IF OBJECT_ID(N'psi.jornadas', N'U') IS NOT NULL DROP TABLE psi.jornadas;
IF SCHEMA_ID(N'psi') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'psi'))
    EXEC sys.sp_executesql N'DROP SCHEMA psi;';

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_persona_empresa_tipo' AND parent_object_id = OBJECT_ID(N'dbo.persona_empresa')
           AND definition LIKE N'%''paciente''%')
BEGIN
    ALTER TABLE dbo.persona_empresa DROP CONSTRAINT CK_persona_empresa_tipo;
    ALTER TABLE dbo.persona_empresa WITH CHECK ADD CONSTRAINT CK_persona_empresa_tipo
        CHECK (tipo_vinculo IN ('empleado', 'cliente', 'proveedor', 'usuario', 'contacto', 'otro'));
END

COMMIT TRANSACTION;
PRINT N'ROLLBACK 042 aplicado: esquema psi retirado y CK_persona_empresa_tipo con los 6 tipos de antes.';
*/
