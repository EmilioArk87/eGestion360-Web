using System.Globalization;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>Qué líneas muestra la gráfica de tendencias.</summary>
    public static class SerieTendencia
    {
        public const string Ambas = "AMBAS";
        public const string Compra = "COMPRA";
        public const string Venta = "VENTA";
        public static readonly string[] Todas = { Ambas, Compra, Venta };
    }

    /// <summary>Un punto clicable de la gráfica, en porcentajes del área del dibujo.</summary>
    public sealed record PuntoGrafica(DateOnly Fecha, string Serie, decimal Valor, string Izquierda, string Arriba, bool Seleccionado);

    public sealed record EtiquetaEje(string Texto, string Posicion);

    /// <summary>
    /// La geometría de la gráfica de tendencias calculada en el servidor: el trazado de las líneas (coordenadas de un
    /// lienzo de <see cref="Ancho"/> × <see cref="Alto"/>), la banda entre compra y venta, la línea de la fecha elegida,
    /// los ejes y los puntos. Todo en cultura invariante: el punto decimal no depende del servidor.
    /// </summary>
    public sealed class GraficaTendencia
    {
        public const int Ancho = 1000, Alto = 340;
        private const int MargenIzq = 74, MargenDer = 30, MargenSup = 16, MargenInf = 44;

        public string Cuadricula { get; private init; } = string.Empty;
        public string Banda { get; private init; } = string.Empty;
        public string LineaElegida { get; private init; } = string.Empty;
        public string TrazoCompra { get; private init; } = string.Empty;
        public string TrazoVenta { get; private init; } = string.Empty;
        public IReadOnlyList<EtiquetaEje> EjeY { get; private init; } = Array.Empty<EtiquetaEje>();
        public IReadOnlyList<EtiquetaEje> EjeX { get; private init; } = Array.Empty<EtiquetaEje>();
        public IReadOnlyList<PuntoGrafica> Puntos { get; private init; } = Array.Empty<PuntoGrafica>();

        /// <param name="puntos">De la fecha más antigua a la más reciente.</param>
        /// <param name="serie">Una de <see cref="SerieTendencia"/>.</param>
        /// <param name="elegida">La fecha resaltada; si no está en la serie, se resalta la última.</param>
        public static GraficaTendencia Calcular(IReadOnlyList<PuntoTendencia> puntos, string serie, DateOnly? elegida)
        {
            var verCompra = serie != SerieTendencia.Venta;
            var verVenta = serie != SerieTendencia.Compra;

            var valores = new List<decimal>();
            foreach (var p in puntos)
            {
                if (verCompra && p.Compra is { } c) valores.Add(c);
                if (verVenta && p.Venta is { } v) valores.Add(v);
            }
            if (puntos.Count == 0 || valores.Count == 0) return new GraficaTendencia();

            var min = valores.Min();
            var max = valores.Max();
            var margen = Math.Max((max - min) * 0.25m, 0.004m);
            min -= margen;
            max += margen;

            var ancho = Ancho - MargenIzq - MargenDer;
            var alto = Alto - MargenSup - MargenInf;
            var primero = puntos[0].Fecha.DayNumber;
            var ultimo = puntos[^1].Fecha.DayNumber;

            double X(DateOnly f) => puntos.Count == 1 || ultimo == primero
                ? MargenIzq + ancho / 2.0
                : MargenIzq + (f.DayNumber - primero) / (double)(ultimo - primero) * ancho;
            double Y(decimal v) => MargenSup + (1 - (double)((v - min) / (max - min))) * alto;

            var marcas = Enumerable.Range(0, 4).Select(i => min + (max - min) * (3 - i) / 3).ToList();
            var cuadricula = string.Join(" ", marcas.Select(v => $"M{MargenIzq} {N(Y(v))} H{Ancho - MargenDer}"));
            var ejeY = marcas.Select(v => new EtiquetaEje(v.ToString("0.000", CultureInfo.InvariantCulture), Pct(Y(v) / Alto))).ToList();
            var ejeX = puntos.Select(p => new EtiquetaEje(p.Fecha.ToString("dd/MM", CultureInfo.InvariantCulture), Pct(X(p.Fecha) / Ancho))).ToList();

            string Trazo(Func<PuntoTendencia, decimal?> valor)
            {
                var visibles = puntos.Where(p => valor(p) != null).ToList();
                return visibles.Count == 0
                    ? string.Empty
                    : "M" + string.Join(" L", visibles.Select(p => $"{N(X(p.Fecha))} {N(Y(valor(p)!.Value))}"));
            }

            var conAmbos = puntos.Where(p => p.Compra != null && p.Venta != null).ToList();
            var banda = verCompra && verVenta && conAmbos.Count > 1
                ? string.Join(" ", conAmbos.Select(p => $"{N(X(p.Fecha))},{N(Y(p.Venta!.Value))}")
                    .Concat(Enumerable.Reverse(conAmbos).Select(p => $"{N(X(p.Fecha))},{N(Y(p.Compra!.Value))}")))
                : string.Empty;

            var fechaElegida = elegida is { } e && puntos.Any(p => p.Fecha == e) ? e : puntos[^1].Fecha;

            var marcados = new List<PuntoGrafica>();
            foreach (var p in puntos)
            {
                var esa = p.Fecha == fechaElegida;
                if (verCompra && p.Compra is { } c)
                    marcados.Add(new PuntoGrafica(p.Fecha, SerieTendencia.Compra, c, Pct(X(p.Fecha) / Ancho), Pct(Y(c) / Alto), esa));
                if (verVenta && p.Venta is { } v)
                    marcados.Add(new PuntoGrafica(p.Fecha, SerieTendencia.Venta, v, Pct(X(p.Fecha) / Ancho), Pct(Y(v) / Alto), esa));
            }

            return new GraficaTendencia
            {
                Cuadricula = cuadricula,
                Banda = banda,
                LineaElegida = $"M{N(X(fechaElegida))} {MargenSup} V{Alto - MargenInf}",
                TrazoCompra = verCompra ? Trazo(p => p.Compra) : string.Empty,
                TrazoVenta = verVenta ? Trazo(p => p.Venta) : string.Empty,
                EjeY = ejeY,
                EjeX = ejeX,
                Puntos = marcados
            };
        }

        private static string N(double valor) => valor.ToString("0.0", CultureInfo.InvariantCulture);

        private static string Pct(double fraccion) => (fraccion * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }
}
