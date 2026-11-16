using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>
    /// Constancias con el talonario del Colegio (folios preimpresos), resultado apto o no apto, registro de impresiones y redacción de
    /// la constancia de licencia de conducir (datos inventados).
    /// </summary>
    public class TalonarioEImpresionTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));
        private CatalogoPsicologiaService Catalogo(ApplicationDbContext db) => new(db, _reloj, NullLogger<CatalogoPsicologiaService>.Instance);
        private ConstanciaService Constancias(ApplicationDbContext db) => new(db, _reloj, NullLogger<ConstanciaService>.Instance);

        private static string Mensajes(IReadOnlyList<ErrorValidacion> errores) => string.Join(" | ", errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        private async Task<int> TipoLicencia(int? desde = 1374060, int? hasta = 1374084, string? plantilla = PlantillasConstancia.LicenciaConducir)
        {
            using var db = Contexto();
            var r = await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = DatosBase.EmpresaA, Codigo = "LICENCIA", Nombre = "Constancia para licencia de conducir", Plantilla = plantilla,
                FolioDesde = desde, FolioHasta = hasta, Usuario = "psi.ana"
            });
            Assert.True(r.Ok, Mensajes(r.Errores));
            return r.Valor;
        }

        private async Task<(int Paciente, int Prueba)> PacienteYPrueba()
        {
            using var db = Contexto();
            var rp = await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(new RegistrarPacienteInput
            {
                IdEmpresa = DatosBase.EmpresaA, Usuario = "psi.ana",
                Persona = new PersonaDatosInput { PrimerNombre = "Carlos", PrimerApellido = "Mejía", Documento = "0501-1998-00412", Sexo = "M" }
            });
            Assert.True(rp.Ok, Mensajes(rp.Errores));
            var prueba = await Catalogo(db).GuardarPruebaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = DatosBase.EmpresaA, Codigo = "PPM", Nombre = "Prueba Psicológica de Manejo", Usuario = "psi.ana"
            });
            return (rp.Paciente!.IdPaciente, prueba.Valor);
        }

        private async Task<ResultadoPsicologia<Constancia>> Emitir(int paciente, int tipo, int prueba, int? folio = null, string? resultado = ResultadosConstancia.Apto)
        {
            using var db = Contexto();
            return await Constancias(db).EmitirAsync(new EmitirConstanciaInput
            {
                IdEmpresa = DatosBase.EmpresaA, IdPaciente = paciente, IdTipoConstancia = tipo, IdsPruebas = new[] { prueba },
                Folio = folio, Resultado = resultado, Usuario = "psi.ana"
            });
        }

        // ── Catálogo ────────────────────────────────────────────────────────

        [Fact]
        public async Task El_tipo_guarda_su_formato_y_su_talonario_y_rechaza_datos_incompletos()
        {
            var id = await TipoLicencia();
            using (var db = Contexto())
            {
                var servicio = Catalogo(db);
                ElementoCatalogoInput Con(int? desde, int? hasta, string? plantilla = null) => new()
                {
                    IdEmpresa = DatosBase.EmpresaA, Id = id, Codigo = "LICENCIA", Nombre = "Licencia", Plantilla = plantilla, FolioDesde = desde, FolioHasta = hasta
                };
                Assert.Contains((await servicio.GuardarTipoConstanciaAsync(Con(1374060, null))).Errores, e => e.Campo == "FolioHasta");
                Assert.Contains((await servicio.GuardarTipoConstanciaAsync(Con(1374084, 1374060))).Errores, e => e.Campo == "FolioHasta");
                Assert.Contains((await servicio.GuardarTipoConstanciaAsync(Con(0, 10))).Errores, e => e.Campo == "FolioDesde");
                Assert.Contains((await servicio.GuardarTipoConstanciaAsync(Con(1, 5000))).Errores, e => e.Campo == "FolioHasta");
                Assert.Contains((await servicio.GuardarTipoConstanciaAsync(Con(null, null, "no_existe"))).Errores, e => e.Campo == "Plantilla");
            }

            using var leer = _bd.Crear();
            var t = await leer.TiposConstancia.SingleAsync();
            Assert.Equal(PlantillasConstancia.LicenciaConducir, t.Plantilla);
            Assert.Equal(1374060, t.FolioDesde);
            Assert.Equal(1374084, t.FolioHasta);
            Assert.True(t.UsaTalonario);
        }

        // ── Folios del talonario ────────────────────────────────────────────

        [Fact]
        public async Task Con_talonario_cada_constancia_toma_el_siguiente_folio_y_la_anulada_no_se_reutiliza()
        {
            var tipo = await TipoLicencia();
            var (paciente, prueba) = await PacienteYPrueba();

            var primera = await Emitir(paciente, tipo, prueba);
            Assert.True(primera.Ok, Mensajes(primera.Errores));
            Assert.Equal("1374060", primera.Valor!.NumeroConstancia);
            Assert.Equal(ResultadosConstancia.Apto, primera.Valor.Resultado);

            using (var db = Contexto())
                Assert.True((await Constancias(db).AnularAsync(DatosBase.EmpresaA, primera.Valor.IdConstancia, "Hoja manchada", "psi.ana")).Ok);
            var segunda = await Emitir(paciente, tipo, prueba, resultado: ResultadosConstancia.NoApto);
            Assert.Equal("1374061", segunda.Valor!.NumeroConstancia);

            using var db2 = Contexto();
            Assert.Equal("1374062", (await Constancias(db2).SiguientesNumerosAsync(DatosBase.EmpresaA, 2026))[tipo]);
        }

        [Fact]
        public async Task El_folio_escrito_tiene_que_ser_del_talonario_y_estar_libre()
        {
            var tipo = await TipoLicencia();
            var (paciente, prueba) = await PacienteYPrueba();

            Assert.Contains((await Emitir(paciente, tipo, prueba, folio: 1374100)).Errores, e => e.Campo == "Folio");
            var salto = await Emitir(paciente, tipo, prueba, folio: 1374070);   // la hoja 060 a 069 se dañó: se escribe el folio de la hoja
            Assert.True(salto.Ok, Mensajes(salto.Errores));
            Assert.Equal("1374070", salto.Valor!.NumeroConstancia);
            Assert.Contains("ya se usó", Assert.Single((await Emitir(paciente, tipo, prueba, folio: 1374070)).Errores).Mensaje);

            Assert.Equal("1374071", (await Emitir(paciente, tipo, prueba)).Valor!.NumeroConstancia);   // sigue después del mayor usado
        }

        [Fact]
        public async Task Con_el_talonario_agotado_no_se_emite_hasta_registrar_el_siguiente()
        {
            var tipo = await TipoLicencia(1374060, 1374061);
            var (paciente, prueba) = await PacienteYPrueba();
            Assert.True((await Emitir(paciente, tipo, prueba)).Ok);
            Assert.True((await Emitir(paciente, tipo, prueba)).Ok);

            var agotado = await Emitir(paciente, tipo, prueba);
            Assert.Contains("agotado", Assert.Single(agotado.Errores).Mensaje);
            using (var db = Contexto())
                Assert.Contains("agotado", (await Constancias(db).SiguientesNumerosAsync(DatosBase.EmpresaA, 2026))[tipo]);

            using (var db = Contexto())
                Assert.True((await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput
                {
                    IdEmpresa = DatosBase.EmpresaA, Id = tipo, Codigo = "LICENCIA", Nombre = "Licencia", Plantilla = PlantillasConstancia.LicenciaConducir,
                    FolioDesde = 1374085, FolioHasta = 1374109
                })).Ok);
            Assert.Equal("1374085", (await Emitir(paciente, tipo, prueba)).Valor!.NumeroConstancia);
        }

        [Fact]
        public async Task El_formato_de_licencia_pide_el_resultado_y_sin_talonario_se_usa_el_correlativo()
        {
            var tipo = await TipoLicencia(null, null);
            var (paciente, prueba) = await PacienteYPrueba();

            Assert.Contains((await Emitir(paciente, tipo, prueba, resultado: null)).Errores, e => e.Campo == "Resultado");
            Assert.Contains((await Emitir(paciente, tipo, prueba, resultado: "dudoso")).Errores, e => e.Campo == "Resultado");
            var r = await Emitir(paciente, tipo, prueba, folio: 99);   // sin talonario, el folio no cuenta
            Assert.True(r.Ok, Mensajes(r.Errores));
            Assert.Equal("LICENCIA-2026-00001", r.Valor!.NumeroConstancia);
        }

        // ── Impresión ───────────────────────────────────────────────────────

        [Fact]
        public async Task La_impresion_queda_registrada_solo_para_una_constancia_emitida_con_formato()
        {
            var tipo = await TipoLicencia();
            var (paciente, prueba) = await PacienteYPrueba();
            var c = (await Emitir(paciente, tipo, prueba)).Valor!;

            using (var db = Contexto())
            {
                var r = await Constancias(db).RegistrarImpresionAsync(DatosBase.EmpresaA, c.IdConstancia, "psi.ana");
                Assert.True(r.Ok, Mensajes(r.Errores));
                Assert.Equal(PlantillasConstancia.LicenciaConducir, r.Valor!.Plantilla);
                Assert.Equal(TamanosPapel.Carta, r.Valor.TamanoPapel);
            }
            using (var db = Contexto())
                Assert.True((await Constancias(db).AnularAsync(DatosBase.EmpresaA, c.IdConstancia, "Error", "psi.ana")).Ok);
            using (var db = Contexto())
                Assert.Contains("anulada", Assert.Single((await Constancias(db).RegistrarImpresionAsync(DatosBase.EmpresaA, c.IdConstancia, "psi.ana")).Errores).Mensaje);
            using (var db = Contexto())
                Assert.False((await Constancias(db).RegistrarImpresionAsync(DatosBase.EmpresaB, c.IdConstancia, "psi.ana")).Ok);   // otra empresa

            using var leer = _bd.Crear();
            var i = await leer.ImpresionesPsicologia.SingleAsync();
            Assert.Equal(c.IdConstancia, i.IdConstancia);
            Assert.Null(i.IdAtencion);
            Assert.Equal("psi.ana", i.CreadoPor);
        }

        // ── Redacción ───────────────────────────────────────────────────────

        [Fact]
        public void La_constancia_de_licencia_se_redacta_con_la_fecha_el_trato_el_DNI_la_conclusion_y_las_pruebas()
        {
            var pruebas = new[] { ("Prueba Psicológica de Manejo", "PPM") };
            var apto = RedaccionDeConstancias.LicenciaConducir(new DateOnly(2026, 11, 15), "Carlos Andrés Mejía Rivera", "M", "0501-1998-00412",
                ResultadosConstancia.Apto, pruebas);
            Assert.Equal("15", apto.Dia);
            Assert.Equal("noviembre", apto.Mes);
            Assert.Equal("26", apto.Anio);
            Assert.Equal("Por este medio se hace constar que el señor Carlos Andrés Mejía Rivera, con DNI 0501-1998-00412, posee personalidad estable, APTO para conducir.", apto.Cuerpo);
            Assert.Equal("Prueba Psicológica de Manejo (PPM)", apto.Pruebas);

            var noApto = RedaccionDeConstancias.LicenciaConducir(new DateOnly(2027, 1, 5), "Ana Torres", "F", null, ResultadosConstancia.NoApto, pruebas);
            Assert.Equal(("5", "enero", "27"), (noApto.Dia, noApto.Mes, noApto.Anio));
            Assert.StartsWith("Por este medio se hace constar que la señora Ana Torres, con DNI ", noApto.Cuerpo);
            Assert.EndsWith(", no reúne las condiciones psicológicas requeridas, NO APTO para conducir.", noApto.Cuerpo);
            Assert.Equal("el (la) señor(a)", RedaccionDeConstancias.Tratamiento(null));
        }
    }
}
