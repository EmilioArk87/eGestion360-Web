using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>Alta de pacientes sobre la persona maestra (datos inventados).</summary>
    public class PacienteServiceTests : IDisposable
    {
        private const string Dni = "0501-1998-00412";
        private const string OtroDni = "0501-1992-02290";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        internal static PacienteService Servicio(ApplicationDbContext db, TimeProvider reloj) =>
            new(db, new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), reloj), reloj, NullLogger<PacienteService>.Instance);

        internal static int NuevaJornada(BaseDeDatosDePrueba bd, int empresa = DatosBase.EmpresaA, string? responsable = null)
        {
            using var db = bd.Crear();
            var j = new Jornada
            {
                IdEmpresa = empresa, Nombre = "Brigada de prueba", Lugar = "San Pedro Sula", FechaInicio = new DateOnly(2026, 11, 15),
                NombreResponsable = responsable, CreadoPor = "pruebas", FechaCreacion = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc)
            };
            db.JornadasPsicologia.Add(j);
            db.SaveChanges();
            return j.IdJornada;
        }

        private static RegistrarPacienteInput Alta(string nombre = "Carlos", string apellido = "Mejía", string? dni = Dni,
            int empresa = DatosBase.EmpresaA, int? jornada = null, byte? edad = 28) => new()
        {
            IdEmpresa = empresa, IdJornada = jornada, EdadDeclarada = edad, Usuario = "psi.ana",
            Persona = new PersonaDatosInput { PrimerNombre = nombre, PrimerApellido = apellido, Documento = dni, Sexo = "M" }
        };

        private async Task<ResultadoPaciente> Registrar(RegistrarPacienteInput input)
        {
            using var db = Contexto();
            return await Servicio(db, _reloj).RegistrarAsync(input);
        }

        private static string Mensajes(ResultadoPaciente r) => string.Join(" | ", r.Errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        [Fact]
        public async Task Registra_persona_vinculo_paciente_y_ficha_con_el_primer_expediente_del_anio()
        {
            var r = await Registrar(Alta());
            Assert.True(r.Ok, Mensajes(r));
            Assert.Equal(EstadoPaciente.Creado, r.Estado);

            using var db = _bd.Crear();
            var paciente = await db.Pacientes.Include(p => p.Vinculo).ThenInclude(v => v.Persona).SingleAsync();
            Assert.Equal("PSI-2026-00001", paciente.NumeroExpediente);
            Assert.Equal(TiposVinculo.Paciente, paciente.TipoVinculo);
            Assert.Equal(TiposVinculo.Paciente, paciente.Vinculo.TipoVinculo);
            Assert.Equal(DatosBase.EmpresaA, paciente.Vinculo.IdEmpresa);
            Assert.Equal(DatosBase.TenantA, paciente.IdTenant);   // lo pone el servidor desde la empresa
            Assert.Equal("Carlos", paciente.Vinculo.Persona.PrimerNombre);
            Assert.Equal("M", paciente.Vinculo.Persona.Sexo);
            Assert.Single(await db.PersonaDocumentos.Where(d => d.IdPersona == paciente.Vinculo.IdPersona).ToListAsync());
        }

        [Fact]
        public async Task Los_expedientes_son_correlativos_por_empresa()
        {
            Assert.True((await Registrar(Alta())).Ok);
            Assert.True((await Registrar(Alta("Ana", "Torres", OtroDni))).Ok);
            Assert.True((await Registrar(Alta("Luis", "Paz", "0501-1980-11111", empresa: DatosBase.EmpresaB))).Ok);

            using var db = _bd.CrearComo(null, plataforma: true);
            var porEmpresa = await db.Pacientes.OrderBy(p => p.IdPaciente).Select(p => new { p.IdEmpresa, p.NumeroExpediente }).ToListAsync();
            Assert.Equal(new[] { "PSI-2026-00001", "PSI-2026-00002" }, porEmpresa.Where(p => p.IdEmpresa == DatosBase.EmpresaA).Select(p => p.NumeroExpediente));
            Assert.Equal("PSI-2026-00001", porEmpresa.Single(p => p.IdEmpresa == DatosBase.EmpresaB).NumeroExpediente);
        }

        [Fact]
        public async Task Una_persona_que_ya_es_empleado_de_la_empresa_se_vuelve_paciente_sin_tocar_sus_datos()
        {
            int idPersona;
            using (var db = _bd.Crear())
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Carlos", "Mejía", dni: Dni, codigoInterno: "E1").IdPersona;

            var r = await Registrar(Alta("Otro", "Nombre"));   // aunque escriban otro nombre, se usan los datos registrados
            Assert.True(r.Ok, Mensajes(r));
            Assert.Equal(EstadoPaciente.Vinculado, r.Estado);
            Assert.Contains(r.Advertencias, a => a.Contains("ya es de", StringComparison.Ordinal));

            using var leer = _bd.Crear();
            Assert.Equal(1, await leer.Personas.CountAsync());
            Assert.Equal("Carlos", (await leer.Personas.SingleAsync()).PrimerNombre);
            Assert.Equal(new[] { TiposVinculo.Empleado, TiposVinculo.Paciente },
                await leer.PersonaEmpresas.Where(v => v.IdPersona == idPersona).OrderBy(v => v.IdPersonaEmpresa).Select(v => v.TipoVinculo).ToListAsync());
        }

        [Fact]
        public async Task En_otra_empresa_de_la_cuenta_se_verifica_el_nombre_sin_revelar_nada()
        {
            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA2, "Carlos", "Mejía", dni: Dni, codigoInterno: "E1");

            var mal = await Registrar(Alta("Pedro", "López"));
            Assert.False(mal.Ok);
            Assert.Contains(mal.Errores, e => e.Mensaje == PacienteService.MensajeNoVerificado);

            var bien = await Registrar(Alta("carlos", "MEJIA"));   // mayúsculas y tildes no cuentan
            Assert.True(bien.Ok, Mensajes(bien));
            Assert.Equal(EstadoPaciente.Vinculado, bien.Estado);
        }

        [Fact]
        public async Task Registrar_otra_vez_no_crea_otra_ficha_y_la_jornada_abre_una_sola_atencion()
        {
            var jornada = NuevaJornada(_bd, responsable: "Lic. Responsable de Prueba");

            var r1 = await Registrar(Alta(jornada: jornada));
            Assert.True(r1.Ok, Mensajes(r1));
            Assert.True(r1.AtencionNueva);

            var r2 = await Registrar(Alta(jornada: jornada));
            Assert.Equal(EstadoPaciente.YaEraPaciente, r2.Estado);
            Assert.False(r2.AtencionNueva);

            using var db = _bd.Crear();
            Assert.Equal(1, await db.Pacientes.CountAsync());
            var atencion = await db.AtencionesPsicologia.SingleAsync();
            Assert.Equal(EstadosAtencion.Borrador, atencion.Estado);
            Assert.Equal(new DateOnly(2026, 11, 15), atencion.FechaAtencion);
            Assert.Equal((byte)28, atencion.EdadDeclarada);
            Assert.Equal("Lic. Responsable de Prueba", atencion.NombreProfesional);
            Assert.Equal(new DateOnly(2026, 11, 15), (await db.Pacientes.SingleAsync()).FechaApertura);
        }

        [Fact]
        public async Task Rechaza_sin_DNI_con_una_jornada_de_otra_empresa_y_con_una_edad_imposible()
        {
            var sinDni = await Registrar(Alta(dni: null));
            Assert.False(sinDni.Ok);
            Assert.Contains(sinDni.Errores, e => e.Campo == "Persona.Documento");

            var jornadaB = NuevaJornada(_bd, DatosBase.EmpresaB);
            var otraEmpresa = await Registrar(Alta(jornada: jornadaB));
            Assert.Contains(otraEmpresa.Errores, e => e.Campo == "IdJornada");

            var edad = await Registrar(Alta(edad: 130));
            Assert.Contains(edad.Errores, e => e.Campo == "EdadDeclarada");

            using var db = _bd.Crear();
            Assert.Equal(0, await db.Pacientes.CountAsync());
            Assert.Equal(0, await db.Personas.CountAsync());
        }

        [Fact]
        public async Task La_bitacora_registra_la_ficha_y_la_atencion_sin_texto_clinico()
        {
            var jornada = NuevaJornada(_bd);
            Assert.True((await Registrar(Alta(jornada: jornada))).Ok);

            using (var db = Contexto())
            {
                var atencion = await db.AtencionesPsicologia.SingleAsync();
                atencion.Observaciones = "Texto clínico que no debe ir a la bitácora";
                atencion.EdadDeclarada = 29;
                await db.SaveChangesAsync();
            }

            using var leer = _bd.Crear();
            var filas = await leer.BitacoraCambios.Where(b => b.Entidad == "pacientes" || b.Entidad == "atenciones").ToListAsync();
            Assert.Contains(filas, b => b.Entidad == "pacientes" && b.Operacion == "INSERT" && b.IdPersona != null);
            Assert.Contains(filas, b => b.Entidad == "atenciones" && b.Operacion == "INSERT" && b.IdPersona != null);
            Assert.Contains(filas, b => b.Entidad == "atenciones" && b.Campo == "edad_declarada" && b.ValorNuevo == "29");
            Assert.DoesNotContain(filas, b => b.Campo == "observaciones");
            Assert.DoesNotContain(filas, b => (b.ValorNuevo ?? string.Empty).Contains("Texto clínico", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Una_empresa_de_otra_cuenta_no_ve_los_pacientes()
        {
            Assert.True((await Registrar(Alta())).Ok);

            using var otraCuenta = _bd.CrearComo(DatosBase.TenantB);
            Assert.Equal(0, await otraCuenta.Pacientes.CountAsync());
            Assert.Equal(0, await otraCuenta.PersonaEmpresas.CountAsync(v => v.TipoVinculo == TiposVinculo.Paciente));

            using var mismaCuenta = _bd.CrearComo(DatosBase.TenantA);
            Assert.Equal(1, await mismaCuenta.Pacientes.CountAsync());
        }
    }
}
