using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.Talleres
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public Taller Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var taller = await _db.Talleres.FindAsync(id);
            if (taller is null || taller.IdEmpresa != GetIdEmpresa() || taller.Eliminado)
                return NotFound();

            Item = taller;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            ModelState.Remove("Item.CreadoPor");
            if (!ModelState.IsValid) return Page();

            var existente = await _db.Talleres.FindAsync(Item.IdTaller);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa() || existente.Eliminado)
                return NotFound();

            // Verificar código duplicado (excluyendo el registro actual)
            string codigo = Item.Codigo.Trim().ToUpper();
            bool duplicado = await _db.Talleres.AnyAsync(t =>
                t.IdEmpresa == existente.IdEmpresa &&
                t.Codigo == codigo &&
                t.IdTaller != existente.IdTaller &&
                !t.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Codigo", "Ya existe un taller con ese código.");
                return Page();
            }

            existente.Codigo             = codigo;
            existente.Nombre             = Item.Nombre.Trim();
            existente.Rtn                = Item.Rtn?.Trim();
            existente.Direccion          = Item.Direccion?.Trim();
            existente.Telefono           = Item.Telefono?.Trim();
            existente.Email              = Item.Email?.Trim();
            existente.Contacto           = Item.Contacto?.Trim();
            existente.Activo             = Item.Activo;
            existente.ModificadoPor      = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion  = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Taller '{existente.Nombre}' actualizado correctamente.";
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
