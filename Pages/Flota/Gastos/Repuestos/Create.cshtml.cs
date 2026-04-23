using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Gastos.Repuestos
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty] public GastoRepuesto Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            Item.Fecha = DateOnly.FromDateTime(DateTime.Today);
            Item.Moneda = "HNL";
            Item.Cantidad = 1;
            await CargarSelectsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            ModelState.Remove("Item.TokenConcurrencia");
            if (!ModelState.IsValid) { await CargarSelectsAsync(); return Page(); }
            Item.IdEmpresa = GetIdEmpresa();
            Item.CreadoPor = HttpContext.Session.GetString("Username") ?? "sistema";
            Item.FechaCreacion = DateTime.UtcNow;
            _db.GastosRepuesto.Add(Item);
            await _db.SaveChangesAsync();
            TempData["Mensaje"] = "Gasto de repuesto registrado.";
            return RedirectToPage("Index");
        }

        private async Task CargarSelectsAsync()
        {
            int id = GetIdEmpresa();
            ViewData["Vehiculos"] = new SelectList(await _db.Vehiculos.Where(v => v.IdEmpresa == id && v.Activo).OrderBy(v => v.Placa).ToListAsync(), "IdVehiculo", "Placa");
            ViewData["Categorias"] = new SelectList(await _db.CategoriasRepuesto.Where(c => c.IdEmpresa == id && c.Activo).OrderBy(c => c.Nombre).ToListAsync(), "IdCategoriaRepuesto", "Nombre");
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
