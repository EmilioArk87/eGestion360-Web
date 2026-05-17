using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.CategoriasRepuesto
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public CategoriaRepuesto Item { get; set; } = new();

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            if (!ModelState.IsValid) return Page();

            // Verificar nombre duplicado en la misma empresa
            int idEmpresa = GetIdEmpresa();
            bool duplicado = await _db.CategoriasRepuesto.AnyAsync(c =>
                c.IdEmpresa == idEmpresa &&
                c.Nombre == Item.Nombre.Trim() &&
                !c.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Nombre", "Ya existe una categoría con ese nombre.");
                return Page();
            }

            Item.IdEmpresa     = idEmpresa;
            Item.Nombre        = Item.Nombre.Trim();
            Item.Descripcion   = Item.Descripcion?.Trim();
            Item.Activo        = true;
            Item.Eliminado     = false;
            Item.CreadoPor     = HttpContext.Session.GetString("Username") ?? "sistema";
            Item.FechaCreacion = DateTime.UtcNow;

            _db.CategoriasRepuesto.Add(Item);
            await _db.SaveChangesAsync();

            TempData["Mensaje"] = $"Categoría '{Item.Nombre}' creada correctamente.";
            return RedirectToPage("Index");
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
