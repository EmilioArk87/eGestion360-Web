-- ============================================================
-- Script   : 017_monedas_iso4217_y_fk.sql
-- Proposito: Completa el catalogo dbo.monedas con los datos de ISO 4217 y le da integridad:
--            codigo numerico y numero de decimales de cada moneda, marca las monedas que ya no
--            figuran en la lista vigente, agrega las vigentes que faltaban y enlaza con claves
--            foraneas las columnas de moneda que hoy no tienen integridad referencial.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-30
-- BD       : eBD_SPD
-- Requiere : dbo.monedas; las 12 columnas de moneda indicadas abajo (todas CHAR(3))
-- Rollback : ver la seccion ROLLBACK al final
-- ============================================================
-- FUENTE: ISO 4217, lista oficial "list-one" que publica SIX (agencia de mantenimiento de ISO 4217),
--   version del 2026-09-17: 178 codigos de moneda. Se compararon uno a uno con dbo.monedas (153):
--   150 coinciden; ANG, BGN y ZWL no figuran en la lista vigente; faltaban KYD, SSP, VED, XCG y ZWG
--   (se omiten fondos, metales y unidades especiales: BOV, CHE, CHW, CLF, COU, MXV, USN, UYI, UYW,
--   XAD, XAG, XAU, XBA-XBD, XDR, XPD, XPT, XSU, XTS, XUA, XXX).
--   Los nombres de las 5 monedas agregadas estan en ingles tal como los publica ISO; falta traducirlos.
--
-- COLUMNAS QUE QUEDAN ENLAZADAS (12, todas CHAR(3)):
--   cargas_combustible.moneda, empleados.moneda_tarifa, empresas.moneda_iso, gastos_repuestos.moneda,
--   ingresos_operativos.moneda, ordenes_mantenimiento.moneda, personas.moneda_tarifa,
--   polizas_seguros.moneda, precios_combustible.moneda, salarios_diarios.moneda,
--   tasas_cambio.moneda_origen, tasas_cambio.moneda_destino.
-- QUEDAN PARA UN PASO POSTERIOR (8 columnas con otro tipo; hay que alinear el tipo a CHAR(3) primero):
--   peajes.moneda (VARCHAR), clientes.moneda_iso_default, proveedores.moneda_iso_default, facturas.moneda,
--   notas.moneda, pagos.moneda y tipos_cambio.moneda_origen / moneda_destino (NVARCHAR).
--
-- IMPORTANTE: monedas.codigo_iso pasa de NVARCHAR(3) a CHAR(3). Un codigo ISO 4217 siempre son 3 letras
-- ASCII, y una clave foranea exige el mismo tipo que la columna que referencia. Hay que reflejarlo en la
-- entidad EF Moneda (por ejemplo [Column(TypeName = "char(3)")]) y agregarle las nuevas propiedades.
--
-- Seguridad de ejecucion (igual que 014 y 015): una sola sesion; el PRECHECK deja una marca temporal
-- que exigen los demas bloques; idempotente; los bloques que modifican estructura corren en transaccion
-- con TRY/CATCH. Requiere aprobacion /alerta-bd. Archivo en UTF-8 con BOM (el nombre de Bolivar lleva tilde).
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
    THROW 50017, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) < 14
    THROW 50017, N'Se requiere SQL Server 2017 o superior. Abortar.', 1;

IF OBJECT_ID(N'dbo.monedas', N'U') IS NULL
    THROW 50017, N'Falta dbo.monedas. Abortar.', 1;

-- Codigos de moneda actuales: 3 letras mayusculas
IF EXISTS (SELECT 1 FROM dbo.monedas WHERE codigo_iso COLLATE Latin1_General_BIN NOT LIKE '[A-Z][A-Z][A-Z]')
    THROW 50017, N'dbo.monedas tiene codigos que no son de 3 letras mayusculas. Abortar.', 1;

-- Las 12 columnas a enlazar: existen, son CHAR(3) y todos sus valores existen en monedas
DECLARE @col TABLE (n INT IDENTITY(1,1) PRIMARY KEY, tabla SYSNAME NOT NULL, columna SYSNAME NOT NULL);
INSERT INTO @col (tabla, columna) VALUES
    (N'cargas_combustible', N'moneda'), (N'empleados', N'moneda_tarifa'), (N'empresas', N'moneda_iso'),
    (N'gastos_repuestos', N'moneda'), (N'ingresos_operativos', N'moneda'), (N'ordenes_mantenimiento', N'moneda'),
    (N'personas', N'moneda_tarifa'), (N'polizas_seguros', N'moneda'), (N'precios_combustible', N'moneda'),
    (N'salarios_diarios', N'moneda'), (N'tasas_cambio', N'moneda_origen'), (N'tasas_cambio', N'moneda_destino');

