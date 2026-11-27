using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;
using static eGestion360Web.Tests.SqlServer.Rls.EntornoRlsReal;
using PaginaDeRestablecer = eGestion360Web.Pages.Admin.ResetPasswordModel;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>La base con la política encendida, también sobre la auditoría de seguridad (script 045).</summary>
    public sealed class BaseRlsConAuditoria : IAsyncLifetime
    {
        private readonly BaseRlsReal _base = new();

        public Entorno? Entorno => _base.Entorno;

        public async Task InitializeAsync()
        {
            await _base.InitializeAsync();
            if (Entorno == null) return;

            // Los mismos predicados que pone el script 045, tomados de su texto.
            var predicados = Regex.Match(LeerScript("045_auditoria_seguridad.sql"), @"N'(ALTER SECURITY POLICY seg\.pol_tenant\s+ADD [^']+)'");
            Assert.True(predicados.Success, "No se encontró en el script 045 el alta de dbo.auditoria_seguridad en la política.");
            await EjecutarAsync(Entorno.Cadena, predicados.Groups[1].Value);
        }

        public Task DisposeAsync() => _base.DisposeAsync();
    }

    /// <summary>
    /// Cambiar la clave de un usuario de empresa con la política RLS ENCENDIDA. El guardado deja la fila <c>usuario.editado</c> en la
    /// auditoría del tenant del usuario (ADR-030), así que la petición tiene que estar en ese tenant aunque la pantalla no tenga
    /// ninguno (sin sesión, o el operador) o tenga otro. Desde F1.8b hasta el 2026-11-27 la base rechazaba esa fila y la clave no se
    /// podía cambiar.
    /// </summary>
    [Trait("Categoria", "SqlServer")]
    [Collection(ColeccionRlsReal.Nombre)]
    public class ClavesConRlsSqlServerTests : IClassFixture<BaseRlsConAuditoria>
    {
        private const string ClaveVieja = "Clave-Vigente-2026!";
        private const string ClaveNueva = "Clave-Nueva-Segura-2027!";

        private readonly BaseRlsConAuditoria _bd;

        public ClavesConRlsSqlServerTests(BaseRlsConAuditoria bd) => _bd = bd;

        private Entorno Entorno => _bd.Entorno ?? throw new InvalidOperationException("Sin servidor de pruebas.");

        /// <summary>Los servicios que intervienen, registrados como en Program.cs y con los interceptores en el mismo orden.</summary>
        private ServiceProvider Servicios(IContextoAuditoria quien)
        {
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddMemoryCache();
            servicios.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            servicios.AddSingleton(quien);
            servicios.AddScoped<TenantContext>();
            servicios.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
            servicios.AddScoped<IResolvedorDeTenant, ResolvedorDeTenantEf>();
            servicios.AddScoped<ConstructorDeEventosDeAdministracion>();
            servicios.AddScoped<AuditoriaCambiosInterceptor>();
            servicios.AddScoped<TenantAlGuardarInterceptor>();
            servicios.AddScoped<ContextoDeSesionInterceptor>();
            servicios.AddScoped(sp => new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena)
                    .AddInterceptors(sp.GetRequiredService<AuditoriaCambiosInterceptor>(), sp.GetRequiredService<TenantAlGuardarInterceptor>(),
                        sp.GetRequiredService<ContextoDeSesionInterceptor>()).Options,
                sp.GetRequiredService<ITenantContext>()));
            servicios.AddScoped<IValidadorDeIdentidad, ValidadorDeIdentidadInerte>();
            servicios.AddScoped<ISincronizadorDeCuentas, SincronizadorDeCuentas>();
            servicios.AddScoped<IPasswordResetService, PasswordResetService>();
            servicios.AddSingleton<IPasswordService, ClavesRapidas>();
            servicios.AddSingleton<IEmailService, CorreoEspia>();
            return servicios.BuildServiceProvider();
        }

        private static ContextoAuditoriaFijo Anonimo() => new() { Usuario = "system" };

        private static ContextoAuditoriaFijo Operador() => new() { Usuario = "admin_pruebas", EsOperador = true };

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static T ComoOperador<T>(T pagina) where T : PageModel
        {
            var http = SesionFalsa.Contexto("admin");
            pagina.PageContext = new PageContext { HttpContext = http, ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) };
            pagina.TempData = new TempDataDictionary(http, new TempDataEnMemoria());
            return pagina;
        }

        /// <summary>Un contexto sin política de por medio: Users, las cuentas y los códigos no son tablas de tenant.</summary>
        private ApplicationDbContext SinSesion() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Entorno.Cadena).Options);

        private async Task<User> NuevoUsuarioAsync(string nombre, int? idEmpresa)
        {
            await using var db = SinSesion();
            var usuario = new User
            {
                Username = nombre, Email = nombre + "@prueba.local", Password = BCrypt.Net.BCrypt.HashPassword(ClaveVieja, 4),
                Role = idEmpresa == null ? "admin" : "empresa_user", IsActive = true, EmpresaId = idEmpresa,
            };
            db.Users.Add(usuario);
            await db.SaveChangesAsync();

            db.Cuentas.Add(new Cuenta
            {
                UidPublico = Guid.NewGuid(), Correo = usuario.Email, NombreUsuario = nombre, HashClave = usuario.Password,
                Estado = EstadoCuenta.Activa, IdUserLegado = usuario.Id, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return usuario;
        }

        private async Task AssertClaveNuevaAsync(User usuario)
        {
            await using var db = SinSesion();
            var guardado = await db.Users.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
            var cuenta = await db.Cuentas.AsNoTracking().SingleAsync(c => c.IdUserLegado == usuario.Id);
            Assert.True(BCrypt.Net.BCrypt.Verify(ClaveNueva, guardado.Password), $"La clave de {usuario.Username} no cambió.");
            Assert.Equal(guardado.Password, cuenta.HashClave);
        }

        private async Task<int> EventosEnElTenant(int tenant, User usuario) => Convert.ToInt32(await ConContexto(Entorno.Cadena, tenant, "tenant",
            $"SELECT COUNT(*) FROM dbo.auditoria_seguridad WHERE tipo = 'usuario.editado' AND objeto_id = N'{usuario.Id}' AND detalle LIKE N'%claveCambiada%'"));

        private async Task<int> EventosEnLaPlataforma(User usuario) => Convert.ToInt32(await ConContexto(Entorno.Cadena, null, "plataforma",
            $"SELECT COUNT(*) FROM saas.auditoria_plataforma WHERE tipo = 'usuario.editado' AND objeto_id = N'{usuario.Id}' AND detalle LIKE N'%claveCambiada%'"));

        /// <summary>La pantalla pública, como la abre quien olvidó su clave: sin sesión y en ámbito de plataforma.</summary>
        private async Task<eGestion360Web.Pages.ResetPasswordModel> RestablecerConCodigoAsync(User usuario)
        {
            await using (var db = SinSesion())
            {
                db.PasswordResetCodes.Add(new PasswordResetCode { UserId = usuario.Id, Email = usuario.Email, Code = "483920", ExpiresAt = DateTime.UtcNow.AddMinutes(15) });
                await db.SaveChangesAsync();
            }

            await using var servicios = Servicios(Anonimo());
            await using var ambito = servicios.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<TenantContext>().FijarPlataforma();   // como AmbitoDePlataformaPageFilter
            var pagina = ActivatorUtilities.CreateInstance<eGestion360Web.Pages.ResetPasswordModel>(ambito.ServiceProvider);
            pagina.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
            (pagina.Email, pagina.VerificationCode, pagina.NewPassword, pagina.ConfirmPassword) = (usuario.Email, "483920", ClaveNueva, ClaveNueva);

            await pagina.OnPostAsync();
            return pagina;
        }

        [FactSqlServer]
        public async Task Con_el_codigo_un_usuario_de_empresa_restablece_su_clave_y_queda_la_auditoria_en_su_tenant()
        {
            var usuario = await NuevoUsuarioAsync("rls.codigo", DatosBase.EmpresaA);

            var pagina = await RestablecerConCodigoAsync(usuario);

            Assert.True(pagina.PasswordReset, string.Join(" ", pagina.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            await AssertClaveNuevaAsync(usuario);
            await using var db = SinSesion();
            Assert.True((await db.PasswordResetCodes.AsNoTracking().SingleAsync(c => c.UserId == usuario.Id)).IsUsed);
            Assert.Equal(1, await EventosEnElTenant(DatosBase.TenantA, usuario));
            Assert.Equal(0, await EventosEnElTenant(DatosBase.TenantB, usuario));
        }

        [FactSqlServer]
        public async Task Con_el_codigo_un_usuario_sin_empresa_sigue_restableciendo_su_clave()
        {
            var usuario = await NuevoUsuarioAsync("rls.sin.empresa", idEmpresa: null);

            var pagina = await RestablecerConCodigoAsync(usuario);

            Assert.True(pagina.PasswordReset);
            await AssertClaveNuevaAsync(usuario);
            Assert.Equal(1, await EventosEnLaPlataforma(usuario));
            Assert.Equal(0, await EventosEnElTenant(DatosBase.TenantA, usuario));
        }

        // El operador en la plataforma (sin tenant) y dentro de un tenant de soporte que no es el del usuario.
        [FactSqlServer]
        public async Task El_operador_restablece_la_clave_de_un_usuario_de_empresa_desde_la_pantalla_de_restablecer()
        {
            foreach (var enSoporte in new[] { false, true })
            {
                var usuario = await NuevoUsuarioAsync($"rls.restablecer.{(enSoporte ? "soporte" : "plataforma")}", DatosBase.EmpresaA);
                await using var servicios = Servicios(Operador());
                await using var ambito = servicios.CreateAsyncScope();
                var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
                if (enSoporte) tenant.Fijar(DatosBase.TenantB, DatosBase.EmpresaB); else tenant.FijarPlataforma();
                var pagina = ComoOperador(ActivatorUtilities.CreateInstance<PaginaDeRestablecer>(ambito.ServiceProvider));
                (pagina.TargetUserId, pagina.NewPassword) = (usuario.Id, ClaveNueva);

                Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());

                await AssertClaveNuevaAsync(usuario);
                Assert.Equal(1, await EventosEnElTenant(DatosBase.TenantA, usuario));
                Assert.Equal(1, await EventosEnLaPlataforma(usuario));
                Assert.Equal(0, await EventosEnElTenant(DatosBase.TenantB, usuario));
            }
        }

        [FactSqlServer]
        public async Task El_operador_restablece_la_clave_de_un_usuario_de_empresa_desde_gestion_de_usuarios()
        {
            foreach (var enSoporte in new[] { false, true })
            {
                var usuario = await NuevoUsuarioAsync($"rls.usuarios.{(enSoporte ? "soporte" : "plataforma")}", DatosBase.EmpresaA);
                await using var servicios = Servicios(Operador());
                await using var ambito = servicios.CreateAsyncScope();
                var tenant = ambito.ServiceProvider.GetRequiredService<TenantContext>();
                if (enSoporte) tenant.Fijar(DatosBase.TenantB, DatosBase.EmpresaB); else tenant.FijarPlataforma();
                var pagina = ComoOperador(ActivatorUtilities.CreateInstance<UserManagementModel>(ambito.ServiceProvider));
                (pagina.TargetUserId, pagina.NewPassword) = (usuario.Id, ClaveNueva);

                Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());

                await AssertClaveNuevaAsync(usuario);
                Assert.Equal(1, await EventosEnElTenant(DatosBase.TenantA, usuario));
                Assert.Equal(1, await EventosEnLaPlataforma(usuario));
                Assert.Equal(0, await EventosEnElTenant(DatosBase.TenantB, usuario));
            }
        }

        [FactSqlServer]
        public async Task Cambiar_la_propia_clave_con_la_sesion_en_otro_tenant_guarda_y_audita_en_el_tenant_del_usuario()
        {
            var usuario = await NuevoUsuarioAsync("rls.propia", DatosBase.EmpresaA);
            await using var servicios = Servicios(Anonimo());
            await using var ambito = servicios.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<TenantContext>().Fijar(DatosBase.TenantB, DatosBase.EmpresaB);
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa() with { IdUserLegado = usuario.Id });
            var pagina = new ChangePasswordModel(ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                ambito.ServiceProvider.GetRequiredService<ISincronizadorDeCuentas>(), autenticador: null!)   // solo lo usa el cambio obligatorio
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
                CurrentPassword = ClaveVieja, NewPassword = ClaveNueva, ConfirmPassword = ClaveNueva,
            };

            Assert.IsType<RedirectToPageResult>(await pagina.OnPostAsync());

            await AssertClaveNuevaAsync(usuario);
            Assert.Equal(1, await EventosEnElTenant(DatosBase.TenantA, usuario));
            Assert.Equal(0, await EventosEnElTenant(DatosBase.TenantB, usuario));
        }
    }
}
