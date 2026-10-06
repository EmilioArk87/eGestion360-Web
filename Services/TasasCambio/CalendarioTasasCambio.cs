namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// La hora de Honduras y las fechas que importan al job: hoy, día hábil, fecha objetivo y límite de reintentos.
    /// Sale de <see cref="TimeProvider"/> para que las pruebas fijen el reloj.
    ///
    /// Día hábil = lunes a viernes. No hay calendario de feriados: un feriado se ve como un día sin publicación
    /// (OMITIDA_SIN_DATOS, no es fallo) y, si se juntan dos días hábiles seguidos sin tasa, se avisa.
    /// </summary>
    public sealed class CalendarioTasasCambio
    {
        private readonly TimeProvider _reloj;
        private readonly TasasCambioOptions _opt;
        private readonly TimeZoneInfo _zona;

        public CalendarioTasasCambio(TimeProvider reloj, TasasCambioOptions opciones)
        {
            _reloj = reloj;
            _opt = opciones;
            _zona = ZonaHorariaHonduras.Obtener(opciones.ZonaHoraria);
        }

        public DateTime AhoraUtc => _reloj.GetUtcNow().UtcDateTime;

        public DateTime AhoraHonduras => AHonduras(AhoraUtc);

        public DateOnly Hoy => DateOnly.FromDateTime(AhoraHonduras);

        public DateTime AHonduras(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zona);

        public static bool EsDiaHabil(DateOnly fecha) =>
            fecha.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        /// <summary>El día hábil inmediatamente anterior a <paramref name="fecha"/> (sin contarla).</summary>
        public static DateOnly DiaHabilAnterior(DateOnly fecha)
        {
            do fecha = fecha.AddDays(-1);
            while (!EsDiaHabil(fecha));
            return fecha;
        }

        /// <summary>El día hábil inmediatamente posterior a <paramref name="fecha"/> (sin contarla).</summary>
        public static DateOnly SiguienteDiaHabil(DateOnly fecha)
        {
            do fecha = fecha.AddDays(1);
            while (!EsDiaHabil(fecha));
            return fecha;
        }

        /// <summary>
        /// La fecha de vigencia más lejana que se acepta: dos días hábiles adelante. El BCH publica la tasa del día
        /// hábil siguiente por adelantado (un viernes ya trae la del lunes); el segundo día hábil tolera un feriado.
        /// </summary>
        public static DateOnly LimiteFechaFutura(DateOnly hoy) => SiguienteDiaHabil(SiguienteDiaHabil(hoy));

        /// <summary>
        /// Día hábil y ya pasó el primer intento (<see cref="TasasCambioOptions.PrimerIntento"/>): la hora en que el
        /// job busca la tasa del día hábil siguiente.
        /// </summary>
        public bool EnVentanaDeLaTarde =>
            EsDiaHabil(Hoy) && TimeOnly.FromDateTime(AhoraHonduras) >= _opt.HoraPrimerIntento;

        /// <summary>
        /// La fecha cuya tasa hay que tener. El BCH publica la tasa de un día hábil la tarde del día hábil anterior
        /// (comprobado con su Excel el 2026-10-05: a las 22:51 ya traía la del 6), así que:
        ///   * día hábil antes del primer intento: hoy (se publicó ayer y rige desde las 00:00);
        ///   * día hábil desde el primer intento, o fin de semana: el día hábil siguiente (un viernes en la tarde,
        ///     el sábado y el domingo apuntan al lunes).
        /// </summary>
        public DateOnly FechaObjetivo() => EsDiaHabil(Hoy) && !EnVentanaDeLaTarde ? Hoy : SiguienteDiaHabil(Hoy);

        /// <summary>Instante UTC de una hora de Honduras en una fecha.</summary>
        public DateTime AUtc(DateOnly fecha, TimeOnly hora) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fecha.ToDateTime(hora), DateTimeKind.Unspecified), _zona);

        /// <summary>Después de este instante (UTC) ya no se programan reintentos para hoy.</summary>
        public DateTime LimiteReintentosHoyUtc => AUtc(Hoy, _opt.HoraUltimoIntento);
    }
}
