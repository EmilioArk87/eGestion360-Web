-- ============================================================
-- Script   : 015_catalogos_base_honduras.sql
-- Proposito: Carga inicial de los catalogos base de Honduras y cierre de pendientes del 014:
--            departamentos (18) y municipios (298) con codigos oficiales, tipos de documento
--            (DNI, RTN, pasaporte, carne de residente), 9 categorias de licencia de conducir, cargos base
--            para las empresas que no los tienen, restricciones de integridad en catalogo_paises y
--            correccion del tipo de persona_documentos.numero_normalizado (advertencia del 014).
--            Agrega la columna "fuente" a los catalogos para dejar la trazabilidad de cada dato.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-30
-- BD       : eBD_SPD
-- Requiere : 014_personas_maestra_estructura.sql aplicado; dbo.catalogo_paises, dbo.cargos, dbo.empresas
-- Rollback : ver la seccion ROLLBACK al final
-- ============================================================
-- FUENTES DE LOS DATOS (todas verificables):
--   Departamentos: INE Honduras, "Directorio de Establecimientos Economicos 2024", tabla 2
--     (18 departamentos y 298 municipios; municipios por departamento) y OCHA/SINIT.
--   Municipios - codigos: SINIT/SEPLAN, publicados por OCHA en HDX (cod-ab-hnd, CC BY-IGO);
--     las filas en las que el INE (DEE 2024) y OCHA difieren (Colon 0205-0208, Gracias a Dios
--     0903-0906, Santa Barbara 1606-1625) se confirmaron contra los perfiles municipales de la
--     SGJD (Secretaria de Gobernacion, Justicia y Descentralizacion), que coinciden con OCHA.
--   Municipios - nombres: ortografia con tildes cruzada con el listado de municipios (Wikipedia) en
--     291 filas; 7 filas tomadas de la SGJD: Cabanas 0402, Cantarranas 0820 (Decreto 94-2011),
--     Ramon Villeda Morales 0905, Wampusirpi 0906, San Marcos de la Sierra 1013,
--     San Miguelito 1014, San Jose de Colinas 1619.
--   Tipos de documento: RNP (DNI de 13 digitos) y SAR (RTN de persona natural = DNI + 1 digito).
--   Tipos de licencia: las 9 categorias (A, B, B1, BE, C1, C, CE, D1, D) y sus definiciones salen de la
--     tabla "Tipos de permisos de conducir y sus equivalencias" de la Policia Nacional (DNVT, Direccion
--     Nacional de Vialidad y Transporte), recibida del usuario el 2026-09-30. Las 9 categorias coinciden
--     con la lista de la Secretaria de Seguridad (seguridad.gob.hn), que escribe B+E y C+E: es la misma
--     categoria; aqui se usan los codigos BE y CE de la DNVT. Base legal: Ley de Transito, Decreto 205-2005.
--   Paises: no se carga nada; catalogo_paises ya coincide con ISO 3166-1 (249) y con UNSD M49 (248).
--
-- Seguridad de ejecucion (igual que el 014): ejecutar todo el archivo en una sola sesion; el
-- PRECHECK deja una marca temporal que exigen los demas bloques; es idempotente; los bloques que
-- modifican estructura corren en transaccion con TRY/CATCH. Requiere aprobacion /alerta-bd.
-- Este archivo esta en UTF-8 con BOM porque los nombres llevan tildes y enes.
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
    THROW 50015, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 14
    THROW 50015, N'Se requiere SQL Server 2017 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.catalogo_paises', N'U') IS NULL
   OR OBJECT_ID(N'dbo.catalogo_departamentos', N'U') IS NULL
   OR OBJECT_ID(N'dbo.catalogo_municipios', N'U') IS NULL
   OR OBJECT_ID(N'dbo.catalogo_tipos_documento', N'U') IS NULL
   OR OBJECT_ID(N'dbo.catalogo_tipos_licencia', N'U') IS NULL
   OR OBJECT_ID(N'dbo.persona_documentos', N'U') IS NULL
   OR OBJECT_ID(N'dbo.cargos', N'U') IS NULL
   OR OBJECT_ID(N'dbo.empresas', N'U') IS NULL
    THROW 50015, N'Faltan tablas. Ejecute antes el script 014. Abortar.', 1;

-- catalogo_paises debe cumplir el formato antes de agregarle restricciones
IF EXISTS (SELECT 1 FROM dbo.catalogo_paises
           WHERE pais_iso  COLLATE Latin1_General_BIN NOT LIKE '[A-Z][A-Z]'
              OR pais_iso3 COLLATE Latin1_General_BIN NOT LIKE '[A-Z][A-Z][A-Z]'
              OR pais_num NOT LIKE '[0-9][0-9][0-9]')
    THROW 50015, N'catalogo_paises tiene codigos con formato invalido. Abortar.', 1;