-- Solo se admiten claves foraneas hacia monedas que sean las de este script (nombre FK_tabla_columna)
IF EXISTS (SELECT 1 FROM sys.foreign_keys f
           WHERE f.referenced_object_id = OBJECT_ID(N'dbo.monedas')
             AND NOT EXISTS (SELECT 1 FROM @col c WHERE f.name = N'FK_' + c.tabla + N'_' + c.columna))
    THROW 50017, N'Ya hay claves foraneas hacia monedas que este script no conoce. Revise antes de seguir. Abortar.', 1;

DECLARE @i INT = 1, @max INT = (SELECT MAX(n) FROM @col), @t SYSNAME, @c SYSNAME, @sql NVARCHAR(1000), @malos INT, @msg NVARCHAR(400);
WHILE @i <= @max
BEGIN
    SELECT @t = tabla, @c = columna FROM @col WHERE n = @i;

    IF NOT EXISTS (SELECT 1 FROM sys.columns col JOIN sys.types ty ON ty.user_type_id = col.user_type_id
                   WHERE col.object_id = OBJECT_ID(N'dbo.' + @t) AND col.name = @c AND ty.name = N'char' AND col.max_length = 3)
    BEGIN
        SET @msg = N'La columna dbo.' + @t + N'.' + @c + N' no existe o no es CHAR(3). Abortar.';
        THROW 50017, @msg, 1;
    END;

    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.' + QUOTENAME(@t) + N' WHERE ' + QUOTENAME(@c)
             + N' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.monedas m WHERE m.codigo_iso = ' + QUOTENAME(@c) + N')';
    EXEC sys.sp_executesql @sql, N'@n INT OUTPUT', @n = @malos OUTPUT;
    IF @malos > 0
    BEGIN
        SET @msg = N'dbo.' + @t + N'.' + @c + N' tiene valores que no existen en monedas. Abortar.';
        THROW 50017, @msg, 1;
    END;

    SET @i += 1;
END;

IF OBJECT_ID(N'tempdb..#precheck_017') IS NOT NULL DROP TABLE #precheck_017;
CREATE TABLE #precheck_017 (ok BIT NOT NULL);
INSERT INTO #precheck_017 (ok) VALUES (1);

