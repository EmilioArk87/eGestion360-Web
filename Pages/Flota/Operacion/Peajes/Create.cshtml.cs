using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Operacion.Peajes
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPersonaConsultaService _personal;

        public CreateModel(ApplicationDbContext db, IPersonaConsultaService personal)
        {
            _db = db;
            _personal = personal;
        }

        [BindProperty] public Peaje Item { get; set; } = new();

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

            _db.Peajes.Add(Item);
            await _db.SaveChangesAsync();
            TempData["Mensaje"] = $"Peaje registrado para {Item.Fecha:dd/MM/yyyy}.";
            return RedirectToPage("Index");
        }

        private async Task CargarSelectsAsync()
        {
            int idEmpresa = GetIdEmpresa();
            ViewData["Vehiculos"] = new SelectList(
                await _db.Vehiculos.Where(v => v.IdEmpresa == idEmpresa && v.Activo).OrderBy(v => v.Placa).ToListAsync(),
                "IdVehiculo", "Placa");
            ViewData["Rutas"] = new SelectList(
                await _db.Rutas.Where(r => r.IdEmpresa == idEmpresa && r.Activo).OrderBy(r => r.Nombre).ToListAsync(),
                "IdRuta", "Nombre");
            ViewData["Conductores"] = new SelectList(
                await _personal.PersonalParaSeleccionAsync(idEmpresa, "CONDUCTOR"),
                "IdPersona", "NombreCompleto");
        }

        private int GetIdEmpresa() => AuthHelper.GetEmpresaIdRequerida(HttpContext);
    }
}
