using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Centro de navegación: cada opción pide su propio permiso")]
    public class MainMenuModel : PageModel
    {
        private readonly IPermisosDeLaPeticion _permisos;
        private PermisosEfectivos _efectivos = PermisosEfectivos.Ninguno;

        public MainMenuModel(IPermisosDeLaPeticion permisos) => _permisos = permisos;

        public string Username      { get; set; } = string.Empty;

        /// <summary>El nombre de la persona que usa la cuenta («Emilio Garay»); el usuario si no lo hay.</summary>
        public string NombrePersona { get; set; } = string.Empty;
        public string Email         { get; set; } = string.Empty;
        public int?   EmpresaId     { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            Username       = HttpContext.Session.GetString("Username") ?? "Usuario";
            NombrePersona  = AuthHelper.NombreVisible(HttpContext);
            Email          = HttpContext.Session.GetString("Email") ?? "";
            EmpresaId      = AuthHelper.GetEmpresaId(HttpContext);
            _efectivos     = await _permisos.ObtenerAsync();

            return Page();
        }

        /// <summary>True si el usuario tiene acceso al módulo (superadmin siempre tiene acceso).</summary>
        public bool TieneModulo(string codigo) => AuthHelper.HasModulo(HttpContext, codigo);

        /// <summary>True si tiene el permiso del catálogo (la misma pregunta que hace la pantalla a la que lleva la opción).</summary>
        public bool Puede(string codigo) => _efectivos.Puede(codigo);

        /// <summary>
        /// La tarjeta de un módulo se muestra si el módulo está activo para la empresa y la persona puede hacer algo en él: una tarjeta que
        /// lleva a un centro donde no puede abrir nada solo estorba. Un módulo sin permisos en el catálogo (aún sin pantallas) se muestra por el módulo.
        /// </summary>
        public bool MostrarModulo(string codigo)
            => TieneModulo(codigo) && (!CatalogoDePermisos.ModulosConPermisos.Contains(codigo)
                                       || _efectivos.Codigos.Any(c => c.StartsWith(codigo + ".", StringComparison.Ordinal)));

        /// <summary>True si puede abrir alguna de las pantallas de administración de la plataforma (para mostrar su encabezado).</summary>
        public bool PuedeAdministrarElSistema => _efectivos.Codigos.Any(c => c.StartsWith("plataforma.", StringComparison.Ordinal));

        /// <summary>True si puede abrir alguna de las pantallas de administración de la empresa (para mostrar su encabezado).</summary>
        public bool PuedeAdministrarLaEmpresa => _efectivos.Codigos.Any(c => c.StartsWith("administracion.", StringComparison.Ordinal));
    }
}