IF (SELECT COUNT(*) FROM dbo.catalogo_paises) <> (SELECT COUNT(DISTINCT pais_iso3) FROM dbo.catalogo_paises)
   OR (SELECT COUNT(*) FROM dbo.catalogo_paises) <> (SELECT COUNT(DISTINCT pais_num) FROM dbo.catalogo_paises)
    THROW 50015, N'catalogo_paises tiene codigos ISO3 o numericos repetidos. Abortar.', 1;

-- Reconstruir numero_normalizado solo es seguro si persona_documentos esta vacia
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos')
           AND name = N'numero_normalizado' AND max_length > 100)
   AND EXISTS (SELECT 1 FROM dbo.persona_documentos)
    THROW 50015, N'persona_documentos tiene filas: no se reconstruye numero_normalizado. Abortar.', 1;

IF OBJECT_ID(N'tempdb..#precheck_015') IS NOT NULL DROP TABLE #precheck_015;
CREATE TABLE #precheck_015 (ok BIT NOT NULL);
INSERT INTO #precheck_015 (ok) VALUES (1);

-- Estado previo (informativo)
SELECT
    (SELECT COUNT(*) FROM dbo.catalogo_departamentos)    AS departamentos,
    (SELECT COUNT(*) FROM dbo.catalogo_municipios)       AS municipios,
    (SELECT COUNT(*) FROM dbo.catalogo_tipos_documento)  AS tipos_documento,
    (SELECT COUNT(*) FROM dbo.catalogo_tipos_licencia)   AS tipos_licencia,
    (SELECT COUNT(*) FROM dbo.catalogo_paises)           AS paises,
    (SELECT COUNT(*) FROM dbo.empresas e WHERE e.eliminado = 0
        AND NOT EXISTS (SELECT 1 FROM dbo.cargos c WHERE c.id_empresa = e.id_empresa AND c.eliminado = 0)) AS empresas_sin_cargos;
GO

-- ------------------------------------------------------------
-- CAMBIO 1/8: [VERDE / AGREGA] columna "fuente" en los catalogos nuevos (trazabilidad del dato)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.catalogo_departamentos', N'fuente') IS NULL
        ALTER TABLE dbo.catalogo_departamentos ADD fuente NVARCHAR(300) NULL;
    IF COL_LENGTH(N'dbo.catalogo_municipios', N'fuente') IS NULL
        ALTER TABLE dbo.catalogo_municipios ADD fuente NVARCHAR(300) NULL;
    IF COL_LENGTH(N'dbo.catalogo_tipos_documento', N'fuente') IS NULL
        ALTER TABLE dbo.catalogo_tipos_documento ADD fuente NVARCHAR(300) NULL;
    IF COL_LENGTH(N'dbo.catalogo_tipos_licencia', N'fuente') IS NULL
        ALTER TABLE dbo.catalogo_tipos_licencia ADD fuente NVARCHAR(300) NULL;

    COMMIT TRANSACTION;
    PRINT N'  + columna fuente en los 4 catalogos';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 2/8: [VERDE / AGREGA] restricciones de integridad en dbo.catalogo_paises
