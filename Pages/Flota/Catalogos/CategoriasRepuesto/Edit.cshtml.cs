using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.CategoriasRepuesto
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public CategoriaRepuesto Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var categoria = await _db.CategoriasRepuesto.FindAsync(id);
            if (categoria is null || categoria.IdEmpresa != GetIdEmpresa() || categoria.Eliminado)
                return NotFound();

            Item = categoria;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            ModelState.Remove("Item.CreadoPor");
            if (!ModelState.IsValid) return Page();

            var existente = await _db.CategoriasRepuesto.FindAsync(Item.IdCategoriaRepuesto);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa() || existente.Eliminado)
                return NotFound();

            // Verificar nombre duplicado (excluyendo el registro actual)
            bool duplicado = await _db.CategoriasRepuesto.AnyAsync(c =>
                c.IdEmpresa == existente.IdEmpresa &&
                c.Nombre == Item.Nombre.Trim() &&
                c.IdCategoriaRepuesto != existente.IdCategoriaRepuesto &&
                !c.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Nombre", "Ya existe una categoría con ese nombre.");
                return Page();
            }

            existente.Nombre             = Item.Nombre.Trim();
            existente.Descripcion        = Item.Descripcion?.Trim();
            existente.EsLlanta           = Item.EsLlanta;
            existente.Activo             = Item.Activo;
            existente.ModificadoPor      = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion  = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Categoría '{existente.Nombre}' actualizada correctamente.";
                return RedirectToPage("Index");
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty,
                    "El registro fue modificado por otro usuario. Recargue la página e intente de nuevo.");
                return Page();
            }
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
