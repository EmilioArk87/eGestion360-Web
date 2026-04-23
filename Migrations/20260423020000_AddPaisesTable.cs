using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPaisesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paises",
                columns: table => new
                {
                    codigo_iso = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paises", x => x.codigo_iso);
                });

            // Seed data — países en español
            migrationBuilder.Sql(@"
INSERT INTO paises (codigo_iso, nombre, activo) VALUES
('AF','Afganistán',1),('AL','Albania',1),('DE','Alemania',1),('AD','Andorra',1),
('AO','Angola',1),('AG','Antigua y Barbuda',1),('SA','Arabia Saudita',1),
('DZ','Argelia',1),('AR','Argentina',1),('AM','Armenia',1),('AU','Australia',1),
('AT','Austria',1),('AZ','Azerbaiyán',1),('BS','Bahamas',1),('BH','Baréin',1),
('BD','Bangladés',1),('BB','Barbados',1),('BE','Bélgica',1),('BZ','Belice',1),
('BJ','Benín',1),('BY','Bielorrusia',1),('BO','Bolivia',1),
('BA','Bosnia y Herzegovina',1),('BW','Botsuana',1),('BR','Brasil',1),
('BN','Brunéi',1),('BG','Bulgaria',1),('BF','Burkina Faso',1),('BI','Burundi',1),
('BT','Bután',1),('CV','Cabo Verde',1),('KH','Camboya',1),('CM','Camerún',1),
('CA','Canadá',1),('QA','Catar',1),('TD','Chad',1),('CL','Chile',1),
('CN','China',1),('CY','Chipre',1),('CO','Colombia',1),('KM','Comoras',1),
('CG','Congo',1),('CD','Congo (RDC)',1),('KP','Corea del Norte',1),
('KR','Corea del Sur',1),('CI','Costa de Marfil',1),('CR','Costa Rica',1),
('HR','Croacia',1),('CU','Cuba',1),('DK','Dinamarca',1),('DJ','Yibuti',1),
('DM','Dominica',1),('EC','Ecuador',1),('EG','Egipto',1),('SV','El Salvador',1),
('AE','Emiratos Árabes Unidos',1),('ER','Eritrea',1),('SK','Eslovaquia',1),
('SI','Eslovenia',1),('ES','España',1),('US','Estados Unidos',1),
('EE','Estonia',1),('ET','Etiopía',1),('PH','Filipinas',1),('FI','Finlandia',1),
('FJ','Fiyi',1),('FR','Francia',1),('GA','Gabón',1),('GM','Gambia',1),
('GE','Georgia',1),('GH','Ghana',1),('GD','Granada',1),('GR','Grecia',1),
('GT','Guatemala',1),('GN','Guinea',1),('GQ','Guinea Ecuatorial',1),
('GW','Guinea-Bisáu',1),('GY','Guyana',1),('HT','Haití',1),('HN','Honduras',1),
('HU','Hungría',1),('IN','India',1),('ID','Indonesia',1),('IQ','Irak',1),
('IR','Irán',1),('IE','Irlanda',1),('IS','Islandia',1),('MH','Islas Marshall',1),
('SB','Islas Salomón',1),('IL','Israel',1),('IT','Italia',1),('JM','Jamaica',1),
('JP','Japón',1),('JO','Jordania',1),('KZ','Kazajistán',1),('KE','Kenia',1),
('KG','Kirguistán',1),('KI','Kiribati',1),('KW','Kuwait',1),('LA','Laos',1),
('LS','Lesoto',1),('LV','Letonia',1),('LB','Líbano',1),('LR','Liberia',1),
('LY','Libia',1),('LI','Liechtenstein',1),('LT','Lituania',1),
('LU','Luxemburgo',1),('MK','Macedonia del Norte',1),('MG','Madagascar',1),
('MY','Malasia',1),('MW','Malaui',1),('MV','Maldivas',1),('ML','Malí',1),
('MT','Malta',1),('MA','Marruecos',1),('MU','Mauricio',1),('MR','Mauritania',1),
('MX','México',1),('FM','Micronesia',1),('MD','Moldavia',1),('MC','Mónaco',1),
('MN','Mongolia',1),('ME','Montenegro',1),('MZ','Mozambique',1),('MM','Myanmar',1),
('NA','Namibia',1),('NR','Nauru',1),('NP','Nepal',1),('NI','Nicaragua',1),
('NE','Níger',1),('NG','Nigeria',1),('NO','Noruega',1),('NZ','Nueva Zelanda',1),
('OM','Omán',1),('PK','Pakistán',1),('PW','Palaos',1),('PA','Panamá',1),
('PG','Papúa Nueva Guinea',1),('PY','Paraguay',1),('NL','Países Bajos',1),
('PE','Perú',1),('PL','Polonia',1),('PT','Portugal',1),('GB','Reino Unido',1),
('CF','República Centroafricana',1),('CZ','República Checa',1),
('DO','República Dominicana',1),('RW','Ruanda',1),('RO','Rumanía',1),
('RU','Rusia',1),('WS','Samoa',1),('KN','San Cristóbal y Nieves',1),
('SM','San Marino',1),('VC','San Vicente y las Granadinas',1),
('LC','Santa Lucía',1),('ST','Santo Tomé y Príncipe',1),('SN','Senegal',1),
('RS','Serbia',1),('SC','Seychelles',1),('SL','Sierra Leona',1),
('SG','Singapur',1),('SY','Siria',1),('SO','Somalia',1),('LK','Sri Lanka',1),
('SZ','Suazilandia',1),('ZA','Sudáfrica',1),('SS','Sudán del Sur',1),
('SD','Sudán',1),('SE','Suecia',1),('CH','Suiza',1),('SR','Surinam',1),
('TH','Tailandia',1),('TZ','Tanzania',1),('TJ','Tayikistán',1),
('TL','Timor Oriental',1),('TG','Togo',1),('TO','Tonga',1),
('TT','Trinidad y Tobago',1),('TN','Túnez',1),('TM','Turkmenistán',1),
('TR','Turquía',1),('TV','Tuvalu',1),('UA','Ucrania',1),('UG','Uganda',1),
('UY','Uruguay',1),('UZ','Uzbekistán',1),('VU','Vanuatu',1),
('VE','Venezuela',1),('VN','Vietnam',1),('YE','Yemen',1),
('ZM','Zambia',1),('ZW','Zimbabue',1);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "paises");
        }
    }
}
