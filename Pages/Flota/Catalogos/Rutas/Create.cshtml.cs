using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Rutas
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Ruta Item { get; set; } = new();

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

            // Verificar código duplicado en la misma empresa
            int idEmpresa = GetIdEmpresa();
            string codigo = Item.Codigo.Trim().ToUpper();
            bool duplicado = await _db.Rutas.AnyAsync(r =>
                r.IdEmpresa == idEmpresa &&
                r.Codigo == codigo &&
                !r.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Codigo", "Ya existe una ruta con ese código.");
                return Page();
            }

            Item.IdEmpresa     = idEmpresa;
            Item.Codigo        = codigo;
            Item.Nombre        = Item.Nombre.Trim();
            Item.Descripcion   = Item.Descripcion?.Trim();
            Item.Activo        = true;
            Item.Eliminado     = false;
            Item.CreadoPor     = HttpContext.Session.GetString("Username") ?? "sistema";
            Item.FechaCreacion = DateTime.UtcNow;

            _db.Rutas.Add(Item);
            await _db.SaveChangesAsync();

            TempData["Mensaje"] = $"Ruta '{Item.Nombre}' creada correctamente.";
            return RedirectToPage("Index");
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
