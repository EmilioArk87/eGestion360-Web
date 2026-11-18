using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>Editar los datos de un paciente ya registrado (pedido del 2026-11-17). Datos inventados.</summary>
    public class EdicionPacienteTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private EdicionPacienteService Servicio(ApplicationDbContext db) =>
            new(db, new PersonaService(db, new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj), _reloj,
                NullLogger<PersonaService>.Instance));

        private async Task<int> NuevoPaciente(string dni, string nombre = "Carlos", string apellido = "Mejía", int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var r = await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(new RegistrarPacienteInput
            {
                IdEmpresa = empresa, Usuario = "psi.ana",
                Persona = new PersonaDatosInput { PrimerNombre = nombre, PrimerApellido = apellido, Documento = dni, Sexo = "M", Telefono = "9999-0000" }
            });
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Mensaje)));
            return r.Paciente!.IdPaciente;
        }

        private async Task<EditarPacienteInput> Formulario(int idPaciente, int empresa = DatosBase.EmpresaA)
        {
            using var db = Contexto();
            var p = await Servicio(db).ObtenerAsync(empresa, idPaciente);
            Assert.NotNull(p);
            p!.Datos.Usuario = "psi.jefa";
            return p.Datos;
        }

        private async Task<ResultadoEditarPaciente> Guardar(EditarPacienteInput input)
        {
            using var db = Contexto();
            return await Servicio(db).GuardarAsync(input);
        }

        [Fact]
        public async Task El_formulario_trae_los_datos_del_paciente_y_solo_de_la_empresa()
        {
            var id = await NuevoPaciente("0501-1998-00412");

            var f = await Formulario(id);
            Assert.Equal("0501-1998-00412", f.Dni);
            Assert.Equal(("Carlos", "Mejía", "M", "99990000"), (f.PrimerNombre, f.PrimerApellido, f.Sexo, f.Telefono));   // el teléfono se guarda solo con dígitos
            Assert.NotNull(f.TokenConcurrencia);

            using var db = Contexto();
            Assert.Null(await Servicio(db).ObtenerAsync(DatosBase.EmpresaB, id));
        }

        [Fact]
        public async Task Edita_nombres_DNI_sexo_y_contacto_sin_borrar_el_resto_de_la_ficha_y_queda_en_la_bitacora()
        {
            var id = await NuevoPaciente("0501-1998-00412");
            int idPersona;
            using (var db = _bd.Crear())
            {
                // Datos que el formulario de pacientes no muestra: no se pueden perder al editar.
                var persona = await db.Pacientes.Where(p => p.IdPaciente == id).Select(p => p.Vinculo.Persona).SingleAsync();
                idPersona = persona.IdPersona;
                persona.FechaNacimiento = new DateOnly(1998, 3, 2);
                persona.DireccionResidencia = "Barrio de ensayo, casa 1";
                persona.TelefonoSecundario = "88880000";   // como lo guarda la validación de personas
                await db.SaveChangesAsync();
            }

            var f = await Formulario(id);
            f.PrimerNombre = "Carlos";
            f.SegundoNombre = "Andrés";
            f.PrimerApellido = "Mejía";
            f.SegundoApellido = "Rivera";
            f.Dni = "0501-1998-00413";       // un dígito mal escrito al registrarlo
            f.Sexo = "M";
            f.Telefono = "9999-1111";
            f.Correo = "carlos.ensayo@ejemplo.test";
            var r = await Guardar(f);
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => $"{e.Campo}: {e.Mensaje}")));

            using var leer = _bd.Crear();
            var p = await leer.Personas.Include(x => x.Documentos).SingleAsync(x => x.IdPersona == idPersona);
            Assert.Equal(("Carlos", "Andrés", "Mejía", "Rivera"), (p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido));
            Assert.Equal(("99991111", "carlos.ensayo@ejemplo.test"), (p.Telefono, p.Email));
            Assert.Equal(new DateOnly(1998, 3, 2), p.FechaNacimiento);
            Assert.Equal("Barrio de ensayo, casa 1", p.DireccionResidencia);
            Assert.Equal("88880000", p.TelefonoSecundario);
            var dni = Assert.Single(p.Documentos, d => !d.Eliminado);
            Assert.Equal("0501199800413", DocumentosIdentidad.NormalizarNumero(dni.Numero));   // el mismo documento, corregido

            var cambios = await leer.BitacoraCambios.Where(b => b.Entidad == "personas" && b.Operacion == "UPDATE").Select(b => b.Campo).ToListAsync();
            Assert.Contains("segundo_nombre", cambios);
            Assert.Contains("telefono", cambios);
            Assert.DoesNotContain("direccion_residencia", cambios);
            Assert.DoesNotContain("fecha_nacimiento", cambios);
        }

        [Fact]
        public async Task Rechaza_sin_DNI_con_el_DNI_de_otro_paciente_y_de_otra_empresa()
        {
            var id = await NuevoPaciente("0501-1998-00412");
            await NuevoPaciente("0501-1992-02290", "Ana", "Torres");

            var f = await Formulario(id);
            f.Dni = "  ";
            Assert.Equal("Dni", Assert.Single((await Guardar(f)).Errores).Campo);

            f = await Formulario(id);
            f.Dni = "0501-1992-02290";        // el de otro paciente de la misma empresa
            var duplicado = await Guardar(f);
            Assert.False(duplicado.Ok);
            Assert.Contains(duplicado.Errores, e => e.Campo == "Dni");

            f = await Formulario(id);
            f.IdEmpresa = DatosBase.EmpresaB;
            Assert.Equal(EdicionPacienteService.MensajeNoEncontrado, Assert.Single((await Guardar(f)).Errores).Mensaje);

            using var leer = _bd.Crear();
            var dni = await leer.Pacientes.Where(p => p.IdPaciente == id)
                .SelectMany(p => p.Vinculo.Persona.Documentos).Where(d => !d.Eliminado).Select(d => d.Numero).SingleAsync();
            Assert.Equal("0501199800412", DocumentosIdentidad.NormalizarNumero(dni));   // nada cambió
        }

        [Theory]
        [InlineData("Documento", "Dni")]
        [InlineData("TipoDocumento", "Dni")]
        [InlineData("Email", "Correo")]
        [InlineData("PrimerNombre", "PrimerNombre")]
        [InlineData("Telefono", "Telefono")]
        [InlineData("FechaNacimiento", "")]
        public void Los_errores_de_la_persona_van_al_campo_del_formulario(string campo, string esperado) =>
            Assert.Equal(esperado, EdicionPacienteService.CampoDelFormulario(campo));
    }
}
