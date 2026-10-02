using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Personas
{
    /// <summary>
    /// Pruebas del servicio de validación contra una base SQLite con los catálogos reales (formas y patrones).
    /// La base se comparte entre las pruebas de la clase: cada prueba que inserta datos usa documentos y
    /// códigos propios, para que el orden no importe.
    /// </summary>
    public class PersonaValidacionServiceTests : IClassFixture<BaseDeDatosDePrueba>
    {
        private static readonly DateOnly Hoy = RelojFijo.Hoy;
        private readonly BaseDeDatosDePrueba _bd;

        public PersonaValidacionServiceTests(BaseDeDatosDePrueba bd) => _bd = bd;

        // ── Utilidades ──────────────────────────────────────────────────────

        private static PersonaValidacionService Servicio(ApplicationDbContext db, PersonaValidacionOptions? opciones = null) =>
            new(db, Options.Create(opciones ?? new PersonaValidacionOptions()), RelojFijo.PorDefecto());

        /// <summary>Un alta de empleado válida y completa; cada prueba cambia solo lo que quiere probar.</summary>
        private static PersonaDatosInput Alta(int empresa = DatosBase.EmpresaB) => new()
        {
            Modo = ModoValidacionPersona.Alta,
            IdEmpresa = empresa,
            PrimerNombre = "Luis",
            PrimerApellido = "Pérez",
            TipoDocumento = "DNI",
            Documento = "0801-1990-99999",
            FechaNacimiento = new DateOnly(1990, 5, 10),
            Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "ZZ900" }
        };

        private async Task<ResultadoValidacionPersona> Validar(PersonaDatosInput input)
        {
            using var db = _bd.Crear();
            return await Servicio(db).ValidarAsync(input);
        }

        private static bool Tiene(ResultadoValidacionPersona r, string campo, string? parte = null) =>
            r.Errores.Any(e => e.Campo == campo && (parte == null || e.Mensaje.Contains(parte, StringComparison.OrdinalIgnoreCase)));

        private static string Detalle(ResultadoValidacionPersona r) =>
            "Errores: [" + string.Join(" | ", r.Errores.Select(e => $"{e.Campo}: {e.Mensaje}")) + "] Avisos: [" + string.Join(" | ", r.Advertencias) + "]";

        // ── Alta válida ─────────────────────────────────────────────────────

        [Fact]
        public async Task Un_alta_completa_es_valida_sin_errores_ni_avisos_y_se_normaliza()
        {
            var r = await Validar(Alta());

            Assert.True(r.Ok, Detalle(r));
            Assert.Empty(r.Advertencias);
            Assert.Null(r.DocumentoExistente);
            Assert.Equal("0801199099999", r.Datos.Documento);
            Assert.Equal("0801-1990-99999", r.Datos.DocumentoFormateado);
            Assert.Equal("Luis", r.Datos.Nombres);
            Assert.Equal("Pérez", r.Datos.Apellidos);
            Assert.Equal("LUIS PEREZ", r.Datos.NombreNormalizado);
            Assert.True(r.Datos.TieneDocumentoDeIdentidad);
            Assert.Equal("MECANICO", r.Datos.Empleado?.Cargo);
        }

        [Fact]
        public async Task La_empresa_es_obligatoria()
        {
            var r = await Validar(Alta(empresa: 0));
            Assert.True(Tiene(r, "IdEmpresa"), Detalle(r));
        }

        [Fact]
        public async Task Con_errores_los_datos_se_devuelven_normalizados_igual()
        {
            var i = Alta();
            i.PrimerNombre = null; i.PrimerApellido = null; i.Documento = "x"; i.Telefono = "1";
            var r = await Validar(i);

            Assert.False(r.Ok);
            Assert.NotNull(r.Datos);
            Assert.True(r.Errores.Count >= 3, Detalle(r));
        }

        // ── Nombres ─────────────────────────────────────────────────────────

        [Fact]
        public async Task El_primer_nombre_y_el_primer_apellido_son_obligatorios()
        {
            var i = Alta(); i.PrimerNombre = " "; i.PrimerApellido = "";
            var r = await Validar(i);

            Assert.False(r.Ok);
            Assert.True(Tiene(r, "PrimerNombre", "obligatorio"), Detalle(r));
            Assert.True(Tiene(r, "PrimerApellido", "obligatorio"), Detalle(r));
        }

        [Fact]
        public async Task Un_nombre_con_digitos_se_rechaza()
        {
            var i = Alta(); i.PrimerNombre = "J0se";
            Assert.True(Tiene(await Validar(i), "PrimerNombre", "solo admite letras"));
        }

        [Fact]
        public async Task Una_parte_del_nombre_de_mas_de_50_caracteres_se_rechaza()
        {
            var i = Alta(); i.SegundoApellido = new string('a', 51);
            Assert.True(Tiene(await Validar(i), "SegundoApellido", "50"));
        }

        [Fact]
        public async Task Los_nombres_en_mayusculas_pasan_a_formato_propio_y_se_componen_los_campos_legados()
        {
            var i = Alta(); i.PrimerNombre = "JUAN"; i.SegundoNombre = "CARLOS";
            var r = await Validar(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("Juan", r.Datos.PrimerNombre);
            Assert.Equal("Juan Carlos", r.Datos.Nombres);
            Assert.Equal("JUAN CARLOS PEREZ", r.Datos.NombreNormalizado);
        }

        // ── Documento de identidad ──────────────────────────────────────────

        [Theory]
        [InlineData("0801-1990-1234", "13 dígitos")]
        [InlineData("1901-1990-12345", "departamento")]
        [InlineData("0801-1850-12345", "año")]
        public async Task Un_DNI_mal_formado_se_rechaza_con_su_mensaje(string dni, string mensaje)
        {
            var i = Alta(); i.Documento = dni;
            Assert.True(Tiene(await Validar(i), "Documento", mensaje));
        }

        [Fact]
        public async Task Un_municipio_del_DNI_que_no_esta_en_el_catalogo_solo_avisa()
        {
            var i = Alta(); i.Documento = "0899-1990-12345";
            var r = await Validar(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Contains(r.Advertencias, a => a.Contains("municipio"));
        }

        [Fact]
        public async Task Documento_sin_tipo_y_tipo_sin_documento_se_rechazan()
        {
            var sinTipo = Alta(); sinTipo.TipoDocumento = null;
            Assert.True(Tiene(await Validar(sinTipo), "TipoDocumento"));

            var sinNumero = Alta(); sinNumero.Documento = null;
            Assert.True(Tiene(await Validar(sinNumero), "Documento", "número"));
        }

        [Fact]
        public async Task INTERNO_ya_no_es_un_tipo_de_documento()
        {
            var i = Alta(); i.TipoDocumento = "INTERNO";
            Assert.True(Tiene(await Validar(i), "TipoDocumento", "no es válido"));
        }

        [Fact]
        public async Task El_pasaporte_se_normaliza_a_mayusculas_y_respeta_su_largo()
        {
            var bueno = Alta(); bueno.TipoDocumento = "PASAPORTE"; bueno.Documento = "ab-1234";
            var r = await Validar(bueno);
            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("AB1234", r.Datos.Documento);

            var corto = Alta(); corto.TipoDocumento = "PASAPORTE"; corto.Documento = "AB12";
            Assert.True(Tiene(await Validar(corto), "Documento", "entre 5 y 20"));
        }

        [Fact]
        public async Task El_RTN_exige_14_digitos()
        {
            var bueno = Alta(); bueno.TipoDocumento = "RTN"; bueno.Documento = "08011990123456";
            Assert.True((await Validar(bueno)).Ok);

            var malo = Alta(); malo.TipoDocumento = "RTN"; malo.Documento = "0801199012345";
            Assert.True(Tiene(await Validar(malo), "Documento", "14 dígitos"));
        }

        [Fact]
        public async Task Un_tipo_sin_patron_en_el_catalogo_solo_admite_letras_y_numeros()
        {
            var bueno = Alta(); bueno.TipoDocumento = "CARNE_RESIDENTE"; bueno.Documento = "R-12345";
            var r = await Validar(bueno);
            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("R12345", r.Datos.Documento);

            var malo = Alta(); malo.TipoDocumento = "CARNE_RESIDENTE"; malo.Documento = "R_123";
            Assert.True(Tiene(await Validar(malo), "Documento", "letras y números"));
        }

        [Fact]
        public async Task El_pais_emisor_debe_existir_y_coincidir_con_el_del_tipo()
        {
            var inexistente = Alta(); inexistente.PaisEmisor = "XX";
            Assert.True(Tiene(await Validar(inexistente), "PaisEmisor"));

            var distinto = Alta(); distinto.PaisEmisor = "US";
            Assert.True(Tiene(await Validar(distinto), "PaisEmisor", "no corresponde"));
        }

        [Fact]
        public async Task Un_patron_mal_escrito_en_el_catalogo_no_rompe_la_validacion()
        {
            using var db = _bd.Crear();
            db.CatalogoTiposDocumento.Add(new CatalogoTipoDocumento
            {
                Codigo = "RARO", Nombre = "Documento raro", Patron = "([", FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var i = Alta(); i.TipoDocumento = "RARO"; i.PaisEmisor = "HN"; i.Documento = "ABC123";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(r.Ok, Detalle(r));   // cae a las reglas generales: letras y números
        }

        // ── Documento duplicado ─────────────────────────────────────────────

        [Fact]
        public async Task Un_documento_ya_registrado_en_la_misma_empresa_es_un_error()
        {
            using var db = _bd.Crear();
            var existente = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Ana", "Mejía", dni: "0801199011111");

            var i = Alta(DatosBase.EmpresaA); i.Documento = "0801-1990-11111";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(Tiene(r, "Documento", "ya existe"), Detalle(r));
            Assert.NotNull(r.DocumentoExistente);
            Assert.Equal(existente.IdPersona, r.DocumentoExistente!.IdPersona);
            Assert.True(r.DocumentoExistente.TieneVinculoEnEstaEmpresa);
        }

        [Fact]
        public async Task Un_documento_que_solo_existe_en_otra_empresa_no_da_error_ni_aviso_decision_D8()
        {
            using var db = _bd.Crear();
            var existente = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Beto", "Rivera", dni: "0801199022222");

            var i = Alta(DatosBase.EmpresaB); i.Documento = "0801199022222";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Empty(r.Errores);
            Assert.Empty(r.Advertencias);   // nada que revele que la persona existe
            Assert.NotNull(r.DocumentoExistente);
            Assert.Equal(existente.IdPersona, r.DocumentoExistente!.IdPersona);
            Assert.False(r.DocumentoExistente.TieneVinculoEnEstaEmpresa);
        }

        [Fact]
        public async Task El_propio_documento_de_una_persona_no_es_un_duplicado()
        {
            using var db = _bd.Crear();
            var existente = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Carla", "Soto", dni: "0801199033333");

            var i = Alta(DatosBase.EmpresaA);
            i.Modo = ModoValidacionPersona.Edicion; i.IdPersona = existente.IdPersona; i.Documento = "0801199033333";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Null(r.DocumentoExistente);
        }

        [Fact]
        public async Task El_duplicado_se_detecta_aunque_se_escriba_con_guiones()
        {
            using var db = _bd.Crear();
            PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Dino", "Lara", dni: "0801199044444");

            var i = Alta(DatosBase.EmpresaA); i.Documento = "0801-1990-44444";
            Assert.True(Tiene(await Servicio(db).ValidarAsync(i), "Documento", "ya existe"));
        }

        [Fact]
        public async Task Un_vinculo_terminado_en_la_empresa_no_cuenta_como_vinculo_vigente_pero_si_como_visible()
        {
            // La persona ya estuvo en esta empresa (vínculo con fecha de fin): sigue siendo "de esta empresa".
            using var db = _bd.Crear();
            PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Eva", "Núñez", dni: "0801199055555", vigente: false);

            var i = Alta(DatosBase.EmpresaA); i.Documento = "0801199055555";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(Tiene(r, "Documento", "ya existe"), Detalle(r));
        }

        // ── Código de empleado ──────────────────────────────────────────────

        [Fact]
        public async Task El_codigo_de_empleado_repetido_en_la_empresa_es_un_error_pero_no_en_otra()
        {
            using var db = _bd.Crear();
            var existente = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Fidel", "Cruz", codigoInterno: "CODA1");

            var enLaMisma = Alta(DatosBase.EmpresaB); enLaMisma.Documento = null; enLaMisma.TipoDocumento = null;
            enLaMisma.Empleado!.CodigoInterno = "CODA1";
            Assert.True(Tiene(await Servicio(db).ValidarAsync(enLaMisma), "Empleado.CodigoInterno", "ya existe"));

            var propio = Alta(DatosBase.EmpresaB); propio.Documento = null; propio.TipoDocumento = null;
            propio.Modo = ModoValidacionPersona.Edicion; propio.IdPersona = existente.IdPersona;
            propio.Empleado!.CodigoInterno = "CODA1";
            Assert.False(Tiene(await Servicio(db).ValidarAsync(propio), "Empleado.CodigoInterno"));

            var enOtra = Alta(DatosBase.EmpresaA); enOtra.Documento = null; enOtra.TipoDocumento = null;
            enOtra.Empleado!.CodigoInterno = "CODA1";
            Assert.False(Tiene(await Servicio(db).ValidarAsync(enOtra), "Empleado.CodigoInterno"));
        }

        [Fact]
        public async Task Un_codigo_con_espacios_se_rechaza()
        {
            var i = Alta(); i.Empleado!.CodigoInterno = "AB 12";
            Assert.True(Tiene(await Validar(i), "Empleado.CodigoInterno", "sin espacios"));
        }

        [Fact]
        public async Task Un_empleado_nuevo_necesita_documento_o_codigo()
        {
            var ninguno = Alta(); ninguno.Documento = null; ninguno.TipoDocumento = null; ninguno.Empleado!.CodigoInterno = null;
            Assert.True(Tiene(await Validar(ninguno), "Documento", "código de empleado"));

            var soloCodigo = Alta(); soloCodigo.Documento = null; soloCodigo.TipoDocumento = null;
            Assert.False(Tiene(await Validar(soloCodigo), "Documento"));
        }

        // ── Cargo ───────────────────────────────────────────────────────────

        [Fact]
        public async Task El_cargo_debe_estar_definido_y_activo_en_la_empresa()
        {
            var noDefinido = Alta(); noDefinido.Empleado!.Cargo = "AYUDANTE";
            Assert.True(Tiene(await Validar(noDefinido), "Empleado.Cargo", "definido"));

            using var db = _bd.Crear();
            db.Cargos.Add(new eGestion360Web.Models.Flota.Cargo
            {
                IdEmpresa = DatosBase.EmpresaB, Codigo = "RETIRADO", Nombre = "Retirado", Activo = false,
                CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var inactivo = Alta(); inactivo.Empleado!.Cargo = "RETIRADO";
            Assert.True(Tiene(await Servicio(db).ValidarAsync(inactivo), "Empleado.Cargo", "definido"));
        }

        [Fact]
        public async Task El_cargo_se_pasa_a_mayusculas()
        {
            var i = Alta(); i.Empleado!.Cargo = "mecanico";
            var r = await Validar(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("MECANICO", r.Datos.Empleado?.Cargo);
        }

        [Fact]
        public async Task Sin_cargo_es_error_en_el_alta_y_solo_aviso_en_la_edicion()
        {
            var alta = Alta(); alta.Empleado!.Cargo = null;
            Assert.True(Tiene(await Validar(alta), "Empleado.Cargo"));

            var edicion = Alta(); edicion.Empleado!.Cargo = null; edicion.Modo = ModoValidacionPersona.Edicion;
            var r = await Validar(edicion);
            Assert.False(Tiene(r, "Empleado.Cargo"), Detalle(r));
            Assert.Contains(r.Advertencias, a => a.Contains("cargo"));
        }

        // ── Fecha de nacimiento y edad ──────────────────────────────────────

        [Theory]
        [InlineData(-15, 0, false, false)]   // 15 años: menor de 16
        [InlineData(-16, 0, false, true)]    // justo 16 años
        [InlineData(-16, 1, false, false)]   // un día antes de cumplir 16
        [InlineData(-17, 0, true, false)]    // conductor de 17
        [InlineData(-18, 0, true, true)]     // conductor de 18
        public async Task La_edad_minima_es_16_y_18_para_conductores(int anios, int dias, bool conductor, bool valida)
        {
            var i = Alta();
            i.FechaNacimiento = Hoy.AddYears(anios).AddDays(dias);
            if (conductor)
            {
                i.Empleado!.Cargo = "CONDUCTOR";
                i.LicenciaTipo = "B"; i.LicenciaNumero = "L1"; i.LicenciaVencimiento = Hoy.AddYears(1);
            }

            var r = await Validar(i);
            Assert.Equal(valida, !Tiene(r, "FechaNacimiento"));
            if (!valida && conductor) Assert.True(Tiene(r, "FechaNacimiento", "conductor debe tener al menos 18"));
        }

        [Fact]
        public async Task La_fecha_de_nacimiento_no_puede_ser_futura_ni_anterior_a_1900()
        {
            var futura = Alta(); futura.FechaNacimiento = Hoy.AddDays(1);
            Assert.True(Tiene(await Validar(futura), "FechaNacimiento", "futura"));

            var antigua = Alta(); antigua.FechaNacimiento = new DateOnly(1899, 12, 31);
            Assert.True(Tiene(await Validar(antigua), "FechaNacimiento", "no es válida"));
        }

        [Fact]
        public async Task La_fecha_de_nacimiento_es_obligatoria_solo_en_el_alta_de_un_empleado()
        {
            var alta = Alta(); alta.FechaNacimiento = null;
            Assert.True(Tiene(await Validar(alta), "FechaNacimiento", "obligatoria"));

            var edicion = Alta(); edicion.FechaNacimiento = null; edicion.Modo = ModoValidacionPersona.Edicion;
            Assert.False(Tiene(await Validar(edicion), "FechaNacimiento"));

            var sinRol = new PersonaDatosInput { IdEmpresa = DatosBase.EmpresaB, PrimerNombre = "Ana", PrimerApellido = "Paz" };
            Assert.True((await Validar(sinRol)).Ok);
        }

        // ── Listas cerradas y catálogos ─────────────────────────────────────

        [Fact]
        public async Task Sexo_estado_civil_y_tipo_de_sangre_se_normalizan_o_se_rechazan()
        {
            var bueno = Alta(); bueno.Sexo = "f"; bueno.EstadoCivil = "UNION_LIBRE"; bueno.TipoSangre = "o+";
            var r = await Validar(bueno);
            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("F", r.Datos.Sexo);
            Assert.Equal("union_libre", r.Datos.EstadoCivil);
            Assert.Equal("O+", r.Datos.TipoSangre);

            var malo = Alta(); malo.Sexo = "x"; malo.EstadoCivil = "complicado"; malo.TipoSangre = "C+";
            var rm = await Validar(malo);
            Assert.True(Tiene(rm, "Sexo")); Assert.True(Tiene(rm, "EstadoCivil")); Assert.True(Tiene(rm, "TipoSangre"));
        }

        [Fact]
        public async Task La_nacionalidad_y_los_municipios_deben_existir()
        {
            using var db = _bd.Crear();
            var municipio = await db.CatalogoMunicipios.AsNoTracking().OrderBy(m => m.IdMunicipio).Select(m => m.IdMunicipio).FirstAsync();

            var i = Alta(); i.PaisNacionalidad = "hn"; i.IdMunicipioNacimiento = municipio; i.IdMunicipioResidencia = 99999;
            var r = await Servicio(db).ValidarAsync(i);

            Assert.Equal("HN", r.Datos.PaisNacionalidad);
            Assert.Equal(municipio, r.Datos.IdMunicipioNacimiento);
            Assert.True(Tiene(r, "IdMunicipioResidencia"));

            var mala = Alta(); mala.PaisNacionalidad = "xx";
            Assert.True(Tiene(await Servicio(db).ValidarAsync(mala), "PaisNacionalidad"));
        }

        // ── Contacto ────────────────────────────────────────────────────────

        [Fact]
        public async Task Los_telefonos_y_el_correo_se_normalizan()
        {
            var i = Alta(); i.Telefono = "+504 9876-5432"; i.TelefonoSecundario = "2222 3333"; i.Email = "  Luis.Perez@Empresa.COM ";
            var r = await Validar(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("98765432", r.Datos.Telefono);
            Assert.Equal("22223333", r.Datos.TelefonoSecundario);
            Assert.Equal("luis.perez@empresa.com", r.Datos.Email);
        }

        [Fact]
        public async Task Telefonos_y_correo_invalidos_se_rechazan_con_su_mensaje()
        {
            var i = Alta(); i.Telefono = "123"; i.TelefonoSecundario = "abc"; i.Email = "no-es-correo";
            var r = await Validar(i);

            Assert.True(Tiene(r, "Telefono", "8 dígitos"));
            Assert.True(Tiene(r, "TelefonoSecundario", "8 dígitos"));
            Assert.True(Tiene(r, "Email", "correo válido"));
        }

        [Fact]
        public async Task Un_correo_usado_por_otra_persona_de_la_empresa_solo_avisa()
        {
            using var db = _bd.Crear();
            var otra = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Gina", "Vega", codigoInterno: "GV1");
            otra.Email = "compartido@empresa.com";
            await db.SaveChangesAsync();

            var i = Alta(DatosBase.EmpresaB); i.Email = "Compartido@Empresa.com";
            var r = await Servicio(db).ValidarAsync(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Contains(r.Advertencias, a => a.Contains("correo"));

            // En otra empresa no se avisa: no se revela que existe.
            var enOtra = Alta(DatosBase.EmpresaA); enOtra.Email = "compartido@empresa.com";
            Assert.DoesNotContain((await Servicio(db).ValidarAsync(enOtra)).Advertencias, a => a.Contains("correo"));
        }

        [Fact]
        public async Task El_contacto_de_emergencia_pide_los_tres_datos_o_ninguno()
        {
            var incompleto = Alta(); incompleto.ContactoEmergenciaNombre = "María López";
            Assert.True(Tiene(await Validar(incompleto), "ContactoEmergenciaNombre", "Completa nombre, teléfono y parentesco"));

            var completo = Alta();
            completo.ContactoEmergenciaNombre = "MARÍA LÓPEZ"; completo.ContactoEmergenciaTelefono = "9999-0000"; completo.ContactoEmergenciaParentesco = "Madre";
            var r = await Validar(completo);
            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("María López", r.Datos.ContactoEmergenciaNombre);
            Assert.Equal("99990000", r.Datos.ContactoEmergenciaTelefono);
            Assert.Equal("madre", r.Datos.ContactoEmergenciaParentesco);

            var telefonoMalo = Alta();
            telefonoMalo.ContactoEmergenciaNombre = "María"; telefonoMalo.ContactoEmergenciaTelefono = "12"; telefonoMalo.ContactoEmergenciaParentesco = "Madre";
            Assert.True(Tiene(await Validar(telefonoMalo), "ContactoEmergenciaTelefono", "del contacto"));
        }

        // ── Licencia de conducir ────────────────────────────────────────────

        private static PersonaDatosInput Conductor(Action<PersonaDatosInput>? ajustar = null)
        {
            var i = Alta();
            i.Empleado!.Cargo = "CONDUCTOR";
            i.LicenciaTipo = "c"; i.LicenciaNumero = "l-123"; i.LicenciaVencimiento = Hoy.AddYears(1);
            ajustar?.Invoke(i);
            return i;
        }

        [Fact]
        public async Task Un_conductor_nuevo_necesita_licencia_completa_pero_en_edicion_solo_se_avisa()
        {
            var alta = Alta(); alta.Empleado!.Cargo = "CONDUCTOR";
            var r = await Validar(alta);
            Assert.True(Tiene(r, "LicenciaTipo")); Assert.True(Tiene(r, "LicenciaNumero")); Assert.True(Tiene(r, "LicenciaVencimiento"));

            var edicion = Alta(); edicion.Empleado!.Cargo = "CONDUCTOR"; edicion.Modo = ModoValidacionPersona.Edicion;
            var re = await Validar(edicion);
            Assert.False(Tiene(re, "LicenciaTipo"));
            Assert.Contains(re.Advertencias, a => a.Contains("licencia"));
        }

        [Fact]
        public async Task La_licencia_valida_se_normaliza()
        {
            var r = await Validar(Conductor());

            Assert.True(r.Ok, Detalle(r));
            Assert.Equal("C", r.Datos.LicenciaTipo);
            Assert.Equal("L-123", r.Datos.LicenciaNumero);
            Assert.Equal("CONDUCTOR", r.Datos.Empleado?.Cargo);
        }

        [Theory]
        [InlineData(10, "vence en 10 días")]
        [InlineData(30, "30 días")]
        public async Task Una_licencia_que_vence_en_30_dias_o_menos_avisa(int dias, string mensaje)
        {
            var r = await Validar(Conductor(i => i.LicenciaVencimiento = Hoy.AddDays(dias)));

            Assert.True(r.Ok, Detalle(r));
            Assert.Contains(r.Advertencias, a => a.Contains(mensaje));
        }

        [Fact]
        public async Task Una_licencia_que_vence_en_31_dias_no_avisa()
        {
            var r = await Validar(Conductor(i => i.LicenciaVencimiento = Hoy.AddDays(31)));
            Assert.True(r.Ok, Detalle(r));
            Assert.Empty(r.Advertencias);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task En_el_alta_una_licencia_vencida_o_que_vence_hoy_es_un_error(int dias)
        {
            var r = await Validar(Conductor(i => i.LicenciaVencimiento = Hoy.AddDays(dias)));
            Assert.True(Tiene(r, "LicenciaVencimiento"), Detalle(r));
        }

        [Fact]
        public async Task En_la_edicion_una_licencia_vencida_o_que_vence_hoy_solo_avisa()
        {
            var vencida = await Validar(Conductor(i => { i.Modo = ModoValidacionPersona.Edicion; i.LicenciaVencimiento = Hoy.AddDays(-5); }));
            Assert.False(Tiene(vencida, "LicenciaVencimiento"), Detalle(vencida));
            Assert.Contains(vencida.Advertencias, a => a.Contains("está vencida"));

            var hoy = await Validar(Conductor(i => { i.Modo = ModoValidacionPersona.Edicion; i.LicenciaVencimiento = Hoy; }));
            Assert.Contains(hoy.Advertencias, a => a.Contains("vence hoy"));
        }

        [Fact]
        public async Task La_categoria_y_el_numero_de_licencia_se_validan()
        {
            Assert.True(Tiene(await Validar(Conductor(i => i.LicenciaTipo = "Z9")), "LicenciaTipo", "no es válida"));
            Assert.True(Tiene(await Validar(Conductor(i => i.LicenciaNumero = "L 1!")), "LicenciaNumero"));
        }

        [Fact]
        public async Task La_licencia_parcial_de_un_no_conductor_pide_el_numero_pero_no_el_vencimiento()
        {
            var i = Alta(); i.LicenciaTipo = "B";
            var r = await Validar(i);

            Assert.True(Tiene(r, "LicenciaNumero"), Detalle(r));
            Assert.False(Tiene(r, "LicenciaVencimiento"), Detalle(r));
        }

        // ── Fechas laborales, tarifa y moneda ───────────────────────────────

        [Fact]
        public async Task La_baja_no_puede_ser_anterior_al_ingreso()
        {
            var antes = Alta(); antes.Empleado!.FechaIngreso = new DateOnly(2026, 5, 1); antes.Empleado.FechaBaja = new DateOnly(2026, 4, 30);
            Assert.True(Tiene(await Validar(antes), "Empleado.FechaBaja", "anterior al ingreso"));

            var mismoDia = Alta(); mismoDia.Empleado!.FechaIngreso = new DateOnly(2026, 5, 1); mismoDia.Empleado.FechaBaja = new DateOnly(2026, 5, 1);
            Assert.False(Tiene(await Validar(mismoDia), "Empleado.FechaBaja"));
        }

        [Fact]
        public async Task La_tarifa_no_puede_ser_negativa_ni_tener_mas_de_2_decimales()
        {
            var negativa = Alta(); negativa.Empleado!.TarifaDiaria = -1m;
            Assert.True(Tiene(await Validar(negativa), "Empleado.TarifaDiaria", "negativa"));

            var decimales = Alta(); decimales.Empleado!.TarifaDiaria = 350.555m;
            Assert.True(Tiene(await Validar(decimales), "Empleado.TarifaDiaria", "2 decimales"));
        }

        [Fact]
        public async Task Una_tarifa_sin_moneda_toma_la_moneda_por_defecto()
        {
            var i = Alta(); i.Empleado!.TarifaDiaria = 350m;
            var r = await Validar(i);

            Assert.True(r.Ok, Detalle(r));
            Assert.Equal(350m, r.Datos.Empleado?.TarifaDiaria);
            Assert.Equal("HNL", r.Datos.Empleado?.MonedaTarifa);
        }

        [Theory]
        [InlineData("usd", true)]
        [InlineData("ZZZ", false)]   // no existe
        [InlineData("ANG", false)]   // existe pero está inactiva
        public async Task La_moneda_debe_existir_y_estar_activa(string moneda, bool valida)
        {
            var i = Alta(); i.Empleado!.TarifaDiaria = 350m; i.Empleado.MonedaTarifa = moneda;
            var r = await Validar(i);

            Assert.Equal(valida, !Tiene(r, "Empleado.MonedaTarifa"));
            if (valida) Assert.Equal("USD", r.Datos.Empleado?.MonedaTarifa);
        }

        // ── Solo lectura ────────────────────────────────────────────────────

        [Fact]
        public async Task El_servicio_no_guarda_nada()
        {
            using var db = _bd.Crear();
            var antes = await db.Personas.CountAsync();

            await Servicio(db).ValidarAsync(Alta());

            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(antes, await db.Personas.CountAsync());
        }
    }
}
