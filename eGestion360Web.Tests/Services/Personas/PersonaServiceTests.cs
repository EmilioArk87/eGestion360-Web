using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Personas
{
    public class PersonaServiceTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "rrhh.ana", IdEmpresa = DatosBase.EmpresaB };

        public void Dispose() => _bd.Dispose();

        // ── Utilidades ──────────────────────────────────────────────────────

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private PersonaService Servicio(ApplicationDbContext db) =>
            new(db,
                new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj),
                _reloj,
                NullLogger<PersonaService>.Instance);

        private static CrearPersonaInput Alta(int empresa = DatosBase.EmpresaB) => new()
        {
            Usuario = "rrhh.ana",
            Datos = new PersonaDatosInput
            {
                IdEmpresa = empresa,
                PrimerNombre = "Luis", PrimerApellido = "Pérez",
                TipoDocumento = "DNI", Documento = "0801-1990-12345",
                FechaNacimiento = new DateOnly(1990, 5, 10),
                Telefono = "9876-5432",
                Empleado = new EmpleadoDatosInput
                {
                    Cargo = "MECANICO", CodigoInterno = "M100",
                    FechaIngreso = new DateOnly(2026, 3, 1), TarifaDiaria = 350m
                }
            }
        };

        private async Task<int> CrearPersona(CrearPersonaInput input)
        {
            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(input);
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Mensaje)));
            return r.IdPersona!.Value;
        }

        /// <summary>Lo que ve la pantalla al abrir a una persona para editarla: todos sus datos tal como están.</summary>
        private async Task<ActualizarPersonaInput> Edicion(int idPersona, int idEmpresa, Action<PersonaDatosInput>? cambiar = null)
        {
            using var db = _bd.Crear();
            var p = await db.Personas.AsNoTracking()
                .Include(x => x.Documentos)
                .Include(x => x.Vinculos).ThenInclude(v => v.Empleado)
                .SingleAsync(x => x.IdPersona == idPersona);
            var vinculo = p.Vinculos.First(v => v.IdEmpresa == idEmpresa);
            var documento = p.Documentos.FirstOrDefault(d => !d.Eliminado);

            var datos = new PersonaDatosInput
            {
                IdEmpresa = idEmpresa, IdPersona = idPersona,
                PrimerNombre = p.PrimerNombre, SegundoNombre = p.SegundoNombre, PrimerApellido = p.PrimerApellido, SegundoApellido = p.SegundoApellido,
                TipoDocumento = documento?.TipoDocumento, Documento = documento?.Numero, PaisEmisor = documento?.PaisEmisor,
                FechaNacimiento = p.FechaNacimiento, Telefono = p.Telefono, Email = p.Email,
                Empleado = new EmpleadoDatosInput
                {
                    CodigoInterno = vinculo.Empleado?.CodigoInterno, Cargo = vinculo.Empleado?.Cargo,
                    FechaIngreso = vinculo.FechaInicio, FechaBaja = vinculo.FechaFin,
                    TarifaDiaria = vinculo.Empleado?.TarifaDiaria, MonedaTarifa = vinculo.Empleado?.MonedaTarifa
                }
            };
            cambiar?.Invoke(datos);
            return new ActualizarPersonaInput { Usuario = "rrhh.ana", Datos = datos };
        }

        private static Dictionary<string, JsonElement> Json(string? texto) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(texto!)!;

        // ── Crear ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Crear_guarda_persona_documento_vinculo_y_ficha_y_deja_la_bitacora()
        {
            int id;
            using (var db = Contexto())
            {
                var r = await Servicio(db).CrearAsync(Alta());
                Assert.Equal(EstadoCrearPersona.Creada, r.Estado);
                Assert.True(r.Ok);
                Assert.NotNull(r.IdPersonaEmpresa);
                id = r.IdPersona!.Value;
            }

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Documentos).Include(x => x.Vinculos).ThenInclude(v => v.Empleado).SingleAsync(x => x.IdPersona == id);

            // Persona maestra
            Assert.Equal("Luis", p.PrimerNombre);
            Assert.Equal("Pérez", p.PrimerApellido);
            Assert.Equal("LUIS PEREZ", p.NombreNormalizado);
            Assert.Equal(new DateOnly(1990, 5, 10), p.FechaNacimiento);
            Assert.Equal("98765432", p.Telefono);
            Assert.Equal(EstadosIdentidad.Verificada, p.EstadoIdentidad);
            Assert.Equal("rrhh.ana", p.CreadoPor);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, p.FechaCreacion);

            // LEGADO: las pantallas actuales siguen leyendo estas columnas
            Assert.Equal(DatosBase.EmpresaB, p.IdEmpresa);
            Assert.Equal("Luis", p.Nombres);
            Assert.Equal("Pérez", p.Apellidos);
            Assert.Equal("DNI", p.TipoDocumento);
            Assert.Equal("0801199012345", p.Documento);
            Assert.Equal("MECANICO", p.Cargo);
            Assert.Equal(350m, p.TarifaDiaria);
            Assert.Equal("HNL", p.MonedaTarifa);
            Assert.Equal(new DateOnly(2026, 3, 1), p.FechaIngreso);
            Assert.True(p.Activo);

            // Documento, vínculo y ficha
            var documento = Assert.Single(p.Documentos);
            Assert.Equal("0801199012345", documento.Numero);
            Assert.Equal("0801199012345", documento.NumeroNormalizado);
            Assert.True(documento.EsPrincipal);
            var vinculo = Assert.Single(p.Vinculos);
            Assert.Equal(TiposVinculo.Empleado, vinculo.TipoVinculo);
            Assert.Equal(DatosBase.EmpresaB, vinculo.IdEmpresa);
            Assert.Equal(new DateOnly(2026, 3, 1), vinculo.FechaInicio);
            Assert.Null(vinculo.FechaFin);
            Assert.NotNull(vinculo.Empleado);
            Assert.Equal("M100", vinculo.Empleado!.CodigoInterno);
            Assert.Equal("MECANICO", vinculo.Empleado.Cargo);
            Assert.Equal(DatosBase.EmpresaB, vinculo.Empleado.IdEmpresa);
            Assert.Equal(TiposVinculo.Empleado, vinculo.Empleado.TipoVinculo);

            // Bitácora: una fila de alta por cada registro, todas con el usuario y en una transacción
            var filas = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();
            Assert.Equal(4, filas.Count);
            Assert.All(filas, f => { Assert.Equal("INSERT", f.Operacion); Assert.Equal("rrhh.ana", f.Usuario); Assert.Equal(id, f.IdPersona); });
            Assert.Single(filas.Select(f => f.IdTransaccion).Distinct());
        }

        [Fact]
        public async Task Crear_sin_documento_usa_el_codigo_de_empleado_como_documento_legado_y_queda_pendiente()
        {
            var input = Alta(); input.Datos.TipoDocumento = null; input.Datos.Documento = null; input.Datos.Empleado!.CodigoInterno = "M101";
            var id = await CrearPersona(input);

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Documentos).SingleAsync(x => x.IdPersona == id);

            Assert.Equal(EstadosIdentidad.Pendiente, p.EstadoIdentidad);
            Assert.Empty(p.Documentos);
            Assert.Equal("INTERNO", p.TipoDocumento);   // LEGADO: convención de las personas sin documento
            Assert.Equal("M101", p.Documento);
        }

        [Fact]
        public async Task Crear_exige_el_rol_de_empleado()
        {
            var input = Alta(); input.Datos.Empleado = null;
            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(input);

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Empleado");
            Assert.Equal(0, await db.Personas.CountAsync());
        }

        [Fact]
        public async Task Crear_con_datos_invalidos_se_rechaza_con_los_errores_y_no_guarda_nada()
        {
            var input = Alta(); input.Datos.PrimerNombre = ""; input.Datos.Empleado!.Cargo = "AYUDANTE";
            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(input);

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "PrimerNombre");
            Assert.Contains(r.Errores, e => e.Campo == "Empleado.Cargo");
            Assert.Equal(0, await db.Personas.CountAsync());
            Assert.Equal(0, await db.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task Crear_ignora_el_modo_y_el_id_que_vengan_en_los_datos()
        {
            var input = Alta(); input.Datos.Modo = ModoValidacionPersona.Edicion; input.Datos.IdPersona = 999;
            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(input);

            Assert.True(r.Ok);
            Assert.NotEqual(999, r.IdPersona);
        }

        [Fact]
        public async Task Crear_con_un_documento_ya_registrado_en_la_misma_empresa_se_rechaza()
        {
            await CrearPersona(Alta());

            var otra = Alta(); otra.Datos.Empleado!.CodigoInterno = "M200";
            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(otra);

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Documento" && e.Mensaje.Contains("ya existe", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, await db.Personas.CountAsync());
        }

        // ── Crear: documento que ya existe en otra empresa (D8) ─────────────

        private int PersonaEnOtraEmpresa(DateOnly? nacimiento = null)
        {
            using var db = _bd.Crear();
            return PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Mario", "Mejía", dni: "0801199077777",
                nacimiento: nacimiento ?? new DateOnly(1988, 3, 4)).IdPersona;
        }

        private static CrearPersonaInput AltaDelDocumentoDeOtraEmpresa(string apellido = "MEJIA", DateOnly? nacimiento = null)
        {
            var input = Alta(DatosBase.EmpresaB);
            input.Datos.PrimerNombre = "Mario"; input.Datos.PrimerApellido = apellido;
            input.Datos.Documento = "0801199077777";
            input.Datos.FechaNacimiento = nacimiento ?? new DateOnly(1988, 3, 4);
            input.Datos.Empleado!.CodigoInterno = "M300";
            return input;
        }

        [Fact]
        public async Task Si_el_documento_existe_en_otra_empresa_y_la_verificacion_coincide_se_vincula_a_la_persona_existente()
        {
            var idExistente = PersonaEnOtraEmpresa();

            using (var db = Contexto())
            {
                var r = await Servicio(db).CrearAsync(AltaDelDocumentoDeOtraEmpresa());

                Assert.Equal(EstadoCrearPersona.Vinculada, r.Estado);
                Assert.Equal(idExistente, r.IdPersona);
                Assert.NotNull(r.IdPersonaEmpresa);
            }

            using var lectura = _bd.Crear();
            Assert.Equal(1, await lectura.Personas.CountAsync());   // no se creó otra persona
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Vinculos).ThenInclude(v => v.Empleado).SingleAsync();

            Assert.Equal(2, p.Vinculos.Count);
            var nuevo = p.Vinculos.Single(v => v.IdEmpresa == DatosBase.EmpresaB);
            Assert.Equal("M300", nuevo.Empleado!.CodigoInterno);
            Assert.Equal("MECANICO", nuevo.Empleado.Cargo);

            // La persona existente no se toca: ni nombre ni empresa legada.
            Assert.Equal("Mario", p.PrimerNombre);
            Assert.Equal("Mejía", p.PrimerApellido);
            Assert.Equal(DatosBase.EmpresaA, p.IdEmpresa);
        }

        [Theory]
        [InlineData("Soto", "1988-03-04")]    // otro primer apellido
        [InlineData("Mejía", "1988-03-05")]   // otra fecha de nacimiento
        public async Task Si_la_verificacion_no_coincide_se_rechaza_con_un_mensaje_generico_y_no_se_vincula(string apellido, string nacimiento)
        {
            PersonaEnOtraEmpresa();

            using var db = Contexto();
            var r = await Servicio(db).CrearAsync(AltaDelDocumentoDeOtraEmpresa(apellido, DateOnly.Parse(nacimiento)));

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            var error = Assert.Single(r.Errores);
            Assert.Equal("Documento", error.Campo);
            Assert.Contains("No se pudo registrar a la persona con esos datos", error.Mensaje);
            Assert.DoesNotContain("existe", error.Mensaje, StringComparison.OrdinalIgnoreCase);   // no revela que la persona existe
            Assert.Equal(1, await db.PersonaEmpresas.CountAsync());
        }

        [Fact]
        public async Task Si_la_persona_existente_no_tiene_fecha_de_nacimiento_no_se_puede_verificar()
        {
            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Mario", "Mejía", dni: "0801199077777", nacimiento: null);

            using var db2 = Contexto();
            var r = await Servicio(db2).CrearAsync(AltaDelDocumentoDeOtraEmpresa());

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            Assert.Equal(1, await db2.PersonaEmpresas.CountAsync());
        }

        [Fact]
        public async Task El_mensaje_de_verificacion_fallida_es_el_mismo_en_todos_los_casos()
        {
            PersonaEnOtraEmpresa();
            using var db = Contexto();

            var apellido = await Servicio(db).CrearAsync(AltaDelDocumentoDeOtraEmpresa("Soto"));
            var fecha = await Servicio(db).CrearAsync(AltaDelDocumentoDeOtraEmpresa("Mejía", new DateOnly(1999, 1, 1)));

            Assert.Equal(apellido.Errores.Single().Mensaje, fecha.Errores.Single().Mensaje);
        }

        // ── Crear: personas parecidas en la empresa ─────────────────────────

        [Fact]
        public async Task Si_hay_una_persona_parecida_en_la_empresa_pide_confirmacion_y_con_ella_crea()
        {
            int parecida;
            using (var db = _bd.Crear())
                parecida = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Perez", codigoInterno: "OLD1", nacimiento: new DateOnly(1990, 5, 10)).IdPersona;

            using (var db = Contexto())
            {
                var sin = await Servicio(db).CrearAsync(Alta());
                Assert.Equal(EstadoCrearPersona.RequiereConfirmacion, sin.Estado);
                Assert.False(sin.Ok);
                var p = Assert.Single(sin.Parecidas);
                Assert.Equal(parecida, p.IdPersona);
                Assert.Equal(1, await db.Personas.CountAsync());   // todavía no se creó

                var con = Alta(); con.ConfirmarQueEsOtraPersona = true;
                var creada = await Servicio(db).CrearAsync(con);
                Assert.Equal(EstadoCrearPersona.Creada, creada.Estado);
                Assert.Equal(2, await db.Personas.CountAsync());
            }
        }

        [Fact]
        public async Task Una_persona_con_otra_fecha_de_nacimiento_no_es_parecida_pero_una_sin_fecha_si()
        {
            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Perez", codigoInterno: "OLD1", nacimiento: new DateOnly(1971, 1, 1));
            using (var db = Contexto())
                Assert.Equal(EstadoCrearPersona.Creada, (await Servicio(db).CrearAsync(Alta())).Estado);

            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Ana", "Mejía", codigoInterno: "OLD2", nacimiento: null);
            var ana = Alta(); ana.Datos.PrimerNombre = "Ana"; ana.Datos.PrimerApellido = "Mejia"; ana.Datos.Documento = "0801199099999"; ana.Datos.Empleado!.CodigoInterno = "M777";
            using (var db = Contexto())
                Assert.Equal(EstadoCrearPersona.RequiereConfirmacion, (await Servicio(db).CrearAsync(ana)).Estado);
        }

        [Theory]
        [InlineData("Luis", "Perez", "1990-05-10", "Luis Perez Lopez", true)]      // mismo nombre sin segundo apellido
        [InlineData("Luis", "Mejia", "1990-05-10", "Luis Perez Lopez", false)]     // otro apellido
        [InlineData("Luis", "Lopez", "1990-05-10", "Luis Perez Lopez", true)]      // el apellido figura como segundo apellido: solo se avisa
        [InlineData("Luis", "Perez", "1991-05-10", "Luis Perez Lopez", false)]     // otra fecha de nacimiento
        [InlineData("Luis", "Perez", "1990-05-10", "Luis Alberto Perez", true)]    // el otro tiene segundo nombre
        [InlineData("Ana", "Perez", "1990-05-10", "Luis Perez Lopez", false)]      // otro primer nombre
        [InlineData("Alberto", "Perez", "1990-05-10", "Luis Alberto Perez", false)] // el primer nombre no es el primero del otro
        public async Task Cuenta_como_parecida_a_quien_tiene_el_mismo_primer_nombre_y_primer_apellido_y_la_misma_fecha_aunque_cambie_lo_demas(
            string nombre, string apellido, string nacimiento, string existente, bool esParecida)
        {
            var partes = existente.Split(' ');
            using (var db = _bd.Crear())
            {
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, partes[0], partes[^1], codigoInterno: "OLD1", nacimiento: new DateOnly(1990, 5, 10));
                // El nombre normalizado completo es el que escribe el servicio: aquí se arma como el de la ficha existente.
                p.NombreNormalizado = NombresPersona.Normalizar(partes);
                db.SaveChanges();
            }

            var input = Alta();
            input.Datos.PrimerNombre = nombre; input.Datos.PrimerApellido = apellido;
            input.Datos.FechaNacimiento = DateOnly.Parse(nacimiento);
            using var db2 = Contexto();
            var r = await Servicio(db2).CrearAsync(input);

            Assert.Equal(esParecida ? EstadoCrearPersona.RequiereConfirmacion : EstadoCrearPersona.Creada, r.Estado);
        }

        [Fact]
        public async Task Las_personas_parecidas_de_otra_empresa_no_se_revelan()
        {
            using (var db = _bd.Crear())
                PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Luis", "Perez", codigoInterno: "OLD1", nacimiento: new DateOnly(1990, 5, 10));

            using var db2 = Contexto();
            var r = await Servicio(db2).CrearAsync(Alta(DatosBase.EmpresaB));

            Assert.Equal(EstadoCrearPersona.Creada, r.Estado);
            Assert.Empty(r.Parecidas);
        }

        [Fact]
        public async Task Una_persona_fusionada_o_eliminada_no_cuenta_como_parecida()
        {
            using (var db = _bd.Crear())
            {
                var vieja = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Perez", codigoInterno: "OLD1", nacimiento: new DateOnly(1990, 5, 10));
                vieja.Eliminado = true;
                db.SaveChanges();
            }

            using var db2 = Contexto();
            Assert.Equal(EstadoCrearPersona.Creada, (await Servicio(db2).CrearAsync(Alta())).Estado);
        }

        // ── Actualizar ──────────────────────────────────────────────────────

        [Fact]
        public async Task Actualizar_no_encuentra_a_una_persona_de_otra_empresa_ni_una_que_no_existe()
        {
            var id = await CrearPersona(Alta(DatosBase.EmpresaB));

            using var db = Contexto();
            var deOtraEmpresa = await Edicion(id, DatosBase.EmpresaB);
            deOtraEmpresa.Datos.IdEmpresa = DatosBase.EmpresaA;   // la sesión es de otra empresa
            Assert.Equal(EstadoActualizarPersona.NoEncontrada, (await Servicio(db).ActualizarAsync(deOtraEmpresa)).Estado);

            var inexistente = await Edicion(id, DatosBase.EmpresaB);
            inexistente.Datos.IdPersona = 9999;
            Assert.Equal(EstadoActualizarPersona.NoEncontrada, (await Servicio(db).ActualizarAsync(inexistente)).Estado);

            var sinId = await Edicion(id, DatosBase.EmpresaB);
            sinId.Datos.IdPersona = null;
            Assert.Equal(EstadoActualizarPersona.NoEncontrada, (await Servicio(db).ActualizarAsync(sinId)).Estado);
        }

        [Fact]
        public async Task Actualizar_cambia_los_datos_mantiene_los_campos_legados_y_registra_cada_cambio()
        {
            var id = await CrearPersona(Alta());
            using (var db = Contexto()) { db.BitacoraCambios.RemoveRange(db.BitacoraCambios); await db.SaveChangesAsync(); }

            _reloj.Avanzar(TimeSpan.FromHours(1));
            var edicion = await Edicion(id, DatosBase.EmpresaB, d =>
            {
                d.SegundoNombre = "Alberto";
                d.Telefono = "2222-3333";
                d.Email = "Luis.Perez@Empresa.com";
                d.Empleado!.Cargo = "SUPERVISOR";
                d.Empleado.TarifaDiaria = 400m;
            });

            using (var db = Contexto())
            {
                var r = await Servicio(db).ActualizarAsync(edicion);
                Assert.Equal(EstadoActualizarPersona.Actualizada, r.Estado);
                Assert.Equal(id, r.IdPersona);
            }

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Vinculos).ThenInclude(v => v.Empleado).SingleAsync(x => x.IdPersona == id);

            Assert.Equal("Alberto", p.SegundoNombre);
            Assert.Equal("Luis Alberto", p.Nombres);              // LEGADO compuesto
            Assert.Equal("LUIS ALBERTO PEREZ", p.NombreNormalizado);
            Assert.Equal("22223333", p.Telefono);
            Assert.Equal("luis.perez@empresa.com", p.Email);
            Assert.Equal("SUPERVISOR", p.Cargo);                  // LEGADO
            Assert.Equal(400m, p.TarifaDiaria);                   // LEGADO
            Assert.Equal("rrhh.ana", p.ModificadoPor);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, p.FechaModificacion);
            var empleado = p.Vinculos.Single().Empleado!;
            Assert.Equal("SUPERVISOR", empleado.Cargo);
            Assert.Equal(400m, empleado.TarifaDiaria);
            Assert.Equal("rrhh.ana", empleado.ModificadoPor);

            var campos = await lectura.BitacoraCambios.AsNoTracking().Where(b => b.Operacion == "UPDATE").Select(b => b.Entidad + "." + b.Campo).ToListAsync();
            Assert.Contains("personas.segundo_nombre", campos);
            Assert.Contains("personas.nombres", campos);
            Assert.Contains("personas.telefono", campos);
            Assert.Contains("personas.email", campos);
            Assert.Contains("personas.cargo", campos);
            Assert.Contains("empleados.cargo", campos);
            Assert.Contains("empleados.tarifa_diaria", campos);
            Assert.DoesNotContain("personas.primer_nombre", campos);          // no cambió
            Assert.DoesNotContain("personas.modificado_por", campos);          // no se registra
        }

        [Fact]
        public async Task Actualizar_sin_cambios_no_toca_nada()
        {
            var id = await CrearPersona(Alta());
            using (var db = Contexto()) { db.BitacoraCambios.RemoveRange(db.BitacoraCambios); await db.SaveChangesAsync(); }

            using (var db = Contexto())
                Assert.Equal(EstadoActualizarPersona.Actualizada, (await Servicio(db).ActualizarAsync(await Edicion(id, DatosBase.EmpresaB))).Estado);

            using var lectura = _bd.Crear();
            Assert.Equal(0, await lectura.BitacoraCambios.CountAsync());
            Assert.Null((await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == id)).ModificadoPor);
        }

        [Fact]
        public async Task Actualizar_con_un_error_de_validacion_no_cambia_nada()
        {
            var id = await CrearPersona(Alta());
            var edicion = await Edicion(id, DatosBase.EmpresaB, d => { d.PrimerNombre = "J0se"; d.Telefono = "12"; });

            using (var db = Contexto())
            {
                var r = await Servicio(db).ActualizarAsync(edicion);
                Assert.Equal(EstadoActualizarPersona.Rechazada, r.Estado);
                Assert.Contains(r.Errores, e => e.Campo == "PrimerNombre");
            }

            using var lectura = _bd.Crear();
            Assert.Equal("Luis", (await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == id)).PrimerNombre);
        }

        [Fact]
        public async Task Actualizar_agrega_el_documento_a_quien_no_lo_tenia_y_pasa_la_identidad_a_verificada()
        {
            var input = Alta(); input.Datos.TipoDocumento = null; input.Datos.Documento = null;
            var id = await CrearPersona(input);

            var edicion = await Edicion(id, DatosBase.EmpresaB, d => { d.TipoDocumento = "DNI"; d.Documento = "0801-1990-55555"; });
            using (var db = Contexto())
                Assert.Equal(EstadoActualizarPersona.Actualizada, (await Servicio(db).ActualizarAsync(edicion)).Estado);

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Documentos).SingleAsync(x => x.IdPersona == id);
            var documento = Assert.Single(p.Documentos);
            Assert.Equal("0801199055555", documento.Numero);
            Assert.True(documento.EsPrincipal);
            Assert.Equal(EstadosIdentidad.Verificada, p.EstadoIdentidad);
            Assert.Equal("0801199055555", p.Documento);   // LEGADO
            Assert.Equal("DNI", p.TipoDocumento);         // LEGADO
        }

        [Fact]
        public async Task Actualizar_con_el_documento_vacio_lo_deja_como_esta()
        {
            var id = await CrearPersona(Alta());
            var edicion = await Edicion(id, DatosBase.EmpresaB, d => { d.TipoDocumento = null; d.Documento = null; d.Telefono = "2222-3333"; });

            using (var db = Contexto())
                Assert.Equal(EstadoActualizarPersona.Actualizada, (await Servicio(db).ActualizarAsync(edicion)).Estado);

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().Include(x => x.Documentos).SingleAsync(x => x.IdPersona == id);
            Assert.Equal("0801199012345", Assert.Single(p.Documentos).Numero);
            Assert.Equal(EstadosIdentidad.Verificada, p.EstadoIdentidad);
        }

        [Fact]
        public async Task Actualizar_corrige_el_numero_del_documento_del_mismo_tipo_sin_crear_otro()
        {
            var id = await CrearPersona(Alta());
            var edicion = await Edicion(id, DatosBase.EmpresaB, d => d.Documento = "0801-1990-99999");

            using (var db = Contexto())
                Assert.Equal(EstadoActualizarPersona.Actualizada, (await Servicio(db).ActualizarAsync(edicion)).Estado);

            using var lectura = _bd.Crear();
            var documento = Assert.Single(await lectura.PersonaDocumentos.AsNoTracking().ToListAsync());
            Assert.Equal("0801199099999", documento.Numero);
            Assert.Equal("0801199099999", documento.NumeroNormalizado);
        }

        [Fact]
        public async Task Actualizar_el_cargo_a_uno_no_definido_se_rechaza()
        {
            var id = await CrearPersona(Alta());
            var edicion = await Edicion(id, DatosBase.EmpresaB, d => d.Empleado!.Cargo = "AYUDANTE");

            using var db = Contexto();
            var r = await Servicio(db).ActualizarAsync(edicion);

            Assert.Equal(EstadoActualizarPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Empleado.Cargo");
        }

        // ── Actualizar: documento de otra empresa → fusión ──────────────────

        /// <summary>Una persona ya existente con DNI en la empresa A y otra sin DNI en la empresa B que en realidad es la misma.</summary>
        private (int PrincipalEnA, int SobranteEnB) LaMismaPersonaEnDosEmpresas()
        {
            using var db = _bd.Crear();
            var a = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Mario", "Mejía", dni: "0801199077777", nacimiento: new DateOnly(1988, 3, 4));
            var b = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Mario", "Mejia", codigoInterno: "B900", nacimiento: new DateOnly(1988, 3, 4));
            return (a.IdPersona, b.IdPersona);
        }

        [Fact]
        public async Task Capturar_un_documento_que_ya_es_de_otra_empresa_ofrece_la_fusion_sin_guardar_nada()
        {
            var (principal, sobrante) = LaMismaPersonaEnDosEmpresas();
            var edicion = await Edicion(sobrante, DatosBase.EmpresaB, d => { d.TipoDocumento = "DNI"; d.Documento = "0801199077777"; });

            using (var db = Contexto())
            {
                var r = await Servicio(db).ActualizarAsync(edicion);
                Assert.Equal(EstadoActualizarPersona.RequiereFusion, r.Estado);
                Assert.False(r.Ok);
                Assert.Null(r.IdPersona);   // no se revela la persona hasta que se confirme
            }

            using var lectura = _bd.Crear();
            var p = await lectura.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == sobrante);
            Assert.False(p.Eliminado);
            Assert.Null(p.IdPersonaPrincipal);
            Assert.Equal(0, await lectura.PersonaDocumentos.CountAsync(d => d.IdPersona == sobrante));
        }

        [Fact]
        public async Task Con_la_fusion_confirmada_la_ficha_sobrante_pasa_a_la_persona_existente()
        {
            var (principal, sobrante) = LaMismaPersonaEnDosEmpresas();
            var edicion = await Edicion(sobrante, DatosBase.EmpresaB, d => { d.TipoDocumento = "DNI"; d.Documento = "0801199077777"; });
            edicion.ConfirmarFusion = true;

            using (var db = Contexto())
            {
                var r = await Servicio(db).ActualizarAsync(edicion);
                Assert.Equal(EstadoActualizarPersona.Fusionada, r.Estado);
                Assert.True(r.Ok);
                Assert.Equal(principal, r.IdPersona);
            }

            using var lectura = _bd.Crear();
            var fusionada = await lectura.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == sobrante);
            Assert.True(fusionada.Eliminado);
            Assert.Equal(principal, fusionada.IdPersonaPrincipal);

            var vinculos = await lectura.PersonaEmpresas.AsNoTracking().Where(v => v.IdPersona == principal && !v.Eliminado).ToListAsync();
            Assert.Equal(new[] { DatosBase.EmpresaA, DatosBase.EmpresaB }, vinculos.Select(v => v.IdEmpresa).Order().ToArray());
        }

        [Fact]
        public async Task Capturar_un_documento_de_otra_empresa_sin_que_coincida_la_verificacion_se_rechaza_igual_que_siempre()
        {
            var (_, sobrante) = LaMismaPersonaEnDosEmpresas();
            var edicion = await Edicion(sobrante, DatosBase.EmpresaB, d =>
            {
                d.TipoDocumento = "DNI"; d.Documento = "0801199077777"; d.PrimerApellido = "Otro";
            });

            using var db = Contexto();
            var r = await Servicio(db).ActualizarAsync(edicion);

            Assert.Equal(EstadoActualizarPersona.Rechazada, r.Estado);
            var error = Assert.Single(r.Errores);
            Assert.Contains("No se pudo registrar a la persona con esos datos", error.Mensaje);
        }

        // ── Fusionar ────────────────────────────────────────────────────────

        private (Vehiculo Vehiculo, int Salario, int Peaje) RegistrosOperativosDe(ApplicationDbContext db, int idPersona)
        {
            var ahora = DateTime.UtcNow;
            var tipo = new TipoVehiculo { IdEmpresa = DatosBase.EmpresaB, Codigo = "BUS", Nombre = "Bus", CreadoPor = "pruebas", FechaCreacion = ahora };
            var vehiculo = new Vehiculo { IdEmpresa = DatosBase.EmpresaB, TipoVehiculo = tipo, Placa = "HAA1234", CreadoPor = "pruebas", FechaCreacion = ahora };
            var salario = new SalarioDiario { IdEmpresa = DatosBase.EmpresaB, Vehiculo = vehiculo, IdPersona = idPersona, Fecha = new DateOnly(2026, 9, 1), Monto = 350m, CreadoPor = "pruebas", FechaCreacion = ahora };
            var peaje = new Peaje { IdEmpresa = DatosBase.EmpresaB, Vehiculo = vehiculo, IdConductor = idPersona, Fecha = new DateOnly(2026, 9, 1), Monto = 20m, CreadoPor = "pruebas", FechaCreacion = ahora };
            db.SalariosDiarios.Add(salario);
            db.Peajes.Add(peaje);
            db.SaveChanges();
            return (vehiculo, salario.IdSalarioDiario, peaje.IdPeaje);
        }

        [Fact]
        public async Task Fusionar_mueve_documentos_y_registros_operativos_y_da_de_baja_el_vinculo_duplicado()
        {
            int sobrante, principal, salario, peaje;
            using (var db = _bd.Crear())
            {
                // Las dos fichas son de la empresa B: la sobrante tiene el DNI, la principal no.
                var s = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", dni: "0801199011111", codigoInterno: "S1", nacimiento: new DateOnly(1980, 2, 2));
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "P1", nacimiento: new DateOnly(1980, 2, 2));
                sobrante = s.IdPersona; principal = p.IdPersona;
                var op = RegistrosOperativosDe(db, sobrante); salario = op.Salario; peaje = op.Peaje;
            }

            ResultadoFusion r;
            using (var db = Contexto())
                r = await Servicio(db).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = sobrante, IdPersonaPrincipal = principal, Usuario = "rrhh.ana" });

            Assert.True(r.Ok, string.Join(" | ", r.Errores));
            Assert.Equal(principal, r.IdPersonaPrincipal);
            Assert.Equal(new ResumenFusion(Documentos: 1, DocumentosRepetidos: 0, Vinculos: 0, VinculosEnConflicto: 1, RegistrosOperativos: 2), r.Resumen);

            using var lectura = _bd.Crear();

            // La ficha sobrante queda marcada y apuntando a la principal
            var fusionada = await lectura.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == sobrante);
            Assert.True(fusionada.Eliminado);
            Assert.False(fusionada.Activo);
            Assert.Equal(principal, fusionada.IdPersonaPrincipal);
            Assert.NotNull(fusionada.FechaEliminado);

            // El documento pasó a la principal, que queda verificada
            var documento = await lectura.PersonaDocumentos.AsNoTracking().SingleAsync();
            Assert.Equal(principal, documento.IdPersona);
            Assert.True(documento.EsPrincipal);
            Assert.False(documento.Eliminado);
            Assert.Equal(EstadosIdentidad.Verificada, (await lectura.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == principal)).EstadoIdentidad);

            // Las dos tenían un vínculo vigente de empleado en la empresa B: el de la sobrante se da de baja con su ficha
            var vinculos = await lectura.PersonaEmpresas.AsNoTracking().Include(v => v.Empleado).ToListAsync();
            var deLaSobrante = vinculos.Single(v => v.IdPersona == sobrante);
            Assert.True(deLaSobrante.Eliminado); Assert.False(deLaSobrante.Activo);
            Assert.True(deLaSobrante.Empleado!.Eliminado);
            Assert.False(vinculos.Single(v => v.IdPersona == principal).Eliminado);

            // Los registros operativos apuntan a la principal
            Assert.Equal(principal, (await lectura.SalariosDiarios.AsNoTracking().SingleAsync(x => x.IdSalarioDiario == salario)).IdPersona);
            Assert.Equal(principal, (await lectura.Peajes.AsNoTracking().SingleAsync(x => x.IdPeaje == peaje)).IdConductor);

            // Y la fusión quedó en la bitácora
            var fila = await lectura.BitacoraCambios.AsNoTracking().SingleAsync(b => b.Campo == "fusion");
            Assert.Equal(sobrante, fila.IdRegistro);
            Assert.Equal(principal, fila.IdPersona);
            Assert.Equal("rrhh.ana", fila.Usuario);
            Assert.Equal(2, Json(fila.ValorNuevo)["registros_operativos"].GetInt32());
            Assert.Contains(await lectura.BitacoraCambios.AsNoTracking().ToListAsync(), b => b.Entidad == "personas" && b.IdRegistro == sobrante && b.Campo == "id_persona_principal");
        }

        [Fact]
        public async Task Fusionar_pasa_a_la_principal_los_vinculos_de_otras_empresas_de_la_sobrante()
        {
            int sobrante, principal;
            using (var db = _bd.Crear())
            {
                var s = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "S1");
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "P1");
                sobrante = s.IdPersona; principal = p.IdPersona;

                // La sobrante además trabajó en la empresa A (vínculo ya terminado) y es cliente de la B
                db.PersonaEmpresas.Add(new PersonaEmpresa { IdEmpresa = DatosBase.EmpresaA, IdPersona = sobrante, TipoVinculo = TiposVinculo.Cliente, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                db.SaveChanges();
            }

            using (var db = Contexto())
            {
                var r = await Servicio(db).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = sobrante, IdPersonaPrincipal = principal, Usuario = "rrhh.ana" });
                Assert.True(r.Ok);
                Assert.Equal(1, r.Resumen!.Vinculos);
                Assert.Equal(1, r.Resumen.VinculosEnConflicto);
            }

            using var lectura = _bd.Crear();
            var delCliente = await lectura.PersonaEmpresas.AsNoTracking().SingleAsync(v => v.TipoVinculo == TiposVinculo.Cliente);
            Assert.Equal(principal, delCliente.IdPersona);
            Assert.False(delCliente.Eliminado);
        }

        [Fact]
        public async Task Fusionar_da_de_baja_el_documento_que_la_principal_ya_tiene()
        {
            int sobrante, principal;
            using (var db = _bd.Crear())
            {
                var s = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", dni: "0801199022222", codigoInterno: "S1");
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "P1");
                // La principal ya tiene el mismo documento, con otro formato
                db.PersonaDocumentos.Add(new PersonaDocumento { IdPersona = p.IdPersona, TipoDocumento = "DNI", PaisEmisor = "HN", Numero = "0801-1990-22222", EsPrincipal = true, Eliminado = false, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                s.Documentos.Single().Eliminado = false;
                db.SaveChanges();
                sobrante = s.IdPersona; principal = p.IdPersona;
            }

            ResultadoFusion r;
            using (var db = Contexto())
                r = await Servicio(db).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = sobrante, IdPersonaPrincipal = principal, Usuario = "rrhh.ana" });

            Assert.True(r.Ok);
            Assert.Equal(0, r.Resumen!.Documentos);
            Assert.Equal(1, r.Resumen.DocumentosRepetidos);

            using var lectura = _bd.Crear();
            var documentos = await lectura.PersonaDocumentos.AsNoTracking().ToListAsync();
            Assert.Single(documentos, d => !d.Eliminado && d.IdPersona == principal);
            Assert.Single(documentos, d => d.Eliminado && d.IdPersona == sobrante);
        }

        [Fact]
        public async Task Fusionar_solo_con_el_documento_de_la_sobrante_deja_un_unico_documento_principal()
        {
            int sobrante, principal;
            using (var db = _bd.Crear())
            {
                var s = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", dni: "0801199033333", codigoInterno: "S1");
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", dni: "0801199044444", codigoInterno: "P1");
                sobrante = s.IdPersona; principal = p.IdPersona;
            }

            using (var db = Contexto())
                Assert.True((await Servicio(db).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = sobrante, IdPersonaPrincipal = principal, Usuario = "rrhh.ana" })).Ok);

            using var lectura = _bd.Crear();
            var delaPrincipal = await lectura.PersonaDocumentos.AsNoTracking().Where(d => d.IdPersona == principal && !d.Eliminado).ToListAsync();
            Assert.Equal(2, delaPrincipal.Count);
            Assert.Single(delaPrincipal, d => d.EsPrincipal);   // UX_persona_documentos_principal: uno solo
        }

        [Fact]
        public async Task Fusionar_exige_que_las_dos_personas_sean_de_la_empresa_de_la_sesion()
        {
            int deB, deA;
            using (var db = _bd.Crear())
            {
                deB = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "S1").IdPersona;
                deA = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Rosa", "Lagos", codigoInterno: "P1").IdPersona;
            }

            using var db2 = Contexto();
            var r = await Servicio(db2).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = deB, IdPersonaPrincipal = deA, Usuario = "rrhh.ana" });

            Assert.False(r.Ok);
            Assert.Contains("No se encontraron las dos personas", r.Errores.Single());
            Assert.False((await db2.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == deB)).Eliminado);
        }

        [Fact]
        public async Task Fusionar_rechaza_la_misma_persona_y_las_ya_fusionadas()
        {
            int a, b;
            using (var db = _bd.Crear())
            {
                a = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "S1").IdPersona;
                b = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "P1").IdPersona;
            }

            using var db2 = Contexto();
            var servicio = Servicio(db2);

            Assert.False((await servicio.FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = a, IdPersonaPrincipal = a })).Ok);
            Assert.False((await servicio.FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = 0, IdPersonaPrincipal = b })).Ok);

            Assert.True((await servicio.FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = a, IdPersonaPrincipal = b })).Ok);
            // Ya fusionada: la sobrante no vuelve a ser visible ni se puede fusionar otra vez
            Assert.False((await servicio.FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = a, IdPersonaPrincipal = b })).Ok);
        }

        // ── Cambiar estado ──────────────────────────────────────────────────

        private static CambiarEstadoPersonaInput Estado(int idPersona, int idEmpresa, bool activo) =>
            new() { IdPersona = idPersona, IdEmpresa = idEmpresa, Activo = activo, Usuario = "rrhh.ana" };

        [Fact]
        public async Task Desactivar_apaga_el_vinculo_la_ficha_y_la_columna_legada_y_se_puede_volver_a_activar()
        {
            var id = await CrearPersona(Alta());

            using (var db = Contexto())
            {
                var r = await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaB, activo: false));
                Assert.True(r.Ok);
                Assert.True(r.Encontrada);
                Assert.Equal("Luis Pérez", r.NombreCompleto);
            }

            using (var lectura = _bd.Crear())
            {
                var p = await lectura.Personas.AsNoTracking().Include(x => x.Vinculos).ThenInclude(v => v.Empleado).SingleAsync(x => x.IdPersona == id);
                Assert.False(p.Activo);
                Assert.False(p.Vinculos.Single().Activo);
                Assert.False(p.Vinculos.Single().Empleado!.Activo);
                Assert.Equal("rrhh.ana", p.ModificadoPor);
                Assert.Null(p.Vinculos.Single().FechaFin);   // desactivar no termina el vínculo
            }

            using (var db = Contexto())
                Assert.True((await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaB, activo: true))).Ok);

            using var lectura2 = _bd.Crear();
            var activa = await lectura2.Personas.AsNoTracking().Include(x => x.Vinculos).ThenInclude(v => v.Empleado).SingleAsync(x => x.IdPersona == id);
            Assert.True(activa.Activo);
            Assert.True(activa.Vinculos.Single().Empleado!.Activo);
        }

        [Fact]
        public async Task Desactivar_deja_registro_en_la_bitacora()
        {
            var id = await CrearPersona(Alta());
            using (var db = Contexto()) { db.BitacoraCambios.RemoveRange(db.BitacoraCambios); await db.SaveChangesAsync(); }

            using (var db = Contexto())
                await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaB, activo: false));

            using var lectura = _bd.Crear();
            var campos = await lectura.BitacoraCambios.AsNoTracking().Select(b => b.Entidad + "." + b.Campo).ToListAsync();
            Assert.Contains("personas.activo", campos);
            Assert.Contains("persona_empresa.activo", campos);
            Assert.Contains("empleados.activo", campos);
        }

        [Fact]
        public async Task Desactivar_en_una_empresa_no_apaga_a_la_persona_si_sigue_activa_en_otra()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var p = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Mario", "Mejía", dni: "0801199077777", codigoInterno: "A1");
                p.Vinculos.Add(new PersonaEmpresa
                {
                    IdEmpresa = DatosBase.EmpresaB, TipoVinculo = TiposVinculo.Empleado, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow,
                    Empleado = new Empleado { IdEmpresa = DatosBase.EmpresaB, Cargo = "MECANICO", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow }
                });
                db.SaveChanges();
                id = p.IdPersona;
            }

            using (var db = Contexto())
                Assert.True((await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaB, activo: false))).Ok);

            using var lectura = _bd.Crear();
            var persona = await lectura.Personas.AsNoTracking().Include(x => x.Vinculos).SingleAsync(x => x.IdPersona == id);
            Assert.True(persona.Activo);   // sigue activa en la empresa A
            Assert.False(persona.Vinculos.Single(v => v.IdEmpresa == DatosBase.EmpresaB).Activo);
            Assert.True(persona.Vinculos.Single(v => v.IdEmpresa == DatosBase.EmpresaA).Activo);

            using (var db = Contexto())
                Assert.True((await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaA, activo: false))).Ok);
            using var lectura2 = _bd.Crear();
            Assert.False((await lectura2.Personas.AsNoTracking().SingleAsync(x => x.IdPersona == id)).Activo);   // ahora no queda ninguno activo
        }

        [Fact]
        public async Task Cambiar_el_estado_de_una_persona_de_otra_empresa_o_inexistente_no_hace_nada()
        {
            var id = await CrearPersona(Alta(DatosBase.EmpresaB));

            using var db = Contexto();
            var deOtra = await Servicio(db).CambiarEstadoAsync(Estado(id, DatosBase.EmpresaA, activo: false));
            Assert.False(deOtra.Ok); Assert.False(deOtra.Encontrada); Assert.Equal(string.Empty, deOtra.NombreCompleto);

            Assert.False((await Servicio(db).CambiarEstadoAsync(Estado(9999, DatosBase.EmpresaB, false))).Encontrada);
            Assert.False((await Servicio(db).CambiarEstadoAsync(Estado(id, 0, false))).Encontrada);
            Assert.False((await Servicio(db).CambiarEstadoAsync(Estado(0, DatosBase.EmpresaB, false))).Encontrada);

            using var lectura = _bd.Crear();
            Assert.True((await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == id)).Activo);
        }

        [Fact]
        public async Task Despues_de_fusionar_la_ficha_sobrante_ya_no_se_puede_editar()
        {
            int a, b;
            using (var db = _bd.Crear())
            {
                a = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "S1").IdPersona;
                b = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Rosa", "Lagos", codigoInterno: "P1").IdPersona;
            }
            using (var db = Contexto())
                Assert.True((await Servicio(db).FusionarAsync(new FusionarPersonasInput { IdEmpresa = DatosBase.EmpresaB, IdPersonaSobrante = a, IdPersonaPrincipal = b })).Ok);

            var edicion = await Edicion(b, DatosBase.EmpresaB);
            edicion.Datos.IdPersona = a;
            using var db2 = Contexto();
            Assert.Equal(EstadoActualizarPersona.NoEncontrada, (await Servicio(db2).ActualizarAsync(edicion)).Estado);
        }
    }
}
