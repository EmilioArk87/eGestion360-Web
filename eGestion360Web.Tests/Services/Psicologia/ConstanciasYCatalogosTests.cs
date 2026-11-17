using System.Globalization;
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
    /// <summary>Catálogo de pruebas y tipos de constancia, pruebas aplicadas en la atención y constancias con su correlativo (datos inventados).</summary>
    public class ConstanciasYCatalogosTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        // Después de la jornada de prueba (15-nov-2026), para poder emitir con la fecha de hoy.
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private CatalogoPsicologiaService Catalogo(ApplicationDbContext db) => new(db, _reloj, NullLogger<CatalogoPsicologiaService>.Instance);
        private ConstanciaService Constancias(ApplicationDbContext db) => new(db, _reloj, NullLogger<ConstanciaService>.Instance);
        private AtencionService Atenciones(ApplicationDbContext db) => new(db, _reloj);

        /// <summary>Solo las pruebas aplicadas, sin resultados, conclusión ni observaciones.</summary>
        private static EvaluacionInput SoloPruebas(params int[] ids) => new() { Pruebas = ids.Select(id => new PruebaAplicadaInput(id, null)).ToList() };

        private async Task<int> NuevaPrueba(string codigo, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var r = await Catalogo(db).GuardarPruebaAsync(new ElementoCatalogoInput { IdEmpresa = empresa, Codigo = codigo, Nombre = "Prueba " + codigo, Usuario = "psi.ana" });
            Assert.True(r.Ok, Mensajes(r.Errores));
            return r.Valor;
        }

        private async Task<int> NuevoTipo(string codigo, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var r = await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = empresa, Codigo = codigo, Nombre = "Constancia " + codigo, Usuario = "psi.ana" });
            Assert.True(r.Ok, Mensajes(r.Errores));
            return r.Valor;
        }

        /// <summary>Un paciente registrado desde una jornada (con su atención en borrador). Devuelve el paciente y su atención.</summary>
        private async Task<(int Paciente, int Atencion)> NuevoPaciente(string dni, int empresa = DatosBase.EmpresaA)
        {
            var jornada = PacienteServiceTests.NuevaJornada(_bd, empresa, "Lic. Responsable de Prueba");
            int idPaciente;
            using (var db = Contexto())
            {
                var r = await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(new RegistrarPacienteInput
                {
                    IdEmpresa = empresa, IdJornada = jornada, EdadDeclarada = 30, Usuario = "psi.ana",
                    Persona = new PersonaDatosInput { PrimerNombre = "Carlos", PrimerApellido = "Mejía", Documento = dni, Sexo = "M" }
                });
                Assert.True(r.Ok, Mensajes(r.Errores));
                idPaciente = r.Paciente!.IdPaciente;
            }
            using var leer = _bd.Crear();
            return (idPaciente, await leer.AtencionesPsicologia.Where(a => a.IdPaciente == idPaciente).Select(a => a.IdAtencion).SingleAsync());
        }

        private async Task<ResultadoPsicologia<Constancia>> Emitir(int paciente, int tipo, int[] pruebas, int? atencion = null,
            int empresa = DatosBase.EmpresaA, DateOnly? fecha = null)
        {
            using var db = Contexto();
            return await Constancias(db).EmitirAsync(new EmitirConstanciaInput
            {
                IdEmpresa = empresa, IdPaciente = paciente, IdTipoConstancia = tipo, IdAtencion = atencion, IdsPruebas = pruebas,
                FechaEmision = fecha, Usuario = "psi.ana"
            });
        }

        private static string Mensajes(IReadOnlyList<ErrorValidacion> errores) => string.Join(" | ", errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        /// <summary>Le pone fecha de fin a la brigada de la atención (la de prueba es de un solo día, el 15-nov).</summary>
        private async Task BrigadaHasta(int atencion, DateOnly fin)
        {
            using var db = _bd.Crear();
            var idJornada = await db.AtencionesPsicologia.Where(a => a.IdAtencion == atencion).Select(a => a.IdJornada).SingleAsync();
            var jornada = await db.JornadasPsicologia.SingleAsync(j => j.IdJornada == idJornada);
            jornada.FechaFin = fin;
            await db.SaveChangesAsync();
        }

        private async Task<ResultadoPsicologia<Constancia>> CorregirFecha(int idConstancia, DateOnly? fecha, int empresa = DatosBase.EmpresaA,
            TimeProvider? reloj = null)
        {
            using var db = Contexto();
            return await new ConstanciaService(db, reloj ?? _reloj, NullLogger<ConstanciaService>.Instance)
                .CorregirFechaEmisionAsync(empresa, idConstancia, fecha, "psi.jefa");
        }

        // ── Catálogos ───────────────────────────────────────────────────────

        [Fact]
        public async Task El_catalogo_guarda_el_codigo_normalizado_y_no_lo_repite_en_la_empresa()
        {
            var id = await NuevaPrueba("mmpi 2");

            using (var db = Contexto())
            {
                var servicio = Catalogo(db);
                var repetida = await servicio.GuardarPruebaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "MMPI-2", Nombre = "Otra" });
                Assert.Contains(repetida.Errores, e => e.Campo == "Codigo");

                var invalida = await servicio.GuardarPruebaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "M!", Nombre = "X" });
                Assert.Contains(invalida.Errores, e => e.Campo == "Codigo");

                var sinNombre = await servicio.GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "ARMAS" });
                Assert.Contains(sinNombre.Errores, e => e.Campo == "Nombre");

                var editada = await servicio.GuardarPruebaAsync(new ElementoCatalogoInput
                {
                    IdEmpresa = DatosBase.EmpresaA, Id = id, Codigo = "MMPI-2", Nombre = "Inventario Multifásico de Personalidad de Minnesota-2", Descripcion = "Personalidad"
                });
                Assert.True(editada.Ok, Mensajes(editada.Errores));
            }
            await NuevaPrueba("MMPI-2", DatosBase.EmpresaB);   // otra empresa puede usar el mismo código

            using var leer = _bd.CrearComo(null, plataforma: true);
            var deA = await leer.PruebasPsicologicas.SingleAsync(p => p.IdEmpresa == DatosBase.EmpresaA);
            Assert.Equal("MMPI-2", deA.Codigo);
            Assert.Equal("Personalidad", deA.Descripcion);
            Assert.Equal(DatosBase.TenantA, deA.IdTenant);
            Assert.Equal(2, await leer.PruebasPsicologicas.CountAsync());
        }

        [Theory]
        [InlineData("mmpi 2", "MMPI-2")]
        [InlineData("  Ráven ", "RAVEN")]
        [InlineData("16pf", "16PF")]
        [InlineData(null, "")]
        public void El_codigo_va_en_mayusculas_sin_tildes_ni_espacios(string? escrito, string esperado) =>
            Assert.Equal(esperado, CatalogoPsicologiaService.NormalizarCodigo(escrito));

        // ── Pruebas aplicadas en la atención ─────────────────────────────────

        [Fact]
        public async Task Las_pruebas_de_la_atencion_se_agregan_y_se_quitan()
        {
            var (_, atencion) = await NuevoPaciente("0501-1998-00412");
            var raven = await NuevaPrueba("RAVEN");
            var mmpi = await NuevaPrueba("MMPI-2");

            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(raven, mmpi), "psi.ana")).Ok);
            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(mmpi), "psi.ana")).Ok);

            using var leer = _bd.Crear();
            Assert.Equal(new[] { mmpi }, await leer.AtencionPruebas.Where(a => a.IdAtencion == atencion).Select(a => a.IdPrueba).ToListAsync());
        }

        [Fact]
        public async Task No_acepta_pruebas_de_otra_empresa_ni_inactivas_ni_cambia_una_atencion_cerrada()
        {
            var (_, atencion) = await NuevoPaciente("0501-1998-00412");
            var deOtra = await NuevaPrueba("RAVEN", DatosBase.EmpresaB);
            var vieja = await NuevaPrueba("BENDER");
            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(vieja), "psi.ana")).Ok);
            using (var db = Contexto())
                Assert.True((await Catalogo(db).CambiarEstadoPruebaAsync(DatosBase.EmpresaA, vieja, false, "psi.ana")).Ok);

            using (var db = Contexto())
            {
                var servicio = Atenciones(db);
                Assert.Contains((await servicio.GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(vieja, deOtra), "psi.ana")).Errores, e => e.Campo == "Pruebas");
                // la que se desactivó después puede quedarse en la atención
                Assert.True((await servicio.GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(vieja), "psi.ana")).Ok);
            }

            using (var db = _bd.Crear())
            {
                var a = await db.AtencionesPsicologia.SingleAsync(x => x.IdAtencion == atencion);
                a.Estado = EstadosAtencion.Cerrada;
                a.FechaCierre = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
                a.CerradoPor = "psi.ana";
                await db.SaveChangesAsync();
            }
            using (var db = Contexto())
            {
                var cerrada = await Atenciones(db).GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(), "psi.ana");
                Assert.Equal(AtencionService.MensajeNoEditable, Assert.Single(cerrada.Errores).Mensaje);
            }
            using var leer = _bd.Crear();
            Assert.Equal(new[] { vieja }, await leer.AtencionPruebas.Where(a => a.IdAtencion == atencion).Select(a => a.IdPrueba).ToListAsync());
        }

        // ── Constancias ─────────────────────────────────────────────────────

        [Fact]
        public async Task Emite_la_constancia_con_su_tipo_pruebas_fecha_y_el_primer_correlativo()
        {
            var (paciente, atencion) = await NuevoPaciente("0501-1998-00412");
            var tipo = await NuevoTipo("ARMAS");
            var raven = await NuevaPrueba("RAVEN");
            var mmpi = await NuevaPrueba("MMPI-2");

            var r = await Emitir(paciente, tipo, new[] { raven, mmpi }, atencion);
            Assert.True(r.Ok, Mensajes(r.Errores));
            Assert.Equal("ARMAS-2026-00001", r.Valor!.NumeroConstancia);

            using var leer = _bd.Crear();
            var c = await leer.Constancias.Include(x => x.Pruebas).SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 15), c.FechaEmision);            // sin fecha, el día de la brigada de un día de la evaluación
            Assert.Equal(EstadosConstancia.Emitida, c.Estado);
            Assert.Equal(tipo, c.IdTipoConstancia);
            Assert.Equal(atencion, c.IdAtencion);
            Assert.Equal("Lic. Responsable de Prueba", c.NombreProfesional);       // el de la atención
            Assert.Equal(DatosBase.TenantA, c.IdTenant);
            Assert.Equal(new[] { mmpi, raven }.OrderBy(x => x), c.Pruebas.Select(p => p.IdPrueba).OrderBy(x => x));
        }

        [Fact]
        public async Task Cada_tipo_lleva_su_correlativo_por_empresa_y_no_reutiliza_los_anulados()
        {
            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            var (pacienteB, _) = await NuevoPaciente("0501-1992-02290", DatosBase.EmpresaB);
            var armas = await NuevoTipo("ARMAS");
            var empleo = await NuevoTipo("EMPLEO");
            var prueba = await NuevaPrueba("RAVEN");
            var tipoB = await NuevoTipo("ARMAS", DatosBase.EmpresaB);
            var pruebaB = await NuevaPrueba("RAVEN", DatosBase.EmpresaB);

            var primera = await Emitir(paciente, armas, new[] { prueba });
            using (var db = Contexto())
                Assert.True((await Constancias(db).AnularAsync(DatosBase.EmpresaA, primera.Valor!.IdConstancia, "Error en el tipo", "psi.ana")).Ok);
            var segunda = await Emitir(paciente, armas, new[] { prueba });
            var deEmpleo = await Emitir(paciente, empleo, new[] { prueba });
            var deB = await Emitir(pacienteB, tipoB, new[] { pruebaB }, empresa: DatosBase.EmpresaB);

            Assert.Equal("ARMAS-2026-00001", primera.Valor.NumeroConstancia);
            Assert.Equal("ARMAS-2026-00002", segunda.Valor!.NumeroConstancia);    // la anulada no se reutiliza
            Assert.Equal("EMPLEO-2026-00001", deEmpleo.Valor!.NumeroConstancia);  // otro tipo empieza en 1
            Assert.Equal("ARMAS-2026-00001", deB.Valor!.NumeroConstancia);        // otra empresa empieza en 1
            using var db2 = Contexto();
            var siguientes = await Constancias(db2).SiguientesNumerosAsync(DatosBase.EmpresaA, 2026);
            Assert.Equal("ARMAS-2026-00003", siguientes[armas]);
            Assert.Equal("EMPLEO-2026-00002", siguientes[empleo]);
            Assert.Equal(2, siguientes.Count);
        }

        [Fact]
        public async Task El_codigo_del_tipo_es_corto_y_no_cambia_cuando_ya_tiene_constancias()
        {
            using (var db = Contexto())
            {
                var largo = await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "PORTACIONDEARMAS", Nombre = "Armas" });
                Assert.Contains(largo.Errores, e => e.Campo == "Codigo" && e.Mensaje.Contains("10", StringComparison.Ordinal));
            }

            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            var tipo = await NuevoTipo("ARMAS");
            using (var db = Contexto())   // sin constancias todavía, el código se puede corregir
                Assert.True((await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Id = tipo, Codigo = "ARMA", Nombre = "Armas" })).Ok);
            Assert.True((await Emitir(paciente, tipo, new[] { await NuevaPrueba("RAVEN") })).Ok);

            using (var db = Contexto())
            {
                var servicio = Catalogo(db);
                var cambio = await servicio.GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Id = tipo, Codigo = "ARMAS", Nombre = "Armas" });
                Assert.Contains("no se puede cambiar", Assert.Single(cambio.Errores).Mensaje);
                var nombre = await servicio.GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Id = tipo, Codigo = "ARMA", Nombre = "Constancia para portación de armas" });
                Assert.True(nombre.Ok, Mensajes(nombre.Errores));
            }

            using var leer = _bd.Crear();
            Assert.Equal("ARMA-2026-00001", (await leer.Constancias.SingleAsync()).NumeroConstancia);
            Assert.Equal("ARMA", (await leer.TiposConstancia.SingleAsync()).Codigo);
        }

        [Fact]
        public async Task Rechaza_sin_pruebas_con_datos_de_otra_empresa_atencion_de_otro_paciente_y_fecha_futura()
        {
            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            var (_, atencionDeOtro) = await NuevoPaciente("0501-1992-02290");
            var tipo = await NuevoTipo("ARMAS");
            var prueba = await NuevaPrueba("RAVEN");
            var tipoB = await NuevoTipo("EMPLEO", DatosBase.EmpresaB);
            var pruebaB = await NuevaPrueba("BENDER", DatosBase.EmpresaB);

            Assert.Contains((await Emitir(paciente, tipo, Array.Empty<int>())).Errores, e => e.Campo == "Pruebas");
            Assert.Contains((await Emitir(paciente, tipoB, new[] { prueba })).Errores, e => e.Campo == "IdTipoConstancia");
            Assert.Contains((await Emitir(paciente, tipo, new[] { pruebaB })).Errores, e => e.Campo == "Pruebas");
            Assert.Contains((await Emitir(paciente, tipo, new[] { prueba }, atencionDeOtro)).Errores, e => e.Campo == "IdAtencion");
            Assert.Contains((await Emitir(paciente, tipo, new[] { prueba }, fecha: new DateOnly(2026, 11, 21))).Errores, e => e.Campo == "FechaEmision");
            Assert.Contains((await Emitir(paciente, tipo, new[] { prueba }, fecha: new DateOnly(2026, 11, 14))).Errores, e => e.Campo == "FechaEmision");
            Assert.False((await Emitir(paciente, tipo, new[] { prueba }, empresa: DatosBase.EmpresaB)).Ok);   // el paciente es de otra empresa

            using var leer = _bd.CrearComo(null, plataforma: true);
            Assert.Equal(0, await leer.Constancias.CountAsync());
        }

        [Fact]
        public async Task Anular_exige_motivo_no_se_hace_dos_veces_y_queda_en_la_bitacora()
        {
            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            var emitida = await Emitir(paciente, await NuevoTipo("ARMAS"), new[] { await NuevaPrueba("RAVEN") });
            var id = emitida.Valor!.IdConstancia;

            using (var db = Contexto())
            {
                var servicio = Constancias(db);
                Assert.Contains((await servicio.AnularAsync(DatosBase.EmpresaA, id, "   ", "psi.ana")).Errores, e => e.Campo == "Motivo");
                Assert.False((await servicio.AnularAsync(DatosBase.EmpresaB, id, "Otra empresa", "psi.ana")).Ok);
                Assert.True((await servicio.AnularAsync(DatosBase.EmpresaA, id, "Se emitió con el tipo equivocado", "psi.ana")).Ok);
            }
            using (var db = Contexto())
                Assert.Contains("ya estaba anulada", Assert.Single((await Constancias(db).AnularAsync(DatosBase.EmpresaA, id, "Otra vez", "psi.ana")).Errores).Mensaje);

            using var leer = _bd.Crear();
            var c = await leer.Constancias.SingleAsync();
            Assert.Equal(EstadosConstancia.Anulada, c.Estado);
            Assert.Equal("Se emitió con el tipo equivocado", c.MotivoAnulacion);
            Assert.Equal("psi.ana", c.AnuladaPor);
            Assert.NotNull(c.FechaAnulacion);

            var bitacora = await leer.BitacoraCambios.Where(b => b.Entidad == "constancias").ToListAsync();
            Assert.Contains(bitacora, b => b.Operacion == "INSERT" && b.IdPersona != null);
            Assert.Contains(bitacora, b => b.Campo == "estado" && b.ValorNuevo == EstadosConstancia.Anulada);
        }

        // ── Fecha de emisión: dentro de la brigada, corregible y con la fecha de creación intocable (pedido del 2026-11-16) ──

        [Theory]
        // hoy, apertura, evaluación, inicio y fin de la brigada -> primera y última fecha posibles
        [InlineData("2026-11-20", "2026-11-15", null, null, null, "2026-11-15", "2026-11-20")]                         // sin evaluación
        [InlineData("2026-11-20", "2026-11-10", "2026-11-12", null, null, "2026-11-12", "2026-11-20")]                 // consulta sin brigada
        [InlineData("2026-11-20", "2026-11-15", "2026-11-15", "2026-11-15", null, "2026-11-15", "2026-11-15")]         // brigada de un día
        [InlineData("2026-11-20", "2026-11-15", "2026-11-15", "2026-11-15", "2026-11-17", "2026-11-15", "2026-11-17")] // brigada de tres días
        [InlineData("2026-11-20", "2026-11-15", "2026-11-16", "2026-11-15", "2026-11-17", "2026-11-16", "2026-11-17")] // evaluado el segundo día
        [InlineData("2026-11-16", "2026-11-15", "2026-11-15", "2026-11-15", "2026-11-18", "2026-11-15", "2026-11-16")] // brigada en curso: hasta hoy
        public void El_rango_de_la_fecha_de_emision_sigue_la_apertura_la_evaluacion_y_la_brigada(string hoy, string apertura, string? evaluacion,
            string? inicio, string? fin, string desde, string hasta)
        {
            static DateOnly? F(string? s) => s == null ? null : DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var r = RangoFechaEmision.Calcular(F(hoy)!.Value, F(apertura)!.Value, F(evaluacion), F(inicio), F(fin));

            Assert.Equal(F(desde), r.Desde);
            Assert.Equal(F(hasta), r.Hasta);
            Assert.Null(r.Error(r.Desde));
            Assert.Null(r.Error(r.Hasta));
            Assert.NotNull(r.Error(r.Desde.AddDays(-1)));
            Assert.NotNull(r.Error(r.Hasta.AddDays(1)));
        }

        [Fact]
        public async Task Al_emitir_desde_una_brigada_la_fecha_va_dentro_de_sus_dias()
        {
            var (paciente, atencion) = await NuevoPaciente("0501-1998-00412");   // brigada de un solo día: 15-nov
            var tipo = await NuevoTipo("ARMAS");
            var prueba = await NuevaPrueba("RAVEN");

            var fuera = await Emitir(paciente, tipo, new[] { prueba }, atencion, fecha: new DateOnly(2026, 11, 16));
            Assert.Contains("15/11/2026", Assert.Single(fuera.Errores, e => e.Campo == "FechaEmision").Mensaje);
            var dentro = await Emitir(paciente, tipo, new[] { prueba }, atencion, fecha: new DateOnly(2026, 11, 15));
            Assert.True(dentro.Ok, Mensajes(dentro.Errores));
            // Sin la evaluación, la brigada no cuenta: va de la apertura del expediente a hoy.
            var sinEvaluacion = await Emitir(paciente, tipo, new[] { prueba }, fecha: new DateOnly(2026, 11, 18));
            Assert.True(sinEvaluacion.Ok, Mensajes(sinEvaluacion.Errores));
        }

        [Fact]
        public async Task Corregir_la_fecha_respeta_la_brigada_queda_en_la_bitacora_y_no_toca_la_creacion()
        {
            var (paciente, atencion) = await NuevoPaciente("0501-1998-00412");
            await BrigadaHasta(atencion, new DateOnly(2026, 11, 17));                       // la brigada duró del 15 al 17
            var emitida = await Emitir(paciente, await NuevoTipo("ARMAS"), new[] { await NuevaPrueba("RAVEN") }, atencion, fecha: new DateOnly(2026, 11, 15));
            Assert.True(emitida.Ok, Mensajes(emitida.Errores));
            var id = emitida.Valor!.IdConstancia;
            DateTime registrada;
            using (var antes = _bd.Crear()) registrada = (await antes.Constancias.SingleAsync()).FechaCreacion;

            Assert.Contains("del 15/11/2026 al 17/11/2026", Assert.Single((await CorregirFecha(id, new DateOnly(2026, 11, 18))).Errores).Mensaje);
            Assert.Contains("del 15/11/2026 al 17/11/2026", Assert.Single((await CorregirFecha(id, new DateOnly(2026, 11, 14))).Errores).Mensaje);
            Assert.Contains("futura", Assert.Single((await CorregirFecha(id, new DateOnly(2026, 11, 21))).Errores).Mensaje);
            Assert.Contains("ya tiene", Assert.Single((await CorregirFecha(id, new DateOnly(2026, 11, 15))).Errores).Mensaje);
            Assert.Equal("FechaEmision", Assert.Single((await CorregirFecha(id, null)).Errores).Campo);
            Assert.False((await CorregirFecha(id, new DateOnly(2026, 11, 17), DatosBase.EmpresaB)).Ok);   // de otra empresa

            var corregida = await CorregirFecha(id, new DateOnly(2026, 11, 17));
            Assert.True(corregida.Ok, Mensajes(corregida.Errores));

            using var leer = _bd.Crear();
            var c = await leer.Constancias.SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 17), c.FechaEmision);
            Assert.Equal(registrada, c.FechaCreacion);        // cuándo se registró no cambia
            Assert.Equal("psi.ana", c.CreadoPor);             // ni quién
            Assert.Equal("psi.jefa", c.ModificadoPor);
            Assert.Equal("ARMAS-2026-00001", c.NumeroConstancia);
            var cambio = Assert.Single(await leer.BitacoraCambios.Where(b => b.Entidad == "constancias" && b.Campo == "fecha_emision").ToListAsync());
            Assert.Equal("2026-11-15", cambio.ValorAnterior);
            Assert.Equal("2026-11-17", cambio.ValorNuevo);
        }

        [Fact]
        public async Task Una_constancia_anulada_no_se_corrige_y_el_correlativo_no_cambia_de_anio()
        {
            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            var tipo = await NuevoTipo("ARMAS");
            var prueba = await NuevaPrueba("RAVEN");
            var vigente = (await Emitir(paciente, tipo, new[] { prueba }, fecha: new DateOnly(2026, 11, 20))).Valor!;   // ARMAS-2026-00001
            var anulada = (await Emitir(paciente, tipo, new[] { prueba }, fecha: new DateOnly(2026, 11, 20))).Valor!;
            using (var db = Contexto())
                Assert.True((await Constancias(db).AnularAsync(DatosBase.EmpresaA, anulada.IdConstancia, "Se emitió con el tipo equivocado", "psi.ana")).Ok);

            // En enero de 2027 ya se podría elegir una fecha de 2027, pero el número es de 2026.
            var enero = new RelojFijo(new DateTimeOffset(2027, 1, 5, 18, 0, 0, TimeSpan.Zero));
            Assert.Contains("2026", Assert.Single((await CorregirFecha(vigente.IdConstancia, new DateOnly(2027, 1, 2), reloj: enero)).Errores).Mensaje);
            Assert.True((await CorregirFecha(vigente.IdConstancia, new DateOnly(2026, 12, 30), reloj: enero)).Ok);
            Assert.Contains("anulada", Assert.Single((await CorregirFecha(anulada.IdConstancia, new DateOnly(2026, 11, 19))).Errores).Mensaje);
        }

        [Fact]
        public async Task La_fecha_y_el_usuario_de_creacion_no_se_pueden_cambiar_ni_llegan_desde_el_formulario()
        {
            var (paciente, _) = await NuevoPaciente("0501-1998-00412");
            Assert.True((await Emitir(paciente, await NuevoTipo("ARMAS"), new[] { await NuevaPrueba("RAVEN") })).Ok);

            using (var db = Contexto())
            {
                var c = await db.Constancias.SingleAsync();
                c.FechaCreacion = c.FechaCreacion.AddDays(-30);
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }
            using (var db = Contexto())
            {
                var c = await db.Constancias.SingleAsync();
                c.CreadoPor = "otra.persona";
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }
            using (var leer = _bd.Crear())
                Assert.Equal("psi.ana", (await leer.Constancias.SingleAsync()).CreadoPor);

            // Ni el formulario de Emitir ni lo que recibe el servicio tienen dónde poner cuándo o quién la registró.
            foreach (var tipo in new[] { typeof(EmitirConstanciaInput), typeof(eGestion360Web.Pages.Psicologia.Constancias.EmitirModel.FormularioConstancia) })
                Assert.DoesNotContain(tipo.GetProperties(), p => p.Name is "FechaCreacion" or "CreadoPor");
        }

        [Fact]
        public async Task Una_empresa_de_otra_cuenta_no_ve_constancias_ni_catalogos_ni_pruebas_aplicadas()
        {
            var (paciente, atencion) = await NuevoPaciente("0501-1998-00412");
            var prueba = await NuevaPrueba("RAVEN");
            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarEvaluacionAsync(DatosBase.EmpresaA, atencion, SoloPruebas(prueba), "psi.ana")).Ok);
            Assert.True((await Emitir(paciente, await NuevoTipo("ARMAS"), new[] { prueba })).Ok);

            using var otraCuenta = _bd.CrearComo(DatosBase.TenantB);
            Assert.Equal(0, await otraCuenta.Constancias.CountAsync());
            Assert.Equal(0, await otraCuenta.ConstanciaPruebas.CountAsync());
            Assert.Equal(0, await otraCuenta.PruebasPsicologicas.CountAsync());
            Assert.Equal(0, await otraCuenta.TiposConstancia.CountAsync());
            Assert.Equal(0, await otraCuenta.AtencionPruebas.CountAsync());

            using var mismaCuenta = _bd.CrearComo(DatosBase.TenantA);
            Assert.Equal(1, await mismaCuenta.Constancias.CountAsync());
            Assert.Equal(1, await mismaCuenta.AtencionPruebas.CountAsync());
        }
    }
}
