namespace eGestion360Web.Tests.Infra
{
    /// <summary>Reloj fijo para que las pruebas con fechas (edad, vencimiento de licencia) no dependan del día en que se corren.</summary>
    public sealed class RelojFijo : TimeProvider
    {
        private DateTimeOffset _ahora;

        public RelojFijo(DateTimeOffset ahora) => _ahora = ahora;

        /// <summary>Mediodía en Honduras (UTC-6) del 1 de octubre de 2026.</summary>
        public static RelojFijo PorDefecto() => new(new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero));

        /// <summary>La fecha de "hoy" en Honduras que ve el servicio con <see cref="PorDefecto"/>.</summary>
        public static readonly DateOnly Hoy = new(2026, 10, 1);

        public void Avanzar(TimeSpan tiempo) => _ahora = _ahora.Add(tiempo);

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
