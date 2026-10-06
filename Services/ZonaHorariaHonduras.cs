namespace eGestion360Web.Services
{
    /// <summary>
    /// Zona horaria de Honduras en cualquier servidor. Lo usan los procesos en segundo plano que se programan por
    /// hora local (tasas de cambio, KPI).
    /// </summary>
    public static class ZonaHorariaHonduras
    {
        // ID en Windows: "Central America Standard Time"
        // ID en Linux/macOS: "America/Tegucigalpa"
        private static readonly string[] IdsConocidos = { "Central America Standard Time", "America/Tegucigalpa" };

        /// <summary>
        /// La zona <paramref name="idPreferido"/> si existe en el servidor; si no, la primera de las conocidas que
        /// exista; y si ninguna, UTC-6 fijo (Honduras no usa horario de verano).
        /// </summary>
        public static TimeZoneInfo Obtener(string? idPreferido = null)
        {
            var ids = string.IsNullOrWhiteSpace(idPreferido)
                ? IdsConocidos
                : new[] { idPreferido.Trim() }.Concat(IdsConocidos);

            foreach (var id in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { /* intentar siguiente */ }
            }
            return TimeZoneInfo.CreateCustomTimeZone("HN", TimeSpan.FromHours(-6), "Honduras", "Honduras");
        }
    }
}