SELECT (SELECT COUNT(*) FROM dbo.monedas) AS monedas_antes,
       (SELECT COUNT(*) FROM dbo.monedas WHERE activo = 1) AS activas_antes,
       (SELECT ty.name + '(' + CAST(c.max_length / CASE WHEN ty.name = 'nvarchar' THEN 2 ELSE 1 END AS VARCHAR(5)) + ')'
          FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
         WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso') AS tipo_codigo_iso;
GO

-- ------------------------------------------------------------
-- CAMBIO 1/6: [VERDE / AGREGA] columnas codigo_numerico, decimales y fuente en dbo.monedas
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.monedas', N'codigo_numerico') IS NULL
        ALTER TABLE dbo.monedas ADD codigo_numerico CHAR(3) NULL;
    IF COL_LENGTH(N'dbo.monedas', N'decimales') IS NULL
        ALTER TABLE dbo.monedas ADD decimales TINYINT NULL;
    IF COL_LENGTH(N'dbo.monedas', N'fuente') IS NULL
        ALTER TABLE dbo.monedas ADD fuente NVARCHAR(300) NULL;

    COMMIT TRANSACTION;
    PRINT N'  + monedas: codigo_numerico, decimales y fuente';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 2/6: [AMARILLO / MODIFICA] dbo.monedas.codigo_iso de NVARCHAR(3) a CHAR(3)
--   Se suelta y se recrea la clave primaria (PK_monedas). Ninguna tabla la referencia todavia
--   (el PRECHECK lo comprueba). No cambia ningun valor.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso' AND ty.name = N'nvarchar')
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @pk SYSNAME = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.monedas') AND type = 'PK');
        DECLARE @drop NVARCHAR(400) = N'ALTER TABLE dbo.monedas DROP CONSTRAINT ' + QUOTENAME(@pk) + N';';
        EXEC sys.sp_executesql @drop;

        ALTER TABLE dbo.monedas ALTER COLUMN codigo_iso CHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;

        ALTER TABLE dbo.monedas ADD CONSTRAINT PK_monedas PRIMARY KEY CLUSTERED (codigo_iso);

        COMMIT TRANSACTION;
        PRINT N'  ~ monedas.codigo_iso: CHAR(3), clave primaria PK_monedas';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- ------------------------------------------------------------
-- CAMBIO 3/6: [AMARILLO / MODIFICA y VERDE / AGREGA] datos de ISO 4217
--   a) codigo numerico y decimales de las monedas que ya existen (150)
--   b) ANG, BGN y ZWL: ya no figuran en la lista vigente, se marcan inactivas (no se borran)
--   c) KYD, SSP, VED, XCG y ZWG: monedas vigentes que faltaban
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE m
       SET m.codigo_numerico = v.numerico,
           m.decimales       = v.decimales,
           m.fuente          = N'ISO 4217 (SIX), lista publicada el 2026-09-17'
    FROM dbo.monedas m
    JOIN (VALUES
        ('AED', '784', 2),
        ('AFN', '971', 2),
        ('ALL', '008', 2),
        ('AMD', '051', 2),
        ('AOA', '973', 2),
        ('ARS', '032', 2),
        ('AUD', '036', 2),
        ('AWG', '533', 2),
        ('AZN', '944', 2),
        ('BAM', '977', 2),
        ('BBD', '052', 2),
        ('BDT', '050', 2),
        ('BHD', '048', 3),
        ('BIF', '108', 0),
        ('BMD', '060', 2),
        ('BND', '096', 2),
        ('BOB', '068', 2),
        ('BRL', '986', 2),
        ('BSD', '044', 2),
        ('BTN', '064', 2),
        ('BWP', '072', 2),
        ('BYN', '933', 2),
        ('BZD', '084', 2),
        ('CAD', '124', 2),
        ('CDF', '976', 2),
        ('CHF', '756', 2),
        ('CLP', '152', 0),
        ('CNY', '156', 2),
        ('COP', '170', 2),
        ('CRC', '188', 2),
        ('CUP', '192', 2),
        ('CVE', '132', 2),
        ('CZK', '203', 2),
        ('DJF', '262', 0),
        ('DKK', '208', 2),
        ('DOP', '214', 2),
        ('DZD', '012', 2),
        ('EGP', '818', 2),
        ('ERN', '232', 2),
        ('ETB', '230', 2),
        ('EUR', '978', 2),
        ('FJD', '242', 2),
        ('FKP', '238', 2),
        ('GBP', '826', 2),
        ('GEL', '981', 2),
        ('GHS', '936', 2),
        ('GIP', '292', 2),
        ('GMD', '270', 2),
        ('GNF', '324', 0),
        ('GTQ', '320', 2),
        ('GYD', '328', 2),
        ('HKD', '344', 2),
        ('HNL', '340', 2),
        ('HTG', '332', 2),
        ('HUF', '348', 2),
        ('IDR', '360', 2),
        ('ILS', '376', 2),
        ('INR', '356', 2),
        ('IQD', '368', 3),
        ('IRR', '364', 2),
        ('ISK', '352', 0),
        ('JMD', '388', 2),
        ('JOD', '400', 3),
        ('JPY', '392', 0),
        ('KES', '404', 2),
        ('KGS', '417', 2),
        ('KHR', '116', 2),
        ('KMF', '174', 0),
        ('KPW', '408', 2),
        ('KRW', '410', 0),
        ('KWD', '414', 3),
        ('KZT', '398', 2),
        ('LAK', '418', 2),
        ('LBP', '422', 2),
        ('LKR', '144', 2),
        ('LRD', '430', 2),
        ('LSL', '426', 2),
        ('LYD', '434', 3),
        ('MAD', '504', 2),
        ('MDL', '498', 2),
        ('MGA', '969', 2),
        ('MKD', '807', 2),
        ('MMK', '104', 2),
        ('MNT', '496', 2),
        ('MOP', '446', 2),
        ('MRU', '929', 2),
        ('MUR', '480', 2),
        ('MVR', '462', 2),
        ('MWK', '454', 2),
        ('MXN', '484', 2),
        ('MYR', '458', 2),
        ('MZN', '943', 2),
        ('NAD', '516', 2),
        ('NGN', '566', 2),
        ('NIO', '558', 2),
        ('NOK', '578', 2),
        ('NPR', '524', 2),
        ('NZD', '554', 2),
        ('OMR', '512', 3),
        ('PAB', '590', 2),
        ('PEN', '604', 2),
        ('PGK', '598', 2),
        ('PHP', '608', 2),
        ('PKR', '586', 2),
        ('PLN', '985', 2),
        ('PYG', '600', 0),
        ('QAR', '634', 2),
        ('RON', '946', 2),
        ('RSD', '941', 2),
        ('RUB', '643', 2),
        ('RWF', '646', 0),
        ('SAR', '682', 2),
        ('SBD', '090', 2),
        ('SCR', '690', 2),
        ('SDG', '938', 2),
        ('SEK', '752', 2),
        ('SGD', '702', 2),
        ('SHP', '654', 2),
        ('SLE', '925', 2),
        ('SOS', '706', 2),
        ('SRD', '968', 2),
        ('STN', '930', 2),
        ('SVC', '222', 2),
        ('SYP', '760', 2),
        ('SZL', '748', 2),
        ('THB', '764', 2),
        ('TJS', '972', 2),
        ('TMT', '934', 2),
        ('TND', '788', 3),
        ('TOP', '776', 2),
        ('TRY', '949', 2),
        ('TTD', '780', 2),
        ('TWD', '901', 2),
        ('TZS', '834', 2),
        ('UAH', '980', 2),
        ('UGX', '800', 0),
        ('USD', '840', 2),
        ('UYU', '858', 2),
        ('UZS', '860', 2),
        ('VES', '928', 2),
        ('VND', '704', 0),
        ('VUV', '548', 0),
        ('WST', '882', 2),
        ('XAF', '950', 0),
        ('XCD', '951', 2),
        ('XOF', '952', 0),
        ('XPF', '953', 0),
        ('YER', '886', 2),
        ('ZAR', '710', 2),
        ('ZMW', '967', 2)
    ) AS v (codigo, numerico, decimales) ON v.codigo = m.codigo_iso
    WHERE m.codigo_numerico IS NULL OR m.codigo_numerico <> v.numerico
       OR (m.decimales IS NULL AND v.decimales IS NOT NULL) OR m.decimales <> v.decimales;
    PRINT N'  ~ monedas actualizadas con ISO 4217: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    UPDATE dbo.monedas
       SET activo = 0,
           fuente = N'No figura en la lista vigente de ISO 4217 (SIX, 2026-09-17)'
    WHERE codigo_iso IN ('ANG', 'BGN', 'ZWL') AND activo = 1;
    PRINT N'  ~ monedas marcadas inactivas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    INSERT INTO dbo.monedas (codigo_iso, nombre, simbolo, activo, codigo_numerico, decimales, fuente)
    SELECT v.codigo, v.nombre, v.codigo, 1, v.numerico, v.decimales,
           N'ISO 4217 (SIX), lista publicada el 2026-09-17; nombre en inglés, falta traducir'
    FROM (VALUES
        ('KYD', N'Cayman Islands Dollar', '136', 2),
        ('SSP', N'South Sudanese Pound', '728', 2),
        ('VED', N'Bolívar Soberano', '926', 2),
        ('XCG', N'Caribbean Guilder', '532', 2),
        ('ZWG', N'Zimbabwe Gold', '924', 2)
    ) AS v (codigo, nombre, numerico, decimales)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.monedas m WHERE m.codigo_iso = v.codigo);
    PRINT N'  + monedas nuevas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 4/6: [VERDE / AGREGA] restricciones de integridad en dbo.monedas
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_codigo' AND parent_object_id = OBJECT_ID(N'dbo.monedas'))
        ALTER TABLE dbo.monedas ADD CONSTRAINT CK_monedas_codigo
            CHECK (codigo_iso COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_numerico' AND parent_object_id = OBJECT_ID(N'dbo.monedas'))
        ALTER TABLE dbo.monedas ADD CONSTRAINT CK_monedas_numerico
            CHECK (codigo_numerico IS NULL OR codigo_numerico LIKE '[0-9][0-9][0-9]');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_decimales' AND parent_object_id = OBJECT_ID(N'dbo.monedas'))
        ALTER TABLE dbo.monedas ADD CONSTRAINT CK_monedas_decimales
            CHECK (decimales IS NULL OR decimales BETWEEN 0 AND 4);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_monedas_numerico' AND object_id = OBJECT_ID(N'dbo.monedas'))
        CREATE UNIQUE INDEX UX_monedas_numerico ON dbo.monedas (codigo_numerico) WHERE codigo_numerico IS NOT NULL;

    COMMIT TRANSACTION;
    PRINT N'  ~ monedas: 3 CHECK y un indice unico del codigo numerico';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- CAMBIO 5/6: [VERDE / AGREGA] claves foraneas hacia dbo.monedas (12 columnas CHAR(3))
--   Los datos actuales de todas son HNL, que existe en monedas (el PRECHECK lo comprueba).
--   Efecto en la aplicacion: ahora un codigo de moneda inexistente se rechaza al guardar.
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_moneda' AND parent_object_id = OBJECT_ID(N'dbo.cargas_combustible'))
        ALTER TABLE dbo.cargas_combustible ADD CONSTRAINT FK_cargas_combustible_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_moneda_tarifa' AND parent_object_id = OBJECT_ID(N'dbo.empleados'))
        ALTER TABLE dbo.empleados ADD CONSTRAINT FK_empleados_moneda_tarifa FOREIGN KEY (moneda_tarifa) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_moneda_iso' AND parent_object_id = OBJECT_ID(N'dbo.empresas'))
        ALTER TABLE dbo.empresas ADD CONSTRAINT FK_empresas_moneda_iso FOREIGN KEY (moneda_iso) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_moneda' AND parent_object_id = OBJECT_ID(N'dbo.gastos_repuestos'))
        ALTER TABLE dbo.gastos_repuestos ADD CONSTRAINT FK_gastos_repuestos_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_moneda' AND parent_object_id = OBJECT_ID(N'dbo.ingresos_operativos'))
        ALTER TABLE dbo.ingresos_operativos ADD CONSTRAINT FK_ingresos_operativos_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_moneda' AND parent_object_id = OBJECT_ID(N'dbo.ordenes_mantenimiento'))
        ALTER TABLE dbo.ordenes_mantenimiento ADD CONSTRAINT FK_ordenes_mantenimiento_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_moneda_tarifa' AND parent_object_id = OBJECT_ID(N'dbo.personas'))
        ALTER TABLE dbo.personas ADD CONSTRAINT FK_personas_moneda_tarifa FOREIGN KEY (moneda_tarifa) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_moneda' AND parent_object_id = OBJECT_ID(N'dbo.polizas_seguros'))
        ALTER TABLE dbo.polizas_seguros ADD CONSTRAINT FK_polizas_seguros_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_moneda' AND parent_object_id = OBJECT_ID(N'dbo.precios_combustible'))
        ALTER TABLE dbo.precios_combustible ADD CONSTRAINT FK_precios_combustible_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_moneda' AND parent_object_id = OBJECT_ID(N'dbo.salarios_diarios'))
        ALTER TABLE dbo.salarios_diarios ADD CONSTRAINT FK_salarios_diarios_moneda FOREIGN KEY (moneda) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_origen' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
        ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_moneda_origen FOREIGN KEY (moneda_origen) REFERENCES dbo.monedas (codigo_iso);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_destino' AND parent_object_id = OBJECT_ID(N'dbo.tasas_cambio'))
        ALTER TABLE dbo.tasas_cambio ADD CONSTRAINT FK_tasas_cambio_moneda_destino FOREIGN KEY (moneda_destino) REFERENCES dbo.monedas (codigo_iso);

    COMMIT TRANSACTION;
    PRINT N'  + 12 claves foraneas hacia monedas';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_017') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

DECLARE @problemas TABLE (problema NVARCHAR(300) NOT NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.monedas WHERE codigo_iso = 'HNL' AND codigo_numerico = '340' AND decimales = 2 AND activo = 1)
    INSERT INTO @problemas VALUES (N'HNL no tiene numerico 340, 2 decimales y estado activo');

IF EXISTS (SELECT 1 FROM dbo.monedas WHERE activo = 1 AND (codigo_numerico IS NULL OR decimales IS NULL))
    INSERT INTO @problemas VALUES (N'Hay monedas activas sin codigo numerico o sin decimales');

IF (SELECT COUNT(*) FROM dbo.monedas WHERE codigo_iso IN ('KYD', 'SSP', 'VED', 'XCG', 'ZWG') AND activo = 1) <> 5
    INSERT INTO @problemas VALUES (N'Faltan monedas nuevas');

IF (SELECT COUNT(*) FROM dbo.monedas WHERE codigo_iso IN ('ANG', 'BGN', 'ZWL') AND activo = 0) <> 3
    INSERT INTO @problemas VALUES (N'ANG, BGN y ZWL no quedaron inactivas');

IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.monedas') AND c.name = N'codigo_iso' AND ty.name = N'char' AND c.max_length = 3)
    INSERT INTO @problemas VALUES (N'monedas.codigo_iso no es CHAR(3)');

IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'dbo.monedas')) <> 12
    INSERT INTO @problemas VALUES (N'Se esperaban 12 claves foraneas hacia monedas');