--   Hoy solo tiene la clave primaria. Se agregan unicidad de ISO alfa-3 y del codigo numerico
--   y la validacion de formato. Los datos ya las cumplen (verificado en el PRECHECK).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_catalogo_paises_iso3' AND object_id = OBJECT_ID(N'dbo.catalogo_paises'))
        CREATE UNIQUE INDEX UX_catalogo_paises_iso3 ON dbo.catalogo_paises (pais_iso3);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_catalogo_paises_num' AND object_id = OBJECT_ID(N'dbo.catalogo_paises'))
        CREATE UNIQUE INDEX UX_catalogo_paises_num ON dbo.catalogo_paises (pais_num);

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_iso2' AND parent_object_id = OBJECT_ID(N'dbo.catalogo_paises'))
        ALTER TABLE dbo.catalogo_paises ADD CONSTRAINT CK_catalogo_paises_iso2
            CHECK (pais_iso COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z]');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_iso3' AND parent_object_id = OBJECT_ID(N'dbo.catalogo_paises'))
        ALTER TABLE dbo.catalogo_paises ADD CONSTRAINT CK_catalogo_paises_iso3
            CHECK (pais_iso3 COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_num' AND parent_object_id = OBJECT_ID(N'dbo.catalogo_paises'))
        ALTER TABLE dbo.catalogo_paises ADD CONSTRAINT CK_catalogo_paises_num
            CHECK (pais_num LIKE '[0-9][0-9][0-9]');

    COMMIT TRANSACTION;
    PRINT N'  ~ dbo.catalogo_paises: 2 indices unicos y 3 CHECK';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 3/8: [AMARILLO / MODIFICA] dbo.persona_documentos.numero_normalizado
--   El 014 la dejo como NVARCHAR(4000) (REPLACE devuelve ese tipo) y el servidor aviso de una clave
--   de indice de 8022 bytes. Se reconstruye como NVARCHAR(30). La tabla esta vacia (el PRECHECK lo
--   exige); no se pierde ningun dato.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos')
           AND name = N'numero_normalizado' AND max_length > 100)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM dbo.persona_documentos)
            THROW 50016, N'persona_documentos tiene filas: se cancela la reconstruccion.', 1;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_persona_documentos_identidad' AND object_id = OBJECT_ID(N'dbo.persona_documentos'))
            DROP INDEX UX_persona_documentos_identidad ON dbo.persona_documentos;

        ALTER TABLE dbo.persona_documentos DROP COLUMN numero_normalizado;

        ALTER TABLE dbo.persona_documentos ADD numero_normalizado AS
            (CAST(UPPER(REPLACE(REPLACE(REPLACE(numero, N'-', N''), N' ', N''), N'.', N'')) AS NVARCHAR(30))) PERSISTED;

        CREATE UNIQUE INDEX UX_persona_documentos_identidad
            ON dbo.persona_documentos (tipo_documento, pais_emisor, numero_normalizado)
            WHERE eliminado = 0;

        COMMIT TRANSACTION;
        PRINT N'  ~ dbo.persona_documentos.numero_normalizado: NVARCHAR(30)';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 4/8: [VERDE / AGREGA] departamentos de Honduras (18)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.catalogo_departamentos (pais_iso, codigo, nombre, fuente)
SELECT 'HN', v.codigo, v.nombre,
       N'INE Honduras, Directorio de Establecimientos Economicos 2024 (tabla 2); codigos SINIT/OCHA'
FROM (VALUES
    ('01', N'Atlántida'),
    ('02', N'Colón'),
    ('03', N'Comayagua'),
    ('04', N'Copán'),
    ('05', N'Cortés'),
    ('06', N'Choluteca'),
    ('07', N'El Paraíso'),
    ('08', N'Francisco Morazán'),
    ('09', N'Gracias a Dios'),
    ('10', N'Intibucá'),
    ('11', N'Islas de la Bahía'),
    ('12', N'La Paz'),
    ('13', N'Lempira'),
    ('14', N'Ocotepeque'),
    ('15', N'Olancho'),
    ('16', N'Santa Bárbara'),
    ('17', N'Valle'),
    ('18', N'Yoro')
) AS v (codigo, nombre)
WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_departamentos d WHERE d.pais_iso = 'HN' AND d.codigo = v.codigo);

PRINT N'  + departamentos: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' filas nuevas';
GO

-- ------------------------------------------------------------
-- CAMBIO 5/8: [VERDE / AGREGA] municipios de Honduras (298)
--   El codigo de 4 digitos es departamento (2) + municipio (2), el mismo esquema que usa el DNI.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.catalogo_municipios (id_departamento, codigo, nombre, fuente)
SELECT d.id_departamento, v.codigo, v.nombre,
       N'Codigos SINIT/SEPLAN via OCHA (HDX cod-ab-hnd), confirmados con SGJD; nombres INE/SGJD'
