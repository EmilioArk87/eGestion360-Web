using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Euro en lempiras cuando el BCH no da un indicador oficial: EUR/HNL = EUR/USD (BCE) × USD/HNL (BCH), por
    /// separado para compra y venta. El EUR/USD es el de la misma fecha o, si el BCE no publicó ese día (fin de
    /// semana, feriado europeo), el anterior más cercano dentro de <see cref="MaxDiasAtrasBce"/> días.
    /// </summary>
    public static class EuroDerivado
    {
        public const int MaxDiasAtrasBce = 7;

        /// <param name="usdLocal">Lectura USD→moneda local (compra o venta) ya validada.</param>
        /// <param name="eurUsd">Lecturas EUR→USD del BCE.</param>
        /// <returns>La lectura EUR→moneda local, redondeada a 4 decimales, o nulo si no hay EUR/USD aplicable.</returns>
        public static LecturaTasa? Calcular(LecturaTasa usdLocal, IEnumerable<LecturaTasa> eurUsd)
        {
            var bce = eurUsd
                .Where(e => e.Fecha <= usdLocal.Fecha && e.Fecha >= usdLocal.Fecha.AddDays(-MaxDiasAtrasBce))
                .MaxBy(e => e.Fecha);
            if (bce == null) return null;

            var valor = TextoTasas.Redondear4(bce.Valor * usdLocal.Valor);
            var referencia =
                $"EUR/USD BCE {bce.Fecha:yyyy-MM-dd} = {TextoTasas.Formatear(bce.Valor)} × " +
                $"USD/{usdLocal.MonedaDestino} {usdLocal.TipoTasa} {usdLocal.Fuente} {usdLocal.Fecha:yyyy-MM-dd} = {TextoTasas.Formatear(usdLocal.Valor)}";

            return new LecturaTasa("EUR", usdLocal.MonedaDestino, usdLocal.TipoTasa, usdLocal.Fecha, valor,
                TasasCambioCatalogo.Fuente.Derivada, TextoTasas.Cortar(referencia, 400), EsDerivada: true);
        }
    }
}
