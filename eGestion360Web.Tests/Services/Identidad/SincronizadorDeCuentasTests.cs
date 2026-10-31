using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Identidad
{
    /// <summary>Las cuentas globales siguen a <c>dbo.Users</c> mientras Users sea donde se escribe (F1.5 a F1.9).</summary>
    public class SincronizadorDeCuentasTests : IDisposable
    {
        private sealed class ValidadorEspia : IValidadorDeIdentidad
        {
            public List<int> Invalidadas { get; } = new();
            public Task<ResultadoDeValidacion> ValidarAsync(ClaimsPrincipal principal, CancellationToken ct = default) => Task.FromResult(ResultadoDeValidacion.SinCambios);
            public void Invalidar(int idCuenta) => Invalidadas.Add(idCuenta);
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ValidadorEspia _validador = new();

        public void Dispose() => _bd.Dispose();

        private SincronizadorDeCuentas Servicio(ApplicationDbContext db)
            => new(db, IdentidadDePrueba.Tenant(), _validador, NullLogger<SincronizadorDeCuentas>.Instance, _reloj);

        private static User NuevoUsuario(ApplicationDbContext db, string nombre, string rol = "empresa_user", int? idEmpresa = DatosBase.EmpresaA,
            int? idRol = null, bool conPersona = true, bool activo = true, string? correo = null, bool cambiarClave = false)
        {
            int? idPersona = conPersona ? IdentidadDePrueba.NuevaPersona(db, idEmpresa ?? DatosBase.EmpresaA) : null;
            var usuario = new User
            {
                Username = nombre, Email = correo ?? $"{nombre}@Prueba.Local", Password = IdentidadDePrueba.Hash("Clave-Segura-1"), Role = rol,
                EmpresaId = idEmpresa, EmpresaRolId = idRol, PersonaId = idPersona, IsActive = activo, RequirePasswordChange = cambiarClave
            };
            db.Users.Add(usuario);
            db.SaveChanges();
            return usuario;
        }

        [Fact]
        public async Task Un_usuario_nuevo_con_empresa_y_persona_obtiene_cuenta_membresia_y_acceso()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "ana");

            await Servicio(db).SincronizarAsync(usuario.Id);

            var cuenta = Assert.Single(db.Cuentas);
            Assert.Equal("ana@prueba.local", cuenta.Correo);                    // el correo se guarda en minúsculas
            Assert.Equal("ana", cuenta.NombreUsuario);
            Assert.Equal(usuario.Password, cuenta.HashClave);
            Assert.Equal(EstadoCuenta.Activa, cuenta.Estado);
            Assert.False(cuenta.EsOperador);
            Assert.Equal(usuario.Id, cuenta.IdUserLegado);

            var membresia = Assert.Single(db.Membresias);
            Assert.Equal(cuenta.IdCuenta, membresia.IdCuenta);
            Assert.Equal(DatosBase.TenantA, membresia.IdTenant);
            Assert.Equal(usuario.PersonaId, membresia.IdPersona);
            Assert.Equal(EstadoMembresia.Activa, membresia.Estado);

            var acceso = Assert.Single(db.AccesosEmpresa);
            Assert.Equal(membresia.IdMembresia, acceso.IdMembresia);
            Assert.Equal(DatosBase.EmpresaA, acceso.IdEmpresa);
            Assert.Equal(DatosBase.TenantA, acceso.IdTenant);
            Assert.Equal("empresa_user", acceso.RolLegado);
            Assert.True(acceso.Activo);
            Assert.True(acceso.EsPrincipal);
            Assert.Contains(cuenta.IdCuenta, _validador.Invalidadas);
        }

        [Fact]
        public async Task El_rol_de_empresa_y_el_rol_de_texto_pasan_al_acceso()
        {
            using var db = _bd.Crear();
            var rol = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, Nombre = "Flota" };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();
            var admin = NuevoUsuario(db, "jefe", "empresa_admin");
            var operativo = NuevoUsuario(db, "chofer", "empresa_user", idRol: rol.IdRol);
            var adminConEmpresa = NuevoUsuario(db, "superjefe", "admin");

            foreach (var u in new[] { admin, operativo, adminConEmpresa }) await Servicio(db).SincronizarAsync(u.Id);

            string RolDe(User u) => db.AccesosEmpresa.Single(a => a.IdMembresia == db.Membresias.Single(m => m.IdCuenta == db.Cuentas.Single(c => c.IdUserLegado == u.Id).IdCuenta).IdMembresia).RolLegado;
            Assert.Equal("empresa_admin", RolDe(admin));
            Assert.Equal("empresa_user", RolDe(operativo));
            Assert.Equal("empresa_admin", RolDe(adminConEmpresa));   // la base solo admite empresa_admin y empresa_user
            Assert.Equal(rol.IdRol, db.AccesosEmpresa.Single(a => a.IdMembresia == db.Membresias.Single(m => m.IdCuenta == db.Cuentas.Single(c => c.IdUserLegado == operativo.Id).IdCuenta).IdMembresia).IdRol);
        }

        [Fact]
        public async Task Un_administrador_general_sin_empresa_es_operador_y_no_tiene_membresia()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "operador", "admin", idEmpresa: null);

            await Servicio(db).SincronizarAsync(usuario.Id);

            Assert.True(db.Cuentas.Single().EsOperador);
            Assert.Empty(db.Membresias);
            Assert.Empty(db.AccesosEmpresa);
        }

        [Fact]
        public async Task Sin_persona_la_cuenta_nace_inactiva_la_membresia_suspendida_y_el_acceso_inactivo()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "sinpersona", conPersona: false);

            await Servicio(db).SincronizarAsync(usuario.Id);

            Assert.Equal(EstadoCuenta.Inactiva, db.Cuentas.Single().Estado);
            Assert.Equal(EstadoMembresia.Suspendida, db.Membresias.Single().Estado);
            Assert.False(db.AccesosEmpresa.Single().Activo);
        }

        [Fact]
        public async Task Vincular_la_persona_despues_activa_la_cuenta_la_membresia_y_el_acceso()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "luego", conPersona: false);
            await Servicio(db).SincronizarAsync(usuario.Id);

            usuario.PersonaId = IdentidadDePrueba.NuevaPersona(db, DatosBase.EmpresaA);
            db.SaveChanges();
            await Servicio(db).SincronizarAsync(usuario.Id);

            Assert.Equal(EstadoCuenta.Activa, db.Cuentas.Single().Estado);
            Assert.Equal(usuario.PersonaId, db.Membresias.Single().IdPersona);
            Assert.Equal(EstadoMembresia.Activa, db.Membresias.Single().Estado);
            Assert.True(db.AccesosEmpresa.Single().Activo);
        }

        [Fact]
        public async Task Desactivar_al_usuario_desactiva_la_cuenta_y_su_acceso()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "baja");
            await Servicio(db).SincronizarAsync(usuario.Id);

            usuario.IsActive = false;
            db.SaveChanges();
            await Servicio(db).SincronizarAsync(usuario.Id);

            Assert.Equal(EstadoCuenta.Inactiva, db.Cuentas.Single().Estado);
            Assert.Equal(EstadoMembresia.Suspendida, db.Membresias.Single().Estado);
            Assert.False(db.AccesosEmpresa.Single().Activo);
        }

        [Fact]
        public async Task Cambiar_correo_clave_y_cambio_pendiente_llega_a_la_cuenta_y_repetir_no_duplica()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "cambio");
            await Servicio(db).SincronizarAsync(usuario.Id);

            usuario.Email = "Nuevo@Prueba.Local";
            usuario.Password = IdentidadDePrueba.Hash("Otra-Clave-2");
            usuario.RequirePasswordChange = true;
            db.SaveChanges();
            await Servicio(db).SincronizarAsync(usuario.Id);
            await Servicio(db).SincronizarAsync(usuario.Id);

            var cuenta = Assert.Single(db.Cuentas);
            Assert.Equal("nuevo@prueba.local", cuenta.Correo);
            Assert.Equal(usuario.Password, cuenta.HashClave);
            Assert.True(cuenta.DebeCambiarClave);
            Assert.Single(db.Membresias);
            Assert.Single(db.AccesosEmpresa);
        }

        [Fact]
        public async Task Si_el_usuario_cambia_de_empresa_el_acceso_anterior_deja_de_valer()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "muda");
            await Servicio(db).SincronizarAsync(usuario.Id);

            usuario.EmpresaId = DatosBase.EmpresaA2;     // otra empresa del mismo tenant
            db.SaveChanges();
            await Servicio(db).SincronizarAsync(usuario.Id);

            var accesos = db.AccesosEmpresa.OrderBy(a => a.IdEmpresa).ToList();
            Assert.Equal(2, accesos.Count);
            Assert.False(accesos.Single(a => a.IdEmpresa == DatosBase.EmpresaA).Activo);
            Assert.False(accesos.Single(a => a.IdEmpresa == DatosBase.EmpresaA).EsPrincipal);
            Assert.True(accesos.Single(a => a.IdEmpresa == DatosBase.EmpresaA2).Activo);
            Assert.True(accesos.Single(a => a.IdEmpresa == DatosBase.EmpresaA2).EsPrincipal);
        }

        [Fact]
        public async Task Un_alias_cambia_solo_su_acceso_y_la_clave_no_el_correo_ni_el_estado_de_la_cuenta()
        {
            using var db = _bd.Crear();
            // La cuenta de Emilio: principal "operador" y un alias "egaray" que trabaja en la empresa A.
            var admin = NuevoUsuario(db, "operador", "admin", idEmpresa: null);
            await Servicio(db).SincronizarAsync(admin.Id);
            var egaray = NuevoUsuario(db, "egaray", "empresa_user", correo: "egaray@otro.local");
            var cuenta = db.Cuentas.Single();
            db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "egaray", Tipo = TipoAlias.Usuario, PuedeIniciarSesion = true, IdUserLegado = egaray.Id, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            db.SaveChanges();
            var correoAntes = cuenta.Correo;

            egaray.Password = IdentidadDePrueba.Hash("Clave-De-Egaray-1");
            egaray.IsActive = false;                     // desactivar el alias NO desactiva la cuenta de Emilio
            db.SaveChanges();
            await Servicio(db).SincronizarAsync(egaray.Id);

            var despues = Assert.Single(db.Cuentas);
            Assert.Equal(correoAntes, despues.Correo);
            Assert.Equal("operador", despues.NombreUsuario);
            Assert.Equal(EstadoCuenta.Activa, despues.Estado);
            Assert.Equal(egaray.Password, despues.HashClave);
            Assert.False(db.AccesosEmpresa.Single().Activo);   // el acceso del alias sí se apaga
        }

        [Fact]
        public async Task Si_el_correo_ya_lo_usa_otra_cuenta_no_se_crea_otra_y_no_se_rompe_nada()
        {
            using var db = _bd.Crear();
            IdentidadDePrueba.NuevaCuenta(db, "repetido@prueba.local");
            var usuario = NuevoUsuario(db, "repetido", correo: "Repetido@Prueba.Local");

            await Servicio(db).SincronizarAsync(usuario.Id);

            Assert.Single(db.Cuentas);
            Assert.Empty(db.Membresias);
        }

        [Fact]
        public async Task Un_usuario_que_no_existe_no_hace_nada()
        {
            using var db = _bd.Crear();

            await Servicio(db).SincronizarAsync(9999);
            await Servicio(db).SincronizarClaveAsync(9999);

            Assert.Empty(db.Cuentas);
        }

        // ── Solo la clave ─────────────────────────────────────────────────────

        [Fact]
        public async Task La_clave_nueva_llega_a_la_cuenta_levanta_el_bloqueo_y_olvida_lo_recordado()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "olvido");
            await Servicio(db).SincronizarAsync(usuario.Id);
            var cuenta = db.Cuentas.Single();
            db.CuentaIntentos.Add(new CuentaIntentos { IdCuenta = cuenta.IdCuenta, IntentosFallidos = 3, BloqueadaHasta = IdentidadDePrueba.Ahora.AddHours(1) });
            db.SaveChanges();

            usuario.Password = IdentidadDePrueba.Hash("Clave-Restablecida-1");
            usuario.RequirePasswordChange = true;
            db.SaveChanges();
            _validador.Invalidadas.Clear();
            await Servicio(db).SincronizarClaveAsync(usuario.Id);

            db.ChangeTracker.Clear();
            Assert.Equal(usuario.Password, db.Cuentas.Single().HashClave);
            Assert.True(db.Cuentas.Single().DebeCambiarClave);
            var intentos = db.CuentaIntentos.Single();
            Assert.Equal(0, intentos.IntentosFallidos);
            Assert.Null(intentos.BloqueadaHasta);
            Assert.Equal(new[] { cuenta.IdCuenta }, _validador.Invalidadas);
        }

        [Fact]
        public async Task La_clave_de_un_usuario_sin_cuenta_no_crea_ninguna()
        {
            using var db = _bd.Crear();
            var usuario = NuevoUsuario(db, "sincuenta");

            await Servicio(db).SincronizarClaveAsync(usuario.Id);

            Assert.Empty(db.Cuentas);
        }
    }
}