FROM (VALUES
    ('0101', N'La Ceiba'),
    ('0102', N'El Porvenir'),
    ('0103', N'Esparta'),
    ('0104', N'Jutiapa'),
    ('0105', N'La Masica'),
    ('0106', N'San Francisco'),
    ('0107', N'Tela'),
    ('0108', N'Arizona'),
    ('0201', N'Trujillo'),
    ('0202', N'Balfate'),
    ('0203', N'Iriona'),
    ('0204', N'Limón'),
    ('0205', N'Sabá'),
    ('0206', N'Santa Fe'),
    ('0207', N'Santa Rosa de Aguán'),
    ('0208', N'Sonaguera'),
    ('0209', N'Tocoa'),
    ('0210', N'Bonito Oriental'),
    ('0301', N'Comayagua'),
    ('0302', N'Ajuterique'),
    ('0303', N'El Rosario'),
    ('0304', N'Esquías'),
    ('0305', N'Humuya'),
    ('0306', N'La Libertad'),
    ('0307', N'Lamaní'),
    ('0308', N'La Trinidad'),
    ('0309', N'Lejamaní'),
    ('0310', N'Meámbar'),
    ('0311', N'Minas de Oro'),
    ('0312', N'Ojos de Agua'),
    ('0313', N'San Jerónimo'),
    ('0314', N'San José de Comayagua'),
    ('0315', N'San José del Potrero'),
    ('0316', N'San Luis'),
    ('0317', N'San Sebastián'),
    ('0318', N'Siguatepeque'),
    ('0319', N'Villa de San Antonio'),
    ('0320', N'Las Lajas'),
    ('0321', N'Taulabé'),
    ('0401', N'Santa Rosa de Copán'),
    ('0402', N'Cabañas'),
    ('0403', N'Concepción'),
    ('0404', N'Copán Ruinas'),
    ('0405', N'Corquín'),
    ('0406', N'Cucuyagua'),
    ('0407', N'Dolores'),
    ('0408', N'Dulce Nombre'),
    ('0409', N'El Paraíso'),
    ('0410', N'Florida'),
    ('0411', N'La Jigua'),
    ('0412', N'La Unión'),
    ('0413', N'Nueva Arcadia'),
    ('0414', N'San Agustín'),
    ('0415', N'San Antonio'),
    ('0416', N'San Jerónimo'),
    ('0417', N'San José'),
    ('0418', N'San Juan de Opoa'),
    ('0419', N'San Nicolás'),
    ('0420', N'San Pedro'),
    ('0421', N'Santa Rita'),
    ('0422', N'Trinidad de Copán'),
    ('0423', N'Veracruz'),
    ('0501', N'San Pedro Sula'),
    ('0502', N'Choloma'),
    ('0503', N'Omoa'),
    ('0504', N'Pimienta'),
    ('0505', N'Potrerillos'),
    ('0506', N'Puerto Cortés'),
    ('0507', N'San Antonio de Cortés'),
    ('0508', N'San Francisco de Yojoa'),
    ('0509', N'San Manuel'),
    ('0510', N'Santa Cruz de Yojoa'),
    ('0511', N'Villanueva'),
    ('0512', N'La Lima'),
    ('0601', N'Choluteca'),
    ('0602', N'Apacilagua'),
    ('0603', N'Concepción de María'),
    ('0604', N'Duyure'),
    ('0605', N'El Corpus'),
    ('0606', N'El Triunfo'),
    ('0607', N'Marcovia'),
    ('0608', N'Morolica'),
    ('0609', N'Namasigüe'),
    ('0610', N'Orocuina'),
    ('0611', N'Pespire'),
    ('0612', N'San Antonio de Flores'),
    ('0613', N'San Isidro'),
    ('0614', N'San José'),
    ('0615', N'San Marcos de Colón'),
    ('0616', N'Santa Ana de Yusguare'),
    ('0701', N'Yuscarán'),
    ('0702', N'Alauca'),
    ('0703', N'Danlí'),
    ('0704', N'El Paraíso'),
    ('0705', N'Güinope'),
    ('0706', N'Jacaleapa'),
    ('0707', N'Liure'),
    ('0708', N'Morocelí'),
    ('0709', N'Oropolí'),
    ('0710', N'Potrerillos'),
    ('0711', N'San Antonio de Flores'),
    ('0712', N'San Lucas'),
    ('0713', N'San Matías'),
    ('0714', N'Soledad'),
    ('0715', N'Teupasenti'),
    ('0716', N'Texiguat'),
    ('0717', N'Vado Ancho'),
    ('0718', N'Yauyupe'),
    ('0719', N'Trojes'),
    ('0801', N'Distrito Central'),
    ('0802', N'Alubarén'),
    ('0803', N'Cedros'),
    ('0804', N'Curarén'),
    ('0805', N'El Porvenir'),
    ('0806', N'Guaimaca'),
    ('0807', N'La Libertad'),
    ('0808', N'La Venta'),
    ('0809', N'Lepaterique'),
    ('0810', N'Maraita'),
    ('0811', N'Marale'),
    ('0812', N'Nueva Armenia'),
    ('0813', N'Ojojona'),
    ('0814', N'Orica'),
    ('0815', N'Reitoca'),
    ('0816', N'Sabanagrande'),
    ('0817', N'San Antonio de Oriente'),
    ('0818', N'San Buenaventura'),
    ('0819', N'San Ignacio'),
    ('0820', N'Cantarranas'),
    ('0821', N'San Miguelito'),
    ('0822', N'Santa Ana'),
    ('0823', N'Santa Lucía'),
    ('0824', N'Talanga'),
    ('0825', N'Tatumbla'),
    ('0826', N'Valle de Ángeles'),
    ('0827', N'Villa de San Francisco'),
    ('0828', N'Vallecillo'),
    ('0901', N'Puerto Lempira'),
    ('0902', N'Brus Laguna'),
    ('0903', N'Ahuas'),
    ('0904', N'Juan Francisco Bulnes'),
    ('0905', N'Ramón Villeda Morales'),
    ('0906', N'Wampusirpi'),
    ('1001', N'La Esperanza'),
    ('1002', N'Camasca'),
    ('1003', N'Colomoncagua'),
    ('1004', N'Concepción'),
    ('1005', N'Dolores'),
    ('1006', N'Intibucá'),
    ('1007', N'Jesús de Otoro'),
    ('1008', N'Magdalena'),
    ('1009', N'Masaguara'),
    ('1010', N'San Antonio'),
    ('1011', N'San Isidro'),
    ('1012', N'San Juan'),
    ('1013', N'San Marcos de la Sierra'),
    ('1014', N'San Miguelito'),
    ('1015', N'Santa Lucía'),
    ('1016', N'Yamaranguila'),
    ('1017', N'San Francisco de Opalaca'),
    ('1101', N'Roatán'),
    ('1102', N'Guanaja'),
    ('1103', N'José Santos Guardiola'),
    ('1104', N'Utila'),
    ('1201', N'La Paz'),
    ('1202', N'Aguanqueterique'),
    ('1203', N'Cabañas'),
    ('1204', N'Cane'),
    ('1205', N'Chinacla'),
    ('1206', N'Guajiquiro'),
    ('1207', N'Lauterique'),
    ('1208', N'Marcala'),
    ('1209', N'Mercedes de Oriente'),
    ('1210', N'Opatoro'),
    ('1211', N'San Antonio del Norte'),
    ('1212', N'San José'),
    ('1213', N'San Juan'),
    ('1214', N'San Pedro de Tutule'),
    ('1215', N'Santa Ana'),
    ('1216', N'Santa Elena'),
    ('1217', N'Santa María'),
    ('1218', N'Santiago de Puringla'),
    ('1219', N'Yarula'),
    ('1301', N'Gracias'),
    ('1302', N'Belén'),
    ('1303', N'Candelaria'),
    ('1304', N'Cololaca'),
    ('1305', N'Erandique'),
    ('1306', N'Gualcince'),
    ('1307', N'Guarita'),
    ('1308', N'La Campa'),
    ('1309', N'La Iguala'),
    ('1310', N'Las Flores'),
    ('1311', N'La Unión'),
    ('1312', N'La Virtud'),
    ('1313', N'Lepaera'),
    ('1314', N'Mapulaca'),
    ('1315', N'Piraera'),
    ('1316', N'San Andrés'),
    ('1317', N'San Francisco'),
    ('1318', N'San Juan Guarita'),
    ('1319', N'San Manuel Colohete'),
    ('1320', N'San Rafael'),
    ('1321', N'San Sebastián'),
    ('1322', N'Santa Cruz'),
    ('1323', N'Talgua'),
    ('1324', N'Tambla'),
    ('1325', N'Tomalá'),
    ('1326', N'Valladolid'),
    ('1327', N'Virginia'),
    ('1328', N'San Marcos de Caiquín'),
    ('1401', N'Ocotepeque'),
    ('1402', N'Belén Gualcho'),
    ('1403', N'Concepción'),
    ('1404', N'Dolores Merendón'),
    ('1405', N'Fraternidad'),
    ('1406', N'La Encarnación'),
    ('1407', N'La Labor'),
    ('1408', N'Lucerna'),
    ('1409', N'Mercedes'),
    ('1410', N'San Fernando'),
    ('1411', N'San Francisco del Valle'),
    ('1412', N'San Jorge'),
    ('1413', N'San Marcos'),
    ('1414', N'Santa Fe'),
    ('1415', N'Sensenti'),
    ('1416', N'Sinuapa'),
    ('1501', N'Juticalpa'),
    ('1502', N'Campamento'),
    ('1503', N'Catacamas'),
    ('1504', N'Concordia'),
    ('1505', N'Dulce Nombre de Culmí'),
    ('1506', N'El Rosario'),
    ('1507', N'Esquipulas del Norte'),
    ('1508', N'Gualaco'),
    ('1509', N'Guarizama'),
    ('1510', N'Guata'),
    ('1511', N'Guayape'),
    ('1512', N'Jano'),
    ('1513', N'La Unión'),
    ('1514', N'Mangulile'),
    ('1515', N'Manto'),
    ('1516', N'Salamá'),
    ('1517', N'San Esteban'),
    ('1518', N'San Francisco de Becerra'),
    ('1519', N'San Francisco de la Paz'),
    ('1520', N'Santa María del Real'),
    ('1521', N'Silca'),
    ('1522', N'Yocón'),
    ('1523', N'Patuca'),
    ('1601', N'Santa Bárbara'),
    ('1602', N'Arada'),
    ('1603', N'Atima'),
    ('1604', N'Azacualpa'),
    ('1605', N'Ceguaca'),
    ('1606', N'Concepción del Norte'),
    ('1607', N'Concepción del Sur'),
    ('1608', N'Chinda'),
    ('1609', N'El Níspero'),
    ('1610', N'Gualala'),
    ('1611', N'Ilama'),
    ('1612', N'Macuelizo'),
    ('1613', N'Naranjito'),
    ('1614', N'Nuevo Celilac'),
    ('1615', N'Petoa'),
    ('1616', N'Protección'),
    ('1617', N'Quimistán'),
    ('1618', N'San Francisco de Ojuera'),
    ('1619', N'San José de Colinas'),
    ('1620', N'San Luis'),
    ('1621', N'San Marcos'),
    ('1622', N'San Nicolás'),
    ('1623', N'San Pedro Zacapa'),
    ('1624', N'San Vicente Centenario'),
    ('1625', N'Santa Rita'),
    ('1626', N'Trinidad'),
    ('1627', N'Las Vegas'),
    ('1628', N'Nueva Frontera'),
    ('1701', N'Nacaome'),
    ('1702', N'Alianza'),
    ('1703', N'Amapala'),
    ('1704', N'Aramecina'),
    ('1705', N'Caridad'),
    ('1706', N'Goascorán'),
    ('1707', N'Langue'),
    ('1708', N'San Francisco de Coray'),
    ('1709', N'San Lorenzo'),
    ('1801', N'Yoro'),
    ('1802', N'Arenal'),
    ('1803', N'El Negrito'),
    ('1804', N'El Progreso'),
    ('1805', N'Jocón'),
    ('1806', N'Morazán'),
    ('1807', N'Olanchito'),
    ('1808', N'Santa Rita'),
    ('1809', N'Sulaco'),
    ('1810', N'Victoria'),
    ('1811', N'Yorito')
) AS v (codigo, nombre)
JOIN dbo.catalogo_departamentos d ON d.pais_iso = 'HN' AND d.codigo = LEFT(v.codigo, 2)
WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_municipios m
                  WHERE m.id_departamento = d.id_departamento AND m.codigo = v.codigo);

