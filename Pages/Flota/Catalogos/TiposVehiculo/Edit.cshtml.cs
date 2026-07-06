using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Catalogos.TiposVehiculo
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db) => _db = db;

        [BindProperty]
        public TipoVehiculo Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var tipo = await _db.TiposVehiculo.FindAsync(id);
            if (tipo is null || tipo.IdEmpresa != GetIdEmpresa() || tipo.Eliminado)
                return NotFound();

            Item = tipo;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            ModelState.Remove("Item.TokenConcurrencia");
            ModelState.Remove("Item.CreadoPor");
            if (!ModelState.IsValid) return Page();

            var existente = await _db.TiposVehiculo.FindAsync(Item.IdTipoVehiculo);
            if (existente is null || existente.IdEmpresa != GetIdEmpresa() || existente.Eliminado)
                return NotFound();

            // Verificar código duplicado (excluyendo el registro actual)
            string codigo = Item.Codigo.Trim().ToUpper();
            bool duplicado = await _db.TiposVehiculo.AnyAsync(t =>
                t.IdEmpresa == existente.IdEmpresa &&
                t.Codigo == codigo &&
                t.IdTipoVehiculo != existente.IdTipoVehiculo &&
                !t.Eliminado);

            if (duplicado)
            {
                ModelState.AddModelError("Item.Codigo", "Ya existe un tipo de vehículo con ese código.");
                return Page();
            }

            existente.Codigo             = codigo;
            existente.Nombre             = Item.Nombre.Trim();
            existente.Descripcion        = Item.Descripcion?.Trim();
            existente.Activo             = Item.Activo;
            existente.ModificadoPor      = HttpContext.Session.GetString("Username") ?? "sistema";
            existente.FechaModificacion  = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
                TempData["Mensaje"] = $"Tipo de vehículo '{existente.Nombre}' actualizado correctamente.";
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
