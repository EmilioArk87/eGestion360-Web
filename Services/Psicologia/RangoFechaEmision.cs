namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Entre qué fechas puede ir la fecha de emisión de una constancia (la que sale impresa), al emitirla y al corregirla después:
    ///   * nunca futura (hoy en Honduras) ni anterior a la apertura del expediente;
    ///   * si sale de una evaluación, no anterior a esa evaluación;
    ///   * si la evaluación fue en una brigada, dentro de los días de la brigada: desde su primer día hasta su último día incluido (su fecha
    ///     «hasta» o, si no la tiene, su único día). Pedido de Emilio del 2026-11-16.
    /// La fecha y hora en que de verdad se registró la constancia (<c>fecha_creacion</c>) es otra cosa: la pone el servidor y no cambia.
    /// </summary>
    /// <param name="Desde">La primera fecha posible.</param>
    /// <param name="Hasta">La última fecha posible.</param>
    /// <param name="FinBrigada">El último día de la brigada (su único día si no tiene «hasta»); nulo si no hay brigada.</param>
    public sealed record RangoFechaEmision(DateOnly Desde, DateOnly Hasta, DateOnly Hoy, DateOnly Apertura, DateOnly? Evaluacion,
        DateOnly? InicioBrigada, DateOnly? FinBrigada)
    {
        public static RangoFechaEmision Calcular(DateOnly hoy, DateOnly apertura, DateOnly? evaluacion = null,
            DateOnly? inicioBrigada = null, DateOnly? finBrigada = null)
        {
            DateOnly? fin = inicioBrigada == null ? null : finBrigada ?? inicioBrigada;
            var desde = apertura;
            if (evaluacion is { } e && e > desde) desde = e;
            if (inicioBrigada is { } i && i > desde) desde = i;
            var hasta = fin is { } f && f < hoy ? f : hoy;
            return new RangoFechaEmision(desde, hasta, hoy, apertura, evaluacion, inicioBrigada, fin);
        }

        /// <summary>No queda ninguna fecha posible (por ejemplo, una brigada que todavía no empezó).</summary>
        public bool Vacio => Desde > Hasta;

        public bool Contiene(DateOnly fecha) => fecha >= Desde && fecha <= Hasta;

        /// <summary>Por qué la fecha no vale (el primer motivo), o nulo si está dentro del rango.</summary>
        public string? Error(DateOnly fecha)
        {
            if (fecha > Hoy) return "La fecha de emisión no puede ser futura.";
            if (InicioBrigada is { } inicio && FinBrigada is { } fin && (fecha < inicio || fecha > fin))
                return inicio == fin
                    ? $"La evaluación fue en una brigada de un solo día: la fecha de emisión tiene que ser el {inicio:dd/MM/yyyy}."
                    : $"La fecha de emisión tiene que estar dentro de la brigada: del {inicio:dd/MM/yyyy} al {fin:dd/MM/yyyy}.";
            if (Evaluacion is { } evaluacion && fecha < evaluacion)
                return $"La fecha de emisión no puede ser anterior a la evaluación ({evaluacion:dd/MM/yyyy}).";
            if (fecha < Apertura)
                return $"La fecha de emisión no puede ser anterior a la apertura del expediente ({Apertura:dd/MM/yyyy}).";
            return null;
        }

        /// <summary>Para mostrar junto al campo: «el 15/11/2026» o «del 15/11/2026 al 17/11/2026».</summary>
        public string Texto => Desde == Hasta ? $"el {Desde:dd/MM/yyyy}" : $"del {Desde:dd/MM/yyyy} al {Hasta:dd/MM/yyyy}";
    }
}