PRINT N'  + municipios: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' filas nuevas';
GO

-- ------------------------------------------------------------
-- CAMBIO 6/8: [VERDE / AGREGA] tipos de documento
--   Los patrones son expresiones regulares .NET que usa el servicio de validacion; se aplican al
--   numero normalizado (sin guiones, puntos ni espacios).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.catalogo_tipos_documento (codigo, nombre, pais_iso, patron, largo_min, largo_max, es_identidad, fuente)
SELECT v.codigo, v.nombre, v.pais_iso, v.patron, v.largo_min, v.largo_max, 1, v.fuente
FROM (VALUES
    ('DNI',             N'Documento Nacional de Identificación', 'HN', N'^[0-9]{13}$',        13, 13, N'RNP Honduras: numero de identidad de 13 digitos (municipio 4, anio 4, correlativo 5)'),
    ('RTN',             N'Registro Tributario Nacional',         'HN', N'^[0-9]{14}$',        14, 14, N'SAR Honduras: el RTN de persona natural es el DNI mas un digito adicional'),
    ('PASAPORTE',       N'Pasaporte',                            NULL, N'^[A-Z0-9]{5,20}$',    5, 20, N'Regla de diseno: alfanumerico; no existe un formato unico internacional'),
    ('CARNE_RESIDENTE', N'Carné de residente',                   'HN', NULL,                NULL, NULL, N'Instituto Nacional de Migracion de Honduras; formato por confirmar')
) AS v (codigo, nombre, pais_iso, patron, largo_min, largo_max, fuente)
WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_tipos_documento t WHERE t.codigo = v.codigo);

