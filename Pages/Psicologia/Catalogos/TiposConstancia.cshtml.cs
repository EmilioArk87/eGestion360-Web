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
    /// <summary>Tipos de constancia que extiende el centro (D14), cada uno con su numeración y su formato, de la empresa de la sesión.</summary>
    [RequierePermiso("psicologia.catalogos.ver")]
    public class TiposConstanciaModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ICatalogoPsicologiaService _catalogo;
        private readonly IConstanciaService _constancias;
        private readonly TimeProvider _tiempo;

        public TiposConstanciaModel(ApplicationDbContext db, ICatalogoPsicologiaService catalogo, IConstanciaService constancias, TimeProvider tiempo)
        {
            _db = db;
            _catalogo = catalogo;
            _constancias = constancias;
            _tiempo = tiempo;
        }

        public sealed class FormularioTipo
        {
            public int? Id { get; set; }

            [Required(ErrorMessage = "Escribe el código.")]
            [StringLength(30, ErrorMessage = "El código no puede pasar de 30 caracteres.")]
            public string? Codigo { get; set; }

            [Required(ErrorMessage = "Escribe el nombre.")]
            [StringLength(150, ErrorMessage = "El nombre no puede pasar de 150 caracteres.")]
            public string? Nombre { get; set; }

            public string? Plantilla { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "El folio debe ser un número positivo.")]
            public int? FolioDesde { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "El folio debe ser un número positivo.")]
            public int? FolioHasta { get; set; }
        }

        [BindProperty] public FormularioTipo Tipo { get; set; } = new();

        public sealed record FilaTipo(int Id, string Codigo, string Nombre, string? Plantilla, bool Activo, int Usos, int? FolioDesde, int? FolioHasta)
        {
            public string Formato => PlantillasConstancia.Buscar(Plantilla)?.Nombre ?? "por recibir";
        }

        /// <summary>El siguiente número de cada tipo activo (folio del talonario o correlativo).</summary>
        public IReadOnlyDictionary<int, string> Siguientes { get; private set; } = new Dictionary<int, string>();

        public List<FilaTipo> Tipos { get; private set; } = new();
        public bool PuedeEditar => this.Puede("psicologia.catalogos.editar");

        public async Task<IActionResult> OnGetAsync(int? editar)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarAsync();

            if (editar is { } id && Tipos.FirstOrDefault(t => t.Id == id) is { } t)
                Tipo = new FormularioTipo
                {
                    Id = t.Id, Codigo = t.Codigo, Nombre = t.Nombre, Plantilla = t.Plantilla, FolioDesde = t.FolioDesde, FolioHasta = t.FolioHasta
                };
            return Page();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostGuardarAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!ModelState.IsValid) { await CargarAsync(); return Page(); }

            var r = await _catalogo.GuardarTipoConstanciaAsync(new ElementoCatalogoInput
            {
                IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0, Id = Tipo.Id, Codigo = Tipo.Codigo, Nombre = Tipo.Nombre,
                Plantilla = Tipo.Plantilla, FolioDesde = Tipo.FolioDesde, FolioHasta = Tipo.FolioHasta, Usuario = Usuario()
            });
            if (!r.Ok) { Errores(r.Errores); await CargarAsync(); return Page(); }

            TempData["CatalogosMensaje"] = Tipo.Id > 0 ? "Tipo de constancia actualizado." : "Tipo de constancia agregado.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.catalogos.editar")]
        public async Task<IActionResult> OnPostEstadoAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _catalogo.CambiarEstadoTipoConstanciaAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, activo, Usuario());
            TempData["CatalogosMensaje"] = r.Ok ? (activo ? "Tipo de constancia activado." : "Tipo de constancia desactivado: ya no se ofrece al emitir.") : r.Errores[0].Mensaje;
            return RedirectToPage();
        }

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        private void Errores(IReadOnlyList<ErrorValidacion> errores)
        {
            foreach (var e in errores)
                ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : $"{nameof(Tipo)}.{e.Campo}", e.Mensaje);
        }

        private async Task CargarAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Tipos = await _db.TiposConstancia.AsNoTracking()
                .Where(t => t.IdEmpresa == idEmpresa)
                .OrderByDescending(t => t.Activo).ThenBy(t => t.Codigo)
                .Select(t => new FilaTipo(t.IdTipoConstancia, t.Codigo, t.Nombre, t.Plantilla, t.Activo,
                    _db.Constancias.Count(c => c.IdTipoConstancia == t.IdTipoConstancia), t.FolioDesde, t.FolioHasta))
                .ToListAsync();
            Siguientes = await _constancias.SiguientesNumerosAsync(idEmpresa, DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6)).Year);
        }
    }
}
