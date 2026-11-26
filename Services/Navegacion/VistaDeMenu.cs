namespace eGestion360Web.Services.Navegacion
{
    /// <summary>Lo que necesitan las vistas parciales del menú (Pages/Shared/Menu): el menú de la persona, la pantalla actual y su estilo.</summary>
    /// <param name="Menu">El menú ya recortado a lo que la persona puede abrir.</param>
    /// <param name="RutaActual">Ruta Razor de la pantalla que se está mostrando (<c>/Flota/KPI/Dashboard</c>), o nula.</param>
    /// <param name="Estilo">Estilo de menú de la persona (<c>hub</c>, <c>sidebar</c> o <c>topbar</c>).</param>
    public sealed record VistaDeMenu(MenuDeNavegacion Menu, string? RutaActual, string Estilo)
    {
        /// <summary>El módulo de la pantalla actual, o nulo.</summary>
        public ModuloDeNavegacion? ModuloActivo => Menu.ModuloDe(RutaActual);

        /// <summary>La pantalla actual dentro de su módulo, o nula.</summary>
        public PantallaDeNavegacion? PantallaActiva
            => ModuloActivo?.Pantallas.FirstOrDefault(p => MenuDeNavegacion.EsActual(p, RutaActual));

        /// <summary>Iniciales para el avatar («Emilio Garay» → «EG»).</summary>
        public static string Iniciales(string nombre)
        {
            var partes = (nombre ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0) return "?";
            var texto = partes.Length == 1 ? partes[0][..Math.Min(2, partes[0].Length)] : $"{partes[0][0]}{partes[^1][0]}";
            return texto.ToUpperInvariant();
        }
    }
}