PRINT N'  + tipos de documento: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' filas nuevas';
GO

-- ------------------------------------------------------------
-- CAMBIO 7/8: [VERDE / AGREGA] tipos de licencia de conducir
--   Categorias y definiciones: tabla de la Policia Nacional (DNVT), que coincide con la lista de la
--   Secretaria de Seguridad. La tabla dice D "superiores a los 26 pasajeros" y D1 "hasta 25": como esta
--   escrita, un autobus de exactamente 26 pasajeros no queda cubierto; se transcribe tal cual.
--   Un listado posterior del usuario (sin fuente indicada) coincide en 8 categorias y omite BE; BE se
--   conserva porque figura en la tabla de la DNVT (si deja de aplicar: UPDATE ... SET activo = 0).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.catalogo_tipos_licencia (codigo, nombre, fuente)
SELECT v.codigo, v.nombre,
       N'Policía Nacional (DNVT), tabla "Tipos de permisos de conducir y sus equivalencias"; las categorías coinciden con la Secretaría de Seguridad. Base legal: Ley de Tránsito, Decreto 205-2005'
FROM (VALUES
    ('A',   N'Ciclomotores y motocicletas de motor o eléctricas'),
    ('B',   N'Automóviles livianos no comprendidos en las categorías A y B1'),
    ('B1',  N'Todo tipo de triciclos y cuadriciclos de motor'),
    ('BE',  N'Automóviles de la categoría B enganchados a un remolque'),
    ('C1',  N'Automóviles no comprendidos en la categoría B, con masa máxima autorizada de hasta 7,500 kg'),
    ('C',   N'Vehículos de carga no articulados superiores a 7,500 kg'),
    ('CE',  N'Vehículos de la categoría C enganchados a un remolque o semirremolque, para transporte de carga'),
    ('D1',  N'Autobuses de hasta 25 pasajeros'),
    ('D',   N'Autobuses superiores a los 26 pasajeros')
) AS v (codigo, nombre)
WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_tipos_licencia t WHERE t.codigo = v.codigo);