INSERT INTO @problemas (problema)
SELECT N'Falta ' + n
FROM (VALUES (N'CK_monedas_codigo'), (N'CK_monedas_numerico'), (N'CK_monedas_decimales'), (N'UX_monedas_numerico')) v (n)
WHERE NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = n AND type = 'C')
  AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = n);

IF EXISTS (SELECT 1 FROM @problemas)
BEGIN
    SELECT problema FROM @problemas;
    THROW 50017, N'POSTCHECK 017 fallo. Revise la lista anterior.', 1;
END;

SELECT COUNT(*) AS monedas, SUM(CAST(activo AS INT)) AS activas,
       SUM(CASE WHEN codigo_numerico IS NOT NULL THEN 1 ELSE 0 END) AS con_numerico,
       (SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'dbo.monedas')) AS claves_foraneas
FROM dbo.monedas;

IF OBJECT_ID(N'tempdb..#precheck_017') IS NOT NULL DROP TABLE #precheck_017;
PRINT N'Script 017 aplicado y verificado.';
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Deja dbo.monedas como estaba antes de este script.
-- ------------------------------------------------------------
/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cargas_combustible_moneda') ALTER TABLE dbo.cargas_combustible DROP CONSTRAINT FK_cargas_combustible_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empleados_moneda_tarifa') ALTER TABLE dbo.empleados DROP CONSTRAINT FK_empleados_moneda_tarifa;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_empresas_moneda_iso') ALTER TABLE dbo.empresas DROP CONSTRAINT FK_empresas_moneda_iso;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_gastos_repuestos_moneda') ALTER TABLE dbo.gastos_repuestos DROP CONSTRAINT FK_gastos_repuestos_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ingresos_operativos_moneda') ALTER TABLE dbo.ingresos_operativos DROP CONSTRAINT FK_ingresos_operativos_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ordenes_mantenimiento_moneda') ALTER TABLE dbo.ordenes_mantenimiento DROP CONSTRAINT FK_ordenes_mantenimiento_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_personas_moneda_tarifa') ALTER TABLE dbo.personas DROP CONSTRAINT FK_personas_moneda_tarifa;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_polizas_seguros_moneda') ALTER TABLE dbo.polizas_seguros DROP CONSTRAINT FK_polizas_seguros_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_precios_combustible_moneda') ALTER TABLE dbo.precios_combustible DROP CONSTRAINT FK_precios_combustible_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_salarios_diarios_moneda') ALTER TABLE dbo.salarios_diarios DROP CONSTRAINT FK_salarios_diarios_moneda;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_origen') ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_moneda_origen;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tasas_cambio_moneda_destino') ALTER TABLE dbo.tasas_cambio DROP CONSTRAINT FK_tasas_cambio_moneda_destino;

