-- ============================================================
-- Script   : 018_personas_migrar_a_maestra.sql
-- Proposito: Migra las personas actuales de dbo.personas a la estructura de persona maestra creada
--            en el 014. Por cada persona crea su vinculo de empleado (persona_empresa) y su ficha de
--            empleado (empleados: numero de empleado, cargo, tarifa), registra su DNI en
--            persona_documentos, separa nombres y apellidos, calcula el nombre normalizado, marca la
--            identidad y escribe la bitacora de lo que hizo (origen = 'script:018').
--            NO elimina ni modifica las columnas viejas de dbo.personas (eso es el 019).
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-30
-- BD       : eBD_SPD
-- Requiere : 014_personas_maestra_estructura.sql y 015_catalogos_base_honduras.sql aplicados
-- Rollback : ver la seccion ROLLBACK al final (la copia de seguridad es dbo.respaldo_personas_018)
-- ============================================================
-- Plan      : artifact "Mejora de datos de personas", fase F3. Decisiones D6 (el numero de empleado
--            vive en empleados.codigo_interno), D7 (el cargo se valida contra dbo.cargos), D9 (los
--            scripts escriben sus propias filas de bitacora) y D10 (un vinculo por rol).
--
-- Que hace, paso a paso (todo dentro de UNA transaccion; si algo no cuadra, se revierte completo):
--   1. Vinculo de empleado: una fila en persona_empresa por persona que tenga empresa. fecha_inicio =
--      fecha_ingreso y fecha_fin = fecha_baja; activo y eliminado se copian de la persona.
--   2. Ficha de empleado: una fila en empleados por vinculo. codigo_interno = el documento cuando su
--      tipo es INTERNO (el numero de empleado de Transgar); cargo, tarifa_diaria y moneda_tarifa se
--      copian tal cual.
--   3. Documento de identidad: las personas con tipo_documento = 'DNI' y 13 digitos validos reciben
--      una fila en persona_documentos (HN, principal). El numero se guarda SIN guiones ni espacios.
--      La columna vieja dbo.personas.documento NO se toca (los guiones de la empresa 3 se ignoran
--      al migrar y la columna se retira en el 019).
--   4. Nombres: separa dbo.personas.nombres y apellidos en primer/segundo nombre y primer/segundo
--      apellido SOLO cuando la forma es segura: 1 o 2 palabras en cada campo, sin particulas
--      (de, del, la, las, los, san, santa, y) ni simbolos. Lo demas queda en la lista de revision y
--      no se adivina. Si el texto esta todo en mayusculas se pasa a formato de nombre propio. Los
--      campos originales nombres y apellidos no se modifican.
--      CASO AMBIGUO: si el texto completo esta en mayusculas (carga en bloque) y suma exactamente 3
--      palabras, no se sabe si la segunda es un segundo nombre o el primer apellido (por ejemplo
--      "EDWIN" + "JOEL MARTINEZ"). Ahi NO se separan las partes: solo se completa el nombre
--      normalizado, que no depende del reparto, y la persona queda en la lista de revision (primer
--      nombre y primer apellido vacios) hasta que se corrija desde la pantalla. El 019 no puede
--      retirar las columnas viejas mientras quede alguna persona sin esas partes.
--   5. nombre_normalizado = mayusculas, sin tildes ni dieresis, N en lugar de Ñ, espacios simples,
--      con el orden primer nombre, segundo nombre, primer apellido, segundo apellido. El servicio de
--      la fase F4 debe aplicar la misma regla.
--   6. estado_identidad = 'verificada' para quien ya tiene un documento de identidad registrado;
--      las demas personas quedan 'pendiente'.
--   7. Bitacora: una fila por cada campo de personas que se completa (operacion UPDATE) y una fila
--      de alta, con la foto del registro, por cada vinculo, ficha y documento creados. En el
--      documento, el numero va enmascarado (solo los ultimos 4 digitos): la bitacora no se puede
--      borrar y no debe guardar el documento completo.
--
-- Seguridad de ejecucion:
--   * Ejecutar TODO el archivo en una sola sesion (el PRECHECK deja una marca temporal que los
--     demas bloques exigen; si el PRECHECK falla, ningun bloque posterior hace nada).
--   * Idempotente: cada paso solo actua sobre lo que falta (una persona sin vinculo, sin documento o
--     sin nombres separados). Se puede repetir para ponerse al dia con las personas que la
--     aplicacion actual cree antes del 019, porque todavia escribe solo las columnas viejas.
--   * Una copia de seguridad de dbo.personas (dbo.respaldo_personas_018) se crea antes de migrar.
--   * Antes del COMMIT se comprueba que los conteos coincidan con lo previsto y que ninguna columna
--     vieja de dbo.personas haya cambiado.
--   * Antes de ejecutar contra eBD_SPD se requiere aprobacion (/alerta-bd).
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF DB_NAME() <> N'eBD_SPD'
    THROW 50018, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 14
    THROW 50018, N'Se requiere SQL Server 2017 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.personas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.persona_empresa', N'U') IS NULL
   OR OBJECT_ID(N'dbo.empleados', N'U') IS NULL
   OR OBJECT_ID(N'dbo.persona_documentos', N'U') IS NULL
   OR OBJECT_ID(N'dbo.bitacora_cambios', N'U') IS NULL
   OR OBJECT_ID(N'dbo.catalogo_tipos_documento', N'U') IS NULL
   OR OBJECT_ID(N'dbo.cargos', N'U') IS NULL
    THROW 50018, N'Faltan tablas. Ejecute antes los scripts 014 y 015. Abortar.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.catalogo_tipos_documento WHERE codigo = 'DNI')
    THROW 50018, N'Falta el tipo de documento DNI. Ejecute antes el script 015. Abortar.', 1;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.persona_documentos')
             AND name = N'numero_normalizado' AND max_length > 100)
    THROW 50018, N'persona_documentos.numero_normalizado sigue como NVARCHAR(4000). Ejecute antes el script 015. Abortar.', 1;

