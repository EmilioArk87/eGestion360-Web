using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Rutas
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Ruta Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var ruta = await _db.Rutas.FindAsync(id);
            if (ruta is null || ruta.IdEmpresa != GetIdEmpresa() || ruta.Eliminado)
                return NotFound();

            Item = ruta;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            ModelState.Remove("Item.CreadoPor");
            if (!ModelState.IsValid) return Page();

            var existente = await _db.Rutas.FindAsync(Item.IdRuta);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa() || existente.Eliminado)
                return NotFound();

            // Verificar código duplicado (excluyendo el registro actual)
            string codigo = Item.Codigo.Trim().ToUpper();
            bool duplicado = await _db.Rutas.AnyAsync(r =>
                r.IdEmpresa == existente.IdEmpresa &&
                r.Codigo == codigo &&
                r.IdRuta != existente.IdRuta &&
                !r.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Codigo", "Ya existe una ruta con ese código.");
                return Page();
            }

            existente.Codigo             = codigo;
            existente.Nombre             = Item.Nombre.Trim();
            existente.Descripcion        = Item.Descripcion?.Trim();
            existente.DistanciaKm        = Item.DistanciaKm;
            existente.Activo             = Item.Activo;
            existente.ModificadoPor      = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion  = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Ruta '{existente.Nombre}' actualizada correctamente.";
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
