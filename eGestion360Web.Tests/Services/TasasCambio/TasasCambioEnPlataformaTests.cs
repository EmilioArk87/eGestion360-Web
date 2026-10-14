using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// Paso F1.4b: el job de la tasa oficial escribe filas globales (<c>id_tenant</c> nulo) de <c>tasas_cambio</c>, y la política
    /// RLS solo lo permite en ámbito de plataforma (ADR-023). Cada ejecución del job corre en su propio ámbito de servicios y ese
    /// ámbito tiene que quedar en plataforma.
    /// </summary>
    public class TasasCambioEnPlataformaTests
    {
        /// <summary>Anota, al ejecutarse, el ámbito del contexto de tenant que le tocó.</summary>
        private sealed class SincronizadorQueMide : ITasaCambioSyncService
        {
            private readonly TenantContext _tenant;
            public static readonly List<(bool Plataforma, int? IdTenant)> Vistos = new();

            public SincronizadorQueMide(TenantContext tenant) => _tenant = tenant;

            public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, CancellationToken ct = default)
                => Medir();

            public Task<ResultadoEjecucionTasas> EjecutarAsync(string disparador, string ejecutadoPor, DateOnly desde, DateOnly hasta,
                CancellationToken ct = default)
                => Medir();

            private Task<ResultadoEjecucionTasas> Medir()
            {
                lock (Vistos) Vistos.Add((_tenant.EsPlataforma, _tenant.IdTenant));
                return Task.FromResult(ResultadoEjecucionTasas.NoEjecutada("prueba"));
            }

            public Task<ResultadoOperacionTasa> AprobarAsync(int idTasaCambio, string usuario, CancellationToken ct = default)
                => throw new NotSupportedException();

            public Task<ResultadoOperacionTasa> RechazarAsync(int idTasaCambio, string usuario, string motivo, CancellationToken ct = default)
                => throw new NotSupportedException();

            public Task<ResultadoOperacionTasa> RegistrarManualAsync(int idEmpresa, string monedaOrigen, string tipoTasa, DateOnly fecha,
                decimal tasa, string usuario, CancellationToken ct = default)
                => throw new NotSupportedException();
        }

        [Fact]
        public async Task Una_ejecucion_forzada_del_job_corre_en_ambito_de_plataforma_y_sin_tenant()
        {
            SincronizadorQueMide.Vistos.Clear();
            var servicios = new ServiceCollection();
            servicios.AddHttpContextAccessor();
            servicios.AddScoped<TenantContext>();
            servicios.AddScoped<ITasaCambioSyncService, SincronizadorQueMide>();
            await using var proveedor = servicios.BuildServiceProvider();

            var worker = new TasasCambioBackgroundService(
                proveedor.GetRequiredService<IServiceScopeFactory>(), Options.Create(new TasasCambioOptions()),
                new ColaEjecucionTasasCambio(), TimeProvider.System, NullLogger<TasasCambioBackgroundService>.Instance);

            await worker.AtenderAsync(new SolicitudEjecucionTasas("MANUAL", "pruebas"));

            var visto = Assert.Single(SincronizadorQueMide.Vistos);
            Assert.True(visto.Plataforma);
            Assert.Null(visto.IdTenant);
        }
    }
}
