using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Personas
{
    public class UsuarioPersonaServiceTests : IDisposable
    {
        private const int Plataforma = DatosBase.EmpresaA;   // la empresa dueña de la plataforma en estas pruebas
        private const int IdAdmin = 1;                       // el usuario admin que siembra el modelo (sin empresa)

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _auditoria = new() { Usuario = "admin", IdEmpresa = null };

        private static readonly QuienOperaUsuarios Admin = new(true, null, "admin");
        private static QuienOperaUsuarios AdminDe(int empresa) => new(false, empresa, "jefe");

        public void Dispose() => _bd.Dispose();

        // ── Utilidades ──────────────────────────────────────────────────────

        private ApplicationDbContext Contexto() => _bd.Crear(new AuditoriaCambiosInterceptor(_auditoria));

        private UsuarioPersonaService Servicio(ApplicationDbContext db) =>
            new(db,
                new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), _reloj),
                Options.Create(new PlataformaOptions { IdEmpresaPropia = Plataforma }),
                _reloj,
                NullLogger<UsuarioPersonaService>.Instance);

        private int Usuario(string nombre, int? empresa)
        {
            using var db = _bd.Crear();
            var u = new User
            {
                Username = nombre, Email = nombre + "@prueba.hn", Password = "x",
                Role = empresa == null ? "admin" : "empresa_user", EmpresaId = empresa
            };
            db.Users.Add(u);
            db.SaveChanges();
            return u.Id;
        }

        private int Persona(int empresa, string nombre, string apellido, string? dni = null, DateOnly? nacimiento = null)
        {
            using var db = _bd.Crear();
            return PersonasDePrueba.Insertar(db, empresa, nombre, apellido, dni: dni, codigoInterno: "E-" + nombre, nacimiento: nacimiento).IdPersona;
        }

        private static PersonaDatosInput Datos(string nombre = "Emilio", string apellido = "Garay", string? dni = "0801-1990-12345",
            DateOnly? nacimiento = null) => new()
        {
            PrimerNombre = nombre, PrimerApellido = apellido,
            TipoDocumento = dni != null ? "DNI" : null, Documento = dni,
            FechaNacimiento = nacimiento ?? new DateOnly(1990, 5, 10)
        };

        private async Task<ResultadoUsuarioPersona> Vincular(QuienOperaUsuarios quien, int idUsuario, int idPersona)
        {
            using var db = Contexto();
            return await Servicio(db).VincularAsync(quien, idUsuario, idPersona);
        }

        private async Task<ResultadoUsuarioPersona> Crear(QuienOperaUsuarios quien, int idUsuario, PersonaDatosInput datos, bool confirmar = false)
        {
            using var db = Contexto();
            return await Servicio(db).CrearPersonaYVincularAsync(quien, idUsuario, datos, confirmar);
        }

        private async Task<ResultadoUsuarioPersona> Quitar(QuienOperaUsuarios quien, int idUsuario)
        {
            using var db = Contexto();
            return await Servicio(db).QuitarAsync(quien, idUsuario);
        }

        private static string Mensajes(ResultadoUsuarioPersona r) => string.Join(" | ", r.Errores.Select(e => $"{e.Campo}: {e.Mensaje}"));

        // ── Crear la persona del usuario ────────────────────────────────────

        [Fact]
        public async Task Crear_la_persona_del_administrador_la_deja_en_la_empresa_de_la_plataforma_con_un_vinculo_de_usuario()
        {
            var r = await Crear(Admin, IdAdmin, Datos());

            Assert.Equal(EstadoUsuarioPersona.Creado, r.Estado);
            using var db = _bd.Crear();
            var persona = await db.Personas.AsNoTracking().Include(p => p.Vinculos).Include(p => p.Documentos).SingleAsync(p => p.IdPersona == r.IdPersona);
            Assert.Equal("Emilio", persona.PrimerNombre);
            Assert.Equal(EstadosIdentidad.Verificada, persona.EstadoIdentidad);
            var vinculo = Assert.Single(persona.Vinculos);
            Assert.Equal(TiposVinculo.Usuario, vinculo.TipoVinculo);
            Assert.Equal(Plataforma, vinculo.IdEmpresa);
            Assert.True(vinculo.Activo);
            Assert.Null(vinculo.FechaFin);
            Assert.Equal(r.IdPersona, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == IdAdmin)).PersonaId);
        }

        [Fact]
        public async Task La_bitacora_guarda_el_id_real_de_la_persona_del_usuario_y_nada_mas_de_la_cuenta()
        {
            var r = await Crear(Admin, IdAdmin, Datos());

            using var db = _bd.Crear();
            var fila = await db.BitacoraCambios.AsNoTracking().SingleAsync(b => b.Entidad == "Users");
            Assert.Equal("UPDATE", fila.Operacion);
            Assert.Equal("PersonaId", fila.Campo);
            Assert.Null(fila.ValorAnterior);
            Assert.Equal(r.IdPersona!.Value.ToString(), fila.ValorNuevo);
            Assert.Equal(IdAdmin, fila.IdRegistro);
            Assert.Equal(r.IdPersona, fila.IdPersona);
            // La persona y su vínculo también quedaron, como cualquier alta.
            Assert.True(await db.BitacoraCambios.AnyAsync(b => b.Entidad == "personas" && b.Operacion == "INSERT"));
            Assert.True(await db.BitacoraCambios.AnyAsync(b => b.Entidad == "persona_empresa" && b.Operacion == "INSERT"));
        }

        [Fact]
        public async Task Cambiar_la_clave_el_correo_o_el_estado_de_un_usuario_no_deja_nada_en_la_bitacora()
        {
            var idUsuario = Usuario("egaray", Plataforma);
            using (var db = Contexto())
            {
                var u = await db.Users.SingleAsync(x => x.Id == idUsuario);
                u.Password = "otra-clave";
                u.Email = "nuevo@prueba.hn";
                u.IsActive = false;
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.False(await lectura.BitacoraCambios.AnyAsync());
        }

        [Fact]
        public async Task Crear_exige_el_documento_y_la_fecha_de_nacimiento()
        {
            var r = await Crear(Admin, IdAdmin, Datos(dni: null));
            var sinFecha = Datos();
            sinFecha.FechaNacimiento = null;
            var r2 = await Crear(Admin, IdAdmin, sinFecha);

            Assert.Equal(EstadoUsuarioPersona.Rechazado, r.Estado);
            Assert.Contains(r.Errores, e => e.Campo == nameof(PersonaDatosInput.Documento));
            Assert.Equal(EstadoUsuarioPersona.Rechazado, r2.Estado);
            Assert.Contains(r2.Errores, e => e.Campo == nameof(PersonaDatosInput.FechaNacimiento));
            using var db = _bd.Crear();
            Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == IdAdmin)).PersonaId);
        }

        [Fact]
        public async Task Crear_con_un_documento_que_la_empresa_ya_tiene_pide_buscar_a_la_persona()
        {
            Persona(Plataforma, "Emilio", "Garay", dni: "0801-1990-12345");
            var idUsuario = Usuario("egaray", Plataforma);

            var r = await Crear(AdminDe(Plataforma), idUsuario, Datos());

            Assert.Equal(EstadoUsuarioPersona.Rechazado, r.Estado);
            Assert.Contains("búscala", Mensajes(r));
        }

        [Fact]
        public async Task El_administrador_general_que_escribe_un_documento_de_otra_empresa_tambien_debe_buscar_a_la_persona()
        {
            Persona(DatosBase.EmpresaB, "Emilio", "Garay", dni: "0801-1990-12345");

            var r = await Crear(Admin, IdAdmin, Datos());

            Assert.Equal(EstadoUsuarioPersona.Rechazado, r.Estado);
            Assert.Contains("búscala", Mensajes(r));
        }

        [Fact]
        public async Task Un_administrador_de_empresa_vincula_a_una_persona_de_otra_empresa_solo_si_coinciden_nombre_y_apellido()
        {
            var idOtra = Persona(DatosBase.EmpresaB, "Ana", "Paz", dni: "0801-1990-12345");
            var idUsuario = Usuario("ana.paz", Plataforma);

            var mal = await Crear(AdminDe(Plataforma), idUsuario, Datos("Otra", "Gente"));
            Assert.Equal(EstadoUsuarioPersona.Rechazado, mal.Estado);
            Assert.Contains("No se pudo vincular", Mensajes(mal));

            var bien = await Crear(AdminDe(Plataforma), idUsuario, Datos("Ana", "Paz"));
            Assert.Equal(EstadoUsuarioPersona.Vinculado, bien.Estado);
            Assert.Equal(idOtra, bien.IdPersona);

            using var db = _bd.Crear();
            Assert.Equal(1, await db.Personas.CountAsync(p => p.PrimerApellido == "Paz"));   // no se duplicó
            Assert.Contains(await db.PersonaEmpresas.AsNoTracking().Where(v => v.IdPersona == idOtra).ToListAsync(),
                v => v.IdEmpresa == Plataforma && v.TipoVinculo == TiposVinculo.Usuario && v.Activo);
        }

        [Fact]
        public async Task Avisa_de_las_personas_parecidas_de_la_empresa_y_crea_si_se_confirma()
        {
            Persona(Plataforma, "Emilio", "Garay", nacimiento: new DateOnly(1990, 5, 10));
            var idUsuario = Usuario("egaray", Plataforma);

            var aviso = await Crear(AdminDe(Plataforma), idUsuario, Datos());
            Assert.Equal(EstadoUsuarioPersona.RequiereConfirmacion, aviso.Estado);
            Assert.NotEmpty(aviso.Parecidas);

            var creado = await Crear(AdminDe(Plataforma), idUsuario, Datos(), confirmar: true);
            Assert.Equal(EstadoUsuarioPersona.Creado, creado.Estado);
        }

        // ── Vincular y quitar ───────────────────────────────────────────────

        [Fact]
        public async Task Vincular_con_quien_ya_trabaja_en_la_empresa_del_usuario_no_agrega_otro_vinculo()
        {
            var idPersona = Persona(Plataforma, "Emilio", "Garay", dni: "0801-1990-12345");
            var idUsuario = Usuario("egaray", Plataforma);

            var r = await Vincular(AdminDe(Plataforma), idUsuario, idPersona);

            Assert.Equal(EstadoUsuarioPersona.Vinculado, r.Estado);
            using var db = _bd.Crear();
            var vinculo = Assert.Single(await db.PersonaEmpresas.AsNoTracking().Where(v => v.IdPersona == idPersona).ToListAsync());
            Assert.Equal(TiposVinculo.Empleado, vinculo.TipoVinculo);
            Assert.Equal(idPersona, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == idUsuario)).PersonaId);
        }

        [Fact]
        public async Task Vincular_otra_vez_a_la_misma_persona_no_cambia_nada()
        {
            var idPersona = Persona(Plataforma, "Emilio", "Garay");
            var idUsuario = Usuario("egaray", Plataforma);
            await Vincular(AdminDe(Plataforma), idUsuario, idPersona);

            var r = await Vincular(AdminDe(Plataforma), idUsuario, idPersona);

            Assert.Equal(EstadoUsuarioPersona.SinCambios, r.Estado);
            using var db = _bd.Crear();
            Assert.Equal(1, await db.BitacoraCambios.CountAsync(b => b.Entidad == "Users"));
        }

        [Fact]
        public async Task Una_persona_puede_tener_dos_usuarios_y_su_vinculo_de_usuario_se_cierra_al_quitar_el_ultimo()
        {
            var creado = await Crear(Admin, IdAdmin, Datos());
            var idPersona = creado.IdPersona!.Value;
            var idEgaray = Usuario("egaray", Plataforma);

            Assert.Equal(EstadoUsuarioPersona.Vinculado, (await Vincular(Admin, idEgaray, idPersona)).Estado);

            using (var db = _bd.Crear())
            {
                Assert.Equal(2, await db.Users.CountAsync(u => u.PersonaId == idPersona));
                Assert.Single(await db.PersonaEmpresas.Where(v => v.IdPersona == idPersona).ToListAsync());   // no se duplicó
            }

            // Quitar uno: queda egaray, que también es de la plataforma, así que el vínculo sigue abierto.
            Assert.Equal(EstadoUsuarioPersona.Quitado, (await Quitar(Admin, IdAdmin)).Estado);
            using (var db = _bd.Crear())
            {
                var vinculo = await db.PersonaEmpresas.AsNoTracking().SingleAsync(v => v.IdPersona == idPersona);
                Assert.True(vinculo.Activo);
                Assert.Null(vinculo.FechaFin);
            }

            // Quitar el último: el vínculo de usuario se cierra (no se borra) y la persona queda inactiva.
            Assert.Equal(EstadoUsuarioPersona.Quitado, (await Quitar(Admin, idEgaray)).Estado);
            using var lectura = _bd.Crear();
            var cerrado = await lectura.PersonaEmpresas.AsNoTracking().SingleAsync(v => v.IdPersona == idPersona);
            Assert.False(cerrado.Activo);
            Assert.Equal(RelojFijo.Hoy, cerrado.FechaFin);
            Assert.False(cerrado.Eliminado);
            Assert.False((await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).Activo);
            Assert.Equal(0, await lectura.Users.CountAsync(u => u.PersonaId == idPersona));

            // Volver a vincular reabre el mismo vínculo de usuario.
            Assert.Equal(EstadoUsuarioPersona.Vinculado, (await Vincular(Admin, IdAdmin, idPersona)).Estado);
            using var final = _bd.Crear();
            var reabierto = await final.PersonaEmpresas.AsNoTracking().SingleAsync(v => v.IdPersona == idPersona);
            Assert.True(reabierto.Activo);
            Assert.Null(reabierto.FechaFin);
            Assert.True((await final.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).Activo);
        }

        [Fact]
        public async Task Quitar_no_toca_los_vinculos_de_empleado()
        {
            var idPersona = Persona(Plataforma, "Emilio", "Garay");
            var idUsuario = Usuario("egaray", Plataforma);
            await Vincular(AdminDe(Plataforma), idUsuario, idPersona);

            await Quitar(AdminDe(Plataforma), idUsuario);

            using var db = _bd.Crear();
            var vinculo = await db.PersonaEmpresas.AsNoTracking().SingleAsync(v => v.IdPersona == idPersona);
            Assert.True(vinculo.Activo);
            Assert.Null(vinculo.FechaFin);
        }

        // ── Separación entre empresas ───────────────────────────────────────

        [Fact]
        public async Task Un_administrador_de_empresa_no_ve_ni_vincula_personas_ni_usuarios_de_otra_empresa()
        {
            var idDeOtra = Persona(DatosBase.EmpresaB, "Beto", "Rivas", dni: "0801-1985-11111");
            var idUsuarioPropio = Usuario("propio", Plataforma);
            var idUsuarioAjeno = Usuario("ajeno", DatosBase.EmpresaB);
            var idPropia = Persona(Plataforma, "Ana", "Paz");
            var quien = AdminDe(Plataforma);

            using var db = Contexto();
            var servicio = Servicio(db);
            Assert.Empty(await servicio.BuscarPersonasAsync(quien, "Rivas"));
            Assert.Empty(await servicio.BuscarPersonasAsync(quien, "1985111"));
            Assert.Equal(EstadoUsuarioPersona.NoEncontrado, (await servicio.VincularAsync(quien, idUsuarioPropio, idDeOtra)).Estado);
            Assert.Equal(EstadoUsuarioPersona.NoEncontrado, (await servicio.VincularAsync(quien, idUsuarioAjeno, idPropia)).Estado);
            Assert.Equal(EstadoUsuarioPersona.NoEncontrado, (await servicio.QuitarAsync(quien, idUsuarioAjeno)).Estado);
            Assert.Null(await servicio.PersonaDelUsuarioAsync(quien, idUsuarioAjeno));
        }

        [Fact]
        public async Task La_busqueda_enmascara_el_documento_y_solo_muestra_usuarios_y_empresas_a_quien_puede_verlos()
        {
            var idPersona = Persona(Plataforma, "Ana", "Paz", dni: "0801-1990-12345");
            var idPropio = Usuario("ana.paz", Plataforma);
            await Vincular(Admin, idPropio, idPersona);
            await Vincular(Admin, IdAdmin, idPersona);   // el admin no tiene empresa

            using var db = Contexto();
            var servicio = Servicio(db);

            var deEmpresa = Assert.Single(await servicio.BuscarPersonasAsync(AdminDe(Plataforma), "paz"));
            Assert.Equal("Paz, Ana", deEmpresa.NombreCompleto);
            Assert.DoesNotContain("12345", deEmpresa.Documento!.Replace("*", ""), StringComparison.Ordinal);
            Assert.EndsWith("2345", deEmpresa.Documento);
            Assert.Equal(new[] { "ana.paz" }, deEmpresa.Usuarios);   // el admin no es de la empresa: no se muestra
            Assert.Empty(deEmpresa.Empresas);

            var deAdmin = Assert.Single(await servicio.BuscarPersonasAsync(Admin, "PAZ"));
            Assert.Equal(new[] { "admin", "ana.paz" }, deAdmin.Usuarios);
            Assert.NotEmpty(deAdmin.Empresas);

            Assert.Empty(await servicio.BuscarPersonasAsync(Admin, "p"));   // menos de 2 caracteres
        }

        // ── Historial ───────────────────────────────────────────────────────

        [Fact]
        public async Task El_historial_de_la_persona_dice_que_usuario_se_vinculo_sin_mostrar_ids()
        {
            var creado = await Crear(Admin, IdAdmin, Datos());

            using var db = _bd.Crear();
            var historial = await new PersonaAdminConsultaService(db, Options.Create(new PersonaValidacionOptions()))
                .HistorialAsync(creado.IdPersona!.Value);

            var fila = Assert.Single(historial!.Filas, f => f.Entidad == "Usuario del sistema");
            Assert.Equal("Persona vinculada del usuario admin", fila.Campo);
            Assert.Null(fila.ValorAnterior);
            Assert.Equal("Esta persona", fila.ValorNuevo);
        }
    }
}