IF COL_LENGTH(N'dbo.personas', N'primer_nombre') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'primer_apellido') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'nombre_normalizado') IS NULL
   OR COL_LENGTH(N'dbo.personas', N'estado_identidad') IS NULL
    THROW 50018, N'dbo.personas no tiene las columnas del script 014. Abortar.', 1;

-- Un mismo DNI en dos personas vigentes: hay que resolverlo (fusion) antes de migrar
IF EXISTS (SELECT 1
           FROM dbo.personas
           WHERE tipo_documento = 'DNI' AND documento IS NOT NULL AND eliminado = 0
           GROUP BY REPLACE(REPLACE(LTRIM(RTRIM(documento)), N'-', N''), N' ', N'')
           HAVING COUNT(*) > 1)
    THROW 50018, N'Hay un DNI repetido en dos personas vigentes. Resuelva la duplicidad antes. Abortar.', 1;

-- Restricciones de las tablas destino que la migracion no debe violar
IF EXISTS (SELECT 1 FROM dbo.personas WHERE fecha_baja IS NOT NULL AND fecha_ingreso IS NOT NULL AND fecha_baja < fecha_ingreso)
    THROW 50018, N'Hay personas con fecha de baja anterior a la de ingreso. Corrijalas antes. Abortar.', 1;

IF EXISTS (SELECT 1 FROM dbo.personas WHERE tarifa_diaria < 0)
    THROW 50018, N'Hay personas con tarifa diaria negativa. Corrijalas antes. Abortar.', 1;

-- D7: ningun empleado puede quedar con un cargo que su empresa no tenga definido
IF EXISTS (SELECT 1
           FROM dbo.personas p
           WHERE p.id_empresa IS NOT NULL AND p.cargo IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM dbo.cargos c
                             WHERE c.id_empresa = p.id_empresa AND c.codigo = p.cargo AND c.eliminado = 0))
    THROW 50018, N'Hay personas con un cargo que su empresa no tiene definido. Defina el cargo antes. Abortar.', 1;

IF OBJECT_ID(N'tempdb..#precheck_018') IS NOT NULL DROP TABLE #precheck_018;
CREATE TABLE #precheck_018 (ok BIT NOT NULL, id_transaccion UNIQUEIDENTIFIER NOT NULL);
INSERT INTO #precheck_018 (ok, id_transaccion) VALUES (1, NEWID());

-- Estado previo (informativo)
SELECT
    (SELECT COUNT(*) FROM dbo.personas)                                                    AS personas,
    (SELECT COUNT(*) FROM dbo.personas WHERE id_empresa IS NULL)                           AS sin_empresa,
    (SELECT COUNT(*) FROM dbo.personas WHERE tipo_documento = 'DNI')                       AS con_dni,
    (SELECT COUNT(*) FROM dbo.personas WHERE tipo_documento = 'INTERNO')                   AS con_numero_interno,
    (SELECT COUNT(*) FROM dbo.personas WHERE primer_nombre IS NOT NULL)                    AS ya_con_nombres_separados,
    (SELECT COUNT(*) FROM dbo.persona_empresa WHERE tipo_vinculo = 'empleado')             AS vinculos_empleado_existentes,
    (SELECT COUNT(*) FROM dbo.empleados)                                                   AS empleados_existentes,
    (SELECT COUNT(*) FROM dbo.persona_documentos)                                          AS documentos_existentes,
    (SELECT COUNT(*) FROM dbo.bitacora_cambios WHERE origen = 'script:018')                AS bitacora_018_existente,
    CASE WHEN OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NULL THEN 'no existe' ELSE 'ya existe' END AS respaldo;
GO

-- ------------------------------------------------------------
-- CAMBIO 1/2: [VERDE / AGREGA] copia de seguridad dbo.respaldo_personas_018
--   Foto de dbo.personas antes de migrar (todas las columnas salvo token_concurrencia). Sirve para el
--   ROLLBACK y se retira en el 019. Si ya existe (ejecucion repetida) no se vuelve a crear, para
--   conservar la foto de la primera ejecucion.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_018') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.respaldo_personas_018', N'U') IS NULL
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT id_persona, id_empresa, documento, tipo_documento, nombres, apellidos, cargo,
               tarifa_diaria, moneda_tarifa, telefono, email, fecha_ingreso, fecha_baja,
               activo, eliminado, fecha_eliminado, creado_por, fecha_creacion, modificado_por, fecha_modificacion,
               primer_nombre, segundo_nombre, primer_apellido, segundo_apellido, nombre_normalizado,
               sexo, estado_civil, fecha_nacimiento, pais_nacionalidad, tipo_sangre,
               id_municipio_nacimiento, id_municipio_residencia, direccion_residencia, telefono_secundario,
               contacto_emergencia_nombre, contacto_emergencia_telefono, contacto_emergencia_parentesco,
               licencia_tipo, licencia_numero, licencia_vencimiento, estado_identidad, id_persona_principal,
               SYSUTCDATETIME() AS fecha_respaldo
        INTO dbo.respaldo_personas_018
        FROM dbo.personas;

        ALTER TABLE dbo.respaldo_personas_018 ADD CONSTRAINT PK_respaldo_personas_018 PRIMARY KEY (id_persona);

        COMMIT TRANSACTION;
        DECLARE @n_respaldo INT = (SELECT COUNT(*) FROM dbo.respaldo_personas_018);
        PRINT N'  + dbo.respaldo_personas_018: ' + CAST(@n_respaldo AS NVARCHAR(10)) + N' filas';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
