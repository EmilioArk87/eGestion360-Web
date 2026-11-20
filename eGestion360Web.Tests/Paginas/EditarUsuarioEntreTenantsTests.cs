using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using EditModel = eGestion360Web.Pages.Admin.Usuarios.EditModel;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Editar un usuario no lo pasa a otro tenant ni deja nada a medias. El 2026-11-18, desde la plataforma, se le puso al usuario egaray
    /// la empresa de CAPAE: dbo.Users se guardó, la membresía falló (su persona era de SIP) y el usuario quedó con una empresa y la cuenta
    /// con otra. Ahora la empresa tiene que ser del tenant del usuario, el rol de esa empresa, y si la cuenta igual falla, Users se deshace.
    /// </summary>
    public class EditarUsuarioEntreTenantsTests : IDisposable
    {
        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        /// <summary>Un sincronizador que falla la primera vez, como la membresía con la persona de otro tenant en SQL Server.</summary>
        private sealed class SincronizadorQueFalla : ISincronizadorDeCuentas
        {
            private readonly ISincronizadorDeCuentas _real;
            public int Llamadas { get; private set; }
            public SincronizadorQueFalla(ISincronizadorDeCuentas real) => _real = real;

            public Task SincronizarAsync(int idUser, CancellationToken ct = default)
            {
                Llamadas++;
                if (Llamadas == 1) throw new DbUpdateException("FKT_membresias_personas_id_persona (simulado)");
                return _real.SincronizarAsync(idUser, ct);
            }

            public Task SincronizarClaveAsync(int idUser, CancellationToken ct = default) => _real.SincronizarClaveAsync(idUser, ct);
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();

        public void Dispose() => _bd.Dispose();

        private static HttpContext OperadorEnPlataforma() => SesionFalsa.Contexto("admin");

        private EditModel Pagina(ApplicationDbContext db, ISincronizadorDeCuentas? cuentas = null)
        {
            var pagina = new EditModel(db, InvitacionesDePrueba.Personas(db, _reloj), cuentas ?? InvitacionesDePrueba.Sincronizador(db, _reloj),
                NullLogger<EditModel>.Instance);
            var http = OperadorEnPlataforma();
            pagina.PageContext = new PageContext { HttpContext = http };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        /// <summary>Un usuario con persona en la empresa A (tenant A), ya con su cuenta, membresía y acceso.</summary>
        private async Task<int> UsuarioConCuentaAsync(string nombre = "egaray", int? idRol = null)
        {
            using var db = _bd.Crear();
            var idPersona = IdentidadDePrueba.NuevaPersona(db, DatosBase.EmpresaA);
            var usuario = new User
            {
                Username = nombre, Email = nombre + "@prueba.local", Password = IdentidadDePrueba.Hash("Clave-Segura-1"), Role = "empresa_user",
                EmpresaId = DatosBase.EmpresaA, EmpresaRolId = idRol, PersonaId = idPersona, IsActive = true
            };
            db.Users.Add(usuario);
            db.SaveChanges();
            await InvitacionesDePrueba.Sincronizador(db, _reloj).SincronizarAsync(usuario.Id);
            return usuario.Id;
        }

        private int NuevoRol(int empresa)
        {
            using var db = _bd.Crear();
            var rol = new EmpresaRol { IdEmpresa = empresa, IdTenant = DatosBase.TenantDe(empresa), Nombre = "Rol " + empresa, Activo = true };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();
            return rol.IdRol;
        }

        private static void Formulario(EditModel pagina, int idUsuario, int? empresa, int? rol = null, string role = "empresa_admin")
            => (pagina.Id, pagina.Username, pagina.Email, pagina.Role, pagina.IsActive, pagina.EmpresaId, pagina.EmpresaRolId)
             = (idUsuario, "egaray", "egaray@prueba.local", role, true, empresa, rol);

        private User Usuario(int id)
        {
            using var db = _bd.Crear();
            return db.Users.AsNoTracking().Single(u => u.Id == id);
        }

        private int MembresiasEn(int idTenant)
        {
            using var db = _bd.Crear();
            return db.Membresias.Count(m => m.IdTenant == idTenant);
        }

        [Fact]
        public async Task El_operador_no_pasa_un_usuario_a_una_empresa_de_otro_tenant_y_no_se_guarda_nada()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            Formulario(pagina, idUsuario, DatosBase.EmpresaB);

            var resultado = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(resultado);
            Assert.Equal(EditModel.MensajeEmpresaDeOtroTenant, Assert.Single(pagina.ModelState[nameof(EditModel.EmpresaId)]!.Errors).ErrorMessage);
            var usuario = Usuario(idUsuario);
            Assert.Equal(DatosBase.EmpresaA, usuario.EmpresaId);   // ni la empresa
            Assert.Equal("empresa_user", usuario.Role);            // ni el rol de texto cambiaron
            Assert.Equal(0, MembresiasEn(DatosBase.TenantB));       // y la cuenta no se tocó
        }

        [Fact]
        public async Task Dentro_del_mismo_tenant_si_se_cambia_de_empresa()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            Formulario(pagina, idUsuario, DatosBase.EmpresaA2, role: "empresa_user");

            var resultado = await pagina.OnPostAsync();

            Assert.Equal("/UserManagement", Assert.IsType<RedirectToPageResult>(resultado).PageName);
            Assert.Equal(DatosBase.EmpresaA2, Usuario(idUsuario).EmpresaId);
            using var lectura = _bd.Crear();
            Assert.True(lectura.AccesosEmpresa.Single(a => a.IdEmpresa == DatosBase.EmpresaA2).Activo);
        }

        [Fact]
        public async Task Un_rol_de_otra_empresa_no_se_guarda()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            var rolAjeno = NuevoRol(DatosBase.EmpresaA2);
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            Formulario(pagina, idUsuario, DatosBase.EmpresaA, rolAjeno, role: "empresa_user");

            var resultado = await pagina.OnPostAsync();

            Assert.IsType<PageResult>(resultado);
            Assert.Equal(EditModel.MensajeRolDeOtraEmpresa, Assert.Single(pagina.ModelState[nameof(EditModel.EmpresaRolId)]!.Errors).ErrorMessage);
            Assert.Null(Usuario(idUsuario).EmpresaRolId);
        }

        [Fact]
        public async Task Un_rol_de_la_empresa_elegida_si_se_guarda()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            var rolPropio = NuevoRol(DatosBase.EmpresaA);
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            Formulario(pagina, idUsuario, DatosBase.EmpresaA, rolPropio, role: "empresa_user");

            Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());
            Assert.Equal(rolPropio, Usuario(idUsuario).EmpresaRolId);
        }

        [Fact]
        public async Task Si_la_cuenta_no_se_puede_sincronizar_el_usuario_vuelve_a_como_estaba()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            using var db = _bd.Crear();
            var falla = new SincronizadorQueFalla(InvitacionesDePrueba.Sincronizador(db, _reloj));
            var pagina = Pagina(db, falla);
            Formulario(pagina, idUsuario, DatosBase.EmpresaA2);   // un cambio válido, pero la cuenta falla al seguirlo

            var resultado = await pagina.OnPostAsync();

            var vuelta = Assert.IsType<RedirectToPageResult>(resultado);
            Assert.Null(vuelta.PageName);                          // vuelve a la misma pantalla de edición
            Assert.Equal(EditModel.MensajeCuentaNoSincronizada, pagina.TempData["ErrorMessage"]);
            var usuario = Usuario(idUsuario);
            Assert.Equal(DatosBase.EmpresaA, usuario.EmpresaId);
            Assert.Equal("empresa_user", usuario.Role);
            Assert.Equal(2, falla.Llamadas);                       // y la cuenta se volvió a sincronizar con lo de antes
            using var lectura = _bd.Crear();
            Assert.True(lectura.AccesosEmpresa.Single(a => a.IdEmpresa == DatosBase.EmpresaA).Activo);
            Assert.DoesNotContain(lectura.AccesosEmpresa, a => a.IdEmpresa == DatosBase.EmpresaA2);
        }

        [Fact]
        public async Task La_lista_de_empresas_del_operador_trae_solo_las_del_tenant_del_usuario()
        {
            var idUsuario = await UsuarioConCuentaAsync();
            using var db = _bd.Crear();
            var pagina = Pagina(db);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(idUsuario));

            var ofrecidas = pagina.Empresas.Where(e => e.Value != "").Select(e => int.Parse(e.Value)).ToList();
            Assert.Contains(DatosBase.EmpresaA, ofrecidas);
            Assert.Contains(DatosBase.EmpresaA2, ofrecidas);
            Assert.DoesNotContain(DatosBase.EmpresaB, ofrecidas);
        }

        [Fact]
        public async Task Un_usuario_sin_persona_ni_empresa_puede_ir_a_cualquier_empresa()
        {
            int idUsuario;
            using (var arma = _bd.Crear())
            {
                var usuario = new User { Username = "suelto", Email = "suelto@prueba.local", Password = "x", Role = "empresa_user", IsActive = false };
                arma.Users.Add(usuario);
                arma.SaveChanges();
                idUsuario = usuario.Id;
            }
            using var db = _bd.Crear();
            var pagina = Pagina(db);
            Formulario(pagina, idUsuario, DatosBase.EmpresaB, role: "empresa_user");
            pagina.Username = "suelto";
            pagina.Email = "suelto@prueba.local";
            pagina.IsActive = false;

            Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());
            Assert.Equal(DatosBase.EmpresaB, Usuario(idUsuario).EmpresaId);
        }
    }
}
