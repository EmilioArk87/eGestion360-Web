using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Pages.Psicologia.Pacientes;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>Alta de un paciente a la vez desde el formulario (Pacientes/Create), con datos inventados.</summary>
    public class AltaIndividualPacienteTests : IDisposable
    {
        private const string Dni = "0501-1998-00412";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        /// <summary>Lo mismo que arma la página con lo que se escribió en el formulario.</summary>
        private static RegistrarPacienteInput DelFormulario(string? dni = Dni, int? jornada = null, byte? edad = null,
            string? telefono = null, string? correo = null) => new()
        {
            IdEmpresa = DatosBase.EmpresaA, IdJornada = jornada, EdadDeclarada = edad, Usuario = "psi.ana",
            Persona = new PersonaDatosInput
            {
                PrimerNombre = "Carlos", SegundoNombre = "Andrés", PrimerApellido = "Mejía", SegundoApellido = "Rivera",
                Documento = dni, Sexo = "M", Telefono = telefono, Email = correo
            }
        };

        private async Task<ResultadoPaciente> Registrar(RegistrarPacienteInput input)
        {
            using var db = _bd.Crear(new AuditoriaCambiosInterceptor(_quien));
            return await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(input);
        }

        [Fact]
        public async Task Sin_jornada_abre_el_expediente_sin_atencion_y_guarda_el_contacto()
        {
            var r = await Registrar(DelFormulario(telefono: "9999-8888", correo: "Paciente.Prueba@Ejemplo.com"));
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Mensaje)));
            Assert.Equal(EstadoPaciente.Creado, r.Estado);
            Assert.False(r.AtencionNueva);
            Assert.Equal("Paciente registrado con el expediente PSI-2026-00001.", CreateModel.Mensaje(r, conJornada: false));

            using var db = _bd.Crear();
            var paciente = await db.Pacientes.Include(p => p.Vinculo).ThenInclude(v => v.Persona).SingleAsync();
            Assert.Equal("PSI-2026-00001", paciente.NumeroExpediente);
            Assert.Equal("Carlos Andrés", paciente.Vinculo.Persona.Nombres);
            Assert.Equal("Mejía Rivera", paciente.Vinculo.Persona.Apellidos);
            Assert.Equal("99998888", paciente.Vinculo.Persona.Telefono);
            Assert.Equal("paciente.prueba@ejemplo.com", paciente.Vinculo.Persona.Email);
            Assert.Equal(0, await db.AtencionesPsicologia.CountAsync());
        }

        [Fact]
        public async Task Con_jornada_abre_su_atencion_y_registrarlo_otra_vez_no_duplica_nada()
        {
            var jornada = PacienteServiceTests.NuevaJornada(_bd);

            var r1 = await Registrar(DelFormulario(jornada: jornada, edad: 30));
            Assert.True(r1.Ok);
            Assert.Equal("Paciente registrado con el expediente PSI-2026-00001. Se abrió su atención en la jornada.", CreateModel.Mensaje(r1, conJornada: true));

            var r2 = await Registrar(DelFormulario(jornada: jornada, edad: 30));
            Assert.Equal(EstadoPaciente.YaEraPaciente, r2.Estado);
            Assert.Equal("Ya era paciente con el expediente PSI-2026-00001: no se abrió otro expediente. Ya tenía su atención en esa jornada.",
                CreateModel.Mensaje(r2, conJornada: true));

            using var db = _bd.Crear();
            Assert.Equal(1, await db.Pacientes.CountAsync());
            Assert.Equal((byte)30, (await db.AtencionesPsicologia.SingleAsync()).EdadDeclarada);
        }

        [Fact]
        public async Task Un_DNI_o_un_telefono_mal_escritos_se_marcan_en_su_campo_y_no_se_guarda_nada()
        {
            var r = await Registrar(DelFormulario(dni: "0501-1998-0041", telefono: "123"));
            Assert.False(r.Ok);
            var campos = r.Errores.Select(e => CreateModel.CampoDelFormulario(e.Campo)).ToList();
            Assert.Contains("Input.Dni", campos);
            Assert.Contains("Input.Telefono", campos);

            using var db = _bd.Crear();
            Assert.Equal(0, await db.Personas.CountAsync());
        }

        [Theory]
        [InlineData("Persona.Documento", "Input.Dni")]
        [InlineData("Persona.TipoDocumento", "Input.Dni")]
        [InlineData("Persona.Email", "Input.Correo")]
        [InlineData("Persona.PrimerNombre", "Input.PrimerNombre")]
        [InlineData("Persona.SegundoApellido", "Input.SegundoApellido")]
        [InlineData("Persona.Sexo", "Input.Sexo")]
        [InlineData("IdJornada", "Input.IdJornada")]
        [InlineData("EdadDeclarada", "Input.Edad")]
        [InlineData("Persona.FechaNacimiento", "")]   // un campo que el formulario no tiene va arriba, al resumen
        [InlineData("", "")]
        public void Cada_error_del_servicio_va_a_su_campo_del_formulario(string campo, string esperado) =>
            Assert.Equal(esperado, CreateModel.CampoDelFormulario(campo));
    }
}
