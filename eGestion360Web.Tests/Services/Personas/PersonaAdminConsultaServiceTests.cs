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
    /// <summary>El administrador general ve a todas las personas, de todas las empresas, con sus empresas y roles.</summary>
    public class PersonaAdminConsultaServiceTests : IDisposable
    {
        private const string DniAna = "0801-1990-12345";
        private const string DniBeto = "0801-1985-11111";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();

        public void Dispose() => _bd.Dispose();

        private static PersonaAdminConsultaService Servicio(ApplicationDbContext db) =>
            new(db, Options.Create(new PersonaValidacionOptions()));

        private static Persona Insertar(ApplicationDbContext db, int empresa, string nombre, string apellido, string? dni = null,
            string? codigo = null, string cargo = "MECANICO", DateOnly? nacimiento = null) =>
            PersonasDePrueba.Insertar(db, empresa, nombre, apellido, dni, codigo, cargo, nacimiento);

        private VinculoService ServicioVinculos(ApplicationDbContext db) =>
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

        /// <summary>Registra a la persona como cliente natural de la empresa, con el servicio real.</summary>
        private async Task<ResultadoRegistrarCliente> RegistrarCliente(
            ContextoAuditoriaFijo quien, int empresa, string nombre, string apellido, string dni, string codigo)
        {
            using var db = _bd.Crear(new AuditoriaCambiosInterceptor(quien));
            var r = await ServicioVinculos(db).RegistrarClienteNaturalAsync(new RegistrarClienteNaturalInput
            {
                IdEmpresa = empresa,
                Usuario = quien.Usuario,
                Persona = new PersonaDatosInput { PrimerNombre = nombre, PrimerApellido = apellido, TipoDocumento = "DNI", Documento = dni },
                Cliente = new ClienteDatosInput { Codigo = codigo }
            });
            Assert.True(r.Ok, string.Join(" | ", r.Errores.Select(e => e.Campo + ": " + e.Mensaje)));
            return r;
        }

        private static PersonaAdminFiltro Todos() => new();

        // ── Listar ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Listar_trae_a_las_personas_de_todas_las_empresas_con_sus_empresas_y_roles()
        {
            int idAna;
            using (var db = _bd.Crear())
            {
                idAna = Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", DniAna, "A1", cargo: "CONDUCTOR").IdPersona;
                Insertar(db, DatosBase.EmpresaB, "Beto", "Rivas", DniBeto, "B1");
            }
            // Ana es además cliente de la otra empresa: una sola persona con dos roles.
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas", IdEmpresa = DatosBase.EmpresaB },
                DatosBase.EmpresaB, "Ana", "Paz", DniAna, "C-0001");

            using var lectura = _bd.Crear();
            var pagina = await Servicio(lectura).ListarAsync(Todos(), 1, 50);

            Assert.Equal(2, pagina.Total);
            var ana = Assert.Single(pagina.Filas, f => f.IdPersona == idAna);
            Assert.Equal(2, ana.Vinculos.Count);

            var empleado = Assert.Single(ana.Vinculos, v => v.TipoVinculo == TiposVinculo.Empleado);
            Assert.Equal(DatosBase.EmpresaA, empleado.IdEmpresa);
            Assert.Equal("Empresa de prueba 1", empleado.Empresa);
            Assert.Equal("CONDUCTOR · cód. A1", empleado.Detalle);
            Assert.True(empleado.Vigente);

            var cliente = Assert.Single(ana.Vinculos, v => v.TipoVinculo == TiposVinculo.Cliente);
            Assert.Equal(DatosBase.EmpresaB, cliente.IdEmpresa);
            Assert.Equal("cód. C-0001", cliente.Detalle);

            var beto = Assert.Single(pagina.Filas, f => f.NombreCompleto == "Rivas, Beto");
            Assert.Equal(TiposVinculo.Empleado, Assert.Single(beto.Vinculos).TipoVinculo);
        }

        [Fact]
        public async Task Listar_no_trae_eliminadas_ni_fusionadas_pero_si_a_quien_no_tiene_vinculos()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaA, "Luz", "Mora", codigo: "OK1");
            var eliminada = Insertar(db, DatosBase.EmpresaA, "Eli", "Mina", codigo: "E1");
            var principal = Insertar(db, DatosBase.EmpresaB, "Pri", "Ncipal", codigo: "P1");
            var fusionada = Insertar(db, DatosBase.EmpresaA, "Fus", "Ion", codigo: "F1");
            var sinVinculos = Insertar(db, DatosBase.EmpresaA, "Sin", "Vinculo", codigo: "S1");

            eliminada.Eliminado = true;
            fusionada.IdPersonaPrincipal = principal.IdPersona;
            foreach (var v in db.PersonaEmpresas.Where(v => v.IdPersona == sinVinculos.IdPersona)) v.Eliminado = true;
            db.SaveChanges();

            var nombres = (await Servicio(db).ListarAsync(Todos(), 1, 50)).Filas.Select(f => f.NombreCompleto).ToList();

            Assert.Contains("Mora, Luz", nombres);
            Assert.Contains("Ncipal, Pri", nombres);
            Assert.Contains("Vinculo, Sin", nombres);     // sin vínculos: el administrador debe poder verla
            Assert.DoesNotContain("Mina, Eli", nombres);
            Assert.DoesNotContain("Ion, Fus", nombres);
            Assert.Empty(Assert.Single((await Servicio(db).ListarAsync(Todos(), 1, 50)).Filas, f => f.NombreCompleto == "Vinculo, Sin").Vinculos);
        }

        [Fact]
        public async Task Listar_busca_por_nombre_documento_codigo_de_empleado_y_codigo_de_cliente()
        {
            using (var db = _bd.Crear())
            {
                Insertar(db, DatosBase.EmpresaA, "José", "Núñez", DniAna, "EMP77");
                Insertar(db, DatosBase.EmpresaB, "Otro", "Distinto", DniBeto, "EMP88");
            }
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas", IdEmpresa = DatosBase.EmpresaB },
                DatosBase.EmpresaB, "Beto", "Cliente", "0801-1970-22222", "CLI-555");

            using var lectura = _bd.Crear();
            async Task<List<string>> Buscar(string texto) =>
                (await Servicio(lectura).ListarAsync(new PersonaAdminFiltro { Texto = texto }, 1, 50)).Filas.Select(f => f.NombreCompleto).ToList();

            Assert.Equal(new[] { "Núñez, José" }, await Buscar("jose nunez"));    // sin tildes ni mayúsculas
            Assert.Equal(new[] { "Núñez, José" }, await Buscar("0801-1990-12345"));
            Assert.Equal(new[] { "Distinto, Otro" }, await Buscar("EMP88"));
            Assert.Equal(new[] { "Cliente, Beto" }, await Buscar("CLI-555"));
            Assert.Empty(await Buscar("nadie"));
        }

        [Fact]
        public async Task Listar_filtra_por_empresa_y_por_rol_juntos()
        {
            using (var db = _bd.Crear())
            {
                Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", DniAna, "A1");              // empleado en A
                Insertar(db, DatosBase.EmpresaB, "Beto", "Rivas", DniBeto, "B1");          // empleado en B
            }
            // Beto también es cliente de A: tiene vínculos en las dos empresas, pero «cliente en B» no es suyo.
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas", IdEmpresa = DatosBase.EmpresaA },
                DatosBase.EmpresaA, "Beto", "Rivas", DniBeto, "C-1");

            using var lectura = _bd.Crear();
            async Task<List<string>> Listar(int? empresa, string? rol) =>
                (await Servicio(lectura).ListarAsync(new PersonaAdminFiltro { IdEmpresa = empresa, TipoVinculo = rol }, 1, 50))
                    .Filas.Select(f => f.NombreCompleto).ToList();

            Assert.Equal(new[] { "Paz, Ana" }, await Listar(DatosBase.EmpresaA, TiposVinculo.Empleado));
            Assert.Equal(new[] { "Rivas, Beto" }, await Listar(DatosBase.EmpresaA, TiposVinculo.Cliente));
            Assert.Empty(await Listar(DatosBase.EmpresaB, TiposVinculo.Cliente));
            Assert.Equal(new[] { "Paz, Ana", "Rivas, Beto" }, await Listar(DatosBase.EmpresaA, null));
            Assert.Equal(new[] { "Paz, Ana", "Rivas, Beto" }, await Listar(null, TiposVinculo.Empleado));
            Assert.Empty(await Listar(null, TiposVinculo.Proveedor));                       // todavía nadie lo es
            Assert.Empty(await Listar(null, "inventado"));                                  // un rol que no existe no trae nada
        }

        [Fact]
        public async Task Listar_distingue_a_quien_tiene_un_vinculo_vigente_de_quien_solo_tiene_vinculos_terminados()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaA, "Vi", "Gente", codigo: "V1");
            var terminada = Insertar(db, DatosBase.EmpresaA, "Ter", "Minada", codigo: "T1");
            var baja = Insertar(db, DatosBase.EmpresaA, "Baj", "Ada", codigo: "B1");
            db.PersonaEmpresas.Single(v => v.IdPersona == terminada.IdPersona).FechaFin = new DateOnly(2026, 1, 31);
            db.PersonaEmpresas.Single(v => v.IdPersona == baja.IdPersona).Activo = false;
            db.SaveChanges();

            async Task<List<string>> Listar(EstadoVinculoFiltro estado) =>
                (await Servicio(db).ListarAsync(new PersonaAdminFiltro { Estado = estado }, 1, 50)).Filas.Select(f => f.NombreCompleto).ToList();

            Assert.Equal(new[] { "Gente, Vi" }, await Listar(EstadoVinculoFiltro.ConVinculoVigente));
            Assert.Equal(new[] { "Ada, Baj", "Minada, Ter" }, await Listar(EstadoVinculoFiltro.SinVinculoVigente));
            Assert.Equal(3, (await Listar(EstadoVinculoFiltro.Todos)).Count);
        }

        [Fact]
        public async Task Listar_filtra_por_perfil_y_avisa_lo_que_le_falta_a_cada_persona()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaA, "Com", "Pleto", DniAna, "C1", nacimiento: new DateOnly(1990, 5, 10));
            var porRevisar = Insertar(db, DatosBase.EmpresaB, "Rev", "Isar", codigo: "R1");
            porRevisar.PrimerNombre = null; porRevisar.PrimerApellido = null;
            db.SaveChanges();

            async Task<PersonaAdminPagina> Listar(FiltroPerfilPersona perfil) =>
                await Servicio(db).ListarAsync(new PersonaAdminFiltro { Perfil = perfil }, 1, 50);

            var revisar = Assert.Single((await Listar(FiltroPerfilPersona.NombresPorRevisar)).Filas);
            Assert.False(revisar.NombresSeparados);
            Assert.Contains("Separar el nombre y los apellidos", revisar.Faltantes);

            var incompletos = (await Listar(FiltroPerfilPersona.Incompleto)).Filas;
            Assert.Equal(new[] { "Isar, Rev" }, incompletos.Select(f => f.NombreCompleto));
            Assert.DoesNotContain((await Listar(FiltroPerfilPersona.Incompleto)).Filas, f => f.NombreCompleto == "Pleto, Com");
        }

        [Fact]
        public async Task Un_conductor_sin_licencia_figura_incompleto()
        {
            using var db = _bd.Crear();
            Insertar(db, DatosBase.EmpresaA, "Con", "Ductor", DniAna, "C1", cargo: "CONDUCTOR", nacimiento: new DateOnly(1990, 5, 10));
            Insertar(db, DatosBase.EmpresaA, "Los", "Demas", DniBeto, "M1", cargo: "MECANICO", nacimiento: new DateOnly(1990, 5, 10));

            var conductor = Assert.Single((await Servicio(db).ListarAsync(new PersonaAdminFiltro { Perfil = FiltroPerfilPersona.Incompleto }, 1, 50)).Filas);

            Assert.Equal("Ductor, Con", conductor.NombreCompleto);
            Assert.Equal(new[] { "Licencia de conducir" }, conductor.Faltantes);
        }

        [Fact]
        public async Task Listar_pagina_ordena_por_apellidos_y_cuenta_el_total()
        {
            using var db = _bd.Crear();
            foreach (var (nombre, apellido) in new[] { ("E", "Eco"), ("A", "Alfa"), ("D", "Delta"), ("B", "Beta"), ("C", "Cima") })
                Insertar(db, DatosBase.EmpresaA, nombre, apellido, codigo: "X" + apellido);

            var uno = await Servicio(db).ListarAsync(Todos(), 1, 2);
            var dos = await Servicio(db).ListarAsync(Todos(), 2, 2);
            var tres = await Servicio(db).ListarAsync(Todos(), 3, 2);
            var fuera = await Servicio(db).ListarAsync(Todos(), 9, 2);

            Assert.Equal(5, uno.Total);
            Assert.Equal(3, uno.TotalPaginas);
            Assert.Equal(new[] { "Alfa, A", "Beta, B" }, uno.Filas.Select(f => f.NombreCompleto));
            Assert.Equal(new[] { "Cima, C", "Delta, D" }, dos.Filas.Select(f => f.NombreCompleto));
            Assert.Equal(new[] { "Eco, E" }, tres.Filas.Select(f => f.NombreCompleto));
            Assert.Empty(fuera.Filas);
            Assert.Equal(5, fuera.Total);
        }

        [Fact]
        public async Task Los_vinculos_vigentes_van_primero()
        {
            int id;
            using (var db = _bd.Crear())
            {
                id = Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", DniAna, "A1").IdPersona;
                db.PersonaEmpresas.Single(v => v.IdPersona == id).FechaFin = new DateOnly(2026, 1, 31);   // empleo terminado en A
                db.SaveChanges();
            }
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas", IdEmpresa = DatosBase.EmpresaB },
                DatosBase.EmpresaB, "Ana", "Paz", DniAna, "C-9");

            using var lectura = _bd.Crear();
            var fila = Assert.Single((await Servicio(lectura).ListarAsync(Todos(), 1, 50)).Filas);

            Assert.Equal(new[] { TiposVinculo.Cliente, TiposVinculo.Empleado }, fila.Vinculos.Select(v => v.TipoVinculo));
            Assert.True(fila.Vinculos[0].Vigente);
            Assert.False(fila.Vinculos[1].Vigente);
            Assert.True(fila.TieneVinculoVigente);
        }

        // ── Empresas ────────────────────────────────────────────────────────

        [Fact]
        public async Task Empresas_trae_todas_las_que_no_estan_eliminadas()
        {
            using var db = _bd.Crear();
            db.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaB).Eliminado = true;
            db.SaveChanges();

            var empresas = await Servicio(db).EmpresasAsync();

            Assert.Equal(new[] { DatosBase.EmpresaA.ToString() }, empresas.Select(e => e.Valor));
        }

        // ── Detalle ─────────────────────────────────────────────────────────

        [Fact]
        public async Task El_detalle_trae_documentos_y_todos_los_vinculos_vigentes_y_terminados()
        {
            int id;
            using (var db = _bd.Crear())
            {
                id = Insertar(db, DatosBase.EmpresaA, "Ana", "Paz", DniAna, "A1").IdPersona;
                db.PersonaEmpresas.Single(v => v.IdPersona == id).FechaFin = new DateOnly(2026, 1, 31);
                db.SaveChanges();
            }
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas", IdEmpresa = DatosBase.EmpresaB },
                DatosBase.EmpresaB, "Ana", "Paz", DniAna, "C-1");

            using var lectura = _bd.Crear();
            var detalle = await Servicio(lectura).ObtenerAsync(id);

            Assert.NotNull(detalle);
            Assert.Equal("Ana Paz", detalle!.NombreCompleto);
            var documento = Assert.Single(detalle.Documentos);
            Assert.Equal("DNI", documento.TipoDocumento);
            Assert.True(documento.EsPrincipal);
            Assert.Equal(2, detalle.Vinculos.Count);
            Assert.Contains(detalle.Vinculos, v => v.TipoVinculo == TiposVinculo.Empleado && !v.Vigente);
            Assert.Contains(detalle.Vinculos, v => v.TipoVinculo == TiposVinculo.Cliente && v.Vigente);
        }

        [Fact]
        public async Task El_detalle_y_la_edicion_son_nulos_si_no_existe_esta_eliminada_o_fusionada()
        {
            int eliminada, fusionada, normal;
            using (var db = _bd.Crear())
            {
                normal = Insertar(db, DatosBase.EmpresaA, "Nor", "Mal", codigo: "N1").IdPersona;
                var principal = Insertar(db, DatosBase.EmpresaA, "Pri", "Ncipal", codigo: "P1");
                var e = Insertar(db, DatosBase.EmpresaA, "Eli", "Mina", codigo: "E1");
                var f = Insertar(db, DatosBase.EmpresaA, "Fus", "Ion", codigo: "F1");
                e.Eliminado = true;
                f.IdPersonaPrincipal = principal.IdPersona;
                db.SaveChanges();
                eliminada = e.IdPersona; fusionada = f.IdPersona;
            }

            using var lectura = _bd.Crear();
            var s = Servicio(lectura);
            foreach (var id in new[] { 0, -1, 9999, eliminada, fusionada })
            {
                Assert.Null(await s.ObtenerAsync(id));
                Assert.Null(await s.ObtenerParaEdicionAsync(id));
            }
            Assert.NotNull(await s.ObtenerAsync(normal));
            Assert.NotNull(await s.ObtenerParaEdicionAsync(normal));
        }

        [Fact]
        public async Task La_edicion_trae_solo_los_datos_personales_sin_empresa_ni_datos_de_empleo()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var p = Insertar(db, DatosBase.EmpresaB, "Ana", "Paz", DniAna, "A1", cargo: "CONDUCTOR", nacimiento: new DateOnly(1990, 5, 10));
                p.Telefono = "98765432";
                p.LicenciaTipo = "C"; p.LicenciaNumero = "LIC1"; p.LicenciaVencimiento = new DateOnly(2030, 1, 1);
                db.SaveChanges();
                id = p.IdPersona;
            }

            using var lectura = _bd.Crear();
            var edicion = await Servicio(lectura).ObtenerParaEdicionAsync(id);

            Assert.NotNull(edicion);
            var d = edicion!.Datos;
            Assert.Equal(id, d.IdPersona);
            Assert.Equal(0, d.IdEmpresa);
            Assert.Null(d.Empleado);                                 // el empleo es de cada empresa
            Assert.Equal("Ana", d.PrimerNombre);
            Assert.Equal(DniAna, d.Documento);
            Assert.Equal(new DateOnly(1990, 5, 10), d.FechaNacimiento);
            Assert.Equal("C", d.LicenciaTipo);
            Assert.Equal("LIC1", d.LicenciaNumero);
        }

        // ── Historial ───────────────────────────────────────────────────────

        [Fact]
        public async Task El_historial_del_administrador_trae_todo_con_la_empresa_de_cada_cambio_y_sin_ocultar_nada()
        {
            // Alta como empleado en la empresa B y como cliente en la A: cada una deja sus filas en la bitácora.
            int id;
            using (var db = _bd.Crear(new AuditoriaCambiosInterceptor(new ContextoAuditoriaFijo { Usuario = "rrhh.b", IdEmpresa = DatosBase.EmpresaB })))
            {
                var r = await ServicioPersonas(db).CrearAsync(new CrearPersonaInput
                {
                    Usuario = "rrhh.b",
                    Datos = new PersonaDatosInput
                    {
                        IdEmpresa = DatosBase.EmpresaB, PrimerNombre = "Ana", PrimerApellido = "Paz",
                        TipoDocumento = "DNI", Documento = DniAna, FechaNacimiento = new DateOnly(1990, 5, 10),
                        Empleado = new EmpleadoDatosInput { Cargo = "MECANICO", CodigoInterno = "B1", TarifaDiaria = 400m }
                    }
                });
                Assert.True(r.Ok);
                id = r.IdPersona!.Value;
            }
            // La empresa B le sube la tarifa: el administrador verá que cambió, pero no cuánto paga.
            using (var db = _bd.Crear(new AuditoriaCambiosInterceptor(new ContextoAuditoriaFijo { Usuario = "rrhh.b", IdEmpresa = DatosBase.EmpresaB })))
            {
                var empleado = await db.Empleados.SingleAsync();
                empleado.TarifaDiaria = 555m;
                await db.SaveChangesAsync();
            }
            await RegistrarCliente(new ContextoAuditoriaFijo { Usuario = "ventas.a", IdEmpresa = DatosBase.EmpresaA },
                DatosBase.EmpresaA, "Ana", "Paz", DniAna, "C-7");

            using var lectura = _bd.Crear();
            var admin = await Servicio(lectura).HistorialAsync(id);
            var deLaEmpresaA = await new PersonaConsultaService(lectura, Options.Create(new PersonaValidacionOptions())).HistorialAsync(DatosBase.EmpresaA, id);

            Assert.NotNull(admin);
            Assert.All(admin!.Filas, f => Assert.False(f.DeOtraEmpresa));

            // La ficha de empleado y el vínculo de la empresa B, que la empresa A no ve, el administrador sí.
            var fichaB = Assert.Single(admin.Filas, f => f.Entidad == "Ficha de empleado" && f.Operacion == "Alta");
            Assert.Equal("Empresa de prueba 4", fichaB.Empresa);
            Assert.Equal("rrhh.b", fichaB.Usuario);
            Assert.NotNull(fichaB.Detalle);

            // Los salarios no se ven: ni en el resumen del alta ni en el valor de un cambio.
            Assert.Contains("Código de empleado: B1", fichaB.Detalle);
            Assert.DoesNotContain("Tarifa", fichaB.Detalle);
            Assert.DoesNotContain("400", fichaB.Detalle);
            var cambioTarifa = Assert.Single(admin.Filas, f => f.Campo == "Tarifa diaria");
            Assert.Equal("(no se muestra)", cambioTarifa.ValorAnterior);
            Assert.Equal("(no se muestra)", cambioTarifa.ValorNuevo);
            Assert.DoesNotContain(admin.Filas, f => (f.ValorNuevo ?? "").Contains("555") || (f.ValorAnterior ?? "").Contains("400"));
            Assert.DoesNotContain(admin.Filas, f => f.Campo == "Moneda de la tarifa" && f.ValorNuevo != "(no se muestra)");

            var clienteA = Assert.Single(admin.Filas, f => f.Entidad == "Ficha de cliente" && f.Operacion == "Alta");
            Assert.Equal("Empresa de prueba 1", clienteA.Empresa);
            Assert.Equal("ventas.a", clienteA.Usuario);

            Assert.NotNull(deLaEmpresaA);
            Assert.DoesNotContain(deLaEmpresaA!.Filas, f => f.Entidad == "Ficha de empleado");
            Assert.True(admin.Filas.Count > deLaEmpresaA.Filas.Count);
        }

        [Fact]
        public async Task El_historial_es_nulo_si_la_persona_no_existe_o_esta_eliminada()
        {
            int eliminada;
            using (var db = _bd.Crear())
            {
                var e = Insertar(db, DatosBase.EmpresaA, "Eli", "Mina", codigo: "E1");
                e.Eliminado = true; db.SaveChanges();
                eliminada = e.IdPersona;
            }

            using var lectura = _bd.Crear();
            Assert.Null(await Servicio(lectura).HistorialAsync(9999));
            Assert.Null(await Servicio(lectura).HistorialAsync(eliminada));
            Assert.Null(await Servicio(lectura).HistorialAsync(0));
        }
    }
}
