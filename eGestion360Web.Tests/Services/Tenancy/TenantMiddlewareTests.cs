using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>
    /// Paso F1.1: el middleware fija el tenant de cada petición (por ahora, el de la empresa de la sesión), lo deja donde lo
    /// lee <see cref="TenantContext"/> y abre un ámbito de log con tenant, empresa, usuario e id de correlación.
    /// </summary>
    public class TenantMiddlewareTests
    {
        private sealed class ResolvedorFijo : IResolvedorDeTenant
        {
            private readonly Dictionary<int, int> _tenants;
            public List<int> Consultas { get; } = new();
            public ResolvedorFijo(params (int Empresa, int Tenant)[] tenants) => _tenants = tenants.ToDictionary(t => t.Empresa, t => t.Tenant);

            public Task<int?> ResolverPorEmpresaAsync(int idEmpresa, CancellationToken ct = default)
            {
                Consultas.Add(idEmpresa);
                return Task.FromResult<int?>(_tenants.TryGetValue(idEmpresa, out var tenant) ? tenant : null);
            }
        }

        /// <summary>Lo que ve la petición mientras corre el resto del pipeline.</summary>
        private sealed record Visto(int? IdTenant, int? IdEmpresa, string? IdCorrelacion, bool AmbitoAbierto);

        private static async Task<(Visto Visto, int Llamadas, RegistroEnMemoria<TenantMiddleware> Registro, HttpContext Http)> Ejecutar(
            HttpContext http, IResolvedorDeTenant resolvedor)
        {
            var registro = new RegistroEnMemoria<TenantMiddleware>();
            var llamadas = 0;
            Visto? visto = null;
            var middleware = new TenantMiddleware(contexto =>
            {
                llamadas++;
                var tenant = new TenantContext(new HttpContextAccessor { HttpContext = contexto });
                visto = new Visto(tenant.IdTenant, tenant.IdEmpresa, tenant.IdCorrelacion, registro.Ambitos.Count > 0 && registro.Ambitos[0].Abierto);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(http, resolvedor, registro);
            return (visto!, llamadas, registro, http);
        }

        [Fact]
        public async Task Una_sesion_con_empresa_fija_el_tenant_de_esa_empresa()
        {
            var http = SesionFalsa.Contexto(rol: "empresa_admin", empresaId: "4");

            var (visto, llamadas, _, _) = await Ejecutar(http, new ResolvedorFijo((4, 2), (1, 1)));

            Assert.Equal(1, llamadas);
            Assert.Equal(2, visto.IdTenant);
            Assert.Equal(4, visto.IdEmpresa);
        }

        [Fact]
        public async Task El_tenant_sale_de_la_empresa_de_la_sesion_y_no_de_la_peticion()
        {
            // Aunque la URL o el formulario traigan otra empresa o tenant, el tenant sale solo de la sesión (ADR-001).
            var http = SesionFalsa.Contexto(rol: "empresa_user", empresaId: "1");
            http.Request.QueryString = new QueryString("?IdEmpresa=4&IdTenant=2&empresaId=4");
            http.Request.Headers["X-Tenant"] = "2";

            var (visto, _, _, _) = await Ejecutar(http, new ResolvedorFijo((1, 1), (4, 2)));

            Assert.Equal(1, visto.IdTenant);
            Assert.Equal(1, visto.IdEmpresa);
        }

        [Fact]
        public async Task El_ambito_de_log_lleva_tenant_empresa_usuario_y_correlacion_y_se_cierra_al_terminar()
        {
            var http = SesionFalsa.Contexto(rol: "empresa_user", empresaId: "4");

            var (visto, _, registro, _) = await Ejecutar(http, new ResolvedorFijo((4, 2)));

            var ambito = Assert.Single(registro.Ambitos);
            Assert.Equal(2, ambito.Valores["IdTenant"]);
            Assert.Equal(4, ambito.Valores["IdEmpresa"]);
            Assert.Equal("1", ambito.Valores["IdUsuario"]);
            Assert.Equal(http.TraceIdentifier, ambito.Valores["IdCorrelacion"]);
            Assert.True(visto.AmbitoAbierto, "el ámbito debe estar abierto mientras corre el resto de la petición");
            Assert.False(ambito.Abierto, "el ámbito debe cerrarse al terminar la petición");
        }

        [Fact]
        public async Task La_respuesta_lleva_el_id_de_correlacion_y_es_el_mismo_que_ve_el_codigo()
        {
            var http = SesionFalsa.Contexto(empresaId: "1");

            var (visto, _, _, _) = await Ejecutar(http, new ResolvedorFijo((1, 1)));

            Assert.False(string.IsNullOrWhiteSpace(visto.IdCorrelacion));
            Assert.Equal(http.TraceIdentifier, visto.IdCorrelacion);
            Assert.Equal(http.TraceIdentifier, http.Response.Headers[TenantMiddleware.EncabezadoCorrelacion].ToString());
        }

        [Fact]
        public async Task El_administrador_general_sin_empresa_sigue_sin_tenant_y_sin_consultar_la_base()
        {
            var http = SesionFalsa.Contexto(rol: "admin", empresaId: null);
            var resolvedor = new ResolvedorFijo((1, 1));

            var (visto, llamadas, registro, _) = await Ejecutar(http, resolvedor);

            Assert.Equal(1, llamadas);
            Assert.Null(visto.IdTenant);
            Assert.Null(visto.IdEmpresa);
            Assert.Empty(resolvedor.Consultas);
            Assert.Empty(registro.Mensajes);
            Assert.Null(Assert.Single(registro.Ambitos).Valores["IdTenant"]);
        }

        [Fact]
        public async Task Una_empresa_sin_tenant_deja_un_aviso_y_la_peticion_sigue_sin_tenant()
        {
            var http = SesionFalsa.Contexto(rol: "empresa_user", empresaId: "9");

            var (visto, llamadas, registro, _) = await Ejecutar(http, new ResolvedorFijo((1, 1)));

            Assert.Equal(1, llamadas);
            Assert.Null(visto.IdTenant);
            Assert.Equal(9, visto.IdEmpresa);
            var aviso = Assert.Single(registro.Mensajes);
            Assert.Equal(LogLevel.Warning, aviso.Nivel);
            Assert.Contains("9", aviso.Mensaje);
            Assert.Equal(1, aviso.AmbitosAbiertos);   // el aviso ya lleva el tenant, la empresa, el usuario y la correlación
        }

        [Fact]
        public async Task El_ambito_se_imprime_como_una_linea_legible_en_la_consola()
        {
            var http = SesionFalsa.Contexto(rol: "empresa_admin", empresaId: "4");

            var (_, _, registro, _) = await Ejecutar(http, new ResolvedorFijo((4, 2)));

            Assert.Equal($"IdTenant:2 IdEmpresa:4 IdUsuario:1 IdCorrelacion:{http.TraceIdentifier}", Assert.Single(registro.Ambitos).Texto);
        }

        [Fact]
        public async Task Sin_tenant_ni_empresa_la_linea_del_ambito_muestra_guiones()
        {
            var http = SesionFalsa.Contexto(rol: "admin", empresaId: null);

            var (_, _, registro, _) = await Ejecutar(http, new ResolvedorFijo());

            Assert.Equal($"IdTenant:- IdEmpresa:- IdUsuario:1 IdCorrelacion:{http.TraceIdentifier}", Assert.Single(registro.Ambitos).Texto);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-3")]
        [InlineData("abc")]
        [InlineData("")]
        public async Task Una_empresa_invalida_en_la_sesion_se_trata_como_sin_empresa(string enSesion)
        {
            var http = SesionFalsa.Contexto(rol: "empresa_user", empresaId: enSesion);
            var resolvedor = new ResolvedorFijo((1, 1));

            var (visto, llamadas, _, _) = await Ejecutar(http, resolvedor);

            Assert.Equal(1, llamadas);
            Assert.Null(visto.IdTenant);
            Assert.Empty(resolvedor.Consultas);
        }

        [Fact]
        public async Task Una_peticion_sin_sesion_configurada_no_falla_y_no_tiene_tenant()
        {
            var http = new DefaultHttpContext();   // sin ISessionFeature: leer Session lanza InvalidOperationException

            var (visto, llamadas, registro, _) = await Ejecutar(http, new ResolvedorFijo((1, 1)));

            Assert.Equal(1, llamadas);
            Assert.Null(visto.IdTenant);
            Assert.Null(Assert.Single(registro.Ambitos).Valores["IdUsuario"]);
        }

        [Fact]
        public async Task Si_el_resto_de_la_peticion_falla_el_ambito_igual_se_cierra()
        {
            var http = SesionFalsa.Contexto(empresaId: "1");
            var registro = new RegistroEnMemoria<TenantMiddleware>();
            var middleware = new TenantMiddleware(_ => throw new InvalidOperationException("falla la página"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http, new ResolvedorFijo((1, 1)), registro));

            Assert.False(Assert.Single(registro.Ambitos).Abierto);
        }
    }
}
