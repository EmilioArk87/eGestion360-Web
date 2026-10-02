-- ============================================================
-- Script   : 016_seed_catalogo_paises.sql
-- Proposito: Carga inicial (semilla) reproducible del catalogo de paises ISO 3166-1 (249 filas).
--            La tabla dbo.catalogo_paises ya existe en produccion con estos mismos datos; este script
--            existe para poder reconstruir un entorno nuevo, porque el repositorio no tenia ninguno.
--            Es idempotente: solo inserta los paises que faltan y NO modifica los existentes.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-09-30
-- BD       : eBD_SPD
-- Requiere : dbo.catalogo_paises (Estructura BD.sql)
-- Rollback : no aplica (los datos ya existian); ver la nota al final
-- ============================================================
-- FUENTES Y VERIFICACION:
--   Codigos ISO alfa-2, alfa-3 y numerico: ISO 3166-1 (249 codigos oficialmente asignados).
--   Verificados el 2026-09-30 contra la tabla M49 de la Division de Estadistica de la ONU
--   (unstats.un.org/unsd/methodology/m49/overview/): 248 de 249 coinciden exactamente. La unica
--   diferencia es Taiwan (TW, TWN, 158): la ONU no la lista, ISO 3166-1 si; se confirmo con el
--   dataset country-codes (Open Knowledge Foundation), que trae las 249.
--   Nombres en ingles: nombres cortos de ISO 3166 (232 de 248 identicos a los de la ONU; las
--   diferencias son formas cortas frente a formales, p. ej. "Korea (Republic of)").
--   Nombres en espanol: ortografia de uso corriente (p. ej. Banglades, Botsuana, Fiyi); la ONU usa
--   otras grafias (Bangladesh, Botswana, Fiji). Se conservan las actuales de la base.
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
    THROW 50016, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF OBJECT_ID(N'dbo.catalogo_paises', N'U') IS NULL
    THROW 50016, N'Falta dbo.catalogo_paises. Ejecute Estructura BD.sql antes. Abortar.', 1;

IF OBJECT_ID(N'tempdb..#precheck_016') IS NOT NULL DROP TABLE #precheck_016;
CREATE TABLE #precheck_016 (ok BIT NOT NULL);
INSERT INTO #precheck_016 (ok) VALUES (1);

SELECT COUNT(*) AS paises_antes FROM dbo.catalogo_paises;
GO

