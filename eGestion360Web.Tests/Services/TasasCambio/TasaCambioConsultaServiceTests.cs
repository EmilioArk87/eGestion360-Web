using eGestion360Web.Services.TasasCambio;
using eGestion360Web.Tests.Infra;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    /// <summary>
    /// Consultas de tasas. Nota: SQLite trata los NULL como distintos en los índices únicos, así que aquí se podrían
    /// sembrar dos tasas oficiales VIGENTE de la misma clave; en SQL Server lo impide UX_tasas_cambio_vigente. Las pruebas
    /// no lo hacen.
    /// </summary>
    public class TasaCambioConsultaServiceTests : IDisposable
    {
        private readonly EscenarioTasas _e = new();

        public void Dispose() => _e.Dispose();

        private static readonly DateOnly Domingo = new(2026, 10, 4);
        private static readonly DateOnly Lunes = new(2026, 10, 5);

        private async Task<TasaVigente?> Vigente(string moneda, string tipo, DateOnly fecha, int? empresa = null)
        {
            using var db = _e.Bd.Crear();
            return await _e.Consultas(db).ObtenerVigenteAsync(moneda, tipo, fecha, empresa);
        }

        [Fact]
        public async Task La_vigente_de_un_domingo_es_la_del_viernes()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Jueves, 27.0132m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0270m);
            _e.Sembrar("USD", "VENTA", Lunes, 27.0400m);

            Assert.Equal(27.0270m, (await Vigente("USD", "VENTA", Domingo))!.Tasa);
            Assert.Equal(EscenarioTasas.Viernes, (await Vigente("USD", "VENTA", Domingo))!.FechaVigencia);
            Assert.Equal(27.0400m, (await Vigente("usd", "venta", Lunes))!.Tasa);
            Assert.Null(await Vigente("USD", "VENTA", new DateOnly(2026, 9, 1)));   // antes de la primera
            Assert.Null(await Vigente("USD", "COMPRA", Domingo));                   // otro tipo
        }

        [Fact]
        public async Task Solo_cuenta_la_VIGENTE_no_las_reemplazadas_en_revision_ni_rechazadas()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Jueves, 27.0132m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0000m, estado: Cat.EstadoTasa.Reemplazada);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 30.0000m, estado: Cat.EstadoTasa.EnRevision);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 31.0000m, estado: Cat.EstadoTasa.Rechazada);

            Assert.Equal(27.0132m, (await Vigente("USD", "VENTA", EscenarioTasas.Viernes))!.Tasa);
        }

        [Fact]
        public async Task A_igual_fecha_la_tasa_de_la_empresa_gana_sobre_la_oficial()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0270m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.1000m, DatosBase.EmpresaA);

            var deA = (await Vigente("USD", "VENTA", EscenarioTasas.Viernes, DatosBase.EmpresaA))!;
            Assert.Equal(27.1000m, deA.Tasa);
            Assert.True(deA.EsPropiaEmpresa);
            Assert.Equal(Cat.Fuente.Manual, deA.Fuente);

            var oficial = (await Vigente("USD", "VENTA", EscenarioTasas.Viernes))!;
            Assert.Equal(27.0270m, oficial.Tasa);
            Assert.False(oficial.EsPropiaEmpresa);
        }

        [Fact]
        public async Task Gana_la_ultima_fecha_aunque_sea_la_oficial()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Jueves, 27.1000m, DatosBase.EmpresaA);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0270m);

            Assert.Equal(27.0270m, (await Vigente("USD", "VENTA", Domingo, DatosBase.EmpresaA))!.Tasa);
            Assert.Equal(27.1000m, (await Vigente("USD", "VENTA", EscenarioTasas.Jueves, DatosBase.EmpresaA))!.Tasa);
        }

        // ── Aislamiento entre empresas (obligatorio) ────────────────────────

        [Fact]
        public async Task Aislamiento_la_tasa_manual_de_la_empresa_A_no_aparece_para_la_B()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.0270m);
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 27.1000m, DatosBase.EmpresaA);
            _e.Sembrar("EUR", "COMPRA", EscenarioTasas.Viernes, 29.5000m, DatosBase.EmpresaA);   // solo A tiene euro

            // Vigente
            Assert.Equal(27.0270m, (await Vigente("USD", "VENTA", EscenarioTasas.Viernes, DatosBase.EmpresaB))!.Tasa);
            Assert.Null(await Vigente("EUR", "COMPRA", EscenarioTasas.Viernes, DatosBase.EmpresaB));
            Assert.Null(await Vigente("EUR", "COMPRA", EscenarioTasas.Viernes));   // sin empresa: solo oficiales

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            // Vigentes actuales (hoy es el viernes 2 en el reloj del escenario)
            var actualesB = await consultas.VigentesActualesAsync(DatosBase.EmpresaB);
            Assert.Equal(4, actualesB.Count);   // USD y EUR × compra y venta
            Assert.Equal(27.0270m, actualesB.Single(a => a.MonedaOrigen == "USD" && a.TipoTasa == "VENTA").Vigente!.Tasa);
            Assert.Null(actualesB.Single(a => a.MonedaOrigen == "EUR" && a.TipoTasa == "COMPRA").Vigente);
            var actualesA = await consultas.VigentesActualesAsync(DatosBase.EmpresaA);
            Assert.Equal(29.5000m, actualesA.Single(a => a.MonedaOrigen == "EUR" && a.TipoTasa == "COMPRA").Vigente!.Tasa);

            // Histórico
            var historicoB = await consultas.HistoricoAsync(new FiltroHistoricoTasas { IdEmpresa = DatosBase.EmpresaB });
            Assert.Equal(1, historicoB.Total);
            Assert.All(historicoB.Filas, f => Assert.Null(f.IdEmpresa));
            var soloPropiasB = await consultas.HistoricoAsync(new FiltroHistoricoTasas { IdEmpresa = DatosBase.EmpresaB, IncluirOficiales = false });
            Assert.Empty(soloPropiasB.Filas);
            var historicoA = await consultas.HistoricoAsync(new FiltroHistoricoTasas { IdEmpresa = DatosBase.EmpresaA });
            Assert.Equal(3, historicoA.Total);
            var sinEmpresa = await consultas.HistoricoAsync(new FiltroHistoricoTasas());
            Assert.Equal(1, sinEmpresa.Total);
        }

        [Fact]
        public async Task Aislamiento_en_revision_la_empresa_B_no_ve_las_pendientes_propias_de_A()
        {
            _e.Sembrar("USD", "VENTA", EscenarioTasas.Viernes, 30m, estado: Cat.EstadoTasa.EnRevision);
            _e.Sembrar("USD", "COMPRA", EscenarioTasas.Viernes, 31m, DatosBase.EmpresaA, estado: Cat.EstadoTasa.EnRevision);

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            Assert.Single(await consultas.PendientesRevisionAsync(DatosBase.EmpresaB));
            Assert.Equal(2, (await consultas.PendientesRevisionAsync(DatosBase.EmpresaA)).Count);
            Assert.Single(await consultas.PendientesRevisionAsync());
        }

        // ── Histórico, bitácora y revisión ──────────────────────────────────

        [Fact]
        public async Task Historico_filtra_y_pagina_de_la_fecha_mas_reciente_a_la_mas_antigua()
        {
            for (var d = 0; d < 10; d++)
            {
                var fecha = new DateOnly(2026, 9, 21).AddDays(d);
                _e.Sembrar("USD", "COMPRA", fecha, 26.80m + d / 100m);
                _e.Sembrar("USD", "VENTA", fecha, 26.95m + d / 100m);
                _e.Sembrar("EUR", "VENTA", fecha, 29.30m);
            }

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            var pagina1 = await consultas.HistoricoAsync(new FiltroHistoricoTasas { MonedaOrigen = "usd", TipoTasa = "compra", TamanoPagina = 4 });
            var pagina3 = await consultas.HistoricoAsync(new FiltroHistoricoTasas { MonedaOrigen = "USD", TipoTasa = "COMPRA", TamanoPagina = 4, Pagina = 3 });
            var rango = await consultas.HistoricoAsync(new FiltroHistoricoTasas
            {
                Desde = new DateOnly(2026, 9, 25), Hasta = new DateOnly(2026, 9, 26), Estado = Cat.EstadoTasa.Vigente
            });

            Assert.Equal(10, pagina1.Total);
            Assert.Equal(3, pagina1.TotalPaginas);
            Assert.Equal(new DateOnly(2026, 9, 30), pagina1.Filas[0].FechaVigencia);
            Assert.Equal(4, pagina1.Filas.Count);
            Assert.Equal(2, pagina3.Filas.Count);
            Assert.Equal(new DateOnly(2026, 9, 21), pagina3.Filas[^1].FechaVigencia);
            Assert.Equal(6, rango.Total);   // 2 días × 3 tasas
        }

        [Fact]
        public async Task Ultimas_ejecuciones_traen_su_detalle_y_pendientes_traen_motivo_y_vigente()
        {
            _e.ResponderExcel(EscenarioTasas.SemanaBch());
            _e.ResponderBce(EscenarioTasas.SemanaBce());
            await _e.Servicio().EjecutarAsync(Cat.Disparador.Programado, "job");
            var semana = EscenarioTasas.SemanaBch();
            semana[^1] = (EscenarioTasas.Viernes, 28.50m, 28.65m);
            _e.ResponderExcel(semana);
            await _e.Servicio().EjecutarAsync(Cat.Disparador.Manual, "ana");

            using var db = _e.Bd.Crear();
            var consultas = _e.Consultas(db);

            var ejecuciones = await consultas.UltimasEjecucionesAsync(10);
            Assert.Equal(2, ejecuciones.Count);
            Assert.Equal(Cat.Disparador.Manual, ejecuciones[0].Disparador);   // la más reciente primero
            Assert.Equal("ana", ejecuciones[0].EjecutadoPor);
            Assert.Equal(16, ejecuciones[1].Detalles.Count);
            Assert.Contains(ejecuciones[0].Detalles, d => d.Resultado == Cat.ResultadoDetalle.EnRevision);

            var pendientes = await consultas.PendientesRevisionAsync();
            Assert.Equal(2, pendientes.Count);
            var compra = pendientes.Single(p => p.TipoTasa == "COMPRA");
            Assert.Equal(28.50m, compra.Tasa);
            Assert.Equal(26.8925m, compra.TasaVigenteActual);
            Assert.Contains("Variación", compra.Motivo);
            Assert.Equal(ejecuciones[0].IdEjecucion, compra.IdEjecucion);
        }
    }
}
