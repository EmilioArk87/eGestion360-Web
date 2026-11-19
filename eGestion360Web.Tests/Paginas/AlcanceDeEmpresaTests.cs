using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// El alcance de los datos de las pantallas de usuarios y de roles (F1.6e-3, ADR-029): quien no es del personal de la plataforma solo
    /// actúa sobre la empresa de su sesión, tenga el rol de texto que tenga. Antes lo decía <c>IsEmpresaAdmin</c> y un rol con permisos de
    /// administración (que la pantalla de roles ya puede conceder) podía tocar las demás empresas de su tenant.
    /// </summary>
    public class AlcanceDeEmpresaTests : IDisposable
    {
        private const int Propia = DatosBase.EmpresaA;
        private const int Otra = DatosBase.EmpresaA2;   // otra empresa del mismo tenant

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();

        public void Dispose() => _bd.Dispose();

        private static T Preparar<T>(T pagina, HttpContext http) where T : PageModel
        {
            pagina.PageContext = new PageContext { HttpContext = http };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        // Quién pide: de una empresa (sin importar su rol de texto), el operador dentro de una empresa y el operador en la plataforma.
        private static HttpContext DeLaEmpresa(string rol = "empresa_user", int empresa = Propia)
            => SesionFalsa.Contexto(rol, empresa.ToString(), idTenant: DatosBase.TenantDe(empresa));

        private static HttpContext OperadorEnEmpresa(int empresa = Propia)
            => SesionFalsa.Contexto("empresa_user", empresa.ToString(), idTenant: DatosBase.TenantDe(empresa), esOperador: true);

        private static HttpContext OperadorEnPlataforma() => SesionFalsa.Contexto("admin");

        private static string Destino(IActionResult r) => Assert.IsType<RedirectToPageResult>(r).PageName!;

        private int NuevoRol(int empresa, string nombre = "Rol de prueba")
        {
            using var db = _bd.Crear();
            var rol = new EmpresaRol { IdEmpresa = empresa, IdTenant = DatosBase.TenantDe(empresa), Nombre = nombre, Activo = true };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();
            return rol.IdRol;
        }

        private int NuevoUsuario(int? empresa, string nombre)
        {
            using var db = _bd.Crear();
            var usuario = new User { Username = nombre, Email = nombre + "@x.hn", Password = "x", Role = empresa == null ? "admin" : "empresa_user", EmpresaId = empresa, IsActive = true };
            db.Users.Add(usuario);
            db.SaveChanges();
            return usuario.Id;
        }

        private int RolesDe(int empresa)
        {
            using var db = _bd.Crear();
            return db.EmpresaRoles.Count(r => r.IdEmpresa == empresa);
        }

        // ── Roles (Admin/RolesPermisos) ───────────────────────────────────────

        [Theory]
        [InlineData("empresa_user")]
        [InlineData("empresa_admin")]
        public async Task Quien_trabaja_en_una_empresa_no_crea_roles_en_otra_empresa_de_su_tenant(string rol)
        {
            var pagina = Preparar(new Pages.Admin.RolesPermisos.IndexModel(_bd.Crear()), DeLaEmpresa(rol));

            var resultado = await pagina.OnPostCrearAsync(Otra, "Intruso", null);

            Assert.Equal("/MainMenu", Destino(resultado));
            Assert.Equal(0, RolesDe(Otra));
        }

        [Fact]
        public async Task Quien_trabaja_en_una_empresa_si_crea_roles_en_la_suya()
        {
            var pagina = Preparar(new Pages.Admin.RolesPermisos.IndexModel(_bd.Crear()), DeLaEmpresa());

            var resultado = await pagina.OnPostCrearAsync(Propia, "Cajero", null);

            Assert.NotEqual("/MainMenu", Destino(resultado));
            Assert.Equal(1, RolesDe(Propia));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task El_operador_crea_roles_en_cualquier_empresa_que_ve_su_contexto(bool dentroDeUnaEmpresa)
        {
            var http = dentroDeUnaEmpresa ? OperadorEnEmpresa() : OperadorEnPlataforma();
            var pagina = Preparar(new Pages.Admin.RolesPermisos.IndexModel(_bd.Crear()), http);

            await pagina.OnPostCrearAsync(Otra, "De soporte", null);

            Assert.Equal(1, RolesDe(Otra));
        }

        [Fact]
        public async Task Quien_trabaja_en_una_empresa_no_edita_ni_desactiva_los_roles_de_otra()
        {
            var idRol = NuevoRol(Otra, "Original");
            var pagina = Preparar(new Pages.Admin.RolesPermisos.IndexModel(_bd.Crear()), DeLaEmpresa());

            Assert.Equal("/MainMenu", Destino(await pagina.OnPostEditarAsync(idRol, Otra, "Cambiado", null)));
            Assert.Equal("/MainMenu", Destino(await pagina.OnPostToggleAsync(idRol, false, Otra)));

            using var db = _bd.Crear();
            var rol = db.EmpresaRoles.Single(r => r.IdRol == idRol);
            Assert.Equal("Original", rol.Nombre);
            Assert.True(rol.Activo);
        }

        [Fact]
        public async Task La_lista_de_roles_de_una_empresa_ajena_manda_al_menu_y_la_del_operador_se_abre()
        {
            var ajena = Preparar(new Pages.Admin.EmpresaRoles.IndexModel(_bd.Crear()) { EmpresaId = Otra }, DeLaEmpresa("empresa_admin"));
            var delOperador = Preparar(new Pages.Admin.EmpresaRoles.IndexModel(_bd.Crear()) { EmpresaId = Otra }, OperadorEnEmpresa());

            Assert.Equal("/MainMenu", Destino(await ajena.OnGetAsync()));
            Assert.IsType<PageResult>(await delOperador.OnGetAsync());
        }

        [Fact]
        public async Task Quien_trabaja_en_una_empresa_no_crea_ni_desactiva_roles_desde_la_lista_de_otra()
        {
            var idRol = NuevoRol(Otra);
            var pagina = Preparar(new Pages.Admin.EmpresaRoles.IndexModel(_bd.Crear()), DeLaEmpresa());

            Assert.Equal("/MainMenu", Destino(await pagina.OnPostCrearAsync(Otra, "Intruso", null)));
            Assert.Equal("/MainMenu", Destino(await pagina.OnPostToggleAsync(idRol, false, Otra)));

            Assert.Equal(1, RolesDe(Otra));
            using var db = _bd.Crear();
            Assert.True(db.EmpresaRoles.Single(r => r.IdRol == idRol).Activo);
        }

        // ── Usuarios (Admin/Usuarios) ─────────────────────────────────────────

        private Pages.Admin.Usuarios.EditModel PaginaEditar(HttpContext http)
        {
            var db = _bd.Crear();
            return Preparar(new Pages.Admin.Usuarios.EditModel(db, InvitacionesDePrueba.Personas(db, _reloj)), http);
        }

        [Theory]
        [InlineData("empresa_user")]
        [InlineData("empresa_admin")]
        public async Task Quien_trabaja_en_una_empresa_no_abre_los_usuarios_de_otra(string rol)
        {
            var idUsuario = NuevoUsuario(Otra, "ajeno");

            Assert.Equal("/MainMenu", Destino(await PaginaEditar(DeLaEmpresa(rol)).OnGetAsync(idUsuario)));
        }

        [Fact]
        public async Task Quien_trabaja_en_una_empresa_si_abre_los_usuarios_de_la_suya()
        {
            var idUsuario = NuevoUsuario(Propia, "propio");

            Assert.IsType<PageResult>(await PaginaEditar(DeLaEmpresa()).OnGetAsync(idUsuario));
        }

        [Fact]
        public async Task El_usuario_sin_empresa_solo_se_abre_desde_el_contexto_de_plataforma()
        {
            // Un superadmin (sin empresa) editado dentro de una empresa quedaría asignado a otra: la lista no ofrece «sin empresa» ahí.
            var idUsuario = NuevoUsuario(null, "superadmin");

            Assert.Equal("/MainMenu", Destino(await PaginaEditar(DeLaEmpresa("empresa_admin")).OnGetAsync(idUsuario)));
            Assert.Equal("/MainMenu", Destino(await PaginaEditar(OperadorEnEmpresa()).OnGetAsync(idUsuario)));
            Assert.IsType<PageResult>(await PaginaEditar(OperadorEnPlataforma()).OnGetAsync(idUsuario));
        }

        [Fact]
        public async Task El_operador_dentro_de_una_empresa_abre_los_usuarios_de_cualquier_empresa_que_ve()
        {
            var idUsuario = NuevoUsuario(Otra, "de-otra");

            Assert.IsType<PageResult>(await PaginaEditar(OperadorEnEmpresa()).OnGetAsync(idUsuario));
        }

        [Fact]
        public async Task Quien_trabaja_en_una_empresa_no_guarda_cambios_en_un_usuario_de_otra()
        {
            var idUsuario = NuevoUsuario(Otra, "ajeno");
            var pagina = PaginaEditar(DeLaEmpresa("empresa_admin"));
            (pagina.Id, pagina.Username, pagina.Email, pagina.Role, pagina.IsActive) = (idUsuario, "ajeno", "ajeno@x.hn", "empresa_admin", false);

            Assert.Equal("/MainMenu", Destino(await pagina.OnPostAsync()));

            using var db = _bd.Crear();
            var usuario = db.Users.Single(u => u.Id == idUsuario);
            Assert.Equal("empresa_user", usuario.Role);   // no se ascendió
            Assert.True(usuario.IsActive);                // ni se desactivó
        }
    }
}
