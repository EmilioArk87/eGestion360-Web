using Microsoft.Extensions.Options;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Tests.Services.TasasCambio
{
    public class TasaCambioValidadorTests
    {
        private static readonly DateOnly Hoy = new(2026, 10, 2);

        private static TasaCambioValidador Validador() => new(Options.Create(new TasasCambioOptions()));

        private static LecturaTasa Usd(decimal valor, string tipo = "COMPRA", DateOnly? fecha = null) =>
            new("USD", "HNL", tipo, fecha ?? Hoy, valor, "BCH_XLSX", null);

        [Fact]
        public void Tasa_normal_es_valida()
        {
            var r = Validador().Validar(Usd(26.8925m), new ContextoValidacion(Hoy, Hoy.AddDays(-3), 26.8788m, 26.8925m, 27.0270m));
            Assert.Equal(NivelValidacion.Valida, r.Nivel);
            Assert.Null(r.Motivo);
        }

        [Theory]
        [InlineData("USD", 14.99, true)]
        [InlineData("USD", 15.00, false)]
        [InlineData("USD", 45.00, false)]
        [InlineData("USD", 45.01, true)]
        [InlineData("EUR", 59.99, false)]
        [InlineData("EUR", 60.01, true)]
        [InlineData("GBP", 500.00, false)]   // moneda sin rango configurado: no se revisa
        public void Rango_por_moneda(string moneda, double valor, bool fuera) =>
            Assert.Equal(fuera, Validador().RevisarRango(moneda, (decimal)valor) != null);

        [Fact]
        public void Fuera_de_rango_va_a_revision()
        {
            var r = Validador().Validar(Usd(2.68925m), new ContextoValidacion(Hoy));
            Assert.Equal(NivelValidacion.EnRevision, r.Nivel);
            Assert.Contains("Fuera del rango", r.Motivo);
        }

        [Fact]
        public void Variacion_de_mas_de_3_por_ciento_contra_la_ultima_vigente_va_a_revision()
        {
            var v = Validador();
            Assert.Null(v.RevisarVariacion(27.69m, 26.8925m));          // +2.96 %
            Assert.NotNull(v.RevisarVariacion(27.71m, 26.8925m));       // +3.04 %
            Assert.NotNull(v.RevisarVariacion(26.0m, 26.8925m));        // -3.3 %
            Assert.Null(v.RevisarVariacion(40m, null));                 // sin anterior no se compara

            var r = v.Validar(Usd(28.5m), new ContextoValidacion(Hoy, UltimaVigenteAnterior: 26.8925m));
            Assert.Equal(NivelValidacion.EnRevision, r.Nivel);
            Assert.Contains("Variación", r.Motivo);
        }

        [Fact]
        public void Compra_mayor_que_venta_es_invalida_y_diferencia_grande_va_a_revision()
        {
            var v = Validador();
            Assert.NotNull(v.RevisarCompraVenta(27.03m, 26.89m).Invalida);
            Assert.Equal(((string?)null, (string?)null), v.RevisarCompraVenta(26.8925m, 27.0270m));   // 0.5 %
            Assert.NotNull(v.RevisarCompraVenta(26.0m, 26.6m).EnRevision);          // 2.3 %
            Assert.Equal(((string?)null, (string?)null), v.RevisarCompraVenta(26.8925m, null));   // falta una: no se compara

            Assert.Equal(NivelValidacion.Invalida,
                v.Validar(Usd(27.03m), new ContextoValidacion(Hoy, Compra: 27.03m, Venta: 26.89m)).Nivel);
            Assert.Equal(NivelValidacion.EnRevision,
                v.Validar(Usd(26.0m), new ContextoValidacion(Hoy, Compra: 26.0m, Venta: 26.6m)).Nivel);
        }

        [Fact]
        public void Fecha_mas_alla_de_dos_dias_habiles_o_anterior_a_la_puesta_al_dia_es_invalida_salvo_backfill()
        {
            var v = Validador();
            // Hoy es viernes 2: el BCH ya publicó la del lunes 5; el martes 6 se tolera por si el lunes fuera feriado.
            Assert.Null(v.RevisarFecha(Hoy.AddDays(1), Hoy, null));                   // sábado
            Assert.Null(v.RevisarFecha(Hoy.AddDays(3), Hoy, null));                   // lunes: día hábil siguiente
            Assert.Null(v.RevisarFecha(Hoy.AddDays(4), Hoy, null));                   // martes: segundo día hábil
            Assert.NotNull(v.RevisarFecha(Hoy.AddDays(5), Hoy, null));                // miércoles: demasiado adelante
            Assert.NotNull(v.RevisarFecha(Hoy.AddDays(-10), Hoy, Hoy.AddDays(-3)));
            Assert.Null(v.RevisarFecha(Hoy.AddDays(-10), Hoy, null));                 // carga manual de un rango

            Assert.Equal(NivelValidacion.Invalida,
                v.Validar(Usd(26.9m, fecha: Hoy.AddDays(5)), new ContextoValidacion(Hoy)).Nivel);
        }

        [Fact]
        public void Cero_o_negativo_es_invalida()
        {
            Assert.Equal(NivelValidacion.Invalida, Validador().Validar(Usd(0m), new ContextoValidacion(Hoy)).Nivel);
            Assert.Equal(NivelValidacion.Invalida, Validador().Validar(Usd(-1m), new ContextoValidacion(Hoy)).Nivel);
        }

        [Fact]
        public void Los_umbrales_salen_de_la_configuracion()
        {
            var opt = new TasasCambioOptions();
            opt.Validacion.VariacionDiariaMaxPct = 10m;
            opt.Validacion.Rangos["USD"] = new RangoTasa { Min = 1m, Max = 100m };
            var v = new TasaCambioValidador(Options.Create(opt));

            Assert.Null(v.RevisarVariacion(29m, 26.8925m));   // 7.8 % < 10 %
            Assert.Null(v.RevisarRango("USD", 2.5m));
        }
    }
}