PRINT N'  + tipos de licencia: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' filas nuevas';
GO

-- ------------------------------------------------------------
-- CAMBIO 8/8: [VERDE / AGREGA] cargos base para las empresas que no tienen ninguno
--   Hoy solo Transgar (empresa 4). Son los mismos 5 cargos de las demas empresas (decision D7).
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.cargos (id_empresa, codigo, nombre, activo, eliminado, creado_por, fecha_creacion)
SELECT e.id_empresa, v.codigo, v.nombre, 1, 0, N'script_015', SYSUTCDATETIME()
FROM dbo.empresas e
CROSS JOIN (VALUES
    ('CONDUCTOR',  N'Conductor'),
    ('COBRADOR',   N'Cobrador'),
    ('MECANICO',   N'Mecánico'),
    ('SUPERVISOR', N'Supervisor'),
    ('OTRO',       N'Otro')
) AS v (codigo, nombre)
WHERE e.eliminado = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.cargos c WHERE c.id_empresa = e.id_empresa AND c.eliminado = 0);

PRINT N'  + cargos base: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' filas nuevas';
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
--   Falla con THROW si algo no coincide con lo esperado.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_015') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @problemas TABLE (problema NVARCHAR(300) NOT NULL);

IF (SELECT COUNT(*) FROM dbo.catalogo_departamentos WHERE pais_iso = 'HN') <> 18
    INSERT INTO @problemas VALUES (N'Se esperaban 18 departamentos de Honduras');

IF (SELECT COUNT(*) FROM dbo.catalogo_municipios m JOIN dbo.catalogo_departamentos d ON d.id_departamento = m.id_departamento WHERE d.pais_iso = 'HN') <> 298
    INSERT INTO @problemas VALUES (N'Se esperaban 298 municipios de Honduras');

-- Municipios por departamento (cifras oficiales del INE, tabla 2)
INSERT INTO @problemas (problema)
SELECT N'Departamento ' + e.codigo + N': se esperaban ' + CAST(e.esperado AS NVARCHAR(10)) + N' municipios y hay ' + CAST(ISNULL(x.n, 0) AS NVARCHAR(10))
FROM (VALUES
    ('01', 8),
    ('02', 10),
    ('03', 21),
    ('04', 23),
    ('05', 12),
    ('06', 16),
    ('07', 19),
    ('08', 28),
    ('09', 6),
    ('10', 17),
    ('11', 4),
    ('12', 19),
    ('13', 28),
    ('14', 16),
    ('15', 23),
    ('16', 28),
    ('17', 9),
    ('18', 11)
) AS e (codigo, esperado)
LEFT JOIN (SELECT d.codigo, COUNT(*) AS n
           FROM dbo.catalogo_municipios m JOIN dbo.catalogo_departamentos d ON d.id_departamento = m.id_departamento
           WHERE d.pais_iso = 'HN' GROUP BY d.codigo) x ON x.codigo = e.codigo
WHERE ISNULL(x.n, 0) <> e.esperado;

IF EXISTS (SELECT 1 FROM dbo.catalogo_municipios m JOIN dbo.catalogo_departamentos d ON d.id_departamento = m.id_departamento
           WHERE d.pais_iso = 'HN' AND LEFT(m.codigo, 2) <> d.codigo)
    INSERT INTO @problemas VALUES (N'Hay municipios cuyo codigo no empieza por el codigo de su departamento');

IF (SELECT COUNT(*) FROM dbo.catalogo_tipos_documento WHERE codigo IN ('DNI', 'RTN', 'PASAPORTE', 'CARNE_RESIDENTE')) <> 4
    INSERT INTO @problemas VALUES (N'Faltan tipos de documento');

IF (SELECT COUNT(*) FROM dbo.catalogo_tipos_licencia WHERE codigo IN ('A', 'B', 'B1', 'BE', 'C1', 'C', 'CE', 'D1', 'D')) <> 9
    INSERT INTO @problemas VALUES (N'Faltan tipos de licencia');

