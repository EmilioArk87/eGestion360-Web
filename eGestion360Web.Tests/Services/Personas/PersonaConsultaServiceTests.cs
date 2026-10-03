using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Personas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Personas
{
    public class PersonaConsultaServiceTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private PersonaConsultaService Servicio(ApplicationDbContext db) =>
            new(db, Options.Create(new PersonaValidacionOptions()));

        private static Persona Insertar(ApplicationDbContext db, int empresa, string nombre, string apellido, string? dni = null,
            string? codigo = null, string cargo = "MECANICO", DateOnly? nacimiento = null) =>
            PersonasDePrueba.Insertar(db, empresa, nombre, apellido, dni, codigo, cargo, nacimiento);

        // ── Listar ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Listar_solo_trae_a_los_empleados_de_la_empresa()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", codigo: "B1");
            Insertar(db, DatosBase.EmpresaA, "Beto", "Rivas", codigo: "A1");

            var deB = await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro());
            var deA = await Servicio(db).ListarAsync(DatosBase.EmpresaA, new PersonaFiltro());

            Assert.Equal("Paz, Ana", Assert.Single(deB).NombreCompleto);
            Assert.Equal("Rivas, Beto", Assert.Single(deA).NombreCompleto);
        }

        [Fact]
        public async Task Listar_sin_empresa_no_trae_nada()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", codigo: "B1");

            Assert.Empty(await Servicio(db).ListarAsync(0, new PersonaFiltro()));
            Assert.Empty(await Servicio(db).ListarAsync(-1, new PersonaFiltro()));
        }

        [Fact]
        public async Task Listar_no_trae_a_las_personas_eliminadas_fusionadas_ni_a_los_vinculos_eliminados_ni_a_quien_no_es_empleado()
        {
            using var db = _bd.Crear();
            var principal = Insertar(db, DatosBase.EmpresaB, "Luz", "Mora", codigo: "OK1");
            var eliminada = Insertar(db, DatosBase.EmpresaB, "Eli", "Mina", codigo: "E1");
            var fusionada = Insertar(db, DatosBase.EmpresaB, "Fus", "Ion", codigo: "F1");
            var sinVinculo = Insertar(db, DatosBase.EmpresaB, "Sin", "Vin", codigo: "V1");
            var cliente = Insertar(db, DatosBase.EmpresaB, "Cli", "Ente", codigo: "C1");

            eliminada.Eliminado = true;
            fusionada.IdPersonaPrincipal = principal.IdPersona;
            sinVinculo.Vinculos.Single().Eliminado = true;
            cliente.Vinculos.Single().TipoVinculo = TiposVinculo.Cliente;
            db.SaveChanges();

            var filas = await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro());
            Assert.Equal(principal.IdPersona, Assert.Single(filas).IdPersona);
        }

        [Fact]
        public async Task Listar_arma_el_nombre_con_las_partes_y_el_texto_antiguo_si_no_estan_separadas()
        {
            using var db = _bd.Crear();
            var conPartes = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", codigo: "L1");
            conPartes.SegundoNombre = "Alberto"; conPartes.SegundoApellido = "López";
            var sinPartes = Insertar(db, DatosBase.EmpresaB, "Edwin", "Joel Martinez", codigo: "E1");
            sinPartes.PrimerNombre = null; sinPartes.PrimerApellido = null;
            sinPartes.Nombres = "EDWIN"; sinPartes.Apellidos = "JOEL MARTINEZ";
            db.SaveChanges();

            var filas = (await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro())).ToDictionary(f => f.IdPersona);

            Assert.Equal("Pérez López, Luis Alberto", filas[conPartes.IdPersona].NombreCompleto);
            Assert.True(filas[conPartes.IdPersona].NombresSeparados);
            Assert.Equal("JOEL MARTINEZ, EDWIN", filas[sinPartes.IdPersona].NombreCompleto);
            Assert.False(filas[sinPartes.IdPersona].NombresSeparados);
            Assert.Contains("Separar el nombre y los apellidos", filas[sinPartes.IdPersona].Faltantes);
        }

        [Fact]
        public async Task Listar_trae_el_documento_principal_el_codigo_el_cargo_y_la_tarifa()
        {
            using var db = _bd.Crear();
            var p = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", dni: "0801199012345", codigo: "M100", nacimiento: new DateOnly(1990, 5, 10));
            var empleado = p.Vinculos.Single().Empleado!;
            empleado.TarifaDiaria = 350m; empleado.MonedaTarifa = "HNL";
            p.Vinculos.Single().FechaInicio = new DateOnly(2026, 3, 1);
            p.Telefono = "98765432";
            db.SaveChanges();

            var fila = Assert.Single(await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro()));

            Assert.Equal("DNI", fila.TipoDocumento);
            Assert.Equal("0801199012345", fila.Documento);   // sin enmascarar: lo enmascara la pantalla
            Assert.Equal("M100", fila.CodigoInterno);
            Assert.Equal("MECANICO", fila.Cargo);
            Assert.Equal(350m, fila.TarifaDiaria);
            Assert.Equal("HNL", fila.Moneda);
            Assert.Equal(new DateOnly(2026, 3, 1), fila.FechaIngreso);
            Assert.Equal("98765432", fila.Telefono);
            Assert.True(fila.Activo);
            Assert.Equal(EstadosIdentidad.Verificada, fila.EstadoIdentidad);
            Assert.Empty(fila.Faltantes);
            Assert.False(fila.PerfilIncompleto);
        }

        [Fact]
        public async Task Listar_dice_que_le_falta_a_cada_perfil()
        {
            using var db = _bd.Crear();
            var completo = Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", dni: "0801199011111", codigo: "A1", nacimiento: new DateOnly(1990, 1, 1));
            var sinNada = Insertar(db, DatosBase.EmpresaB, "Beto", "Rivas", codigo: "B1");   // sin documento ni nacimiento
            var conductor = Insertar(db, DatosBase.EmpresaB, "Carla", "Soto", dni: "0801199022222", codigo: "C1", cargo: "CONDUCTOR", nacimiento: new DateOnly(1990, 1, 1));

            var filas = (await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro())).ToDictionary(f => f.IdPersona);

            Assert.Empty(filas[completo.IdPersona].Faltantes);
            Assert.Equal(new[] { "Fecha de nacimiento", "Documento de identidad" }, filas[sinNada.IdPersona].Faltantes.ToArray());
            Assert.Equal(new[] { "Licencia de conducir" }, filas[conductor.IdPersona].Faltantes.ToArray());

            conductor.LicenciaTipo = "C"; conductor.LicenciaNumero = "L1";
            db.SaveChanges();
            using var db2 = _bd.Crear();
            Assert.Empty((await Servicio(db2).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro())).Single(f => f.IdPersona == conductor.IdPersona).Faltantes);
        }

        [Fact]
        public async Task Listar_busca_por_nombre_sin_tildes_ni_mayusculas_por_codigo_y_por_documento_con_o_sin_guiones()
        {
            using var db = _bd.Crear();
            var luis = Insertar(db, DatosBase.EmpresaB, "Luis", "López", dni: "0801199012345", codigo: "M100");
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", dni: "0801199099999", codigo: "M200");
            var servicio = Servicio(db);

            async Task<int[]> Buscar(string texto) =>
                (await servicio.ListarAsync(DatosBase.EmpresaB, new PersonaFiltro { Texto = texto })).Select(f => f.IdPersona).ToArray();

            Assert.Equal(new[] { luis.IdPersona }, await Buscar("lopez"));
            Assert.Equal(new[] { luis.IdPersona }, await Buscar("  LÓPEZ "));
            Assert.Equal(new[] { luis.IdPersona }, await Buscar("luis lopez"));
            Assert.Equal(new[] { luis.IdPersona }, await Buscar("M100"));
            Assert.Equal(new[] { luis.IdPersona }, await Buscar("0801-1990-12345"));
            Assert.Equal(new[] { luis.IdPersona }, await Buscar("12345"));
            Assert.Empty(await Buscar("nadie"));
            Assert.Equal(2, (await Buscar("")).Length);
        }

        [Fact]
        public async Task Listar_filtra_por_cargo_estado_y_perfil()
        {
            using var db = _bd.Crear();
            var completo = Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", dni: "0801199011111", codigo: "A1", cargo: "COBRADOR", nacimiento: new DateOnly(1990, 1, 1));
            var incompleto = Insertar(db, DatosBase.EmpresaB, "Beto", "Rivas", codigo: "B1", cargo: "MECANICO");
            var porRevisar = Insertar(db, DatosBase.EmpresaB, "Edwin", "Joel Martinez", codigo: "E1", cargo: "MECANICO");
            porRevisar.PrimerNombre = null; porRevisar.PrimerApellido = null;
            incompleto.Vinculos.Single().Activo = false;
            db.SaveChanges();
            var servicio = Servicio(db);

            async Task<int[]> Ids(PersonaFiltro f) => (await servicio.ListarAsync(DatosBase.EmpresaB, f)).Select(x => x.IdPersona).Order().ToArray();

            Assert.Equal(new[] { completo.IdPersona }, await Ids(new PersonaFiltro { Cargo = "COBRADOR" }));
            Assert.Equal(new[] { incompleto.IdPersona, porRevisar.IdPersona }.Order().ToArray(), await Ids(new PersonaFiltro { Cargo = "MECANICO" }));
            Assert.Equal(new[] { incompleto.IdPersona }, await Ids(new PersonaFiltro { Activo = false }));
            Assert.Equal(2, (await Ids(new PersonaFiltro { Activo = true })).Length);
            Assert.Equal(new[] { incompleto.IdPersona, porRevisar.IdPersona }.Order().ToArray(), await Ids(new PersonaFiltro { Perfil = FiltroPerfilPersona.Incompleto }));
            Assert.Equal(new[] { porRevisar.IdPersona }, await Ids(new PersonaFiltro { Perfil = FiltroPerfilPersona.NombresPorRevisar }));
            Assert.Equal(3, (await Ids(new PersonaFiltro { Perfil = FiltroPerfilPersona.Todos })).Length);
        }

        [Fact]
        public async Task Listar_ordena_por_apellidos_y_nombres()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Zoe", "Alvarado", codigo: "1");
            Insertar(db, DatosBase.EmpresaB, "Ana", "Zelaya", codigo: "2");
            Insertar(db, DatosBase.EmpresaB, "Bea", "Alvarado", codigo: "3");

            var nombres = (await Servicio(db).ListarAsync(DatosBase.EmpresaB, new PersonaFiltro())).Select(f => f.NombreCompleto).ToArray();
            Assert.Equal(new[] { "Alvarado, Bea", "Alvarado, Zoe", "Zelaya, Ana" }, nombres);
        }

        // ── Obtener para edición ────────────────────────────────────────────

        [Fact]
        public async Task ObtenerParaEdicion_devuelve_los_datos_del_formulario_con_el_documento_sin_enmascarar()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var p = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", dni: "0801199012345", codigo: "M100", nacimiento: new DateOnly(1990, 5, 10));
                var municipio = db.CatalogoMunicipios.First();
                p.IdMunicipioNacimiento = municipio.IdMunicipio;
                p.Telefono = "98765432"; p.Email = "luis@x.com";
                p.LicenciaTipo = "B"; p.LicenciaNumero = "L1"; p.LicenciaVencimiento = new DateOnly(2030, 1, 1);
                var v = p.Vinculos.Single();
                v.FechaInicio = new DateOnly(2026, 3, 1);
                v.Empleado!.TarifaDiaria = 350m; v.Empleado.MonedaTarifa = "HNL";
                db.SaveChanges();
                id = p.IdPersona;
            }

            using var lectura = _bd.Crear();
            var e = await Servicio(lectura).ObtenerParaEdicionAsync(DatosBase.EmpresaB, id);

            Assert.NotNull(e);
            var d = e!.Datos;
            Assert.Equal(id, d.IdPersona);
            Assert.Equal(DatosBase.EmpresaB, d.IdEmpresa);
            Assert.Equal("Luis", d.PrimerNombre);
            Assert.Equal("Pérez", d.PrimerApellido);
            Assert.Equal("DNI", d.TipoDocumento);
            Assert.Equal("0801199012345", d.Documento);
            Assert.Equal("HN", d.PaisEmisor);
            Assert.Equal(new DateOnly(1990, 5, 10), d.FechaNacimiento);
            Assert.Equal("98765432", d.Telefono);
            Assert.Equal("B", d.LicenciaTipo);
            Assert.Equal("M100", d.Empleado!.CodigoInterno);
            Assert.Equal("MECANICO", d.Empleado.Cargo);
            Assert.Equal(new DateOnly(2026, 3, 1), d.Empleado.FechaIngreso);
            Assert.Equal(350m, d.Empleado.TarifaDiaria);
            Assert.Equal(EstadosIdentidad.Verificada, e.EstadoIdentidad);
            Assert.True(e.NombresSeparados);
            Assert.True(e.Activo);
            Assert.Equal("pruebas", e.CreadoPor);
            Assert.NotNull(e.IdDepartamentoNacimiento);   // sale del municipio de nacimiento
            Assert.Null(e.IdDepartamentoResidencia);
        }

        [Fact]
        public async Task ObtenerParaEdicion_no_encuentra_a_una_persona_de_otra_empresa_ni_inexistente_ni_fusionada()
        {
            int deA, fusionada, principal;
            using (var db = _bd.Crear())
            {
                deA = Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", codigo: "A1").IdPersona;
                principal = Insertar(db, DatosBase.EmpresaB, "Luz", "Mora", codigo: "B1").IdPersona;
                var f = Insertar(db, DatosBase.EmpresaB, "Fus", "Ion", codigo: "B2");
                f.IdPersonaPrincipal = principal; db.SaveChanges();
                fusionada = f.IdPersona;
            }

            using var lectura = _bd.Crear();
            var servicio = Servicio(lectura);
            Assert.Null(await servicio.ObtenerParaEdicionAsync(DatosBase.EmpresaB, deA));
            Assert.Null(await servicio.ObtenerParaEdicionAsync(DatosBase.EmpresaB, 9999));
            Assert.Null(await servicio.ObtenerParaEdicionAsync(DatosBase.EmpresaB, fusionada));
            Assert.Null(await servicio.ObtenerParaEdicionAsync(0, principal));
            Assert.NotNull(await servicio.ObtenerParaEdicionAsync(DatosBase.EmpresaB, principal));
        }

        [Fact]
        public async Task ObtenerParaEdicion_de_alguien_con_el_nombre_por_revisar_trae_el_texto_antiguo()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var p = Insertar(db, DatosBase.EmpresaB, "Edwin", "Joel Martinez", codigo: "E1");
                p.PrimerNombre = null; p.PrimerApellido = null; p.Nombres = "EDWIN"; p.Apellidos = "JOEL MARTINEZ";
                db.SaveChanges(); id = p.IdPersona;
            }

            using var lectura = _bd.Crear();
            var e = (await Servicio(lectura).ObtenerParaEdicionAsync(DatosBase.EmpresaB, id))!;

            Assert.False(e.NombresSeparados);
            Assert.Equal("EDWIN JOEL MARTINEZ", e.NombresRegistrados);
            Assert.Null(e.Datos.PrimerNombre);
            Assert.Null(e.Datos.PrimerApellido);
        }

        // ── Historial ───────────────────────────────────────────────────────

        private static BitacoraCambio Fila(int idPersona, int? idEmpresa, string entidad, string operacion, string? campo, string? antes, string? despues,
            string usuario = "rrhh.ana", DateTime? cuando = null) => new()
        {
            IdTransaccion = Guid.NewGuid(), IdPersona = idPersona, IdEmpresa = idEmpresa, Entidad = entidad, IdRegistro = idPersona,
            Operacion = operacion, Campo = campo, ValorAnterior = antes, ValorNuevo = despues, Usuario = usuario, Origen = "app",
            FechaHora = cuando ?? new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc)
        };

        [Fact]
        public async Task Historial_muestra_los_cambios_en_orden_con_nombres_en_espanol_y_hora_de_Honduras()
        {
            int id;
            using (var db = _bd.Crear())
            {
                id = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", codigo: "L1").IdPersona;
                db.BitacoraCambios.AddRange(
                    Fila(id, DatosBase.EmpresaB, "personas", "UPDATE", "primer_nombre", "Luis", "Luis Alberto", cuando: new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc)),
                    Fila(id, DatosBase.EmpresaB, "personas", "UPDATE", "activo", "true", "false", usuario: "otro.usuario", cuando: new DateTime(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc)));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            var h = (await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, id))!;

            Assert.Equal("Luis Pérez", h.NombrePersona);
            Assert.False(h.Truncado);
            Assert.Equal(2, h.Filas.Count);

            var reciente = h.Filas[0];
            Assert.Equal("Activo", reciente.Campo);
            Assert.Equal("Sí", reciente.ValorAnterior);
            Assert.Equal("No", reciente.ValorNuevo);
            Assert.Equal("Cambio", reciente.Operacion);
            Assert.Equal("Persona", reciente.Entidad);
            Assert.Equal("otro.usuario", reciente.Usuario);
            Assert.Equal(new DateTime(2026, 10, 1, 21, 0, 0), reciente.FechaHora);   // 03:00 UTC del día 2 = 21:00 del día 1 en Honduras

            Assert.Equal("Primer nombre", h.Filas[1].Campo);
            Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), h.Filas[1].FechaHora);
        }

        [Fact]
        public async Task Historial_resume_las_altas_y_muestra_el_documento_enmascarado_tal_como_esta_guardado()
        {
            int id;
            using (var db = _bd.Crear())
            {
                id = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", codigo: "L1").IdPersona;
                db.BitacoraCambios.Add(Fila(id, DatosBase.EmpresaB, "persona_documentos", "INSERT", null, null,
                    "{\"tipo_documento\":\"DNI\",\"pais_emisor\":\"HN\",\"numero\":\"*********2345\",\"es_principal\":true}"));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            var fila = Assert.Single((await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, id))!.Filas);

            Assert.Equal("Documento", fila.Entidad);
            Assert.Equal("Alta", fila.Operacion);
            Assert.Null(fila.Campo);
            Assert.Equal("Tipo de documento: DNI, País emisor: HN, Número: *********2345, Principal: Sí", fila.Detalle);
        }

        [Fact]
        public async Task Historial_no_existe_para_una_persona_de_otra_empresa()
        {
            int deA;
            using (var db = _bd.Crear())
            {
                deA = Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", codigo: "A1").IdPersona;
                db.BitacoraCambios.Add(Fila(deA, DatosBase.EmpresaA, "personas", "UPDATE", "telefono", null, "22223333"));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            Assert.Null(await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, deA));
            Assert.Null(await Servicio(lectura).HistorialAsync(0, deA));
            Assert.NotNull(await Servicio(lectura).HistorialAsync(DatosBase.EmpresaA, deA));
        }

        [Fact]
        public async Task Historial_de_una_persona_compartida_oculta_a_la_otra_empresa_los_datos_de_la_relacion_laboral_y_el_usuario()
        {
            int id;
            using (var db = _bd.Crear())
            {
                // La misma persona trabaja en las dos empresas.
                var p = Insertar(db, DatosBase.EmpresaA, "Mario", "Mejía", dni: "0801199077777", codigo: "A1");
                db.PersonaEmpresas.Add(new PersonaEmpresa { IdEmpresa = DatosBase.EmpresaB, IdPersona = p.IdPersona, TipoVinculo = TiposVinculo.Empleado, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                id = p.IdPersona;

                db.BitacoraCambios.AddRange(
                    // De la empresa A: datos personales (visibles, con el usuario oculto)
                    Fila(id, DatosBase.EmpresaA, "personas", "UPDATE", "telefono", null, "22223333", usuario: "usuario.de.A"),
                    Fila(id, DatosBase.EmpresaA, "persona_documentos", "UPDATE", "numero", "*********1111", "*********7777", usuario: "usuario.de.A"),
                    // De la empresa A: su relación laboral (privada)
                    Fila(id, DatosBase.EmpresaA, "empleados", "UPDATE", "cargo", "MECANICO", "SUPERVISOR", usuario: "usuario.de.A"),
                    Fila(id, DatosBase.EmpresaA, "persona_empresa", "UPDATE", "fecha_fin", null, "2026-09-30", usuario: "usuario.de.A"),
                    // De la empresa A: columnas legadas de la relación laboral (privadas mientras existan)
                    Fila(id, DatosBase.EmpresaA, "personas", "UPDATE", "cargo", "MECANICO", "SUPERVISOR", usuario: "usuario.de.A"),
                    Fila(id, DatosBase.EmpresaA, "personas", "UPDATE", "tarifa_diaria", "300", "350", usuario: "usuario.de.A"),
                    Fila(id, DatosBase.EmpresaA, "personas", "UPDATE", "activo", "true", "false", usuario: "usuario.de.A"),
                    // De la empresa A: la foto del alta de la persona lleva cargo y tarifa (no se detalla)
                    Fila(id, DatosBase.EmpresaA, "personas", "INSERT", null, null, "{\"primer_nombre\":\"Mario\",\"cargo\":\"MECANICO\",\"tarifa_diaria\":300}", usuario: "usuario.de.A"),
                    // De la empresa B (la que mira)
                    Fila(id, DatosBase.EmpresaB, "empleados", "UPDATE", "cargo", "OTRO", "CONDUCTOR", usuario: "usuario.de.B"));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            var h = (await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, id))!;
            var filas = h.Filas;

            // Visibles de la A, con el usuario oculto
            var telefono = Assert.Single(filas, f => f.Campo == "Teléfono");
            Assert.Equal("otra empresa", telefono.Usuario);
            Assert.True(telefono.DeOtraEmpresa);
            Assert.Equal("22223333", telefono.ValorNuevo);   // se ve el campo y los valores
            Assert.Single(filas, f => f.Entidad == "Documento" && f.Usuario == "otra empresa");

            // Ocultos de la A: relación laboral, columnas legadas laborales
            Assert.DoesNotContain(filas, f => f.Entidad == "Ficha de empleado" && f.Usuario == "otra empresa");
            Assert.DoesNotContain(filas, f => f.Entidad == "Vínculo con la empresa");
            Assert.DoesNotContain(filas, f => f.Campo is "Cargo" or "Tarifa diaria" or "Activo" && f.DeOtraEmpresa);
            Assert.DoesNotContain(filas, f => f.Detalle != null && f.DeOtraEmpresa);

            // El alta de la persona hecha desde la A aparece, pero sin detalle
            var alta = Assert.Single(filas, f => f.Operacion == "Alta");
            Assert.Null(alta.Detalle);
            Assert.Equal("otra empresa", alta.Usuario);

            // Lo propio de la B se ve con su usuario
            var propio = Assert.Single(filas, f => f.Usuario == "usuario.de.B");
            Assert.Equal("Cargo", propio.Campo);
            Assert.False(propio.DeOtraEmpresa);

            // Nada de lo que se ve contiene el nombre del usuario de la otra empresa
            Assert.DoesNotContain(filas, f => f.Usuario == "usuario.de.A");
        }

        [Fact]
        public async Task Historial_limita_la_cantidad_y_avisa_si_hay_mas()
        {
            int id;
            using (var db = _bd.Crear())
            {
                id = Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", codigo: "L1").IdPersona;
                for (var i = 0; i < 12; i++)
                    db.BitacoraCambios.Add(Fila(id, DatosBase.EmpresaB, "personas", "UPDATE", "telefono", i.ToString(), (i + 1).ToString(),
                        cuando: new DateTime(2026, 10, 1, 12, i, 0, DateTimeKind.Utc)));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            var h = (await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, id, maximo: 5))!;

            Assert.Equal(5, h.Filas.Count);
            Assert.True(h.Truncado);
            Assert.Equal("12", h.Filas[0].ValorNuevo);   // los más recientes primero

            Assert.False((await Servicio(lectura).HistorialAsync(DatosBase.EmpresaB, id, maximo: 50))!.Truncado);
        }

        // ── Catálogos ───────────────────────────────────────────────────────

        [Fact]
        public async Task Catalogos_trae_las_listas_del_formulario()
        {
            using var db = _bd.Crear();
            var c = await Servicio(db).CatalogosAsync(DatosBase.EmpresaB);

            Assert.Equal(5, c.Cargos.Count);
            Assert.Contains(c.Cargos, x => x.Valor == "CONDUCTOR");
            Assert.Equal("DNI", c.TiposDocumento[0].Valor);                  // el DNI primero
            Assert.Equal("DNI", c.TiposDocumento[0].Texto);
            Assert.Contains(c.TiposDocumento, x => x.Valor == "PASAPORTE" && x.Texto == "Pasaporte");
            Assert.Equal(9, c.CategoriasLicencia.Count);
            Assert.Equal("HN", c.Paises[0].Valor);                           // Honduras primero
            Assert.Equal(new[] { "HNL", "USD" }, c.Monedas.Select(m => m.Valor).ToArray());
            Assert.Single(c.Departamentos);
            Assert.Equal(2, c.Municipios.Count);
            Assert.All(c.Municipios, m => Assert.Equal(c.Departamentos[0].Id, m.IdDepartamento));
        }

        // ── Personal para elegir en otros módulos (F6) ──────────────────────

        [Fact]
        public async Task Personal_para_seleccion_trae_solo_al_personal_activo_de_la_empresa_ordenado_por_apellido()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Zoe", "Vega", codigo: "B1", cargo: "CONDUCTOR");
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", codigo: "B2", cargo: "COBRADOR");
            Insertar(db, DatosBase.EmpresaA, "Beto", "Rivas", codigo: "A1");

            var lista = await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaB);

            Assert.Equal(new[] { "Ana Paz", "Zoe Vega" }, lista.Select(x => x.NombreCompleto).ToArray());
        }

        [Fact]
        public async Task Personal_para_seleccion_filtra_por_cargo()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Zoe", "Vega", codigo: "B1", cargo: "CONDUCTOR");
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", codigo: "B2", cargo: "COBRADOR");

            var conductores = await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaB, "CONDUCTOR");

            Assert.Equal("Zoe Vega", Assert.Single(conductores).NombreCompleto);
            Assert.Empty(await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaB, "SUPERVISOR"));
        }

        [Fact]
        public async Task Personal_para_seleccion_deja_fuera_a_inactivos_eliminados_fusionados_clientes_y_fichas_apagadas()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Bien", "Activo", codigo: "OK1", cargo: "CONDUCTOR");
            var inactivo = Insertar(db, DatosBase.EmpresaB, "Ina", "Ctivo", codigo: "I1", cargo: "CONDUCTOR");
            var eliminada = Insertar(db, DatosBase.EmpresaB, "Eli", "Minada", codigo: "E1", cargo: "CONDUCTOR");
            var fusionada = Insertar(db, DatosBase.EmpresaB, "Fus", "Ionada", codigo: "F1", cargo: "CONDUCTOR");
            var soloCliente = Insertar(db, DatosBase.EmpresaB, "Sol", "Ocliente", codigo: "C1", cargo: "CONDUCTOR");
            var fichaApagada = Insertar(db, DatosBase.EmpresaB, "Fic", "Apagada", codigo: "A1", cargo: "CONDUCTOR");

            db.PersonaEmpresas.Single(v => v.IdPersona == inactivo.IdPersona).Activo = false;
            eliminada.Eliminado = true;
            fusionada.IdPersonaPrincipal = inactivo.IdPersona;
            db.PersonaEmpresas.Single(v => v.IdPersona == soloCliente.IdPersona).TipoVinculo = TiposVinculo.Cliente;
            db.Empleados.Single(e => e.IdPersonaEmpresa == db.PersonaEmpresas.Single(v => v.IdPersona == fichaApagada.IdPersona).IdPersonaEmpresa).Activo = false;
            db.SaveChanges();

            var conductores = await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaB, "CONDUCTOR");

            Assert.Equal("Bien Activo", Assert.Single(conductores).NombreCompleto);
        }

        [Fact]
        public async Task Personal_para_seleccion_incluye_a_quien_trabaja_en_dos_empresas_en_cada_una()
        {
            using var db = _bd.Crear();
            var persona = Insertar(db, DatosBase.EmpresaB, "Dos", "Empresas", dni: "0801199012345", codigo: "B1", cargo: "CONDUCTOR");

            db.PersonaEmpresas.Add(new PersonaEmpresa
            {
                IdPersona = persona.IdPersona, IdEmpresa = DatosBase.EmpresaA, TipoVinculo = TiposVinculo.Empleado,
                CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow,
                Empleado = new Empleado { IdEmpresa = DatosBase.EmpresaA, CodigoInterno = "A1", Cargo = "CONDUCTOR", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow }
            });
            db.SaveChanges();

            // Con las columnas viejas, esta persona solo aparecía en la empresa que quedó en personas.id_empresa.
            Assert.Single(await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaB, "CONDUCTOR"));
            Assert.Single(await Servicio(db).PersonalParaSeleccionAsync(DatosBase.EmpresaA, "CONDUCTOR"));
        }

        [Fact]
        public async Task Personal_para_seleccion_sin_empresa_no_trae_nada()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", codigo: "B1");

            Assert.Empty(await Servicio(db).PersonalParaSeleccionAsync(0));
            Assert.Empty(await Servicio(db).PersonalParaSeleccionAsync(-1, "CONDUCTOR"));
        }

        [Fact]
        public async Task Catalogos_solo_trae_los_cargos_activos_de_la_empresa()
        {
            using var db = _bd.Crear();
            db.Cargos.Add(new Cargo { IdEmpresa = DatosBase.EmpresaB, Codigo = "RETIRADO", Nombre = "Retirado", Activo = false, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
            db.Cargos.Add(new Cargo { IdEmpresa = DatosBase.EmpresaA, Codigo = "SOLO_A", Nombre = "Solo A", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
            db.SaveChanges();

            var c = await Servicio(db).CatalogosAsync(DatosBase.EmpresaB);

            Assert.DoesNotContain(c.Cargos, x => x.Valor == "RETIRADO");
            Assert.DoesNotContain(c.Cargos, x => x.Valor == "SOLO_A");
        }
    }
}
