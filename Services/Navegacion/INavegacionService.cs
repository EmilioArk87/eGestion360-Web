namespace eGestion360Web.Services.Navegacion
{
    /// <summary>
    /// El menú que ve quien hace la petición: la definición de <see cref="ModeloNavegacion"/> recortada a los módulos activos de su empresa y a las
    /// pantallas que puede abrir. Lo usan los tres estilos de menú, el hub del Menú Principal, el buscador Ctrl+K y los centros de módulo.
    /// </summary>
    public interface INavegacionService
    {
        /// <summary>El menú de esta petición (se arma una vez por petición y se recuerda).</summary>
        Task<MenuDeNavegacion> ObtenerAsync();
    }

    /// <summary>Un elemento del buscador rápido (Ctrl+K y el filtro del hub).</summary>
    /// <param name="Modulo">Nombre del módulo, para mostrar «Módulo › Pantalla».</param>
    /// <param name="Titulo">Nombre de la pantalla.</param>
    /// <param name="Ruta">Ruta Razor (se convierte en URL en la vista).</param>
    /// <param name="Icono">Icono de la pantalla.</param>
    /// <param name="Color">Color del módulo.</param>
    /// <param name="Texto">Todo lo que se puede escribir para encontrarla (título, módulo, descripción, palabras), en minúsculas.</param>
    public sealed record EntradaDeBusqueda(string Modulo, string Titulo, string Ruta, string Icono, string Color, string Texto);

    /// <summary>El menú ya recortado para una persona.</summary>
    public sealed class MenuDeNavegacion
    {
        public static readonly MenuDeNavegacion Vacio = new(Array.Empty<ModuloDeNavegacion>());

        public MenuDeNavegacion(IReadOnlyList<ModuloDeNavegacion> modulos) => Modulos = modulos;

        /// <summary>Los módulos visibles, cada uno solo con las pantallas que la persona puede abrir.</summary>
        public IReadOnlyList<ModuloDeNavegacion> Modulos { get; }

        /// <summary>True si no hay nada que mostrar.</summary>
        public bool EstaVacio => Modulos.Count == 0;

        /// <summary>Los módulos visibles de un grupo.</summary>
        public IEnumerable<ModuloDeNavegacion> DelGrupo(GrupoDeNavegacion grupo) => Modulos.Where(m => m.Grupo == grupo);

        /// <summary>Los grupos que tienen algún módulo visible, en orden.</summary>
        public IEnumerable<GrupoDeNavegacion> Grupos => Modulos.Select(m => m.Grupo).Distinct().OrderBy(g => g);

        /// <summary>El módulo visible al que pertenece la pantalla actual, o nulo (Inicio, Menú Principal, Perfil…).</summary>
        public ModuloDeNavegacion? ModuloDe(string? rutaActual) => Modulos.FirstOrDefault(m => m.Contiene(rutaActual));

        /// <summary>True si <paramref name="pantalla"/> es la pantalla actual (o una de sus hermanas: Create, Edit…).</summary>
        public static bool EsActual(PantallaDeNavegacion pantalla, string? rutaActual)
            => !pantalla.Proximamente && !string.IsNullOrEmpty(rutaActual) && ModuloDeNavegacion.EsDeLaPantalla(rutaActual, pantalla.Ruta);

        /// <summary>Lo que el buscador puede encontrar: las pantallas listas y los centros de módulo, nada que la persona no pueda abrir.</summary>
        public IReadOnlyList<EntradaDeBusqueda> Busqueda()
        {
            var lista = new List<EntradaDeBusqueda>();
            foreach (var m in Modulos)
            {
                if (m.RutaIndice != null && !m.PantallasListas.Any(p => p.Ruta == m.RutaIndice))
                    lista.Add(Entrada(m, "Inicio de " + m.Titulo, m.RutaIndice, m.Icono, m.Descripcion));
                foreach (var p in m.PantallasListas)
                    lista.Add(Entrada(m, p.Titulo, p.Ruta, p.Icono, p.Descripcion + " " + p.Palabras));
            }
            return lista;
        }

        private static EntradaDeBusqueda Entrada(ModuloDeNavegacion m, string titulo, string ruta, string icono, string extra)
            => new(m.Titulo, titulo, ruta, icono, m.Color, $"{titulo} {m.Titulo} {extra}".ToLowerInvariant());
    }
}
