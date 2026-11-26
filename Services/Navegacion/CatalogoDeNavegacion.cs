using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Services.Navegacion
{
    /// <summary>
    /// Arma el menú de la petición a partir de <see cref="ModeloNavegacion"/>, con las mismas reglas que ya usaba el Menú Principal:
    /// <list type="number">
    /// <item>Un módulo de operación aparece solo si la empresa lo tiene activo y vigente (<see cref="AuthHelper.HasModulo"/>, ADR-026); el operador
    /// en el contexto de plataforma los ve todos. Si el módulo tiene permisos en el catálogo, además hace falta alguno de ellos.</item>
    /// <item>Cada pantalla aparece solo si la persona puede abrirla: la misma pregunta que hace el filtro de permisos de la pantalla
    /// (<see cref="DecisorDeVisibilidad"/>, ADR-029, como <c>eg-si-puede-abrir</c>).</item>
    /// <item>Un bloque de administración aparece solo con algún permiso de su prefijo (<c>administracion.</c> o <c>plataforma.</c>).</item>
    /// </list>
    /// Un módulo cuyas pantallas listas quedan todas ocultas no aparece (una tarjeta que lleva a un centro vacío solo estorba). Un módulo que todavía
    /// no tiene pantallas (Bancos, Contabilidad…) aparece como «Próximamente» si está activo. El tenant de soporte no necesita regla propia: ya
    /// decide los módulos y los permisos de la petición (ADR-025). Solo oculta: la guarda real sigue siendo la de cada pantalla.
    /// </summary>
    public sealed class CatalogoDeNavegacion : INavegacionService
    {
        private readonly IHttpContextAccessor _http;
        private readonly DecisorDeVisibilidad _decisor;
        private readonly IPermisosDeLaPeticion _permisos;
        private readonly IReadOnlyList<ModuloDeNavegacion> _definicion;
        private Task<MenuDeNavegacion>? _menu;

        public CatalogoDeNavegacion(IHttpContextAccessor http, DecisorDeVisibilidad decisor, IPermisosDeLaPeticion permisos)
            : this(http, decisor, permisos, ModeloNavegacion.Modulos) { }

        /// <summary>Con otra definición (para las pruebas).</summary>
        public CatalogoDeNavegacion(IHttpContextAccessor http, DecisorDeVisibilidad decisor, IPermisosDeLaPeticion permisos,
            IReadOnlyList<ModuloDeNavegacion> definicion)
        {
            _http = http;
            _decisor = decisor;
            _permisos = permisos;
            _definicion = definicion;
        }

        public Task<MenuDeNavegacion> ObtenerAsync() => _menu ??= ArmarAsync();

        private async Task<MenuDeNavegacion> ArmarAsync()
        {
            var http = _http.HttpContext;
            if (http == null || !AuthHelper.IsAuthenticated(http)) return MenuDeNavegacion.Vacio;

            var efectivos = await _permisos.ObtenerAsync();
            var conPermisos = CatalogoDePermisos.ModulosConPermisos;
            var visibles = new List<ModuloDeNavegacion>();

            foreach (var modulo in _definicion)
            {
                if (modulo.CodigoModulo is { } codigo)
                {
                    if (!AuthHelper.HasModulo(http, codigo)) continue;
                    if (conPermisos.Contains(codigo) && !TieneAlguno(efectivos, codigo + ".")) continue;
                }
                if (modulo.PrefijoDePermiso is { } prefijo && !TieneAlguno(efectivos, prefijo)) continue;

                var pantallas = new List<PantallaDeNavegacion>();
                foreach (var p in modulo.Pantallas)
                    if (p.Proximamente || await PuedeVerAsync(p)) pantallas.Add(p);

                // Un módulo con pantallas listas que la persona no puede abrir no se muestra; uno sin pantallas todavía, sí (Próximamente).
                var algunaLista = pantallas.Any(p => !p.Proximamente);
                if (!algunaLista && !modulo.Proximamente) continue;

                visibles.Add(modulo with { Pantallas = pantallas });
            }

            return new MenuDeNavegacion(visibles);
        }

        // Un centro intermedio sin permiso propio (/Psicologia/Catalogos/Index) se muestra solo si se puede abrir alguna de sus pantallas.
        private async Task<bool> PuedeVerAsync(PantallaDeNavegacion pantalla)
        {
            if (pantalla.RutasQueLaMuestran is not { } destinos) return await _decisor.PuedeAbrirAsync(pantalla.Ruta);
            foreach (var ruta in destinos)
                if (await _decisor.PuedeAbrirAsync(ruta)) return true;
            return false;
        }

        private static bool TieneAlguno(PermisosEfectivos efectivos, string prefijo)
            => efectivos.Codigos.Any(c => c.StartsWith(prefijo, StringComparison.Ordinal));
    }
}