ELSE
    PRINT N'  = dbo.respaldo_personas_018 ya existe: se conserva la copia de la primera ejecucion';
GO

-- ------------------------------------------------------------
-- CAMBIO 2/2: [VERDE / AGREGA + AMARILLO / MODIFICA] migracion a la persona maestra (una transaccion)
--   Agrega filas en persona_empresa, empleados, persona_documentos y bitacora_cambios; completa en
--   dbo.personas las columnas nuevas del 014 (nombres separados, nombre_normalizado, estado_identidad).
--   No cambia ninguna columna vieja ni elimina nada.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_018') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @tx UNIQUEIDENTIFIER = (SELECT TOP (1) id_transaccion FROM #precheck_018);
DECLARE @n_vinculos INT, @n_empleados INT, @n_documentos INT, @n_nombres INT, @n_normalizados INT, @n_estados INT, @n_bitacora INT = 0, @n_revision INT;

IF OBJECT_ID(N'tempdb..#plan_018')      IS NOT NULL DROP TABLE #plan_018;
IF OBJECT_ID(N'tempdb..#vinculos_018')  IS NOT NULL DROP TABLE #vinculos_018;
IF OBJECT_ID(N'tempdb..#empleados_018') IS NOT NULL DROP TABLE #empleados_018;
IF OBJECT_ID(N'tempdb..#documentos_018') IS NOT NULL DROP TABLE #documentos_018;
IF OBJECT_ID(N'tempdb..#estados_018')   IS NOT NULL DROP TABLE #estados_018;
IF OBJECT_ID(N'tempdb..#legacy_018')    IS NOT NULL DROP TABLE #legacy_018;

CREATE TABLE #plan_018 (
    id_persona          INT           NOT NULL PRIMARY KEY,
    id_empresa          INT           NULL,
    codigo_interno      VARCHAR(30)   COLLATE DATABASE_DEFAULT NULL,
    documento_limpio    NVARCHAR(30)  COLLATE DATABASE_DEFAULT NULL,
    necesita_vinculo    BIT           NOT NULL,
    necesita_documento  BIT           NOT NULL,
    aplicar_nombres     BIT           NOT NULL,
    aplicar_normalizado BIT           NOT NULL,
    primer_nombre       NVARCHAR(50)  COLLATE DATABASE_DEFAULT NULL,
    segundo_nombre      NVARCHAR(50)  COLLATE DATABASE_DEFAULT NULL,
    primer_apellido     NVARCHAR(50)  COLLATE DATABASE_DEFAULT NULL,
    segundo_apellido    NVARCHAR(50)  COLLATE DATABASE_DEFAULT NULL,
    nombre_normalizado  NVARCHAR(200) COLLATE DATABASE_DEFAULT NULL,
    motivo_revision     NVARCHAR(300) COLLATE DATABASE_DEFAULT NULL
);
CREATE TABLE #vinculos_018 (
    id_persona_empresa INT NOT NULL, id_empresa INT NOT NULL, id_persona INT NOT NULL,
    fecha_inicio DATE NULL, fecha_fin DATE NULL, activo BIT NOT NULL
);
CREATE TABLE #empleados_018 (
    id_empleado INT NOT NULL, id_persona_empresa INT NOT NULL, id_empresa INT NOT NULL,
    codigo_interno VARCHAR(30) COLLATE DATABASE_DEFAULT NULL, cargo VARCHAR(30) COLLATE DATABASE_DEFAULT NULL,
    tarifa_diaria DECIMAL(18,2) NULL, moneda_tarifa CHAR(3) COLLATE DATABASE_DEFAULT NULL, activo BIT NOT NULL
);
CREATE TABLE #documentos_018 (
    id_persona_documento INT NOT NULL, id_persona INT NOT NULL,
    tipo_documento VARCHAR(20) COLLATE DATABASE_DEFAULT NOT NULL, pais_emisor CHAR(2) COLLATE DATABASE_DEFAULT NOT NULL,
    numero NVARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL, es_principal BIT NOT NULL
);
CREATE TABLE #estados_018 (
    id_persona INT NOT NULL,
    anterior VARCHAR(15) COLLATE DATABASE_DEFAULT NOT NULL, nuevo VARCHAR(15) COLLATE DATABASE_DEFAULT NOT NULL
);

