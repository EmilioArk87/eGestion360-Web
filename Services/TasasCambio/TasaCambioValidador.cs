using System.Globalization;
using Microsoft.Extensions.Options;

namespace eGestion360Web.Services.TasasCambio
{
    public enum NivelValidacion
    {
        /// <summary>Pasa todas las reglas: puede quedar VIGENTE.</summary>
        Valida,

        /// <summary>Dato plausible pero raro (fuera de rango, salto grande, compra y venta muy separadas): se guarda EN_REVISION y se alerta.</summary>
        EnRevision,

        /// <summary>Dato imposible (cero o negativo, compra mayor que venta, fecha fuera de lo permitido): no se guarda.</summary>
        Invalida
    }

    public sealed record ResultadoValidacion(NivelValidacion Nivel, IReadOnlyList<string> Motivos)
    {
        public static readonly ResultadoValidacion Ok = new(NivelValidacion.Valida, Array.Empty<string>());
        public string? Motivo => Motivos.Count == 0 ? null : string.Join("; ", Motivos);
    }

    /// <param name="Hoy">Fecha de hoy en Honduras.</param>
    /// <param name="FechaMinima">Inicio de la puesta al día; nulo en una carga manual de un rango (backfill).</param>
    /// <param name="UltimaVigenteAnterior">Última tasa VIGENTE de la misma clave con fecha anterior, para la variación diaria.</param>
    /// <param name="Compra">Compra de la misma moneda y fecha, si se conoce (para comparar con la venta).</param>
    /// <param name="Venta">Venta de la misma moneda y fecha, si se conoce.</param>
    public sealed record ContextoValidacion(
        DateOnly Hoy,
        DateOnly? FechaMinima = null,
        decimal? UltimaVigenteAnterior = null,
        decimal? Compra = null,
        decimal? Venta = null);

    /// <summary>
    /// Reglas de una tasa antes de guardarla. Los umbrales salen de <see cref="ValidacionOptions"/>.
    ///
    ///   Inválida (no se guarda): valor ≤ 0 · fecha posterior a mañana · fecha anterior a la puesta al día (salvo
    ///   backfill manual) · compra mayor que venta.
    ///   En revisión (se guarda EN_REVISION y se alerta): fuera del rango de la moneda · variación contra la última
    ///   vigente mayor que VariacionDiariaMaxPct · diferencia compra/venta mayor que DiferenciaCompraVentaMaxPct.
    /// </summary>
    public sealed class TasaCambioValidador
    {
        private readonly TasasCambioOptions _opt;

        public TasaCambioValidador(IOptions<TasasCambioOptions> opciones) => _opt = opciones.Value;

        public ResultadoValidacion Validar(LecturaTasa lectura, ContextoValidacion contexto)
        {
            var invalidas = new List<string>();
            var revision = new List<string>();

            if (lectura.Valor <= 0) invalidas.Add("La tasa debe ser mayor que cero.");
            if (RevisarFecha(lectura.Fecha, contexto.Hoy, contexto.FechaMinima) is { } fecha) invalidas.Add(fecha);

            var (compraVentaInvalida, compraVentaRevision) = RevisarCompraVenta(contexto.Compra, contexto.Venta);
            if (compraVentaInvalida != null) invalidas.Add(compraVentaInvalida);
            if (compraVentaRevision != null) revision.Add(compraVentaRevision);

            if (invalidas.Count > 0) return new ResultadoValidacion(NivelValidacion.Invalida, invalidas);

            if (RevisarRango(lectura.MonedaOrigen, lectura.Valor) is { } rango) revision.Add(rango);
            if (RevisarVariacion(lectura.Valor, contexto.UltimaVigenteAnterior) is { } variacion) revision.Add(variacion);

            return revision.Count > 0
                ? new ResultadoValidacion(NivelValidacion.EnRevision, revision)
                : ResultadoValidacion.Ok;
        }

        /// <summary>Motivo si el valor está fuera del rango configurado para la moneda; nulo si está dentro o no hay rango.</summary>
        public string? RevisarRango(string moneda, decimal valor)
        {
            if (!_opt.Validacion.Rangos.TryGetValue(moneda, out var rango)) return null;
            if (valor >= rango.Min && valor <= rango.Max) return null;
            return $"Fuera del rango esperado para {moneda} ({F(rango.Min)} a {F(rango.Max)}): {F(valor)}.";
        }

        /// <summary>Motivo si la variación contra la última vigente anterior supera el umbral.</summary>
        public string? RevisarVariacion(decimal valor, decimal? anterior)
        {
            if (anterior is not > 0m) return null;
            var pct = Math.Abs(valor - anterior.Value) / anterior.Value * 100m;
            if (pct <= _opt.Validacion.VariacionDiariaMaxPct) return null;
            return $"Variación de {pct.ToString("0.##", CultureInfo.InvariantCulture)} % contra la última vigente ({F(anterior.Value)}); " +
                   $"el máximo es {_opt.Validacion.VariacionDiariaMaxPct.ToString("0.##", CultureInfo.InvariantCulture)} %.";
        }

        /// <summary>
        /// Compra y venta de la misma fecha: inválida si la compra supera a la venta; en revisión si la diferencia
        /// supera el umbral. Si falta una de las dos, no se revisa.
        /// </summary>
        public (string? Invalida, string? EnRevision) RevisarCompraVenta(decimal? compra, decimal? venta)
        {
            if (compra is not > 0m || venta is not > 0m) return (null, null);
            if (compra > venta) return ($"La compra ({F(compra.Value)}) es mayor que la venta ({F(venta.Value)}).", null);

            var pct = (venta.Value - compra.Value) / compra.Value * 100m;
            if (pct <= _opt.Validacion.DiferenciaCompraVentaMaxPct) return (null, null);
            return (null, $"Diferencia de {pct.ToString("0.##", CultureInfo.InvariantCulture)} % entre compra y venta; " +
                          $"el máximo es {_opt.Validacion.DiferenciaCompraVentaMaxPct.ToString("0.##", CultureInfo.InvariantCulture)} %.");
        }

        /// <summary>
        /// Motivo si la fecha pasa de dos días hábiles adelante (<see cref="CalendarioTasasCambio.LimiteFechaFutura"/>)
        /// o es anterior a la puesta al día.
        /// </summary>
        public string? RevisarFecha(DateOnly fecha, DateOnly hoy, DateOnly? minima)
        {
            if (fecha > CalendarioTasasCambio.LimiteFechaFutura(hoy)) return $"Fecha futura: {fecha:yyyy-MM-dd} (hoy es {hoy:yyyy-MM-dd}).";
            if (minima is { } min && fecha < min) return $"Fecha {fecha:yyyy-MM-dd} anterior a la puesta al día ({min:yyyy-MM-dd}).";
            return null;
        }

        private static string F(decimal valor) => TextoTasas.Formatear(valor);
    }
}
