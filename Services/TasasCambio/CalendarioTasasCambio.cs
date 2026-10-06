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

        /// <summary>
        /// La fecha cuya tasa ya debería estar publicada: hoy si es día hábil y ya pasó el primer intento
        /// (<see cref="TasasCambioOptions.PrimerIntento"/>); si no, el día hábil anterior.
        /// </summary>
        public DateOnly FechaObjetivo()
        {
            var ahora = AhoraHonduras;
            var hoy = DateOnly.FromDateTime(ahora);
            return EsDiaHabil(hoy) && TimeOnly.FromDateTime(ahora) >= _opt.HoraPrimerIntento
                ? hoy
                : DiaHabilAnterior(hoy);
        }

        /// <summary>Instante UTC de una hora de Honduras en una fecha.</summary>
        public DateTime AUtc(DateOnly fecha, TimeOnly hora) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fecha.ToDateTime(hora), DateTimeKind.Unspecified), _zona);

        /// <summary>Después de este instante (UTC) ya no se programan reintentos para hoy.</summary>
        public DateTime LimiteReintentosHoyUtc => AUtc(Hoy, _opt.HoraUltimoIntento);
    }
}
