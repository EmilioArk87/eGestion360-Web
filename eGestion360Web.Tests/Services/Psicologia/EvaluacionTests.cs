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
    /// La evaluación con su contenido mínimo para el examen de licencia (decisión D17): pruebas con su resultado, conclusión apto o no
    /// apto y observaciones; cerrar, anular, la constancia que sale de ella y el aislamiento entre empresas (datos inventados).
    /// </summary>
    public class EvaluacionTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));
        private CatalogoPsicologiaService Catalogo(ApplicationDbContext db) => new(db, _reloj, NullLogger<CatalogoPsicologiaService>.Instance);
        private ConstanciaService Constancias(ApplicationDbContext db) => new(db, _reloj, NullLogger<ConstanciaService>.Instance);
        private AtencionService Atenciones(ApplicationDbContext db) => new(db, _reloj);

        private static string Mensajes(IReadOnlyList<ErrorValidacion> errores) => string.Join(" | ", errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        private async Task<int> NuevaPrueba(string codigo, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var r = await Catalogo(db).GuardarPruebaAsync(new ElementoCatalogoInput { IdEmpresa = empresa, Codigo = codigo, Nombre = "Prueba " + codigo, Usuario = "psi.ana" });
            Assert.True(r.Ok, Mensajes(r.Errores));
            return r.Valor;
        }

        /// <summary>Un paciente registrado desde una jornada, con su atención en borrador.</summary>
        private async Task<(int Paciente, int Atencion)> PacienteConAtencion(int empresa = DatosBase.EmpresaA)
        {
            var jornada = PacienteServiceTests.NuevaJornada(_bd, empresa, "Lic. Responsable de Prueba");
            int idPaciente;
            using (var db = Contexto())
            {
                var r = await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(new RegistrarPacienteInput
                {
                    IdEmpresa = empresa, IdJornada = jornada, EdadDeclarada = 31, Usuario = "psi.ana",
                    Persona = new PersonaDatosInput { PrimerNombre = "Marta", PrimerApellido = "Zelaya", Documento = "0801-1995-99902", Sexo = "F" }
                });
                Assert.True(r.Ok, Mensajes(r.Errores));
                idPaciente = r.Paciente!.IdPaciente;
            }
            using var leer = _bd.Crear();
            return (idPaciente, await leer.AtencionesPsicologia.Where(a => a.IdPaciente == idPaciente).Select(a => a.IdAtencion).SingleAsync());
        }

        private async Task<ResultadoPsicologia<Atencion>> Guardar(int atencion, EvaluacionInput input, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            return await Atenciones(db).GuardarEvaluacionAsync(empresa, atencion, input, "psi.ana");
        }

        private async Task<ResultadoPsicologia<Atencion>> Cerrar(int atencion, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            return await Atenciones(db).CerrarAsync(empresa, atencion, "psi.ana");
        }

        private async Task<ResultadoPsicologia<Atencion>> Anular(int atencion, string? motivo, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            return await Atenciones(db).AnularAsync(empresa, atencion, motivo, "psi.ana");
        }

        private async Task<Atencion> Leer(int atencion)
        {
            using var db = _bd.Crear();
            return await db.AtencionesPsicologia.AsNoTracking().Include(a => a.Pruebas).SingleAsync(a => a.IdAtencion == atencion);
        }

        // ── Guardar ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Guarda_las_pruebas_con_su_resultado_la_conclusion_y_las_observaciones_y_los_cambia_despues()
        {
            var (_, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            var raven = await NuevaPrueba("RAVEN");

            var r = await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "  42 puntos, dentro de lo normal "), new PruebaAplicadaInput(raven, null), new PruebaAplicadaInput(ppm, "repetida") },
                Conclusion = " APTO ",
                Observaciones = "  Atenta y colaboradora.  "
            });
            Assert.True(r.Ok, Mensajes(r.Errores));

            var guardada = await Leer(atencion);
            Assert.Equal(ResultadosConstancia.Apto, guardada.Conclusion);
            Assert.Equal("Atenta y colaboradora.", guardada.Observaciones);
            Assert.Equal("psi.ana", guardada.ModificadoPor);
            Assert.Equal("42 puntos, dentro de lo normal", guardada.Pruebas.Single(p => p.IdPrueba == ppm).Resultado);
            Assert.Null(guardada.Pruebas.Single(p => p.IdPrueba == raven).Resultado);

            // Cambiar el resultado, quitar una prueba, dejar la conclusión sin definir y borrar las observaciones.
            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "45 puntos") }, Conclusion = "", Observaciones = "  "
            })).Ok);
            guardada = await Leer(atencion);
            Assert.Equal("45 puntos", Assert.Single(guardada.Pruebas).Resultado);
            Assert.Null(guardada.Conclusion);
            Assert.Null(guardada.Observaciones);
        }

        [Fact]
        public async Task Rechaza_conclusion_resultado_u_observaciones_invalidos_sin_guardar_nada()
        {
            var (_, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            var deOtra = await NuevaPrueba("RAVEN", DatosBase.EmpresaB);

            var r = await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, new string('x', AtencionService.LargoResultado + 1)) },
                Conclusion = "quizas",
                Observaciones = new string('o', AtencionService.LargoObservaciones + 1)
            });
            Assert.False(r.Ok);
            Assert.Contains(r.Errores, e => e.Campo == "Pruebas");
            Assert.Contains(r.Errores, e => e.Campo == "Conclusion");
            Assert.Contains(r.Errores, e => e.Campo == "Observaciones");

            var otra = await Guardar(atencion, new EvaluacionInput { Pruebas = new[] { new PruebaAplicadaInput(deOtra, "10") } });
            Assert.Equal("Pruebas", Assert.Single(otra.Errores).Campo);

            var guardada = await Leer(atencion);
            Assert.Empty(guardada.Pruebas);
            Assert.Null(guardada.Conclusion);
            Assert.Null(guardada.Observaciones);
        }

        // ── Cerrar ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Cerrar_exige_pruebas_con_resultado_y_conclusion_y_despues_ya_no_se_cambia()
        {
            var (_, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            var raven = await NuevaPrueba("RAVEN");

            var vacia = await Cerrar(atencion);
            Assert.Contains(vacia.Errores, e => e.Campo == "Pruebas");
            Assert.Contains(vacia.Errores, e => e.Campo == "Conclusion");

            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "42 puntos"), new PruebaAplicadaInput(raven, null) }, Conclusion = ResultadosConstancia.Apto
            })).Ok);
            var sinResultado = await Cerrar(atencion);
            Assert.Contains("falta: RAVEN", Assert.Single(sinResultado.Errores).Mensaje);
            Assert.Equal(EstadosAtencion.Borrador, (await Leer(atencion)).Estado);

            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "42 puntos"), new PruebaAplicadaInput(raven, "percentil 60") }, Conclusion = ResultadosConstancia.Apto
            })).Ok);
            var cerrada = await Cerrar(atencion);
            Assert.True(cerrada.Ok, Mensajes(cerrada.Errores));

            var leida = await Leer(atencion);
            Assert.Equal(EstadosAtencion.Cerrada, leida.Estado);
            Assert.Equal("psi.ana", leida.CerradoPor);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, leida.FechaCierre);

            var cambiar = await Guardar(atencion, new EvaluacionInput { Pruebas = Array.Empty<PruebaAplicadaInput>() });
            Assert.Equal(AtencionService.MensajeNoEditable, Assert.Single(cambiar.Errores).Mensaje);
            Assert.Equal(AtencionService.MensajeNoEditable, Assert.Single((await Cerrar(atencion)).Errores).Mensaje);
            Assert.Equal(2, (await Leer(atencion)).Pruebas.Count);
        }

        // ── Anular y la constancia que sale de la evaluación ────────────────

        [Fact]
        public async Task Anular_pide_motivo_y_no_deja_anular_si_salio_una_constancia_vigente()
        {
            var (paciente, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "42 puntos") }, Conclusion = ResultadosConstancia.Apto
            })).Ok);

            int tipo, constancia;
            using (var db = Contexto())
            {
                tipo = (await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput
                {
                    IdEmpresa = DatosBase.EmpresaA, Codigo = "ARMAS", Nombre = "Constancia para portación de armas", Usuario = "psi.ana"
                })).Valor;
                var emitida = await Constancias(db).EmitirAsync(new EmitirConstanciaInput
                {
                    IdEmpresa = DatosBase.EmpresaA, IdPaciente = paciente, IdTipoConstancia = tipo, IdAtencion = atencion,
                    IdsPruebas = new[] { ppm }, Resultado = ResultadosConstancia.Apto, Usuario = "psi.ana"
                });
                Assert.True(emitida.Ok, Mensajes(emitida.Errores));
                constancia = emitida.Valor!.IdConstancia;
            }

            Assert.Equal("Motivo", Assert.Single((await Anular(atencion, "   ")).Errores).Campo);
            var conConstancia = await Anular(atencion, "Se registró en el paciente equivocado");
            Assert.Contains("ARMAS-2026-00001", Assert.Single(conConstancia.Errores).Mensaje);

            using (var db = Contexto())
                Assert.True((await Constancias(db).AnularAsync(DatosBase.EmpresaA, constancia, "Emitida por error", "psi.ana")).Ok);
            var anulada = await Anular(atencion, "  Se registró en el paciente equivocado  ");
            Assert.True(anulada.Ok, Mensajes(anulada.Errores));

            var leida = await Leer(atencion);
            Assert.Equal(EstadosAtencion.Anulada, leida.Estado);
            Assert.Equal("Se registró en el paciente equivocado", leida.MotivoAnulacion);
            Assert.Equal("La atención ya está anulada.", Assert.Single((await Anular(atencion, "otra vez")).Errores).Mensaje);
        }

        [Fact]
        public async Task La_constancia_no_puede_decir_lo_contrario_de_la_conclusion_de_su_evaluacion()
        {
            var (paciente, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "18 puntos") }, Conclusion = ResultadosConstancia.NoApto
            })).Ok);

            using var db = Contexto();
            var tipo = (await Catalogo(db).GuardarTipoConstanciaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = DatosBase.EmpresaA, Codigo = "ARMAS", Nombre = "Constancia para portación de armas", Usuario = "psi.ana"
            })).Valor;
            EmitirConstanciaInput Pedido(string resultado) => new()
            {
                IdEmpresa = DatosBase.EmpresaA, IdPaciente = paciente, IdTipoConstancia = tipo, IdAtencion = atencion,
                IdsPruebas = new[] { ppm }, Resultado = resultado, Usuario = "psi.ana"
            };

            var contraria = await Constancias(db).EmitirAsync(Pedido(ResultadosConstancia.Apto));
            var error = Assert.Single(contraria.Errores);
            Assert.Equal("Resultado", error.Campo);
            Assert.Contains("concluyó no apto", error.Mensaje);

            var igual = await Constancias(db).EmitirAsync(Pedido(ResultadosConstancia.NoApto));
            Assert.True(igual.Ok, Mensajes(igual.Errores));
            Assert.Equal(ResultadosConstancia.NoApto, igual.Valor!.Resultado);
        }

        // ── Bitácora y aislamiento ──────────────────────────────────────────

        [Fact]
        public async Task La_bitacora_registra_el_cierre_pero_nunca_el_texto_clinico()
        {
            var (_, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "resultado-confidencial-77") },
                Conclusion = ResultadosConstancia.NoApto,
                Observaciones = "observacion-confidencial-88"
            })).Ok);
            Assert.True((await Cerrar(atencion)).Ok);

            using var leer = _bd.Crear();
            var bitacora = await leer.BitacoraCambios.ToListAsync();
            Assert.Contains(bitacora, b => b.Entidad == "atenciones" && b.Campo == "estado" && b.ValorNuevo == EstadosAtencion.Cerrada);
            Assert.DoesNotContain(bitacora, b => (b.ValorNuevo ?? string.Empty).Contains("confidencial") || (b.ValorAnterior ?? string.Empty).Contains("confidencial"));
            Assert.DoesNotContain(bitacora, b => b.Entidad == "atenciones" && (b.Campo == "conclusion" || b.Campo == "observaciones"));
        }

        [Fact]
        public async Task Otra_empresa_no_guarda_cierra_ni_anula_la_evaluacion_y_otra_cuenta_no_la_ve()
        {
            var (_, atencion) = await PacienteConAtencion();
            var ppm = await NuevaPrueba("PPM");
            Assert.True((await Guardar(atencion, new EvaluacionInput
            {
                Pruebas = new[] { new PruebaAplicadaInput(ppm, "42 puntos") }, Conclusion = ResultadosConstancia.Apto
            })).Ok);

            const string noExiste = "La atención no existe en esta empresa.";
            Assert.Equal(noExiste, Assert.Single((await Guardar(atencion, new EvaluacionInput(), DatosBase.EmpresaB)).Errores).Mensaje);
            Assert.Equal(noExiste, Assert.Single((await Cerrar(atencion, DatosBase.EmpresaB)).Errores).Mensaje);
            Assert.Equal(noExiste, Assert.Single((await Anular(atencion, "intento", DatosBase.EmpresaB)).Errores).Mensaje);

            var leida = await Leer(atencion);
            Assert.Equal(EstadosAtencion.Borrador, leida.Estado);
            Assert.Equal("42 puntos", Assert.Single(leida.Pruebas).Resultado);

            using var otraCuenta = _bd.CrearComo(DatosBase.TenantB);
            Assert.Equal(0, await otraCuenta.AtencionesPsicologia.CountAsync());
            Assert.Equal(0, await otraCuenta.AtencionPruebas.CountAsync());
        }
    }
}
