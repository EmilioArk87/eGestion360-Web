using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>Importación de la brigada en tres pasos (borrador, revisar, confirmar), con un listado inventado.</summary>
    public class ImportacionPsicologiaServiceTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private ImportacionPsicologiaService Servicio(ApplicationDbContext db) =>
            new(db, PacienteServiceTests.Servicio(db, _reloj), _reloj, NullLogger<ImportacionPsicologiaService>.Instance);

        private static ArchivoBrigada Archivo(string hash, params FilaBrigada[] filas) => new("brigada.xlsx", hash, "Listado Brigada", filas);

        private static readonly string HashA = new('a', 64);
        private static readonly string HashB = new('b', 64);

        /// <summary>Seis filas: dos listas, una de tres palabras, un DNI repetido dos veces y un DNI imposible.</summary>
        private static ArchivoBrigada Listado() => Archivo(HashA,
            new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M"),
            new FilaBrigada(3, "Ana Lucía Torres Núñez", "0501-1992-02290", "34", "F"),
            new FilaBrigada(4, "Marvin Reyes Castillo", "1801-1997-00093", "29", "M"),
            new FilaBrigada(5, "Luis Fernando Paz Ortega", "0501-1975-99901", "51", "M"),
            new FilaBrigada(6, "Luis Fernando Paz Ortega", "0501-1975-99901", "51", "M"),
            new FilaBrigada(7, "Pedro José Díaz Cruz", "9901-1990-00001", "40", "M"));

        private async Task<ResultadoImportacion> Borrador(ArchivoBrigada archivo, int empresa = DatosBase.EmpresaA, int? jornada = null)
        {
            var idJornada = jornada ?? PacienteServiceTests.NuevaJornada(_bd, empresa);
            using var db = Contexto();
            return await Servicio(db).CrearBorradorAsync(empresa, idJornada, archivo, "psi.ana");
        }

        private async Task<ResultadoImportacion> Corregir(int idImportacion, int numeroFila, Action<CorreccionFila> ajustar, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var fila = await db.ImportacionFilasPsicologia.SingleAsync(f => f.IdImportacion == idImportacion && f.NumeroFila == numeroFila);
            var c = new CorreccionFila
            {
                IdImportacionFila = fila.IdImportacionFila, PrimerNombre = fila.PrimerNombre, SegundoNombre = fila.SegundoNombre,
                PrimerApellido = fila.PrimerApellido, SegundoApellido = fila.SegundoApellido
            };
            ajustar(c);
            return await Servicio(db).CorregirFilaAsync(empresa, idImportacion, c, "psi.ana");
        }

        private async Task<ResultadoImportacion> Confirmar(int idImportacion, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            return await Servicio(db).ConfirmarAsync(empresa, idImportacion, "psi.ana");
        }

        private static string Resultado(Importacion i, int fila) => i.Filas.Single(f => f.NumeroFila == fila).Resultado;

        [Fact]
        public async Task El_borrador_analiza_cada_fila_y_no_crea_pacientes()
        {
            var r = await Borrador(Listado());
            Assert.True(r.Ok, r.Mensaje);
            var i = r.Importacion!;

            Assert.Equal(EstadosImportacion.Borrador, i.Estado);
            Assert.Equal(6, i.TotalFilas);
            Assert.Equal(ResultadosFilaImportacion.Nueva, Resultado(i, 2));
            Assert.Equal(ResultadosFilaImportacion.Nueva, Resultado(i, 3));
            Assert.Equal(ResultadosFilaImportacion.Revisar, Resultado(i, 4));   // tres palabras
            Assert.Equal(ResultadosFilaImportacion.Error, Resultado(i, 5));     // DNI repetido
            Assert.Equal(ResultadosFilaImportacion.Error, Resultado(i, 6));
            Assert.Equal(ResultadosFilaImportacion.Error, Resultado(i, 7));     // departamento 99
            Assert.Contains("filas 5, 6", i.Filas.Single(f => f.NumeroFila == 5).Mensaje);
            Assert.Equal(("Carlos", "Andrés", "Mejía", "Rivera"),
                (i.Filas.Single(f => f.NumeroFila == 2).PrimerNombre, i.Filas.Single(f => f.NumeroFila == 2).SegundoNombre,
                 i.Filas.Single(f => f.NumeroFila == 2).PrimerApellido, i.Filas.Single(f => f.NumeroFila == 2).SegundoApellido));

            using var db = _bd.Crear();
            Assert.Equal(0, await db.Pacientes.CountAsync());
            Assert.Equal(0, await db.Personas.CountAsync());
        }

        [Fact]
        public async Task No_confirma_mientras_haya_filas_por_revisar_o_con_error_y_no_guarda_nada()
        {
            var id = (await Borrador(Listado())).Importacion!.IdImportacion;

            var r = await Confirmar(id);
            Assert.False(r.Ok);
            Assert.Contains("Quedan 4 filas", r.Mensaje);

            using var db = _bd.Crear();
            Assert.Equal(0, await db.Pacientes.CountAsync());
            Assert.Equal(EstadosImportacion.Borrador, (await db.ImportacionesPsicologia.SingleAsync()).Estado);
        }

        [Fact]
        public async Task Corregir_u_omitir_las_filas_deja_el_lote_listo_y_confirmar_crea_todo_de_una_vez()
        {
            var id = (await Borrador(Listado())).Importacion!.IdImportacion;

            // Tres palabras: dos nombres y un apellido.
            var c = await Corregir(id, 4, f => { f.SegundoNombre = "Reyes"; f.PrimerApellido = "Castillo"; f.SegundoApellido = null; });
            Assert.Equal(ResultadosFilaImportacion.Nueva, Resultado(c.Importacion!, 4));

            // DNI repetido: al omitir una, la otra queda lista sin esperar a confirmar.
            var o = await Corregir(id, 6, f => f.Omitir = true);
            Assert.Equal(ResultadosFilaImportacion.Omitida, Resultado(o.Importacion!, 6));
            Assert.Equal(ResultadosFilaImportacion.Nueva, Resultado(o.Importacion!, 5));

            await Corregir(id, 7, f => f.Omitir = true);

            var r = await Confirmar(id);
            Assert.True(r.Ok, r.Mensaje);
            Assert.Equal(EstadosImportacion.Confirmada, r.Importacion!.Estado);
            Assert.Equal(4, r.Importacion.FilasNuevas);
            Assert.Equal(2, r.Importacion.FilasRechazadas);

            using var db = _bd.Crear();
            Assert.Equal(4, await db.Pacientes.CountAsync());
            Assert.Equal(4, await db.AtencionesPsicologia.CountAsync(a => a.Estado == EstadosAtencion.Borrador));
            Assert.Equal(new[] { "PSI-2026-00001", "PSI-2026-00002", "PSI-2026-00003", "PSI-2026-00004" },
                await db.Pacientes.OrderBy(p => p.NumeroExpediente).Select(p => p.NumeroExpediente).ToListAsync());
            var marvin = await db.Personas.SingleAsync(p => p.PrimerNombre == "Marvin");
            Assert.Equal(("Reyes", "Castillo", (string?)null), (marvin.SegundoNombre, marvin.PrimerApellido, marvin.SegundoApellido));
            Assert.All(await db.ImportacionFilasPsicologia.Where(f => f.Resultado == ResultadosFilaImportacion.Nueva).ToListAsync(),
                f => Assert.NotNull(f.IdPaciente));
        }

        [Fact]
        public async Task El_mismo_archivo_no_se_importa_dos_veces_y_otro_listado_con_las_mismas_personas_no_duplica_atenciones()
        {
            var jornada = PacienteServiceTests.NuevaJornada(_bd);
            var listado = Archivo(HashA,
                new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M"),
                new FilaBrigada(3, "Ana Lucía Torres Núñez", "0501-1992-02290", "34", "F"));
            var id = (await Borrador(listado, jornada: jornada)).Importacion!.IdImportacion;
            Assert.True((await Confirmar(id)).Ok);

            var repetido = await Borrador(listado, jornada: jornada);
            Assert.False(repetido.Ok);
            Assert.Contains("ya se importó", repetido.Mensaje);

            // Otro archivo (otra huella) con una persona ya atendida en la jornada y una nueva.
            var segundo = await Borrador(Archivo(HashB,
                new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M"),
                new FilaBrigada(3, "Rosa María Flores Bonilla", "0801-1985-03333", "41", "F")), jornada: jornada);
            Assert.Equal(ResultadosFilaImportacion.Omitida, Resultado(segundo.Importacion!, 2));
            Assert.Equal(ImportacionPsicologiaService.MensajeYaAtendida, segundo.Importacion!.Filas.Single(f => f.NumeroFila == 2).Mensaje);
            Assert.True((await Confirmar(segundo.Importacion.IdImportacion)).Ok);

            using var db = _bd.Crear();
            Assert.Equal(3, await db.Pacientes.CountAsync());
            Assert.Equal(3, await db.AtencionesPsicologia.CountAsync());
        }

        [Fact]
        public async Task Una_persona_que_ya_existe_en_la_empresa_se_reutiliza_y_si_el_nombre_no_coincide_se_revisa()
        {
            using (var db = _bd.Crear())
            {
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Carlos", "Mejía", dni: "0501-1998-00412", codigoInterno: "E1");
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Rosa", "Flores", dni: "0801-1985-03333", codigoInterno: "E2");
            }

            var r = await Borrador(Archivo(HashA,
                new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M"),
                new FilaBrigada(3, "Otra Persona Distinta Nombre", "0801-1985-03333", "41", "F")));

            Assert.Equal(ResultadosFilaImportacion.Reutilizada, Resultado(r.Importacion!, 2));
            Assert.Equal(ResultadosFilaImportacion.Revisar, Resultado(r.Importacion!, 3));
            Assert.Contains("Rosa", r.Importacion!.Filas.Single(f => f.NumeroFila == 3).Mensaje);   // en la propia empresa sí se muestra
        }

        [Fact]
        public async Task Un_lote_de_otra_empresa_no_se_encuentra_ni_se_confirma_ni_lo_ve_otra_cuenta()
        {
            var id = (await Borrador(Archivo(HashA, new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M")))).Importacion!.IdImportacion;

            Assert.False((await Confirmar(id, DatosBase.EmpresaA2)).Encontrado);   // otra empresa de la misma cuenta
            Assert.False((await Confirmar(id, DatosBase.EmpresaB)).Encontrado);    // otra cuenta

            using (var db = Contexto())
                Assert.Null(await Servicio(db).ObtenerAsync(DatosBase.EmpresaB, id));

            using var otraCuenta = _bd.CrearComo(DatosBase.TenantB);
            Assert.Equal(0, await otraCuenta.ImportacionesPsicologia.CountAsync());
            Assert.Equal(0, await otraCuenta.ImportacionFilasPsicologia.CountAsync());

            using var leer = _bd.Crear();
            Assert.Equal(0, await leer.Pacientes.CountAsync());
        }

        [Fact]
        public async Task Descartar_deja_el_lote_sin_pacientes_y_ya_no_se_puede_confirmar()
        {
            var id = (await Borrador(Archivo(HashA, new FilaBrigada(2, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M")))).Importacion!.IdImportacion;
            using (var db = Contexto())
                Assert.True((await Servicio(db).DescartarAsync(DatosBase.EmpresaA, id, "psi.ana")).Ok);

            Assert.False((await Confirmar(id)).Ok);
            using var leer = _bd.Crear();
            Assert.Equal(0, await leer.Pacientes.CountAsync());
        }

        [Theory]
        [InlineData("M", "M")]
        [InlineData("masculino", "M")]
        [InlineData("Femenino", "F")]
        [InlineData("x", null)]
        public void El_genero_se_lee_en_varias_formas(string genero, string? esperado) =>
            Assert.Equal(esperado, ImportacionPsicologiaService.Sexo(genero));

        [Theory]
        [InlineData("28", true, (byte)28)]
        [InlineData("", true, null)]
        [InlineData("28.5", false, null)]
        [InlineData("130", false, null)]
        [InlineData("veinte", false, null)]
        public void La_edad_es_un_entero_entre_0_y_120_o_nada(string texto, bool ok, byte? valor) =>
            Assert.Equal((ok, valor), ImportacionPsicologiaService.Edad(texto));
    }
}