-- ------------------------------------------------------------
-- CAMBIO 1/1: [VERDE / AGREGA] paises que falten (no modifica los existentes)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_016') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.catalogo_paises (pais_iso, pais_iso3, pais_num, nombre_espanol, nombre_ingles)
SELECT v.pais_iso, v.pais_iso3, v.pais_num, v.nombre_espanol, v.nombre_ingles
FROM (VALUES
    ('AD', 'AND', '020', N'Andorra', N'Andorra'),
    ('AE', 'ARE', '784', N'Emiratos Árabes Unidos', N'United Arab Emirates'),
    ('AF', 'AFG', '004', N'Afganistán', N'Afghanistan'),
    ('AG', 'ATG', '028', N'Antigua y Barbuda', N'Antigua and Barbuda'),
    ('AI', 'AIA', '660', N'Anguila', N'Anguilla'),
    ('AL', 'ALB', '008', N'Albania', N'Albania'),
    ('AM', 'ARM', '051', N'Armenia', N'Armenia'),
    ('AO', 'AGO', '024', N'Angola', N'Angola'),
    ('AQ', 'ATA', '010', N'Antártida', N'Antarctica'),
    ('AR', 'ARG', '032', N'Argentina', N'Argentina'),
    ('AS', 'ASM', '016', N'Samoa Americana', N'American Samoa'),
    ('AT', 'AUT', '040', N'Austria', N'Austria'),
    ('AU', 'AUS', '036', N'Australia', N'Australia'),
    ('AW', 'ABW', '533', N'Aruba', N'Aruba'),
    ('AX', 'ALA', '248', N'Islas Åland', N'Åland Islands'),
    ('AZ', 'AZE', '031', N'Azerbaiyán', N'Azerbaijan'),
    ('BA', 'BIH', '070', N'Bosnia y Herzegovina', N'Bosnia and Herzegovina'),
    ('BB', 'BRB', '052', N'Barbados', N'Barbados'),
    ('BD', 'BGD', '050', N'Bangladés', N'Bangladesh'),
    ('BE', 'BEL', '056', N'Bélgica', N'Belgium'),
    ('BF', 'BFA', '854', N'Burkina Faso', N'Burkina Faso'),
    ('BG', 'BGR', '100', N'Bulgaria', N'Bulgaria'),
    ('BH', 'BHR', '048', N'Baréin', N'Bahrain'),
    ('BI', 'BDI', '108', N'Burundi', N'Burundi'),
    ('BJ', 'BEN', '204', N'Benín', N'Benin'),
    ('BL', 'BLM', '652', N'San Bartolomé', N'Saint Barthélemy'),
    ('BM', 'BMU', '060', N'Islas Bermudas', N'Bermuda'),
    ('BN', 'BRN', '096', N'Brunéi Darussalam', N'Brunei Darussalam'),
    ('BO', 'BOL', '068', N'Bolivia (Estado Plurinacional de)', N'Bolivia (Plurinational State of)'),
    ('BQ', 'BES', '535', N'Caribe Neerlandés', N'Bonaire, Sint Eustatius and Saba'),
    ('BR', 'BRA', '076', N'Brasil', N'Brazil'),
    ('BS', 'BHS', '044', N'Bahamas', N'Bahamas'),
    ('BT', 'BTN', '064', N'Bután', N'Bhutan'),
    ('BV', 'BVT', '074', N'Isla Bouvet', N'Bouvet Island'),
    ('BW', 'BWA', '072', N'Botsuana', N'Botswana'),
    ('BY', 'BLR', '112', N'Bielorrusia', N'Belarus'),
    ('BZ', 'BLZ', '084', N'Belice', N'Belize'),
    ('CA', 'CAN', '124', N'Canadá', N'Canada'),
    ('CC', 'CCK', '166', N'Islas Cocos (Keeling)', N'Cocos (Keeling) Islands'),
    ('CD', 'COD', '180', N'Congo (Rep. Dem.)', N'Congo (Democratic Republic of the)'),
    ('CF', 'CAF', '140', N'República Centroafricana', N'Central African Republic'),
    ('CG', 'COG', '178', N'Congo', N'Congo'),
    ('CH', 'CHE', '756', N'Suiza', N'Switzerland'),
    ('CI', 'CIV', '384', N'Côte d''Ivoire', N'Côte d''Ivoire'),
    ('CK', 'COK', '184', N'Islas Cook', N'Cook Islands'),
    ('CL', 'CHL', '152', N'Chile', N'Chile'),
    ('CM', 'CMR', '120', N'Camerún', N'Cameroon'),
    ('CN', 'CHN', '156', N'China', N'China'),
    ('CO', 'COL', '170', N'Colombia', N'Colombia'),
    ('CR', 'CRI', '188', N'Costa Rica', N'Costa Rica'),
    ('CU', 'CUB', '192', N'Cuba', N'Cuba'),
    ('CV', 'CPV', '132', N'Cabo Verde', N'Cabo Verde'),
    ('CW', 'CUW', '531', N'Curazao', N'Curaçao'),
    ('CX', 'CXR', '162', N'Isla de Navidad', N'Christmas Island'),
    ('CY', 'CYP', '196', N'Chipre', N'Cyprus'),
    ('CZ', 'CZE', '203', N'Chequia', N'Czechia'),
    ('DE', 'DEU', '276', N'Alemania', N'Germany'),
    ('DJ', 'DJI', '262', N'Djibouti', N'Djibouti'),
    ('DK', 'DNK', '208', N'Dinamarca', N'Denmark'),
    ('DM', 'DMA', '212', N'Dominica', N'Dominica'),
    ('DO', 'DOM', '214', N'República Dominicana', N'Dominican Republic'),
    ('DZ', 'DZA', '012', N'Argelia', N'Algeria'),
    ('EC', 'ECU', '218', N'Ecuador', N'Ecuador'),
    ('EE', 'EST', '233', N'Estonia', N'Estonia'),
    ('EG', 'EGY', '818', N'Egipto', N'Egypt'),
    ('EH', 'ESH', '732', N'Sahara Occidental', N'Western Sahara'),
    ('ER', 'ERI', '232', N'Eritrea', N'Eritrea'),
    ('ES', 'ESP', '724', N'España', N'Spain'),
    ('ET', 'ETH', '231', N'Etiopía', N'Ethiopia'),
    ('FI', 'FIN', '246', N'Finlandia', N'Finland'),
    ('FJ', 'FJI', '242', N'Fiyi', N'Fiji'),
    ('FK', 'FLK', '238', N'Islas Malvinas (Falkland)', N'Falkland Islands (Malvinas)'),
    ('FM', 'FSM', '583', N'Micronesia (Estados Federados de)', N'Micronesia (Federated States of)'),
    ('FO', 'FRO', '234', N'Islas Feroe', N'Faroe Islands'),
    ('FR', 'FRA', '250', N'Francia', N'France'),
    ('GA', 'GAB', '266', N'Gabón', N'Gabon'),
    ('GB', 'GBR', '826', N'Reino Unido', N'United Kingdom of Great Britain and Northern Ireland'),
    ('GD', 'GRD', '308', N'Granada', N'Grenada'),
    ('GE', 'GEO', '268', N'Georgia', N'Georgia'),
    ('GF', 'GUF', '254', N'Guayana Francesa', N'French Guiana'),
    ('GG', 'GGY', '831', N'Guernsey', N'Guernsey'),
    ('GH', 'GHA', '288', N'Ghana', N'Ghana'),
    ('GI', 'GIB', '292', N'Gibraltar', N'Gibraltar'),
    ('GL', 'GRL', '304', N'Groenlandia', N'Greenland'),
    ('GM', 'GMB', '270', N'Gambia', N'Gambia'),
    ('GN', 'GIN', '324', N'Guinea', N'Guinea'),
    ('GP', 'GLP', '312', N'Guadalupe', N'Guadeloupe'),
    ('GQ', 'GNQ', '226', N'Guinea Ecuatorial', N'Equatorial Guinea'),
    ('GR', 'GRC', '300', N'Grecia', N'Greece'),
    ('GS', 'SGS', '239', N'Islas Georgia del Sur y Sandwich del Sur', N'South Georgia and the South Sandwich Islands'),
    ('GT', 'GTM', '320', N'Guatemala', N'Guatemala'),
    ('GU', 'GUM', '316', N'Guam', N'Guam'),
    ('GW', 'GNB', '624', N'Guinea-Bisáu', N'Guinea-Bissau'),
    ('GY', 'GUY', '328', N'Guyana', N'Guyana'),
    ('HK', 'HKG', '344', N'Hong Kong', N'Hong Kong'),
    ('HM', 'HMD', '334', N'Islas Heard y McDonald', N'Heard Island and McDonald Islands'),
    ('HN', 'HND', '340', N'Honduras', N'Honduras'),
    ('HR', 'HRV', '191', N'Croacia', N'Croatia'),
    ('HT', 'HTI', '332', N'Haití', N'Haiti'),
    ('HU', 'HUN', '348', N'Hungría', N'Hungary'),
    ('ID', 'IDN', '360', N'Indonesia', N'Indonesia'),
    ('IE', 'IRL', '372', N'Irlanda', N'Ireland'),
    ('IL', 'ISR', '376', N'Israel', N'Israel'),
    ('IM', 'IMN', '833', N'Isla de Man', N'Isle of Man'),
    ('IN', 'IND', '356', N'India', N'India'),
    ('IO', 'IOT', '086', N'Territorio Británico del Océano Índico', N'British Indian Ocean Territory'),
    ('IQ', 'IRQ', '368', N'Irak', N'Iraq'),
    ('IR', 'IRN', '364', N'Irán (Rep. Islámica de)', N'Iran (Islamic Republic of)'),
    ('IS', 'ISL', '352', N'Islandia', N'Iceland'),
    ('IT', 'ITA', '380', N'Italia', N'Italy'),
    ('JE', 'JEY', '832', N'Jersey', N'Jersey'),
    ('JM', 'JAM', '388', N'Jamaica', N'Jamaica'),
    ('JO', 'JOR', '400', N'Jordania', N'Jordan'),
    ('JP', 'JPN', '392', N'Japón', N'Japan'),
    ('KE', 'KEN', '404', N'Kenia', N'Kenya'),
    ('KG', 'KGZ', '417', N'Kirguistán', N'Kyrgyzstan'),
    ('KH', 'KHM', '116', N'Camboya', N'Cambodia'),
    ('KI', 'KIR', '296', N'Kiribati', N'Kiribati'),
    ('KM', 'COM', '174', N'Comoras', N'Comoros'),
    ('KN', 'KNA', '659', N'San Cristóbal y Nieves', N'Saint Kitts and Nevis'),
    ('KP', 'PRK', '408', N'Corea del Norte', N'Korea (Democratic People''s Republic of)'),
    ('KR', 'KOR', '410', N'Corea del Sur', N'Korea (Republic of)'),
    ('KW', 'KWT', '414', N'Kuwait', N'Kuwait'),
    ('KY', 'CYM', '136', N'Islas Caimán', N'Cayman Islands'),
    ('KZ', 'KAZ', '398', N'Kazajistán', N'Kazakhstan'),
    ('LA', 'LAO', '418', N'Laos', N'Lao People''s Democratic Republic'),
    ('LB', 'LBN', '422', N'Líbano', N'Lebanon'),
    ('LC', 'LCA', '662', N'Santa Lucía', N'Saint Lucia'),
    ('LI', 'LIE', '438', N'Liechtenstein', N'Liechtenstein'),
    ('LK', 'LKA', '144', N'Sri Lanka', N'Sri Lanka'),
    ('LR', 'LBR', '430', N'Liberia', N'Liberia'),
    ('LS', 'LSO', '426', N'Lesoto', N'Lesotho'),
    ('LT', 'LTU', '440', N'Lituania', N'Lithuania'),
    ('LU', 'LUX', '442', N'Luxemburgo', N'Luxembourg'),
    ('LV', 'LVA', '428', N'Letonia', N'Latvia'),
    ('LY', 'LBY', '434', N'Libia', N'Libya'),
    ('MA', 'MAR', '504', N'Marruecos', N'Morocco'),
    ('MC', 'MCO', '492', N'Mónaco', N'Monaco'),
    ('MD', 'MDA', '498', N'Moldavia (República de)', N'Moldova (Republic of)'),
    ('ME', 'MNE', '499', N'Montenegro', N'Montenegro'),
    ('MF', 'MAF', '663', N'San Martín (parte francesa)', N'Saint Martin (French part)'),
    ('MG', 'MDG', '450', N'Madagascar', N'Madagascar'),
    ('MH', 'MHL', '584', N'Islas Marshall', N'Marshall Islands'),
    ('MK', 'MKD', '807', N'Macedonia del Norte', N'North Macedonia'),
    ('ML', 'MLI', '466', N'Malí', N'Mali'),
    ('MM', 'MMR', '104', N'Myanmar', N'Myanmar'),
    ('MN', 'MNG', '496', N'Mongolia', N'Mongolia'),
    ('MO', 'MAC', '446', N'Macao', N'Macao'),
    ('MP', 'MNP', '580', N'Islas Marianas del Norte', N'Northern Mariana Islands'),
    ('MQ', 'MTQ', '474', N'Martinica', N'Martinique'),
    ('MR', 'MRT', '478', N'Mauritania', N'Mauritania'),
    ('MS', 'MSR', '500', N'Montserrat', N'Montserrat'),
    ('MT', 'MLT', '470', N'Malta', N'Malta'),
    ('MU', 'MUS', '480', N'Mauricio', N'Mauritius'),
    ('MV', 'MDV', '462', N'Maldivas', N'Maldives'),
    ('MW', 'MWI', '454', N'Malaui', N'Malawi'),
    ('MX', 'MEX', '484', N'México', N'Mexico'),
    ('MY', 'MYS', '458', N'Malasia', N'Malaysia'),
    ('MZ', 'MOZ', '508', N'Mozambique', N'Mozambique'),
    ('NA', 'NAM', '516', N'Namibia', N'Namibia'),
    ('NC', 'NCL', '540', N'Nueva Caledonia', N'New Caledonia'),
    ('NE', 'NER', '562', N'Níger', N'Niger'),
    ('NF', 'NFK', '574', N'Isla Norfolk', N'Norfolk Island'),
    ('NG', 'NGA', '566', N'Nigeria', N'Nigeria'),
    ('NI', 'NIC', '558', N'Nicaragua', N'Nicaragua'),
    ('NL', 'NLD', '528', N'Países Bajos', N'Netherlands'),
    ('NO', 'NOR', '578', N'Noruega', N'Norway'),
    ('NP', 'NPL', '524', N'Nepal', N'Nepal'),
    ('NR', 'NRU', '520', N'Nauru', N'Nauru'),
    ('NU', 'NIU', '570', N'Niue', N'Niue'),
    ('NZ', 'NZL', '554', N'Nueva Zelanda', N'New Zealand'),
    ('OM', 'OMN', '512', N'Omán', N'Oman'),
    ('PA', 'PAN', '591', N'Panamá', N'Panama'),
    ('PE', 'PER', '604', N'Perú', N'Peru'),
    ('PF', 'PYF', '258', N'Polinesia Francesa', N'French Polynesia'),
    ('PG', 'PNG', '598', N'Papúa Nueva Guinea', N'Papua New Guinea'),
    ('PH', 'PHL', '608', N'Filipinas', N'Philippines'),
    ('PK', 'PAK', '586', N'Pakistán', N'Pakistan'),
    ('PL', 'POL', '616', N'Polonia', N'Poland'),
    ('PM', 'SPM', '666', N'San Pedro y Miquelón', N'Saint Pierre and Miquelon'),
    ('PN', 'PCN', '612', N'Islas Pitcairn', N'Pitcairn'),
    ('PR', 'PRI', '630', N'Puerto Rico', N'Puerto Rico'),
    ('PS', 'PSE', '275', N'Palestina (Estado de)', N'Palestine, State of'),
    ('PT', 'PRT', '620', N'Portugal', N'Portugal'),
    ('PW', 'PLW', '585', N'Palaos', N'Palau'),
    ('PY', 'PRY', '600', N'Paraguay', N'Paraguay'),
    ('QA', 'QAT', '634', N'Catar', N'Qatar'),
    ('RE', 'REU', '638', N'Reunión', N'Réunion'),
    ('RO', 'ROU', '642', N'Rumanía', N'Romania'),
    ('RS', 'SRB', '688', N'Serbia', N'Serbia'),
    ('RU', 'RUS', '643', N'Rusia (Federación de)', N'Russian Federation'),
    ('RW', 'RWA', '646', N'Ruanda', N'Rwanda'),
    ('SA', 'SAU', '682', N'Arabia Saudita', N'Saudi Arabia'),
    ('SB', 'SLB', '090', N'Islas Salomón', N'Solomon Islands'),
    ('SC', 'SYC', '690', N'Seychelles', N'Seychelles'),
    ('SD', 'SDN', '729', N'Sudán', N'Sudan'),
    ('SE', 'SWE', '752', N'Suecia', N'Sweden'),
    ('SG', 'SGP', '702', N'Singapur', N'Singapore'),
    ('SH', 'SHN', '654', N'Santa Elena, Ascensión y Tristán de Acuña', N'Saint Helena, Ascension and Tristan da Cunha'),
    ('SI', 'SVN', '705', N'Eslovenia', N'Slovenia'),
    ('SJ', 'SJM', '744', N'Svalbard y Jan Mayen', N'Svalbard and Jan Mayen'),
    ('SK', 'SVK', '703', N'Eslovaquia', N'Slovakia'),
    ('SL', 'SLE', '694', N'Sierra Leona', N'Sierra Leone'),
    ('SM', 'SMR', '674', N'San Marino', N'San Marino'),
    ('SN', 'SEN', '686', N'Senegal', N'Senegal'),
    ('SO', 'SOM', '706', N'Somalia', N'Somalia'),
    ('SR', 'SUR', '740', N'Surinam', N'Suriname'),
    ('SS', 'SSD', '728', N'Sudán del Sur', N'South Sudan'),
    ('ST', 'STP', '678', N'Santo Tomé y Príncipe', N'Sao Tome and Principe'),
    ('SV', 'SLV', '222', N'El Salvador', N'El Salvador'),
    ('SX', 'SXM', '534', N'San Martín (parte neerlandesa)', N'Sint Maarten (Dutch part)'),
    ('SY', 'SYR', '760', N'Siria (Rep. Árabe)', N'Syrian Arab Republic'),
    ('SZ', 'SWZ', '748', N'Esuatini', N'Eswatini'),
    ('TC', 'TCA', '796', N'Islas Turcas y Caicos', N'Turks and Caicos Islands'),
    ('TD', 'TCD', '148', N'Chad', N'Chad'),
    ('TF', 'ATF', '260', N'Territorios Australes Franceses', N'French Southern Territories'),
    ('TG', 'TGO', '768', N'Togo', N'Togo'),
    ('TH', 'THA', '764', N'Tailandia', N'Thailand'),
    ('TJ', 'TJK', '762', N'Tayikistán', N'Tajikistan'),
    ('TK', 'TKL', '772', N'Tokelau', N'Tokelau'),
    ('TL', 'TLS', '626', N'Timor-Leste', N'Timor-Leste'),
    ('TM', 'TKM', '795', N'Turkmenistán', N'Turkmenistan'),
    ('TN', 'TUN', '788', N'Túnez', N'Tunisia'),
    ('TO', 'TON', '776', N'Tonga', N'Tonga'),
    ('TR', 'TUR', '792', N'Turquía', N'Türkiye'),
    ('TT', 'TTO', '780', N'Trinidad y Tobago', N'Trinidad and Tobago'),
    ('TV', 'TUV', '798', N'Tuvalu', N'Tuvalu'),
    ('TW', 'TWN', '158', N'Taiwán', N'Taiwan, Province of China'),
    ('TZ', 'TZA', '834', N'Tanzania (Rep. Unida de)', N'Tanzania, United Republic of'),
    ('UA', 'UKR', '804', N'Ucrania', N'Ukraine'),
    ('UG', 'UGA', '800', N'Uganda', N'Uganda'),
    ('UM', 'UMI', '581', N'Islas Ultramarinas Menores de EE. UU.', N'United States Minor Outlying Islands'),
    ('US', 'USA', '840', N'Estados Unidos', N'United States of America'),
    ('UY', 'URY', '858', N'Uruguay', N'Uruguay'),
    ('UZ', 'UZB', '860', N'Uzbekistán', N'Uzbekistan'),
    ('VA', 'VAT', '336', N'Santa Sede', N'Holy See'),
    ('VC', 'VCT', '670', N'San Vicente y las Granadinas', N'Saint Vincent and the Grenadines'),
    ('VE', 'VEN', '862', N'Venezuela (Rep. Bolivariana de)', N'Venezuela (Bolivarian Republic of)'),
    ('VG', 'VGB', '092', N'Islas Vírgenes Británicas', N'Virgin Islands (British)'),
    ('VI', 'VIR', '850', N'Islas Vírgenes de los Estados Unidos', N'Virgin Islands (U.S.)'),
    ('VN', 'VNM', '704', N'Vietnam', N'Viet Nam'),
    ('VU', 'VUT', '548', N'Vanuatu', N'Vanuatu'),
    ('WF', 'WLF', '876', N'Wallis y Futuna', N'Wallis and Futuna'),
    ('WS', 'WSM', '882', N'Samoa', N'Samoa'),
    ('YE', 'YEM', '887', N'Yemen', N'Yemen'),
    ('YT', 'MYT', '175', N'Mayotte', N'Mayotte'),
    ('ZA', 'ZAF', '710', N'Sudáfrica', N'South Africa'),
    ('ZM', 'ZMB', '894', N'Zambia', N'Zambia'),
    ('ZW', 'ZWE', '716', N'Zimbabue', N'Zimbabwe')
) AS v (pais_iso, pais_iso3, pais_num, nombre_espanol, nombre_ingles)
WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_paises p WHERE p.pais_iso = v.pais_iso);