IF EXISTS (SELECT 1 FROM dbo.empresas e WHERE e.eliminado = 0
           AND NOT EXISTS (SELECT 1 FROM dbo.cargos c WHERE c.id_empresa = e.id_empresa AND c.eliminado = 0))
    INSERT INTO @problemas VALUES (N'Quedan empresas sin cargos');

IF EXISTS (SELECT 1 FROM dbo.catalogo_departamentos WHERE fuente IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.catalogo_municipios WHERE fuente IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.catalogo_tipos_documento WHERE fuente IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.catalogo_tipos_licencia WHERE fuente IS NULL)
    INSERT INTO @problemas VALUES (N'Hay filas de catalogo sin fuente');

INSERT INTO @problemas (problema)
SELECT N'Falta ' + n
FROM (VALUES (N'UX_catalogo_paises_iso3'), (N'UX_catalogo_paises_num'), (N'CK_catalogo_paises_iso2'),
             (N'CK_catalogo_paises_iso3'), (N'CK_catalogo_paises_num')) v (n)
WHERE NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = n AND type IN ('C'))
  AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = n);

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.persona_documentos')
           AND name = N'numero_normalizado' AND max_length > 100)
    INSERT INTO @problemas VALUES (N'numero_normalizado sigue siendo NVARCHAR(4000)');

IF EXISTS (SELECT 1 FROM @problemas)
BEGIN
    SELECT problema FROM @problemas;
    THROW 50015, N'POSTCHECK 015 fallo. Revise la lista anterior.', 1;
END;

SELECT N'departamentos de Honduras' AS dato, COUNT(*) AS valor FROM dbo.catalogo_departamentos WHERE pais_iso = 'HN'
UNION ALL SELECT N'municipios de Honduras', COUNT(*) FROM dbo.catalogo_municipios
UNION ALL SELECT N'tipos de documento', COUNT(*) FROM dbo.catalogo_tipos_documento
UNION ALL SELECT N'tipos de licencia', COUNT(*) FROM dbo.catalogo_tipos_licencia
UNION ALL SELECT N'paises (sin cambio)', COUNT(*) FROM dbo.catalogo_paises
UNION ALL SELECT N'cargos (total)', COUNT(*) FROM dbo.cargos WHERE eliminado = 0;

IF OBJECT_ID(N'tempdb..#precheck_015') IS NOT NULL DROP TABLE #precheck_015;
PRINT N'Script 015 aplicado y verificado.';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Quita lo que cargo este script. Valido mientras ninguna tabla use esas filas (personas,
--   persona_documentos, etc. todavia no las referencian). numero_normalizado queda en NVARCHAR(30):
--   no hace falta devolverlo al tipo anterior.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM dbo.cargos WHERE creado_por = N'script_015';

DELETE FROM dbo.catalogo_tipos_licencia WHERE codigo IN ('A', 'B', 'B1', 'BE', 'C1', 'C', 'CE', 'D1', 'D');
DELETE FROM dbo.catalogo_tipos_documento WHERE codigo IN ('DNI', 'RTN', 'PASAPORTE', 'CARNE_RESIDENTE');

DELETE m FROM dbo.catalogo_municipios m
JOIN dbo.catalogo_departamentos d ON d.id_departamento = m.id_departamento WHERE d.pais_iso = 'HN';
DELETE FROM dbo.catalogo_departamentos WHERE pais_iso = 'HN';

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_iso2') ALTER TABLE dbo.catalogo_paises DROP CONSTRAINT CK_catalogo_paises_iso2;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_iso3') ALTER TABLE dbo.catalogo_paises DROP CONSTRAINT CK_catalogo_paises_iso3;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_catalogo_paises_num')  ALTER TABLE dbo.catalogo_paises DROP CONSTRAINT CK_catalogo_paises_num;
DROP INDEX IF EXISTS UX_catalogo_paises_iso3 ON dbo.catalogo_paises;
DROP INDEX IF EXISTS UX_catalogo_paises_num  ON dbo.catalogo_paises;

IF COL_LENGTH(N'dbo.catalogo_departamentos', N'fuente') IS NOT NULL ALTER TABLE dbo.catalogo_departamentos DROP COLUMN fuente;
IF COL_LENGTH(N'dbo.catalogo_municipios', N'fuente')    IS NOT NULL ALTER TABLE dbo.catalogo_municipios DROP COLUMN fuente;
IF COL_LENGTH(N'dbo.catalogo_tipos_documento', N'fuente') IS NOT NULL ALTER TABLE dbo.catalogo_tipos_documento DROP COLUMN fuente;
IF COL_LENGTH(N'dbo.catalogo_tipos_licencia', N'fuente')  IS NOT NULL ALTER TABLE dbo.catalogo_tipos_licencia DROP COLUMN fuente;

COMMIT TRANSACTION;
*/