BEGIN TRY
    BEGIN TRANSACTION;

    -- Foto de las columnas viejas, para comprobar al final que no cambiaron
    SELECT id_persona, id_empresa, documento, tipo_documento, nombres, apellidos, cargo, tarifa_diaria,
           moneda_tarifa, telefono, email, fecha_ingreso, fecha_baja, activo, eliminado, creado_por, fecha_creacion
    INTO #legacy_018
    FROM dbo.personas;

    -- ---- Plan: que hay que hacer con cada persona (solo lectura; se puede simular con un SELECT) ----
    -- [PLAN:SELECT:INICIO]
    INSERT INTO #plan_018 (id_persona, id_empresa, codigo_interno, documento_limpio, necesita_vinculo, necesita_documento,
                           aplicar_nombres, aplicar_normalizado, primer_nombre, segundo_nombre, primer_apellido, segundo_apellido,
                           nombre_normalizado, motivo_revision)
    SELECT p.id_persona,
           p.id_empresa,
           CASE WHEN p.tipo_documento = 'INTERNO' THEN NULLIF(LTRIM(RTRIM(p.documento)), '') END,
           CASE WHEN d.valido = 1 THEN x.d END,
           CASE WHEN p.id_empresa IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa pe
                                 WHERE pe.id_persona = p.id_persona AND pe.id_empresa = p.id_empresa
                                   AND pe.tipo_vinculo = 'empleado') THEN 1 ELSE 0 END,
           CASE WHEN d.valido = 1
                 AND NOT EXISTS (SELECT 1 FROM dbo.persona_documentos pd
                                 WHERE pd.id_persona = p.id_persona AND pd.tipo_documento = 'DNI'
                                   AND pd.eliminado = 0) THEN 1 ELSE 0 END,
           CASE WHEN s.separar = 1 AND (p.primer_nombre IS NULL OR p.primer_apellido IS NULL) THEN 1 ELSE 0 END,
           CASE WHEN z.seguros = 1 AND s.separar = 0 AND p.nombre_normalizado IS NULL THEN 1 ELSE 0 END,
           CASE WHEN s.separar = 1 THEN v.pn1 END,
           CASE WHEN s.separar = 1 THEN v.pn2 END,
           CASE WHEN s.separar = 1 THEN v.pa1 END,
           CASE WHEN s.separar = 1 THEN v.pa2 END,
           CASE WHEN z.seguros = 1
                THEN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(f.completo),
                         N'Á', N'A'), N'É', N'E'), N'Í', N'I'), N'Ó', N'O'), N'Ú', N'U'), N'Ü', N'U'), N'Ñ', N'N')
           END,
           NULLIF(CONCAT_WS(N'; ',
               CASE WHEN p.id_empresa IS NULL THEN N'sin empresa: no se crea vinculo' END,
               CASE WHEN z.seguros = 0 AND (p.primer_nombre IS NULL OR p.primer_apellido IS NULL)
                    THEN N'nombres: forma no segura (3 o mas palabras en un campo, particulas o simbolos); no se separan' END,
               CASE WHEN z.seguros = 1 AND s.separar = 0 AND (p.primer_nombre IS NULL OR p.primer_apellido IS NULL)
                    THEN N'nombres: 3 palabras en mayusculas de una carga en bloque; no se sabe cual es segundo nombre y cual primer apellido. Solo se completa el nombre normalizado' END,
               CASE WHEN p.tipo_documento = 'DNI' AND d.valido = 0 THEN N'DNI invalido o repetido: no se crea documento' END,
               CASE WHEN p.tipo_documento IS NOT NULL AND p.tipo_documento NOT IN ('DNI', 'INTERNO')
                    THEN N'tipo de documento sin regla de migracion' END), N'')
    FROM dbo.personas p
    CROSS APPLY (SELECT n = LTRIM(RTRIM(p.nombres)),
                        a = LTRIM(RTRIM(p.apellidos)),
                        d = REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.documento, N''))), N'-', N''), N' ', N'')) x
    CROSS APPLY (SELECT i = CHARINDEX(N' ', x.n), j = CHARINDEX(N' ', x.a)) y
    CROSS APPLY (SELECT n1 = CASE WHEN y.i = 0 THEN x.n ELSE LEFT(x.n, y.i - 1) END,
                        n2 = CASE WHEN y.i = 0 THEN NULL ELSE SUBSTRING(x.n, y.i + 1, 100) END,
                        a1 = CASE WHEN y.j = 0 THEN x.a ELSE LEFT(x.a, y.j - 1) END,
                        a2 = CASE WHEN y.j = 0 THEN NULL ELSE SUBSTRING(x.a, y.j + 1, 100) END) w
    CROSS APPLY (SELECT seguros = CASE
                    WHEN x.n <> N'' AND x.a <> N''
                     AND (w.n2 IS NULL OR w.n2 NOT LIKE N'% %')
                     AND (w.a2 IS NULL OR w.a2 NOT LIKE N'% %')
                     AND LEN(w.n1) <= 50 AND LEN(ISNULL(w.n2, N'')) <= 50
                     AND LEN(w.a1) <= 50 AND LEN(ISNULL(w.a2, N'')) <= 50
                     AND (x.n + N' ' + x.a) COLLATE Latin1_General_CI_AI NOT LIKE N'%[^a-z ]%'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% DE %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% DEL %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% LA %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% LAS %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% LOS %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% SAN %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% SANTA %'
                     AND N' ' + x.n + N' ' + x.a + N' ' NOT LIKE N'% Y %'
                    THEN 1 ELSE 0 END) z
    CROSS APPLY (SELECT separar = CASE
                    WHEN z.seguros = 1
                     AND NOT (2 + CASE WHEN w.n2 IS NULL THEN 0 ELSE 1 END + CASE WHEN w.a2 IS NULL THEN 0 ELSE 1 END = 3
                              AND (x.n + N' ' + x.a) = UPPER(x.n + N' ' + x.a) COLLATE Latin1_General_CS_AS)
                    THEN 1 ELSE 0 END) s
    CROSS APPLY (SELECT valido = CASE
                    WHEN p.tipo_documento = 'DNI' AND LEN(x.d) = 13 AND x.d NOT LIKE N'%[^0-9]%'
                     AND NOT EXISTS (SELECT 1 FROM dbo.persona_documentos pd
                                     WHERE pd.tipo_documento = 'DNI' AND pd.pais_emisor = 'HN'
                                       AND pd.numero_normalizado = x.d AND pd.eliminado = 0
                                       AND pd.id_persona <> p.id_persona)
                    THEN 1 ELSE 0 END) d
    CROSS APPLY (SELECT pn1 = CASE WHEN w.n1 = UPPER(w.n1) COLLATE Latin1_General_CS_AS AND LEN(w.n1) > 1
                                   THEN UPPER(LEFT(w.n1, 1)) + LOWER(SUBSTRING(w.n1, 2, 50)) ELSE w.n1 END,
                        pn2 = CASE WHEN w.n2 = UPPER(w.n2) COLLATE Latin1_General_CS_AS AND LEN(w.n2) > 1
                                   THEN UPPER(LEFT(w.n2, 1)) + LOWER(SUBSTRING(w.n2, 2, 50)) ELSE w.n2 END,
                        pa1 = CASE WHEN w.a1 = UPPER(w.a1) COLLATE Latin1_General_CS_AS AND LEN(w.a1) > 1
                                   THEN UPPER(LEFT(w.a1, 1)) + LOWER(SUBSTRING(w.a1, 2, 50)) ELSE w.a1 END,
                        pa2 = CASE WHEN w.a2 = UPPER(w.a2) COLLATE Latin1_General_CS_AS AND LEN(w.a2) > 1
                                   THEN UPPER(LEFT(w.a2, 1)) + LOWER(SUBSTRING(w.a2, 2, 50)) ELSE w.a2 END) v
    CROSS APPLY (SELECT completo = v.pn1 + ISNULL(N' ' + v.pn2, N'') + N' ' + v.pa1 + ISNULL(N' ' + v.pa2, N'')) f;
    -- [PLAN:SELECT:FIN]

    -- ---- Paso 1: vinculos de empleado ----
    INSERT INTO dbo.persona_empresa (id_empresa, id_persona, tipo_vinculo, fecha_inicio, fecha_fin, activo, eliminado, fecha_eliminado, creado_por)
    OUTPUT inserted.id_persona_empresa, inserted.id_empresa, inserted.id_persona, inserted.fecha_inicio, inserted.fecha_fin, inserted.activo
      INTO #vinculos_018 (id_persona_empresa, id_empresa, id_persona, fecha_inicio, fecha_fin, activo)
    SELECT p.id_empresa, p.id_persona, 'empleado', p.fecha_ingreso, p.fecha_baja, p.activo, p.eliminado, p.fecha_eliminado, N'script_018'
    FROM #plan_018 pl
    JOIN dbo.personas p ON p.id_persona = pl.id_persona
    WHERE pl.necesita_vinculo = 1;

    -- ---- Paso 2: fichas de empleado ----
    INSERT INTO dbo.empleados (id_persona_empresa, id_empresa, tipo_vinculo, codigo_interno, cargo, tarifa_diaria, moneda_tarifa, activo, eliminado, fecha_eliminado, creado_por)
    OUTPUT inserted.id_empleado, inserted.id_persona_empresa, inserted.id_empresa, inserted.codigo_interno, inserted.cargo,
           inserted.tarifa_diaria, inserted.moneda_tarifa, inserted.activo
      INTO #empleados_018 (id_empleado, id_persona_empresa, id_empresa, codigo_interno, cargo, tarifa_diaria, moneda_tarifa, activo)
    SELECT v.id_persona_empresa, v.id_empresa, 'empleado', pl.codigo_interno, p.cargo, p.tarifa_diaria, p.moneda_tarifa,
           p.activo, p.eliminado, p.fecha_eliminado, N'script_018'
    FROM #vinculos_018 v
    JOIN dbo.personas p ON p.id_persona = v.id_persona
    JOIN #plan_018 pl ON pl.id_persona = v.id_persona;

    -- ---- Paso 3: documentos de identidad (DNI) ----
    INSERT INTO dbo.persona_documentos (id_persona, tipo_documento, pais_emisor, numero, es_principal, activo, eliminado, fecha_eliminado, creado_por)
    OUTPUT inserted.id_persona_documento, inserted.id_persona, inserted.tipo_documento, inserted.pais_emisor, inserted.numero, inserted.es_principal
      INTO #documentos_018 (id_persona_documento, id_persona, tipo_documento, pais_emisor, numero, es_principal)
    SELECT pl.id_persona, 'DNI', 'HN', pl.documento_limpio, 1, p.activo, p.eliminado, p.fecha_eliminado, N'script_018'
    FROM #plan_018 pl
    JOIN dbo.personas p ON p.id_persona = pl.id_persona
    WHERE pl.necesita_documento = 1;

    -- ---- Paso 4 y 5: nombres separados y nombre normalizado ----
    UPDATE p
       SET p.primer_nombre      = pl.primer_nombre,
           p.segundo_nombre     = pl.segundo_nombre,
           p.primer_apellido    = pl.primer_apellido,
           p.segundo_apellido   = pl.segundo_apellido,
           p.nombre_normalizado = pl.nombre_normalizado,
           p.modificado_por     = N'script_018',
           p.fecha_modificacion = SYSUTCDATETIME()
    FROM dbo.personas p
    JOIN #plan_018 pl ON pl.id_persona = p.id_persona
    WHERE pl.aplicar_nombres = 1;

    -- Casos ambiguos: solo el nombre normalizado (no depende del reparto entre nombres y apellidos)
    UPDATE p
       SET p.nombre_normalizado = pl.nombre_normalizado,
           p.modificado_por     = N'script_018',
           p.fecha_modificacion = SYSUTCDATETIME()
    FROM dbo.personas p
    JOIN #plan_018 pl ON pl.id_persona = p.id_persona
    WHERE pl.aplicar_normalizado = 1;

    -- ---- Paso 6: identidad verificada para quien ya tiene un documento de identidad ----
    UPDATE p
       SET p.estado_identidad   = 'verificada',
           p.modificado_por     = N'script_018',
           p.fecha_modificacion = SYSUTCDATETIME()
    OUTPUT inserted.id_persona, deleted.estado_identidad, inserted.estado_identidad
      INTO #estados_018 (id_persona, anterior, nuevo)
    FROM dbo.personas p
    WHERE p.estado_identidad = 'pendiente'
      AND EXISTS (SELECT 1
                  FROM dbo.persona_documentos d
                  JOIN dbo.catalogo_tipos_documento td ON td.codigo = d.tipo_documento AND td.es_identidad = 1
                  WHERE d.id_persona = p.id_persona AND d.eliminado = 0);

    -- ---- Paso 7: bitacora ----
    -- 7a) un campo por fila en las columnas de personas que se completaron
    INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
    SELECT @tx, pl.id_empresa, pl.id_persona, 'personas', pl.id_persona, 'UPDATE', c.campo, NULL, c.valor, N'script_018', 'script:018'
    FROM #plan_018 pl
    CROSS APPLY (VALUES ('primer_nombre',      pl.primer_nombre),
                        ('segundo_nombre',     pl.segundo_nombre),
                        ('primer_apellido',    pl.primer_apellido),
                        ('segundo_apellido',   pl.segundo_apellido),
                        ('nombre_normalizado', pl.nombre_normalizado)) c (campo, valor)
    WHERE (pl.aplicar_nombres = 1 OR pl.aplicar_normalizado = 1) AND c.valor IS NOT NULL;
    SET @n_bitacora += @@ROWCOUNT;

    INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
    SELECT @tx, p.id_empresa, e.id_persona, 'personas', e.id_persona, 'UPDATE', 'estado_identidad', e.anterior, e.nuevo, N'script_018', 'script:018'
    FROM #estados_018 e
    JOIN dbo.personas p ON p.id_persona = e.id_persona;
    SET @n_bitacora += @@ROWCOUNT;

    -- 7b) altas: una fila con campo nulo y la foto del registro
    INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
    SELECT @tx, v.id_empresa, v.id_persona, 'persona_empresa', v.id_persona_empresa, 'INSERT', NULL, NULL,
           (SELECT v.id_empresa AS id_empresa, v.id_persona AS id_persona, N'empleado' AS tipo_vinculo,
                   v.fecha_inicio AS fecha_inicio, v.fecha_fin AS fecha_fin, v.activo AS activo
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           N'script_018', 'script:018'
    FROM #vinculos_018 v;
    SET @n_bitacora += @@ROWCOUNT;

    INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
    SELECT @tx, e.id_empresa, v.id_persona, 'empleados', e.id_empleado, 'INSERT', NULL, NULL,
           (SELECT e.codigo_interno AS codigo_interno, e.cargo AS cargo, e.tarifa_diaria AS tarifa_diaria,
                   e.moneda_tarifa AS moneda_tarifa, e.activo AS activo
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           N'script_018', 'script:018'
    FROM #empleados_018 e
    JOIN #vinculos_018 v ON v.id_persona_empresa = e.id_persona_empresa;
    SET @n_bitacora += @@ROWCOUNT;

    INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
    SELECT @tx, p.id_empresa, d.id_persona, 'persona_documentos', d.id_persona_documento, 'INSERT', NULL, NULL,
           (SELECT d.tipo_documento AS tipo_documento, d.pais_emisor AS pais_emisor,
                   REPLICATE(N'*', CASE WHEN LEN(d.numero) > 4 THEN LEN(d.numero) - 4 ELSE 0 END) + RIGHT(d.numero, 4) AS numero,
                   d.es_principal AS es_principal
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           N'script_018', 'script:018'
    FROM #documentos_018 d
    JOIN dbo.personas p ON p.id_persona = d.id_persona;
    SET @n_bitacora += @@ROWCOUNT;

    -- ---- Verificacion antes de confirmar ----
    SET @n_vinculos   = (SELECT COUNT(*) FROM #vinculos_018);
    SET @n_empleados  = (SELECT COUNT(*) FROM #empleados_018);
    SET @n_documentos = (SELECT COUNT(*) FROM #documentos_018);
    SET @n_nombres    = (SELECT COUNT(*) FROM #plan_018 WHERE aplicar_nombres = 1);
    SET @n_normalizados = (SELECT COUNT(*) FROM #plan_018 WHERE aplicar_normalizado = 1);
    SET @n_estados    = (SELECT COUNT(*) FROM #estados_018);
    SET @n_revision   = (SELECT COUNT(*) FROM #plan_018 WHERE motivo_revision IS NOT NULL);

    IF @n_vinculos <> (SELECT COUNT(*) FROM #plan_018 WHERE necesita_vinculo = 1)
        THROW 50018, N'Los vinculos creados no coinciden con los previstos. Se revierte todo.', 1;

    IF @n_empleados <> @n_vinculos
        THROW 50018, N'Las fichas de empleado no coinciden con los vinculos. Se revierte todo.', 1;

    IF @n_documentos <> (SELECT COUNT(*) FROM #plan_018 WHERE necesita_documento = 1)
        THROW 50018, N'Los documentos creados no coinciden con los previstos. Se revierte todo.', 1;

    IF @n_nombres <> (SELECT COUNT(*) FROM dbo.personas p JOIN #plan_018 pl ON pl.id_persona = p.id_persona
                      WHERE pl.aplicar_nombres = 1 AND p.primer_nombre IS NOT NULL AND p.primer_apellido IS NOT NULL
                        AND p.nombre_normalizado IS NOT NULL)
        THROW 50018, N'Los nombres separados no coinciden con los previstos. Se revierte todo.', 1;

    IF @n_normalizados <> (SELECT COUNT(*) FROM dbo.personas p JOIN #plan_018 pl ON pl.id_persona = p.id_persona
                           WHERE pl.aplicar_normalizado = 1 AND p.nombre_normalizado IS NOT NULL
                             AND p.primer_nombre IS NULL AND p.primer_apellido IS NULL)
        THROW 50018, N'Los nombres normalizados de los casos ambiguos no coinciden con los previstos. Se revierte todo.', 1;

    IF (SELECT COUNT(*) FROM dbo.personas) <> (SELECT COUNT(*) FROM #legacy_018)
        THROW 50018, N'Cambio la cantidad de personas. Se revierte todo.', 1;

    IF EXISTS (SELECT id_persona, id_empresa, documento, tipo_documento, nombres, apellidos, cargo, tarifa_diaria,
                      moneda_tarifa, telefono, email, fecha_ingreso, fecha_baja, activo, eliminado, creado_por, fecha_creacion
               FROM #legacy_018
               EXCEPT
               SELECT id_persona, id_empresa, documento, tipo_documento, nombres, apellidos, cargo, tarifa_diaria,
                      moneda_tarifa, telefono, email, fecha_ingreso, fecha_baja, activo, eliminado, creado_por, fecha_creacion
               FROM dbo.personas)
        THROW 50018, N'Cambio una columna vieja de dbo.personas. Se revierte todo.', 1;

    COMMIT TRANSACTION;

    PRINT N'  + vinculos de empleado (persona_empresa): ' + CAST(@n_vinculos AS NVARCHAR(10));
    PRINT N'  + fichas de empleado (empleados): '         + CAST(@n_empleados AS NVARCHAR(10));
    PRINT N'  + documentos de identidad (DNI): '          + CAST(@n_documentos AS NVARCHAR(10));
    PRINT N'  ~ personas con nombres separados: '        + CAST(@n_nombres AS NVARCHAR(10));
    PRINT N'  ~ personas con solo nombre normalizado (reparto ambiguo, a revision): ' + CAST(@n_normalizados AS NVARCHAR(10));
    PRINT N'  ~ personas marcadas como verificadas: '    + CAST(@n_estados AS NVARCHAR(10));
    PRINT N'  + filas de bitacora (script:018): '        + CAST(@n_bitacora AS NVARCHAR(10));
    PRINT N'  ! personas en la lista de revision: '      + CAST(@n_revision AS NVARCHAR(10));
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- Lista de revision: casos que el script no resolvio por si mismo (se resuelven desde la pantalla)
SELECT p.id_persona, p.id_empresa, p.nombres, p.apellidos, pl.motivo_revision
FROM #plan_018 pl
JOIN dbo.personas p ON p.id_persona = pl.id_persona
WHERE pl.motivo_revision IS NOT NULL
ORDER BY p.id_persona;
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
--   Falla con THROW si algo no coincide con lo esperado.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_018') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @problemas TABLE (n INT IDENTITY(1,1), mensaje NVARCHAR(300));

-- D7: ningun empleado con un cargo que su empresa no tenga definido
IF EXISTS (SELECT 1
           FROM dbo.empleados e
           LEFT JOIN dbo.cargos c ON c.id_empresa = e.id_empresa AND c.codigo = e.cargo AND c.eliminado = 0
           WHERE e.cargo IS NOT NULL AND c.id_cargo IS NULL)
    INSERT INTO @problemas (mensaje) VALUES (N'Hay empleados con un cargo que su empresa no tiene definido');

-- Toda persona con empresa tiene su vinculo de empleado
IF EXISTS (SELECT 1 FROM dbo.personas p
           WHERE p.id_empresa IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa pe
                             WHERE pe.id_persona = p.id_persona AND pe.id_empresa = p.id_empresa
                               AND pe.tipo_vinculo = 'empleado'))
    INSERT INTO @problemas (mensaje) VALUES (N'Hay personas con empresa y sin vinculo de empleado');

-- Todo vinculo de empleado tiene su ficha
IF EXISTS (SELECT 1 FROM dbo.persona_empresa pe
           WHERE pe.tipo_vinculo = 'empleado'
             AND NOT EXISTS (SELECT 1 FROM dbo.empleados e WHERE e.id_persona_empresa = pe.id_persona_empresa))
    INSERT INTO @problemas (mensaje) VALUES (N'Hay vinculos de empleado sin ficha de empleado');

-- Nadie figura como verificada sin un documento de identidad
IF EXISTS (SELECT 1 FROM dbo.personas p
           WHERE p.estado_identidad = 'verificada'
             AND NOT EXISTS (SELECT 1
                             FROM dbo.persona_documentos d
                             JOIN dbo.catalogo_tipos_documento td ON td.codigo = d.tipo_documento AND td.es_identidad = 1
                             WHERE d.id_persona = p.id_persona AND d.eliminado = 0))
    INSERT INTO @problemas (mensaje) VALUES (N'Hay personas verificadas sin documento de identidad');

-- Lo que se creo en esta ejecucion quedo en la bitacora
IF (SELECT COUNT(*) FROM #vinculos_018) > 0
   AND NOT EXISTS (SELECT 1 FROM dbo.bitacora_cambios WHERE origen = 'script:018')
    INSERT INTO @problemas (mensaje) VALUES (N'No hay filas de bitacora de esta migracion');

SELECT N'personas'                                        AS dato, COUNT(*) AS valor FROM dbo.personas
UNION ALL SELECT N'vinculos de empleado',                         COUNT(*) FROM dbo.persona_empresa WHERE tipo_vinculo = 'empleado'
UNION ALL SELECT N'fichas de empleado',                           COUNT(*) FROM dbo.empleados
UNION ALL SELECT N'  con numero de empleado (codigo_interno)',    COUNT(*) FROM dbo.empleados WHERE codigo_interno IS NOT NULL
UNION ALL SELECT N'documentos de identidad',                      COUNT(*) FROM dbo.persona_documentos
UNION ALL SELECT N'personas verificadas',                         COUNT(*) FROM dbo.personas WHERE estado_identidad = 'verificada'
UNION ALL SELECT N'personas pendientes de identidad',             COUNT(*) FROM dbo.personas WHERE estado_identidad = 'pendiente'
UNION ALL SELECT N'personas sin primer nombre o primer apellido', COUNT(*) FROM dbo.personas WHERE primer_nombre IS NULL OR primer_apellido IS NULL
UNION ALL SELECT N'filas de bitacora (script:018)',               COUNT(*) FROM dbo.bitacora_cambios WHERE origen = 'script:018'
UNION ALL SELECT N'copia de seguridad (respaldo_personas_018)',   COUNT(*) FROM dbo.respaldo_personas_018;

IF EXISTS (SELECT 1 FROM @problemas)
BEGIN
    SELECT mensaje AS problema FROM @problemas ORDER BY n;
    THROW 50018, N'POSTCHECK 018 fallo. Revise la lista anterior.', 1;
END;

PRINT N'Script 018 aplicado y verificado.';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Valido mientras la aplicacion no use todavia persona_empresa, empleados ni persona_documentos
--   (antes de la fase F4/F5). Identifica lo creado por esta migracion con la bitacora. La bitacora
--   es de solo lectura: sus filas script:018 se quedan como registro de lo ocurrido.
--   La copia dbo.respaldo_personas_018 se conserva hasta el 019.
-- ------------------------------------------------------------
/*
BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM dbo.empleados
    WHERE id_empleado IN (SELECT id_registro FROM dbo.bitacora_cambios
                          WHERE origen = 'script:018' AND entidad = 'empleados' AND operacion = 'INSERT');

    DELETE FROM dbo.persona_documentos
    WHERE id_persona_documento IN (SELECT id_registro FROM dbo.bitacora_cambios
                                   WHERE origen = 'script:018' AND entidad = 'persona_documentos' AND operacion = 'INSERT');

    DELETE FROM dbo.persona_empresa
    WHERE id_persona_empresa IN (SELECT id_registro FROM dbo.bitacora_cambios
                                 WHERE origen = 'script:018' AND entidad = 'persona_empresa' AND operacion = 'INSERT');

    UPDATE p
       SET p.primer_nombre      = r.primer_nombre,
           p.segundo_nombre     = r.segundo_nombre,
           p.primer_apellido    = r.primer_apellido,
           p.segundo_apellido   = r.segundo_apellido,
           p.nombre_normalizado = r.nombre_normalizado,
           p.estado_identidad   = r.estado_identidad,
           p.modificado_por     = r.modificado_por,
           p.fecha_modificacion = r.fecha_modificacion
    FROM dbo.personas p
    JOIN dbo.respaldo_personas_018 r ON r.id_persona = p.id_persona
    WHERE p.id_persona IN (SELECT id_persona FROM dbo.bitacora_cambios
                           WHERE origen = 'script:018' AND entidad = 'personas' AND operacion = 'UPDATE');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
*/
