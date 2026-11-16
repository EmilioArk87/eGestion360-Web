using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Catalogos
{
    /// <summary>Catálogos del módulo: pruebas psicológicas (D12) y tipos de constancia (D14) de la empresa de la sesión.</summary>
    [RequierePermiso("psicologia.catalogos.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ICatalogoPsicologiaService _catalogo;

        public IndexModel(ApplicationDbContext db, ICatalogoPsicologiaService catalogo)
        {
            _db = db;
            _catalogo = catalogo;
        }

        public sealed class FormularioElemento
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

        [BindProperty] public FormularioElemento Prueba { get; set; } = new();
        [BindProperty] public FormularioElemento Tipo { get; set; } = new();

        public sealed record FilaPrueba(int Id, string Codigo, string Nombre, string? Descripcion, bool Activo, int Usos);
        public sealed record FilaTipo(int Id, string Codigo, string Nombre, string? Plantilla, bool Activo, int Usos);

        public List<FilaPrueba> Pruebas { get; private set; } = new();
        public List<FilaTipo> Tipos { get; private set; } = new();
        public bool PuedeEditar => this.Puede("psicologia.catalogos.editar");

        public async Task<IActionResult> OnGetAsync(int? editarPrueba, int? editarTipo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarAsync();

            if (editarPrueba is { } idP && Pruebas.FirstOrDefault(p => p.Id == idP) is { } p)
                Prueba = new FormularioElemento { Id = p.Id, Codigo = p.Codigo, Nombre = p.Nombre, Descripcion = p.Descripcion };
            if (editarTipo is { } idT && Tipos.FirstOrDefault(t => t.Id == idT) is { } t)
                Tipo = new FormularioElemento { Id = t.Id, Codigo = t.Codigo, Nombre = t.Nombre };
            return Page();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostGuardarPruebaAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            ValidarSoloElFormulario(nameof(Prueba));
            if (!ModelState.IsValid) { await CargarAsync(); return Page(); }

            var r = await _catalogo.GuardarPruebaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0, Id = Prueba.Id, Codigo = Prueba.Codigo, Nombre = Prueba.Nombre,
                Descripcion = Prueba.Descripcion, Usuario = Usuario()
            });
            if (!r.Ok) { Errores(nameof(Prueba), r.Errores); await CargarAsync(); return Page(); }

            TempData["CatalogosMensaje"] = Prueba.Id > 0 ? "Prueba actualizada." : "Prueba agregada al catálogo.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostGuardarTipoAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            ValidarSoloElFormulario(nameof(Tipo));
            if (!ModelState.IsValid) { await CargarAsync(); return Page(); }

            var r = await _catalogo.GuardarTipoConstanciaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0, Id = Tipo.Id, Codigo = Tipo.Codigo, Nombre = Tipo.Nombre, Usuario = Usuario()
            });
            if (!r.Ok) { Errores(nameof(Tipo), r.Errores); await CargarAsync(); return Page(); }

            TempData["CatalogosMensaje"] = Tipo.Id > 0 ? "Tipo de constancia actualizado." : "Tipo de constancia agregado.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostEstadoPruebaAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _catalogo.CambiarEstadoPruebaAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, activo, Usuario());
            TempData["CatalogosMensaje"] = r.Ok ? (activo ? "Prueba activada." : "Prueba desactivada: ya no se ofrece al registrar.") : r.Errores[0].Mensaje;
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostEstadoTipoAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _catalogo.CambiarEstadoTipoConstanciaAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, activo, Usuario());
            TempData["CatalogosMensaje"] = r.Ok ? (activo ? "Tipo de constancia activado." : "Tipo de constancia desactivado: ya no se ofrece al emitir.") : r.Errores[0].Mensaje;
            return RedirectToPage();
        }

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        /// <summary>
        /// La página tiene dos formularios y solo llega uno. El que no llega lo valida igual ASP.NET, y sin datos con su prefijo lo vuelve
        /// a intentar sin prefijo: deja errores en "Codigo" o "Nombre" que ningún campo muestra. Se conservan solo los del formulario enviado.
        /// </summary>
        private void ValidarSoloElFormulario(string prefijo)
        {
            foreach (var clave in ModelState.Keys.Where(k => k.Length > 0 && !k.StartsWith(prefijo + ".", StringComparison.Ordinal)).ToList())
                ModelState.Remove(clave);
        }

        private void Errores(string prefijo, IReadOnlyList<ErrorValidacion> errores)
        {
            foreach (var e in errores)
                ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : $"{prefijo}.{e.Campo}", e.Mensaje);
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
            Tipos = await _db.TiposConstancia.AsNoTracking()
                .Where(t => t.IdEmpresa == idEmpresa)
                .OrderByDescending(t => t.Activo).ThenBy(t => t.Codigo)
                .Select(t => new FilaTipo(t.IdTipoConstancia, t.Codigo, t.Nombre, t.Plantilla, t.Activo,
                    _db.Constancias.Count(c => c.IdTipoConstancia == t.IdTipoConstancia)))
                .ToListAsync();
        }
    }
}
