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
                Assert.True((await Atenciones(db).GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { raven, mmpi }, "psi.ana")).Ok);
            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { mmpi }, "psi.ana")).Ok);

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
                Assert.True((await Atenciones(db).GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { vieja }, "psi.ana")).Ok);
            using (var db = Contexto())
                Assert.True((await Catalogo(db).CambiarEstadoPruebaAsync(DatosBase.EmpresaA, vieja, false, "psi.ana")).Ok);

            using (var db = Contexto())
            {
                var servicio = Atenciones(db);
                Assert.Contains((await servicio.GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { vieja, deOtra }, "psi.ana")).Errores, e => e.Campo == "Pruebas");
                // la que se desactivó después puede quedarse en la atención
                Assert.True((await servicio.GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { vieja }, "psi.ana")).Ok);
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
                var cerrada = await Atenciones(db).GuardarPruebasAsync(DatosBase.EmpresaA, atencion, Array.Empty<int>(), "psi.ana");
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
            Assert.Equal(new DateOnly(2026, 11, 20), c.FechaEmision);            // hoy en Honduras
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

        [Fact]
        public async Task Una_empresa_de_otra_cuenta_no_ve_constancias_ni_catalogos_ni_pruebas_aplicadas()
        {
            var (paciente, atencion) = await NuevoPaciente("0501-1998-00412");
            var prueba = await NuevaPrueba("RAVEN");
            using (var db = Contexto())
                Assert.True((await Atenciones(db).GuardarPruebasAsync(DatosBase.EmpresaA, atencion, new[] { prueba }, "psi.ana")).Ok);
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