PRINT N'  + paises nuevos: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
GO

-- ------------------------------------------------------------
-- POSTCHECK (comprobacion posterior)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_016') IS NULL
BEGIN
    RAISERROR('Ejecute primero el PRECHECK en esta misma sesion. Abortar.', 16, 1);
    RETURN;
END;

IF (SELECT COUNT(*) FROM dbo.catalogo_paises) < 249
    THROW 50016, N'POSTCHECK 016 fallo: se esperaban al menos 249 paises.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.catalogo_paises WHERE pais_iso = 'HN' AND pais_iso3 = 'HND' AND pais_num = '340')
    THROW 50016, N'POSTCHECK 016 fallo: Honduras (HN, HND, 340) no esta o difiere.', 1;

SELECT COUNT(*) AS paises_despues,
       COUNT(DISTINCT pais_iso3) AS iso3_distintos,
       COUNT(DISTINCT pais_num)  AS numericos_distintos
FROM dbo.catalogo_paises;

IF OBJECT_ID(N'tempdb..#precheck_016') IS NOT NULL DROP TABLE #precheck_016;
PRINT N'Script 016 aplicado y verificado.';
GO

-- ------------------------------------------------------------
-- ROLLBACK: no aplica. Los paises ya existian antes de este script y estan referenciados
-- (empresas.pais_iso, personas.pais_nacionalidad, etc.); borrarlos romperia esas relaciones.
-- Si hiciera falta quitar solo lo que agrego este script en un entorno nuevo, restaure el respaldo.
-- ------------------------------------------------------------