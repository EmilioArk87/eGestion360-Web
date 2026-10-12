using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>Paso F1.1: el tenant de una empresa sale de <c>empresas.id_tenant</c>.</summary>
    public class ResolvedorDeTenantTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static IMemoryCache NuevaCache() => new MemoryCache(new MemoryCacheOptions());

        private async Task<int?> Resolver(int idEmpresa, IMemoryCache? cache = null)
        {
            using var db = _bd.Crear();
            return await new ResolvedorDeTenantEf(db, cache ?? NuevaCache()).ResolverPorEmpresaAsync(idEmpresa);
        }

        [Fact]
        public async Task Cada_empresa_resuelve_el_tenant_al_que_pertenece()
        {
            Assert.Equal(DatosBase.TenantA, await Resolver(DatosBase.EmpresaA));
            Assert.Equal(DatosBase.TenantB, await Resolver(DatosBase.EmpresaB));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(999)]
        public async Task Una_empresa_que_no_existe_no_tiene_tenant(int idEmpresa)
            => Assert.Null(await Resolver(idEmpresa));

        [Fact]
        public async Task Una_empresa_eliminada_no_tiene_tenant()
        {
            using (var db = _bd.Crear())
            {
                db.Database.ExecuteSqlRaw("UPDATE empresas SET eliminado = 1 WHERE id_empresa = {0}", DatosBase.EmpresaB);
            }

            Assert.Null(await Resolver(DatosBase.EmpresaB));
            Assert.Equal(DatosBase.TenantA, await Resolver(DatosBase.EmpresaA));
        }

        [Fact]
        public async Task El_resultado_se_recuerda_y_otra_cache_lo_vuelve_a_leer_de_la_base()
        {
            var cache = NuevaCache();
            Assert.Equal(DatosBase.TenantA, await Resolver(DatosBase.EmpresaA, cache));

            using (var db = _bd.Crear())
            {
                db.Database.ExecuteSqlRaw("UPDATE empresas SET id_tenant = {0} WHERE id_empresa = {1}", DatosBase.TenantB, DatosBase.EmpresaA);
            }

            Assert.Equal(DatosBase.TenantA, await Resolver(DatosBase.EmpresaA, cache));            // recordado
            Assert.Equal(DatosBase.TenantB, await Resolver(DatosBase.EmpresaA, NuevaCache()));      // leído de nuevo
        }

        [Fact]
        public async Task Un_resultado_nulo_no_se_recuerda_para_ver_enseguida_la_empresa_que_aparece()
        {
            var cache = NuevaCache();
            Assert.Null(await Resolver(50, cache));

            using (var db = _bd.Crear())
            {
                db.Empresas.Add(new eGestion360Web.Models.Empresa
                {
                    IdEmpresa = 50, IdTenant = DatosBase.TenantB, Codigo = "E50", RazonSocial = "Empresa nueva", PaisIso = "HN",
                    MonedaIso = "HNL", ZonaHoraria = "America/Tegucigalpa", FechaActivacion = DateTime.UtcNow,
                    CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            Assert.Equal(DatosBase.TenantB, await Resolver(50, cache));
        }

        [Fact]
        public void El_contexto_de_tenant_sin_peticion_no_tiene_tenant()
        {
            var contexto = new TenantContext(new HttpContextAccessor());

            Assert.Null(contexto.IdTenant);
            Assert.Null(contexto.IdEmpresa);
            Assert.Null(contexto.IdCorrelacion);
        }

        [Fact]
        public void El_contexto_de_tenant_lee_lo_que_dejo_el_middleware_y_nada_mas()
        {
            var http = new DefaultHttpContext();
            http.Items[TenantMiddleware.ClaveIdTenant] = 7;
            http.Items[TenantMiddleware.ClaveIdEmpresa] = 3;
            http.Items["otra"] = 99;

            var contexto = new TenantContext(new HttpContextAccessor { HttpContext = http });

            Assert.Equal(7, contexto.IdTenant);
            Assert.Equal(3, contexto.IdEmpresa);
        }

        [Fact]
        public void Un_valor_que_no_es_un_entero_en_Items_se_ignora()
        {
            var http = new DefaultHttpContext();
            http.Items[TenantMiddleware.ClaveIdTenant] = "2";   // alguien más escribió una cadena: no es nuestro valor

            var contexto = new TenantContext(new HttpContextAccessor { HttpContext = http });

            Assert.Null(contexto.IdTenant);
        }
    }
}
