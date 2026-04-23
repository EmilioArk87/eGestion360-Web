using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.Operacion.SalariosDiarios
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public CreateModel(ApplicationDbContext db) => _db = db;

        [BindProperty] public SalarioDiario Item { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            Item.Fecha = DateOnly.FromDateTime(DateTime.Today);
            Item.Moneda = "HNL";
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

            _db.SalariosDiarios.Add(Item);
            await _db.SaveChangesAsync();
            TempData["Mensaje"] = "Salario diario registrado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task CargarSelectsAsync()
        {
            int id = GetIdEmpresa();
            ViewData["Vehiculos"] = new SelectList(
                await _db.Vehiculos.Where(v => v.IdEmpresa == id && v.Activo).OrderBy(v => v.Placa).ToListAsync(),
                "IdVehiculo", "Placa");
            ViewData["Personas"] = new SelectList(
                await _db.Personas.Where(p => p.IdEmpresa == id && p.Activo).OrderBy(p => p.Apellidos).ToListAsync(),
                "IdPersona", "NombreCompleto");
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
