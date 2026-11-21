using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Catalogos
{
    /// <summary>Catálogo de pruebas psicológicas que aplica el centro (D12), de la empresa de la sesión.</summary>
    [RequierePermiso("psicologia.catalogos.ver")]
    public class PruebasModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ICatalogoPsicologiaService _catalogo;

        public PruebasModel(ApplicationDbContext db, ICatalogoPsicologiaService catalogo)
        {
            _db = db;
            _catalogo = catalogo;
        }

        public sealed class FormularioPrueba
        {
            public int? Id { get; set; }

            [Required(ErrorMessage = "Escribe el código.")]
            [StringLength(30, ErrorMessage = "El código no puede pasar de 30 caracteres.")]
            public string? Codigo { get; set; }

            [Required(ErrorMessage = "Escribe el nombre.")]
            [StringLength(150, ErrorMessage = "El nombre no puede pasar de 150 caracteres.")]
            public string? Nombre { get; set; }

            [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")]
            public string? Descripcion { get; set; }
        }

        [BindProperty] public FormularioPrueba Prueba { get; set; } = new();

        public sealed record FilaPrueba(int Id, string Codigo, string Nombre, string? Descripcion, bool Activo, int Usos);

        public List<FilaPrueba> Pruebas { get; private set; } = new();
        public bool PuedeEditar => this.Puede("psicologia.catalogos.editar");

        public async Task<IActionResult> OnGetAsync(int? editar)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarAsync();

            if (editar is { } id && Pruebas.FirstOrDefault(p => p.Id == id) is { } p)
                Prueba = new FormularioPrueba { Id = p.Id, Codigo = p.Codigo, Nombre = p.Nombre, Descripcion = p.Descripcion };
            return Page();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostGuardarAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!ModelState.IsValid) { await CargarAsync(); return Page(); }

            var r = await _catalogo.GuardarPruebaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0, Id = Prueba.Id, Codigo = Prueba.Codigo, Nombre = Prueba.Nombre,
                Descripcion = Prueba.Descripcion, Usuario = Usuario()
            });
            if (!r.Ok) { Errores(r.Errores); await CargarAsync(); return Page(); }

            TempData["CatalogosMensaje"] = Prueba.Id > 0 ? "Prueba actualizada." : "Prueba agregada al catálogo.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostEstadoAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _catalogo.CambiarEstadoPruebaAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, activo, Usuario());
            TempData["CatalogosMensaje"] = r.Ok ? (activo ? "Prueba activada." : "Prueba desactivada: ya no se ofrece al registrar.") : r.Errores[0].Mensaje;
            return RedirectToPage();
        }

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        private void Errores(IReadOnlyList<ErrorValidacion> errores)
        {
            foreach (var e in errores)
                ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : $"{nameof(Prueba)}.{e.Campo}", e.Mensaje);
        }

        private async Task CargarAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Pruebas = await _db.PruebasPsicologicas.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa)
                .OrderByDescending(p => p.Activo).ThenBy(p => p.Codigo)
                .Select(p => new FilaPrueba(p.IdPrueba, p.Codigo, p.Nombre, p.Descripcion, p.Activo,
                    _db.AtencionPruebas.Count(a => a.IdPrueba == p.IdPrueba) + _db.ConstanciaPruebas.Count(c => c.IdPrueba == p.IdPrueba)))
                .ToListAsync();
        }
    }
}
