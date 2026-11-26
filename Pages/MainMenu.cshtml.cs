using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Navegacion;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// El hub del Menú Principal (estilo «hub» del menú configurable): buscador en vivo, accesos frecuentes, filtros por categoría y una tarjeta por
    /// módulo con accesos directos a sus pantallas. Todo sale del catálogo de navegación (<see cref="INavegacionService"/>), ya recortado a los
    /// módulos activos de la empresa y a lo que la persona puede abrir. En los estilos «sidebar» y «topbar» sigue siendo la pantalla de inicio.
    /// </summary>
    [PaginaSinPermiso("Centro de navegación: cada opción pide su propio permiso")]
    public class MainMenuModel : PageModel
    {
        private readonly INavegacionService _navegacion;

        public MainMenuModel(INavegacionService navegacion) => _navegacion = navegacion;

        public string Username      { get; set; } = string.Empty;

        /// <summary>El nombre de la persona que usa la cuenta («Emilio Garay»); el usuario si no lo hay.</summary>
        public string NombrePersona { get; set; } = string.Empty;
        public string Email         { get; set; } = string.Empty;
        public int?   EmpresaId     { get; set; }

        /// <summary>Lo que la persona puede abrir, por módulo.</summary>
        public MenuDeNavegacion Menu { get; private set; } = MenuDeNavegacion.Vacio;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            Username       = HttpContext.Session.GetString("Username") ?? "Usuario";
            NombrePersona  = AuthHelper.NombreVisible(HttpContext);
            Email          = HttpContext.Session.GetString("Email") ?? "";
            EmpresaId      = AuthHelper.GetEmpresaId(HttpContext);
            Menu           = await _navegacion.ObtenerAsync();

            return Page();
        }

        /// <summary>Clave de la categoría del filtro del hub para un grupo.</summary>
        public static string Categoria(GrupoDeNavegacion grupo) => grupo switch
        {
            GrupoDeNavegacion.Empresa => "empresa",
            GrupoDeNavegacion.Plataforma => "sistema",
            _ => "operacion",
        };

        /// <summary>Todo lo que se puede escribir en el filtro para encontrar la tarjeta del módulo.</summary>
        public static string TextoDeBusqueda(ModuloDeNavegacion m)
            => string.Join(' ', new[] { m.Titulo, m.Descripcion }.Concat(m.Pantallas.SelectMany(p => new[] { p.Titulo, p.Palabras ?? string.Empty })));
    }
}
