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
    /// <summary>Profesionales del centro, responsable de jornada y firma de constancias (datos inventados).</summary>
    public class ProfesionalesTests : IDisposable
    {
        private const string DniPsicologa = "0801-1990-99911";
        private const string DniAdministrador = "0801-1985-99912";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "psi.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private ProfesionalService Servicio(ApplicationDbContext db) =>
            new(db, new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj), _reloj, NullLogger<ProfesionalService>.Instance);

        private static ProfesionalInput Nuevo(string dni = DniPsicologa, string nombre = "Lucía", string apellido = "Paz", string tipo = TiposProfesional.Psicologo,
            string? colegiacion = "cph-1234", int empresa = DatosBase.EmpresaA) => new()
        {
            IdEmpresa = empresa, Dni = dni, PrimerNombre = nombre, PrimerApellido = apellido, SegundoApellido = "Reyes", Sexo = "F",
            Tipo = tipo, Titulo = "Lic.", NumeroColegiacion = colegiacion, Especialidad = "Psicología clínica", Usuario = "psi.ana"
        };

        private async Task<ResultadoPsicologia<Profesional>> Guardar(ProfesionalInput input)
        {
            using var db = Contexto();
            return await Servicio(db).GuardarAsync(input);
        }

        private static string Mensajes(IReadOnlyList<ErrorValidacion> errores) => string.Join(" | ", errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        [Fact]
        public void Los_tipos_son_los_del_CHECK() =>
            Assert.Equal(new[] { "psicologo", "administrador" }, TiposProfesional.Todos);

        [Fact]
        public async Task Registra_al_profesional_con_su_persona_nueva_y_un_vinculo_otro_en_la_empresa()
        {
            var r = await Guardar(Nuevo());
            Assert.True(r.Ok, Mensajes(r.Errores));

            using var db = _bd.Crear();
            var p = await db.ProfesionalesPsicologia.Include(x => x.Persona).SingleAsync();
            Assert.Equal(TiposProfesional.Psicologo, p.Tipo);
            Assert.Equal("Lucía Paz Reyes", p.NombreFirma);              // por defecto, el nombre de la persona
            Assert.Equal("Lic. Lucía Paz Reyes", p.NombreImpreso);
            Assert.Equal("CPH-1234", p.NumeroColegiacion);               // en mayúsculas
            Assert.Equal(DatosBase.TenantA, p.IdTenant);
            Assert.Equal(new[] { TiposVinculo.Otro }, await db.PersonaEmpresas.Where(v => v.IdPersona == p.IdPersona).Select(v => v.TipoVinculo).ToListAsync());
        }

        [Fact]
        public async Task Una_persona_que_ya_esta_en_la_empresa_se_reutiliza_sin_otro_vinculo_y_no_se_registra_dos_veces()
        {
            int idPersona;
            using (var db = _bd.Crear())
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Lucía", "Paz", dni: DniPsicologa, codigoInterno: "E9").IdPersona;

            var r = await Guardar(Nuevo());
            Assert.True(r.Ok, Mensajes(r.Errores));
            Assert.Equal(idPersona, r.Valor!.IdPersona);

            var otraVez = await Guardar(Nuevo(colegiacion: null));
            Assert.Contains("ya está registrada como profesional", Assert.Single(otraVez.Errores).Mensaje);

            using var leer = _bd.Crear();
            Assert.Equal(1, await leer.Personas.CountAsync());
            Assert.Equal(new[] { TiposVinculo.Empleado }, await leer.PersonaEmpresas.Where(v => v.IdPersona == idPersona).Select(v => v.TipoVinculo).ToListAsync());
        }

        [Fact]
        public async Task En_otra_empresa_de_la_cuenta_se_verifica_el_nombre_sin_revelar_nada()
        {
            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA2, "Lucía", "Paz", dni: DniPsicologa, codigoInterno: "E9");

            var mal = await Guardar(Nuevo(nombre: "Pedro", apellido: "López"));
            Assert.Contains(mal.Errores, e => e.Mensaje == ProfesionalService.MensajeNoVerificado);

            var bien = await Guardar(Nuevo(nombre: "LUCIA", apellido: "paz"));
            Assert.True(bien.Ok, Mensajes(bien.Errores));
        }

        [Fact]
        public async Task Rechaza_sin_DNI_tipo_desconocido_y_colegiacion_repetida_en_la_empresa()
        {
            Assert.True((await Guardar(Nuevo())).Ok);

            Assert.Contains((await Guardar(Nuevo(dni: ""))).Errores, e => e.Campo == "Dni");
            Assert.Contains((await Guardar(Nuevo(dni: DniAdministrador, tipo: "secretario"))).Errores, e => e.Campo == "Tipo");
            Assert.Contains((await Guardar(Nuevo(dni: DniAdministrador, colegiacion: "CPH-1234"))).Errores, e => e.Campo == "NumeroColegiacion");

            // Otra empresa puede tener la misma colegiación, y un administrador puede no tenerla.
            Assert.True((await Guardar(Nuevo(dni: DniAdministrador, nombre: "Mario", apellido: "Ruiz", empresa: DatosBase.EmpresaB))).Ok);
            Assert.True((await Guardar(Nuevo(dni: DniAdministrador, nombre: "Mario", apellido: "Ruiz", tipo: TiposProfesional.Administrador, colegiacion: null))).Ok);
        }

        [Fact]
        public async Task Edita_los_datos_profesionales_sin_tocar_la_persona_y_se_desactiva()
        {
            var r = await Guardar(Nuevo());
            var id = r.Valor!.IdProfesional;

            var editado = await Guardar(new ProfesionalInput
            {
                IdEmpresa = DatosBase.EmpresaA, IdProfesional = id, Tipo = TiposProfesional.Psicologo, Titulo = "M.Sc.", NombreFirma = "Lucía Paz",
                NumeroColegiacion = "CPH-1234", Especialidad = "Psicología educativa", Dni = "no se usa", PrimerNombre = "Otro"
            });
            Assert.True(editado.Ok, Mensajes(editado.Errores));
            using (var db = Contexto())
                Assert.True((await Servicio(db).CambiarEstadoAsync(DatosBase.EmpresaA, id, false, "psi.ana")).Ok);

            using var leer = _bd.Crear();
            var p = await leer.ProfesionalesPsicologia.Include(x => x.Persona).SingleAsync();
            Assert.Equal("M.Sc. Lucía Paz", p.NombreImpreso);
            Assert.Equal("Psicología educativa", p.Especialidad);
            Assert.False(p.Activo);
            Assert.Equal("Lucía", p.Persona.PrimerNombre);
        }

        [Fact]
        public async Task El_responsable_de_la_jornada_pasa_a_las_atenciones_en_borrador_que_lo_seguian()
        {
            var jornada = PacienteServiceTests.NuevaJornada(_bd);
            int[] atenciones;
            using (var db = Contexto())
            {
                var pacientes = PacienteServiceTests.Servicio(db, _reloj);
                foreach (var dni in new[] { "0501-1998-00412", "0501-1992-02290", "0501-1980-11111" })
                {
                    var rp = await pacientes.RegistrarAsync(new RegistrarPacienteInput
                    {
                        IdEmpresa = DatosBase.EmpresaA, IdJornada = jornada, EdadDeclarada = 30, Usuario = "psi.ana",
                        Persona = new PersonaDatosInput { PrimerNombre = "Carlos", PrimerApellido = "Mejía", Documento = dni, Sexo = "M" }
                    });
                    Assert.True(rp.Ok, Mensajes(rp.Errores));
                }
                atenciones = await db.AtencionesPsicologia.OrderBy(a => a.IdAtencion).Select(a => a.IdAtencion).ToArrayAsync();
            }
            using (var db = _bd.Crear())   // a la tercera ya la atiende otro profesional
            {
                var a = await db.AtencionesPsicologia.SingleAsync(x => x.IdAtencion == atenciones[2]);
                a.NombreProfesional = "Lic. Otro Profesional";
                await db.SaveChangesAsync();
            }
            var psicologa = (await Guardar(Nuevo())).Valor!;
            var admin = (await Guardar(Nuevo(dni: DniAdministrador, nombre: "Mario", apellido: "Ruiz", tipo: TiposProfesional.Administrador, colegiacion: null))).Valor!;

            using (var db = Contexto())
                Assert.Equal(2, (await Servicio(db).AsignarResponsableAsync(DatosBase.EmpresaA, jornada, psicologa.IdProfesional, "psi.ana")).Valor);
            using (var db = Contexto())   // un administrador también puede ser responsable (D2); las mismas dos lo siguen
                Assert.Equal(2, (await Servicio(db).AsignarResponsableAsync(DatosBase.EmpresaA, jornada, admin.IdProfesional, "psi.ana")).Valor);

            using var leer = _bd.Crear();
            var j = await leer.JornadasPsicologia.SingleAsync();
            Assert.Equal(admin.IdPersona, j.IdPersonaResponsable);
            Assert.Equal("Lic. Mario Ruiz Reyes", j.NombreResponsable);
            var porId = await leer.AtencionesPsicologia.ToDictionaryAsync(a => a.IdAtencion, a => a.NombreProfesional);
            Assert.Equal("Lic. Mario Ruiz Reyes", porId[atenciones[0]]);
            Assert.Equal("Lic. Mario Ruiz Reyes", porId[atenciones[1]]);
            Assert.Equal("Lic. Otro Profesional", porId[atenciones[2]]);
        }

        [Fact]
        public async Task Firma_la_constancia_un_psicologo_activo_y_no_un_administrador()
        {
            var psicologa = (await Guardar(Nuevo())).Valor!;
            var admin = (await Guardar(Nuevo(dni: DniAdministrador, nombre: "Mario", apellido: "Ruiz", tipo: TiposProfesional.Administrador, colegiacion: null))).Valor!;

            int paciente, tipo, prueba;
            using (var db = Contexto())
            {
                var rp = await PacienteServiceTests.Servicio(db, _reloj).RegistrarAsync(new RegistrarPacienteInput
                {
                    IdEmpresa = DatosBase.EmpresaA, Usuario = "psi.ana",
                    Persona = new PersonaDatosInput { PrimerNombre = "Carlos", PrimerApellido = "Mejía", Documento = "0501-1998-00412", Sexo = "M" }
                });
                paciente = rp.Paciente!.IdPaciente;
                var catalogo = new CatalogoPsicologiaService(db, _reloj, NullLogger<CatalogoPsicologiaService>.Instance);
                tipo = (await catalogo.GuardarTipoConstanciaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "ARMAS", Nombre = "Armas" })).Valor;
                prueba = (await catalogo.GuardarPruebaAsync(new ElementoCatalogoInput { IdEmpresa = DatosBase.EmpresaA, Codigo = "RAVEN", Nombre = "Raven" })).Valor;
            }

            async Task<ResultadoPsicologia<Constancia>> Emitir(int idProfesional)
            {
                using var db = Contexto();
                return await new ConstanciaService(db, _reloj, NullLogger<ConstanciaService>.Instance).EmitirAsync(new EmitirConstanciaInput
                {
                    IdEmpresa = DatosBase.EmpresaA, IdPaciente = paciente, IdTipoConstancia = tipo, IdsPruebas = new[] { prueba },
                    IdProfesional = idProfesional, NombreProfesional = "Se ignora", Usuario = "psi.ana"
                });
            }

            Assert.Contains((await Emitir(admin.IdProfesional)).Errores, e => e.Campo == "IdProfesional");
            var r = await Emitir(psicologa.IdProfesional);
            Assert.True(r.Ok, Mensajes(r.Errores));

            using var leer = _bd.Crear();
            var c = await leer.Constancias.SingleAsync();
            Assert.Equal(psicologa.IdPersona, c.IdPersonaProfesional);
            Assert.Equal("Lic. Lucía Paz Reyes", c.NombreProfesional);
        }

        [Fact]
        public async Task Una_empresa_de_otra_cuenta_no_ve_los_profesionales_y_la_bitacora_registra_el_alta()
        {
            Assert.True((await Guardar(Nuevo())).Ok);

            using var otraCuenta = _bd.CrearComo(DatosBase.TenantB);
            Assert.Equal(0, await otraCuenta.ProfesionalesPsicologia.CountAsync());

            using var leer = _bd.Crear();
            var p = await leer.ProfesionalesPsicologia.SingleAsync();
            var bitacora = await leer.BitacoraCambios.Where(b => b.Entidad == "profesionales").ToListAsync();
            Assert.Contains(bitacora, b => b.Operacion == "INSERT" && b.IdPersona == p.IdPersona);
        }
    }
}
