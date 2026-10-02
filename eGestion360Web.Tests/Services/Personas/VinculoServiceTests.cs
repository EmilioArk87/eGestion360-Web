using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Personas
{
    public class VinculoServiceTests : IDisposable
    {
        private const string Dni = "0801-1990-12345";
        private const string DniNormalizado = "0801199012345";
        private const string OtroDni = "0801-1985-11111";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "ventas.ana", IdEmpresa = DatosBase.EmpresaA };

        public void Dispose() => _bd.Dispose();

        // ── Utilidades ──────────────────────────────────────────────────────

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));

        private VinculoService Servicio(ApplicationDbContext db) =>
            new(db,
                new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj),
                Options.Create(new PersonaValidacionOptions()),
                _reloj,
                NullLogger<VinculoService>.Instance);

        private PersonaService ServicioPersonas(ApplicationDbContext db) =>
            new(db,
                new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj),
                _reloj,
                NullLogger<PersonaService>.Instance);

        private static RegistrarClienteNaturalInput Alta(
            int empresa = DatosBase.EmpresaA, string codigo = "C-0001", string nombre = "Luis", string apellido = "Pérez",
            string? documento = Dni, Action<RegistrarClienteNaturalInput>? ajustar = null)
        {
            var input = new RegistrarClienteNaturalInput
            {
                IdEmpresa = empresa,
                Usuario = "ventas.ana",
                Persona = new PersonaDatosInput
                {
                    PrimerNombre = nombre, PrimerApellido = apellido,
                    TipoDocumento = documento != null ? "DNI" : null, Documento = documento
                },
                Cliente = new ClienteDatosInput { Codigo = codigo }
            };
            ajustar?.Invoke(input);
            return input;
        }

        private async Task<ResultadoRegistrarCliente> Registrar(RegistrarClienteNaturalInput input)
        {
            using var db = Contexto();
            return await Servicio(db).RegistrarClienteNaturalAsync(input);
        }

        private async Task<ResultadoRegistrarCliente> RegistrarOk(RegistrarClienteNaturalInput input)
        {
            var r = await Registrar(input);
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Campo + ": " + e.Mensaje)));
            return r;
        }

        /// <summary>Crea a una persona que ya es empleado de la empresa (con su vínculo, ficha y documento).</summary>
        private int Insertar(int empresa, string nombre, string apellido, string? dni = null, string codigo = "E1")
        {
            using var db = _bd.Crear();
            return PersonasDePrueba.Insertar(db, empresa, nombre, apellido, dni: dni, codigoInterno: codigo).IdPersona;
        }

        private static string Mensajes(ResultadoRegistrarCliente r) =>
            string.Join(" | ", r.Errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        // ── Cliente nuevo con persona nueva ─────────────────────────────────

        [Fact]
        public async Task Registra_a_un_cliente_natural_con_su_persona_nueva_su_vinculo_y_su_ficha()
        {
            var r = await RegistrarOk(Alta(ajustar: i =>
            {
                i.Cliente.NombreComercial = "Taller Pérez";
                i.Cliente.LimiteCredito = 5000m;
            }));

            Assert.Equal(EstadoRegistrarCliente.Creado, r.Estado);

            using var db = _bd.Crear();
            var persona = await db.Personas.AsNoTracking().Include(p => p.Documentos).Include(p => p.Vinculos).SingleAsync();
            Assert.Equal(r.IdPersona, persona.IdPersona);
            Assert.Equal("Luis", persona.PrimerNombre);
            Assert.Equal(EstadosIdentidad.Verificada, persona.EstadoIdentidad);
            Assert.Equal(DniNormalizado, persona.Documentos.Single().Numero);

            var vinculo = persona.Vinculos.Single();
            Assert.Equal(TiposVinculo.Cliente, vinculo.TipoVinculo);
            Assert.Equal(DatosBase.EmpresaA, vinculo.IdEmpresa);
            Assert.Equal(RelojFijo.Hoy, vinculo.FechaInicio);
            Assert.Null(vinculo.FechaFin);
            Assert.Equal(r.IdPersonaEmpresa, vinculo.IdPersonaEmpresa);

            var cliente = await db.Clientes.AsNoTracking().SingleAsync();
            Assert.Equal(r.IdCliente, cliente.IdCliente);
            Assert.Equal("natural", cliente.Tipo);
            Assert.Equal("C-0001", cliente.Codigo);
            Assert.Equal("Luis Pérez", cliente.RazonSocial);
            Assert.Equal("Taller Pérez", cliente.NombreComercial);
            Assert.Equal(5000m, cliente.LimiteCredito);
            Assert.Equal("HNL", cliente.MonedaIsoDefault);
            Assert.Equal(vinculo.IdPersonaEmpresa, cliente.IdPersonaEmpresa);
            Assert.Equal(TiposVinculo.Cliente, cliente.TipoVinculo);
            Assert.True(cliente.Activo);
        }

        [Fact]
        public async Task Quien_solo_es_cliente_no_tiene_empresa_ni_cargo_en_las_columnas_viejas_y_no_aparece_en_las_listas_de_personal()
        {
            await RegistrarOk(Alta());

            using var db = _bd.Crear();
            var persona = await db.Personas.AsNoTracking().SingleAsync();
            Assert.Null(persona.IdEmpresa);
            Assert.Null(persona.Cargo);
            Assert.Null(persona.Documento);
            Assert.Null(persona.TipoDocumento);

            // Lo que hacen las pantallas viejas (salarios, peajes...) y la lista nueva de personal.
            Assert.Empty(await db.Personas.Where(p => p.IdEmpresa == DatosBase.EmpresaA && p.Activo).ToListAsync());
            var lista = await new PersonaConsultaService(db, Options.Create(new PersonaValidacionOptions()))
                .ListarAsync(DatosBase.EmpresaA, new PersonaFiltro());
            Assert.Empty(lista);
        }

        [Fact]
        public async Task Normaliza_los_datos_comerciales()
        {
            await RegistrarOk(Alta(ajustar: i =>
            {
                i.Cliente.Email = "  Ventas@Taller.HN ";
                i.Cliente.Telefono = "+504 2222-3333";
                i.Cliente.IdentificadorFiscal = "0801-1990-123451";
                i.Cliente.Direccion = "  Barrio Abajo   ";
                i.Cliente.Ciudad = "Tegucigalpa";
            }));

            using var db = _bd.Crear();
            var c = await db.Clientes.AsNoTracking().SingleAsync();
            Assert.Equal("ventas@taller.hn", c.Email);
            Assert.Equal("22223333", c.Telefono);
            Assert.Equal("08011990123451", c.IdentificadorFiscal);
            Assert.Equal("Barrio Abajo", c.Direccion);
            Assert.Equal("Tegucigalpa", c.Ciudad);
        }

        [Fact]
        public async Task Usa_la_condicion_de_pago_de_la_empresa()
        {
            int idCondicion;
            using (var db = _bd.Crear())
            {
                var condicion = new CondicionPago
                {
                    IdEmpresa = DatosBase.EmpresaA, Codigo = "30D", Nombre = "Crédito 30 días", Tipo = "credito", DiasCredito = 30,
                    CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };
                db.CondicionesPago.Add(condicion);
                db.SaveChanges();
                idCondicion = condicion.IdCondicionPago;
            }

            await RegistrarOk(Alta(ajustar: i => i.Cliente.IdCondicionPagoDefault = idCondicion));

            using var db2 = _bd.Crear();
            Assert.Equal(idCondicion, (await db2.Clientes.AsNoTracking().SingleAsync()).IdCondicionPagoDefault);
        }

        // ── Errores ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Sin_documento_no_se_crea_la_ficha_y_se_explica_el_consumidor_final()
        {
            var r = await Registrar(Alta(documento: null));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            var error = Assert.Single(r.Errores, e => e.Campo == "Persona.Documento");
            Assert.Contains("consumidor final", error.Mensaje);

            using var db = _bd.Crear();
            Assert.Empty(db.Personas);
            Assert.Empty(db.Clientes);
        }

        [Fact]
        public async Task Los_errores_de_la_persona_llevan_el_prefijo_Persona()
        {
            var r = await Registrar(Alta(nombre: "", apellido: ""));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Persona.PrimerNombre");
            Assert.Contains(r.Errores, e => e.Campo == "Persona.PrimerApellido");
        }

        [Theory]
        [InlineData("", "Cliente.Codigo")]                    // obligatorio
        [InlineData("C 01", "Cliente.Codigo")]                // espacios
        [InlineData("ÑANDÚ", "Cliente.Codigo")]               // caracteres fuera de A-Z
        [InlineData("1234567890123456789012345678901", "Cliente.Codigo")]   // 31 caracteres
        public async Task Rechaza_un_codigo_de_cliente_invalido(string codigo, string campo)
        {
            var r = await Registrar(Alta(codigo: codigo));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == campo);
            using var db = _bd.Crear();
            Assert.Empty(db.Clientes);
        }

        [Fact]
        public async Task Rechaza_un_codigo_repetido_en_la_empresa_pero_lo_acepta_en_otra()
        {
            await RegistrarOk(Alta(codigo: "C-0001"));

            var repetido = await Registrar(Alta(codigo: "C-0001", documento: OtroDni, nombre: "Ana", apellido: "López"));
            Assert.Equal(EstadoRegistrarCliente.Rechazado, repetido.Estado);
            Assert.Contains(repetido.Errores, e => e.Campo == "Cliente.Codigo" && e.Mensaje.Contains("Ya existe"));

            var enOtraEmpresa = await Registrar(Alta(empresa: DatosBase.EmpresaB, codigo: "C-0001", documento: OtroDni, nombre: "Ana", apellido: "López"));
            Assert.True(enOtraEmpresa.Ok);
        }

        [Theory]
        [InlineData("correo-sin-arroba", "Cliente.Email")]
        [InlineData("1234", "Cliente.Telefono")]
        [InlineData("RTN 12/34", "Cliente.IdentificadorFiscal")]
        public async Task Rechaza_contactos_y_datos_fiscales_con_formato_invalido(string valor, string campo)
        {
            var r = await Registrar(Alta(ajustar: i =>
            {
                switch (campo)
                {
                    case "Cliente.Email": i.Cliente.Email = valor; break;
                    case "Cliente.Telefono": i.Cliente.Telefono = valor; break;
                    default: i.Cliente.IdentificadorFiscal = valor; break;
                }
            }));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == campo);
        }

        [Theory]
        [InlineData(-1, "negativo")]
        [InlineData(10.555, "2 decimales")]
        public async Task Rechaza_un_limite_de_credito_invalido(double limite, string fragmento)
        {
            var r = await Registrar(Alta(ajustar: i => i.Cliente.LimiteCredito = (decimal)limite));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Cliente.LimiteCredito" && e.Mensaje.Contains(fragmento));
        }

        [Fact]
        public async Task Rechaza_una_moneda_inactiva_o_inexistente_y_una_condicion_de_pago_de_otra_empresa()
        {
            int idCondicionDeB;
            using (var db = _bd.Crear())
            {
                var condicion = new CondicionPago
                {
                    IdEmpresa = DatosBase.EmpresaB, Codigo = "CONTADO", Nombre = "Contado", Tipo = "contado",
                    CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };
                db.CondicionesPago.Add(condicion);
                db.SaveChanges();
                idCondicionDeB = condicion.IdCondicionPago;
            }

            var inactiva = await Registrar(Alta(ajustar: i => i.Cliente.MonedaIsoDefault = "ang"));
            Assert.Contains(inactiva.Errores, e => e.Campo == "Cliente.MonedaIsoDefault");

            var inexistente = await Registrar(Alta(ajustar: i => i.Cliente.MonedaIsoDefault = "ZZZ"));
            Assert.Contains(inexistente.Errores, e => e.Campo == "Cliente.MonedaIsoDefault");

            var deOtraEmpresa = await Registrar(Alta(ajustar: i => i.Cliente.IdCondicionPagoDefault = idCondicionDeB));
            Assert.Contains(deOtraEmpresa.Errores, e => e.Campo == "Cliente.IdCondicionPagoDefault");
        }

        [Fact]
        public async Task Sin_empresa_no_hace_nada()
        {
            var r = await Registrar(Alta(empresa: 0));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            using var db = _bd.Crear();
            Assert.Empty(db.Personas);
        }

        // ── Persona que ya existe ───────────────────────────────────────────

        [Fact]
        public async Task Si_la_persona_ya_es_empleado_de_la_empresa_le_agrega_el_rol_de_cliente_sin_tocar_sus_datos()
        {
            var idPersona = Insertar(DatosBase.EmpresaA, "Luis", "Pérez", dni: DniNormalizado);

            // Aunque el formulario traiga otro nombre, la persona ya existe: sus datos no cambian.
            var r = await RegistrarOk(Alta(nombre: "Luis", apellido: "Pérez", ajustar: i => i.Persona.SegundoNombre = "Alberto"));

            Assert.Equal(EstadoRegistrarCliente.Vinculado, r.Estado);
            Assert.Equal(idPersona, r.IdPersona);

            using var db = _bd.Crear();
            Assert.Equal(1, await db.Personas.CountAsync());
            var persona = await db.Personas.AsNoTracking().Include(p => p.Vinculos).SingleAsync();
            Assert.Null(persona.SegundoNombre);
            Assert.Equal(DatosBase.EmpresaA, persona.IdEmpresa);   // sigue siendo del personal en las columnas viejas
            Assert.Equal(new[] { TiposVinculo.Cliente, TiposVinculo.Empleado }, persona.Vinculos.Select(v => v.TipoVinculo).OrderBy(x => x).ToArray());

            var cliente = await db.Clientes.AsNoTracking().SingleAsync();
            Assert.Equal("Luis Pérez", cliente.RazonSocial);
            Assert.Equal(r.IdPersonaEmpresa, cliente.IdPersonaEmpresa);
        }

        [Fact]
        public async Task Si_la_persona_es_de_otra_empresa_y_la_verificacion_coincide_la_reutiliza()
        {
            var idPersona = Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado);

            var r = await RegistrarOk(Alta(empresa: DatosBase.EmpresaA, nombre: "luis", apellido: "PEREZ"));

            Assert.Equal(EstadoRegistrarCliente.Vinculado, r.Estado);
            Assert.Equal(idPersona, r.IdPersona);

            using var db = _bd.Crear();
            Assert.Equal(1, await db.Personas.CountAsync());
            var vinculos = await db.PersonaEmpresas.AsNoTracking().Where(v => v.IdPersona == idPersona).ToListAsync();
            Assert.Contains(vinculos, v => v.IdEmpresa == DatosBase.EmpresaA && v.TipoVinculo == TiposVinculo.Cliente);
            Assert.Contains(vinculos, v => v.IdEmpresa == DatosBase.EmpresaB && v.TipoVinculo == TiposVinculo.Empleado);
        }

        [Fact]
        public async Task Si_la_verificacion_no_coincide_responde_un_mensaje_generico_y_no_vincula_nada()
        {
            Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado);

            var r = await Registrar(Alta(empresa: DatosBase.EmpresaA, nombre: "Mario", apellido: "Pérez"));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            var error = Assert.Single(r.Errores);
            Assert.Equal("Persona.Documento", error.Campo);
            Assert.Contains("No se pudo registrar al cliente con esos datos", error.Mensaje);
            Assert.DoesNotContain("existe", error.Mensaje, StringComparison.OrdinalIgnoreCase);

            using var db = _bd.Crear();
            Assert.Empty(db.Clientes);
            Assert.DoesNotContain(db.PersonaEmpresas, v => v.IdEmpresa == DatosBase.EmpresaA);
        }

        [Fact]
        public async Task Un_error_de_datos_gana_a_la_verificacion_para_no_revelar_que_el_documento_existe()
        {
            Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado);

            // Nombre que no coincide Y código inválido: solo se habla del código.
            var r = await Registrar(Alta(empresa: DatosBase.EmpresaA, nombre: "Mario", codigo: "C 1"));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.All(r.Errores, e => Assert.Equal("Cliente.Codigo", e.Campo));
        }

        [Fact]
        public async Task Quien_ya_es_cliente_vigente_de_la_empresa_no_se_registra_dos_veces()
        {
            await RegistrarOk(Alta(codigo: "C-0001"));

            var r = await Registrar(Alta(codigo: "C-0002"));

            Assert.Equal(EstadoRegistrarCliente.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == "Persona.Documento" && e.Mensaje.Contains("ya es cliente"));
            using var db = _bd.Crear();
            Assert.Equal(1, await db.Clientes.CountAsync());
        }

        [Fact]
        public async Task Detecta_a_las_personas_parecidas_de_la_empresa_con_cualquier_rol_y_pide_confirmacion()
        {
            Insertar(DatosBase.EmpresaA, "Luis", "Pérez", codigo: "E1");   // empleado sin documento, mismo nombre

            var r = await Registrar(Alta());

            Assert.Equal(EstadoRegistrarCliente.RequiereConfirmacion, r.Estado);
            Assert.Single(r.Parecidas);
            using (var db = _bd.Crear()) Assert.Empty(db.Clientes);

            var confirmado = await RegistrarOk(Alta(ajustar: i => i.ConfirmarQueEsOtraPersona = true));
            Assert.Equal(EstadoRegistrarCliente.Creado, confirmado.Estado);
            using var db2 = _bd.Crear();
            Assert.Equal(2, await db2.Personas.CountAsync());
        }

        [Fact]
        public async Task No_avisa_de_parecidas_que_solo_estan_en_otra_empresa()
        {
            Insertar(DatosBase.EmpresaB, "Luis", "Pérez", codigo: "E1");

            var r = await Registrar(Alta(empresa: DatosBase.EmpresaA));

            Assert.Equal(EstadoRegistrarCliente.Creado, r.Estado);
            Assert.Empty(r.Parecidas);
        }

        // ── Dar de baja y volver ────────────────────────────────────────────

        [Fact]
        public async Task Termina_la_relacion_con_el_cliente_sin_borrar_nada()
        {
            var r = await RegistrarOk(Alta());

            using (var db = Contexto())
            {
                var t = await Servicio(db).TerminarClienteAsync(new TerminarClienteInput
                {
                    IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente!.Value,
                    FechaFin = RelojFijo.Hoy, Motivo = "  Cerró su taller ", Usuario = "ventas.ana"
                });
                Assert.True(t.Ok);
                Assert.Equal(string.Empty, t.Mensaje);
            }

            using var db2 = _bd.Crear();
            var vinculo = await db2.PersonaEmpresas.AsNoTracking().SingleAsync();
            Assert.Equal(RelojFijo.Hoy, vinculo.FechaFin);
            Assert.Equal("Cerró su taller", vinculo.MotivoFin);
            Assert.False(vinculo.Activo);
            Assert.False(vinculo.Eliminado);

            var cliente = await db2.Clientes.AsNoTracking().SingleAsync();
            Assert.False(cliente.Activo);
            Assert.False(cliente.Eliminado);
            Assert.Equal(1, await db2.Personas.CountAsync());

            // Queda en la bitácora de la persona.
            var cambios = await db2.BitacoraCambios.AsNoTracking()
                .Where(b => b.IdPersona == r.IdPersona && b.Operacion == OperacionesBitacora.Update).ToListAsync();
            Assert.Contains(cambios, b => b.Entidad == "persona_empresa" && b.Campo == "fecha_fin");
            Assert.Contains(cambios, b => b.Entidad == "clientes" && b.Campo == "activo" && b.ValorNuevo == "false");
        }

        [Fact]
        public async Task Sin_fecha_termina_hoy_en_hora_de_Honduras()
        {
            var r = await RegistrarOk(Alta());

            using (var db = Contexto())
            {
                var t = await Servicio(db).TerminarClienteAsync(new TerminarClienteInput
                    { IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente!.Value, Usuario = "ventas.ana" });
                Assert.True(t.Ok);
            }

            using var db2 = _bd.Crear();
            Assert.Equal(RelojFijo.Hoy, (await db2.PersonaEmpresas.AsNoTracking().SingleAsync()).FechaFin);
        }

        [Fact]
        public async Task No_termina_con_una_fecha_anterior_al_inicio_ni_con_un_motivo_demasiado_largo()
        {
            var r = await RegistrarOk(Alta());

            using var db = Contexto();
            var servicio = Servicio(db);

            var anterior = await servicio.TerminarClienteAsync(new TerminarClienteInput
                { IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente!.Value, FechaFin = RelojFijo.Hoy.AddDays(-1) });
            Assert.False(anterior.Ok);
            Assert.True(anterior.Encontrado);
            Assert.Contains("anterior", anterior.Mensaje);

            var largo = await servicio.TerminarClienteAsync(new TerminarClienteInput
                { IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente.Value, Motivo = new string('x', 201) });
            Assert.False(largo.Ok);
            Assert.Contains("200", largo.Mensaje);

            using var db2 = _bd.Crear();
            Assert.True((await db2.Clientes.AsNoTracking().SingleAsync()).Activo);
        }

        [Fact]
        public async Task No_termina_a_un_cliente_de_otra_empresa_ni_a_uno_sin_ficha_de_persona_ni_dos_veces()
        {
            var r = await RegistrarOk(Alta());

            int idSinPersona;
            using (var db = _bd.Crear())
            {
                var suelto = new Cliente
                {
                    IdEmpresa = DatosBase.EmpresaA, Codigo = "CF", RazonSocial = "Consumidor final", Tipo = "natural",
                    CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };
                db.Clientes.Add(suelto);
                db.SaveChanges();
                idSinPersona = suelto.IdCliente;
            }

            using var db2 = Contexto();
            var servicio = Servicio(db2);

            var deOtra = await servicio.TerminarClienteAsync(new TerminarClienteInput { IdEmpresa = DatosBase.EmpresaB, IdCliente = r.IdCliente!.Value });
            Assert.False(deOtra.Encontrado);

            var sinPersona = await servicio.TerminarClienteAsync(new TerminarClienteInput { IdEmpresa = DatosBase.EmpresaA, IdCliente = idSinPersona });
            Assert.False(sinPersona.Encontrado);

            Assert.True((await servicio.TerminarClienteAsync(new TerminarClienteInput { IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente.Value })).Ok);
            var segunda = await servicio.TerminarClienteAsync(new TerminarClienteInput { IdEmpresa = DatosBase.EmpresaA, IdCliente = r.IdCliente.Value });
            Assert.False(segunda.Ok);
            Assert.True(segunda.Encontrado);
            Assert.Contains("ya estaba dado de baja", segunda.Mensaje);
        }

        [Fact]
        public async Task El_cliente_que_vuelve_se_reactiva_con_su_misma_ficha_y_sus_datos_nuevos()
        {
            var primero = await RegistrarOk(Alta(codigo: "C-0001", ajustar: i => i.Cliente.Email = "viejo@taller.hn"));
            using (var db = Contexto())
            {
                await Servicio(db).TerminarClienteAsync(new TerminarClienteInput
                    { IdEmpresa = DatosBase.EmpresaA, IdCliente = primero.IdCliente!.Value, Motivo = "Se fue" });
            }

            // Vuelve con el mismo código (que es el suyo, no un duplicado) y otro correo.
            var vuelve = await RegistrarOk(Alta(codigo: "C-0001", ajustar: i => i.Cliente.Email = "nuevo@taller.hn"));

            Assert.Equal(EstadoRegistrarCliente.Reactivado, vuelve.Estado);
            Assert.Equal(primero.IdCliente, vuelve.IdCliente);
            Assert.Equal(primero.IdPersonaEmpresa, vuelve.IdPersonaEmpresa);

            using var db2 = _bd.Crear();
            var vinculo = await db2.PersonaEmpresas.AsNoTracking().SingleAsync();
            Assert.Null(vinculo.FechaFin);
            Assert.Null(vinculo.MotivoFin);
            Assert.True(vinculo.Activo);
            var cliente = await db2.Clientes.AsNoTracking().SingleAsync();
            Assert.True(cliente.Activo);
            Assert.Equal("nuevo@taller.hn", cliente.Email);
            Assert.Equal(1, await db2.Personas.CountAsync());
        }

        // ── Consultar vínculos ──────────────────────────────────────────────

        [Fact]
        public async Task Lista_los_vinculos_de_la_persona_solo_en_la_empresa_que_pregunta()
        {
            var idPersona = Insertar(DatosBase.EmpresaA, "Luis", "Pérez", dni: DniNormalizado);
            await RegistrarOk(Alta(empresa: DatosBase.EmpresaA));
            await RegistrarOk(Alta(empresa: DatosBase.EmpresaB, codigo: "B-1"));

            using var db = _bd.Crear();
            var servicio = Servicio(db);

            var enA = await servicio.ListarVinculosAsync(DatosBase.EmpresaA, idPersona);
            Assert.Equal(new[] { TiposVinculo.Cliente, TiposVinculo.Empleado }, enA.Select(v => v.TipoVinculo).ToArray());
            Assert.All(enA, v => Assert.True(v.Vigente));

            var enB = await servicio.ListarVinculosAsync(DatosBase.EmpresaB, idPersona);
            Assert.Equal(new[] { TiposVinculo.Cliente }, enB.Select(v => v.TipoVinculo).ToArray());
        }

        [Fact]
        public async Task No_revela_los_vinculos_de_una_persona_que_la_empresa_no_tiene()
        {
            var idPersona = Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado);

            using var db = _bd.Crear();
            Assert.Empty(await Servicio(db).ListarVinculosAsync(DatosBase.EmpresaA, idPersona));
            Assert.Empty(await Servicio(db).ListarVinculosAsync(DatosBase.EmpresaA, 9999));
        }

        // ── Con PersonaService ──────────────────────────────────────────────

        [Fact]
        public async Task El_cliente_que_despues_es_contratado_usa_su_misma_persona_y_completa_las_columnas_viejas()
        {
            var cliente = await RegistrarOk(Alta());

            ResultadoCrearPersona r;
            using (var db = Contexto())
            {
                r = await ServicioPersonas(db).CrearAsync(new CrearPersonaInput
                {
                    Usuario = "rrhh.ana",
                    Datos = new PersonaDatosInput
                    {
                        IdEmpresa = DatosBase.EmpresaA, PrimerNombre = "Luis", PrimerApellido = "Pérez",
                        TipoDocumento = "DNI", Documento = Dni, FechaNacimiento = new DateOnly(1990, 5, 10),
                        Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "M100", FechaIngreso = new DateOnly(2026, 9, 1), TarifaDiaria = 350m }
                    }
                });
            }

            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Mensaje)));
            Assert.Equal(EstadoCrearPersona.Vinculada, r.Estado);
            Assert.Equal(cliente.IdPersona, r.IdPersona);

            using var db2 = _bd.Crear();
            Assert.Equal(1, await db2.Personas.CountAsync());
            var persona = await db2.Personas.AsNoTracking().Include(p => p.Vinculos).SingleAsync();
            Assert.Equal(2, persona.Vinculos.Count);
            Assert.Equal(DatosBase.EmpresaA, persona.IdEmpresa);
            Assert.Equal("MECANICO", persona.Cargo);
            Assert.Equal(350m, persona.TarifaDiaria);
            Assert.Equal(Dni.Replace("-", ""), persona.Documento);

            // Y ahora sí aparece en la lista de personal, sin dejar de ser cliente.
            var lista = await new PersonaConsultaService(db2, Options.Create(new PersonaValidacionOptions()))
                .ListarAsync(DatosBase.EmpresaA, new PersonaFiltro());
            Assert.Single(lista);
            Assert.Equal(1, await db2.Clientes.CountAsync());
        }

        [Fact]
        public async Task El_alta_de_personal_avisa_de_un_cliente_parecido_pero_marca_que_aun_no_es_personal()
        {
            await RegistrarOk(Alta());   // Luis Pérez, solo cliente

            using var db = Contexto();
            var r = await ServicioPersonas(db).CrearAsync(new CrearPersonaInput
            {
                Usuario = "rrhh.ana",
                Datos = new PersonaDatosInput
                {
                    IdEmpresa = DatosBase.EmpresaA, PrimerNombre = "Luis", PrimerApellido = "Pérez",
                    TipoDocumento = "DNI", Documento = OtroDni, FechaNacimiento = new DateOnly(1985, 3, 3),
                    Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "M300" }
                }
            });

            Assert.Equal(EstadoCrearPersona.RequiereConfirmacion, r.Estado);
            var parecida = Assert.Single(r.Parecidas);
            Assert.False(parecida.EsPersonal);   // la pantalla de personal no debe ofrecer abrir su ficha

            // Un empleado parecido, en cambio, sí se puede abrir.
            Insertar(DatosBase.EmpresaB, "Ana", "López", dni: "0801199099999", codigo: "E9");
            var empleado = (await ServicioPersonas(db).CrearAsync(new CrearPersonaInput
            {
                Usuario = "rrhh.ana",
                Datos = new PersonaDatosInput
                {
                    IdEmpresa = DatosBase.EmpresaB, PrimerNombre = "Ana", PrimerApellido = "López",
                    TipoDocumento = "DNI", Documento = "0801198877777", FechaNacimiento = new DateOnly(1988, 3, 4),
                    Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "E10" }
                }
            }));
            Assert.Equal(EstadoCrearPersona.RequiereConfirmacion, empleado.Estado);
            Assert.True(Assert.Single(empleado.Parecidas).EsPersonal);
        }

        [Fact]
        public async Task Un_empleado_no_se_registra_dos_veces_aunque_tambien_sea_cliente()
        {
            Insertar(DatosBase.EmpresaA, "Luis", "Pérez", dni: DniNormalizado);
            await RegistrarOk(Alta());

            using var db = Contexto();
            var r = await ServicioPersonas(db).CrearAsync(new CrearPersonaInput
            {
                Usuario = "rrhh.ana",
                Datos = new PersonaDatosInput
                {
                    IdEmpresa = DatosBase.EmpresaA, PrimerNombre = "Luis", PrimerApellido = "Pérez",
                    TipoDocumento = "DNI", Documento = Dni, FechaNacimiento = new DateOnly(1990, 5, 10),
                    Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "M200" }
                }
            });

            Assert.Equal(EstadoCrearPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Mensaje.Contains("Ya existe una persona con ese documento"));
        }

        [Fact]
        public async Task Al_cambiar_el_nombre_de_la_persona_se_actualiza_la_razon_social_de_sus_clientes_en_todas_las_empresas()
        {
            // Luis es empleado de B y cliente de A y de B; también hay un cliente jurídico y uno sin ficha, que no se tocan.
            var idPersona = Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado, codigo: "E1");
            await RegistrarOk(Alta(empresa: DatosBase.EmpresaA, codigo: "A-1"));
            await RegistrarOk(Alta(empresa: DatosBase.EmpresaB, codigo: "B-1"));
            using (var db = _bd.Crear())
            {
                db.Clientes.AddRange(
                    new Cliente { IdEmpresa = DatosBase.EmpresaA, Codigo = "J-1", RazonSocial = "Pérez y Hermanos S.A.", Tipo = "juridica", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow },
                    new Cliente { IdEmpresa = DatosBase.EmpresaA, Codigo = "CF", RazonSocial = "Consumidor final", Tipo = "natural", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                db.SaveChanges();
            }

            // RR.HH. de B corrige el nombre.
            ResultadoActualizarPersona r;
            using (var db = Contexto())
            {
                r = await ServicioPersonas(db).ActualizarAsync(new ActualizarPersonaInput
                {
                    Usuario = "rrhh.ana",
                    Datos = new PersonaDatosInput
                    {
                        IdEmpresa = DatosBase.EmpresaB, IdPersona = idPersona,
                        PrimerNombre = "Luis", SegundoNombre = "Alberto", PrimerApellido = "Pérez", SegundoApellido = "Gómez",
                        TipoDocumento = "DNI", Documento = DniNormalizado,
                        Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "E1" }
                    }
                });
            }
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Mensaje)));

            using var db2 = _bd.Crear();
            var clientes = await db2.Clientes.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { "Luis Alberto Pérez Gómez", "Luis Alberto Pérez Gómez" },
                clientes.Where(c => c.Codigo is "A-1" or "B-1").Select(c => c.RazonSocial).ToArray());
            Assert.Equal("Pérez y Hermanos S.A.", clientes.Single(c => c.Codigo == "J-1").RazonSocial);
            Assert.Equal("Consumidor final", clientes.Single(c => c.Codigo == "CF").RazonSocial);
        }

        [Fact]
        public async Task Un_valor_manipulado_en_el_formulario_no_vuelve_permitido_un_documento_repetido_al_editar()
        {
            var uno = Insertar(DatosBase.EmpresaA, "Luis", "Pérez", dni: DniNormalizado, codigo: "E1");
            Insertar(DatosBase.EmpresaA, "Ana", "López", dni: "0801198511111", codigo: "E2");

            using var db = Contexto();
            var r = await ServicioPersonas(db).ActualizarAsync(new ActualizarPersonaInput
            {
                Usuario = "rrhh.ana",
                Datos = new PersonaDatosInput
                {
                    IdEmpresa = DatosBase.EmpresaA, IdPersona = uno,
                    PrimerNombre = "Luis", PrimerApellido = "Pérez",
                    TipoDocumento = "DNI", Documento = OtroDni,
                    DocumentoDeLaEmpresaEsDuplicado = false,   // lo que mandaría un formulario manipulado
                    Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "E1" }
                }
            });

            Assert.Equal(EstadoActualizarPersona.Rechazada, r.Estado);
            Assert.Contains(r.Errores, e => e.Mensaje.Contains("Ya existe una persona con ese documento en esta empresa"));
        }

        [Fact]
        public async Task Al_fusionar_los_clientes_de_la_ficha_sobrante_pasan_a_llamarse_como_la_principal()
        {
            var principal = Insertar(DatosBase.EmpresaA, "Luis", "Pérez", dni: DniNormalizado, codigo: "E1");
            var sobrante = await RegistrarOk(Alta(nombre: "Luis", apellido: "Perez Gómez", documento: OtroDni, codigo: "A-1",
                ajustar: i => i.ConfirmarQueEsOtraPersona = true));

            using (var db = Contexto())
            {
                var f = await ServicioPersonas(db).FusionarAsync(new FusionarPersonasInput
                {
                    IdEmpresa = DatosBase.EmpresaA, IdPersonaSobrante = sobrante.IdPersona!.Value, IdPersonaPrincipal = principal, Usuario = "rrhh.ana"
                });
                Assert.True(f.Ok, string.Join(" | ", f.Errores));
            }

            using var db2 = _bd.Crear();
            var cliente = await db2.Clientes.AsNoTracking().Include(c => c.Vinculo).SingleAsync();
            Assert.Equal("Luis Pérez", cliente.RazonSocial);
            Assert.Equal(principal, cliente.Vinculo!.IdPersona);
        }

        // ── Bitácora e historial ────────────────────────────────────────────

        [Fact]
        public async Task El_alta_del_cliente_enlazado_queda_en_la_bitacora_de_la_persona()
        {
            var r = await RegistrarOk(Alta());

            using var db = _bd.Crear();
            var filas = await db.BitacoraCambios.AsNoTracking().Where(b => b.IdPersona == r.IdPersona).ToListAsync();

            var alta = Assert.Single(filas, b => b.Entidad == "clientes");
            Assert.Equal(OperacionesBitacora.Insert, alta.Operacion);
            Assert.Equal(DatosBase.EmpresaA, alta.IdEmpresa);
            Assert.Equal(r.IdCliente!.Value, (int)alta.IdRegistro);
            Assert.Equal("ventas.ana", alta.Usuario);
            Assert.Contains("C-0001", alta.ValorNuevo);
            Assert.Contains(filas, b => b.Entidad == "persona_empresa" && b.Operacion == OperacionesBitacora.Insert);
        }

        [Fact]
        public async Task Un_cliente_sin_ficha_de_persona_no_se_audita()
        {
            using (var db = Contexto())
            {
                db.Clientes.Add(new Cliente
                {
                    IdEmpresa = DatosBase.EmpresaA, Codigo = "CF", RazonSocial = "Consumidor final", Tipo = "natural",
                    CreadoPor = "ventas.ana", FechaCreacion = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            using (var db = Contexto())
            {
                var cliente = await db.Clientes.SingleAsync();
                cliente.RazonSocial = "Consumidor final (mostrador)";
                db.SaveChanges();
            }

            using var db2 = _bd.Crear();
            Assert.DoesNotContain(db2.BitacoraCambios, b => b.Entidad == "clientes");
        }

        [Fact]
        public async Task Que_alguien_sea_cliente_de_una_empresa_no_lo_ve_otra_en_el_historial_de_la_persona()
        {
            var idPersona = Insertar(DatosBase.EmpresaB, "Luis", "Pérez", dni: DniNormalizado);
            await RegistrarOk(Alta(empresa: DatosBase.EmpresaA));

            using var db = _bd.Crear();
            var consulta = new PersonaConsultaService(db, Options.Create(new PersonaValidacionOptions()));

            var desdeA = await consulta.HistorialAsync(DatosBase.EmpresaA, idPersona);
            Assert.NotNull(desdeA);
            Assert.Contains(desdeA!.Filas, f => f.Entidad == "Ficha de cliente" && !f.DeOtraEmpresa);

            var desdeB = await consulta.HistorialAsync(DatosBase.EmpresaB, idPersona);
            Assert.NotNull(desdeB);
            Assert.DoesNotContain(desdeB!.Filas, f => f.Entidad == "Ficha de cliente");
        }
    }
}