DROP INDEX IF EXISTS UX_monedas_numerico ON dbo.monedas;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_codigo')    ALTER TABLE dbo.monedas DROP CONSTRAINT CK_monedas_codigo;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_numerico')  ALTER TABLE dbo.monedas DROP CONSTRAINT CK_monedas_numerico;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_monedas_decimales') ALTER TABLE dbo.monedas DROP CONSTRAINT CK_monedas_decimales;

DELETE FROM dbo.monedas WHERE codigo_iso IN ('KYD', 'SSP', 'VED', 'XCG', 'ZWG');
UPDATE dbo.monedas SET activo = 1 WHERE codigo_iso IN ('ANG', 'BGN', 'ZWL');

IF COL_LENGTH(N'dbo.monedas', N'codigo_numerico') IS NOT NULL ALTER TABLE dbo.monedas DROP COLUMN codigo_numerico;
IF COL_LENGTH(N'dbo.monedas', N'decimales') IS NOT NULL ALTER TABLE dbo.monedas DROP COLUMN decimales;
IF COL_LENGTH(N'dbo.monedas', N'fuente') IS NOT NULL ALTER TABLE dbo.monedas DROP COLUMN fuente;

ALTER TABLE dbo.monedas DROP CONSTRAINT PK_monedas;
ALTER TABLE dbo.monedas ALTER COLUMN codigo_iso NVARCHAR(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;
ALTER TABLE dbo.monedas ADD CONSTRAINT PK_monedas PRIMARY KEY CLUSTERED (codigo_iso);

COMMIT TRANSACTION;
*/
