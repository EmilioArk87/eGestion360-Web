using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Empresas
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Empresa Empresa { get; set; } = new Empresa
        {
            Activa = true,
            PaisIso = "PY",
            MonedaIso = "PYG",
            ZonaHoraria = "America/Guatemala"
        };

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            CargarPaises();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
            {
                return RedirectToPage("/Login");
            }

            if (!AuthHelper.IsAdmin(HttpContext))
            {
                return RedirectToPage("/MainMenu");
            }

            if (!ModelState.IsValid)
            {
                CargarPaises();
                return Page();
            }

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            Empresa.PaisIso = Empresa.PaisIso.ToUpperInvariant();
            Empresa.MonedaIso = Empresa.MonedaIso.ToUpperInvariant();
            Empresa.Eliminado = false;
            Empresa.FechaEliminado = null;
            Empresa.FechaBaja = Empresa.Activa ? null : now;
            Empresa.FechaActivacion = now;
            Empresa.CreadoPor = user;
            Empresa.FechaCreacion = now;
            Empresa.ModificadoPor = null;
            Empresa.FechaModificacion = null;

            _context.Empresas.Add(Empresa);
            await _context.SaveChangesAsync();   // genera Empresa.IdEmpresa

            await SeedCategoriasRepuestoAsync(Empresa.IdEmpresa, user, now);

            TempData["EmpresasMessage"] = "Empresa creada correctamente.";
            return RedirectToPage("Index");
        }

        private static readonly (string Nombre, string Descripcion, bool EsLlanta)[] _categoriasDefault =
        [
            ("Aceites y Fluidos",  "Aceite de motor, aceite de caja, líquido de frenos y dirección hidráulica", false),
            ("Accesorios",         "Repuestos varios y accesorios no clasificados en otras categorías",          false),
            ("Carrocería",         "Paneles, puertas, parabrisas, parachoques y espejos",                       false),
            ("Climatización",      "Compresor de A/C, condensador, evaporador y filtro de habitáculo",          false),
            ("Combustible",        "Bomba de combustible, inyectores, filtro de combustible y depósito",        false),
            ("Correas y Cadenas",  "Correa de distribución, correa serpentina, tensores y cadena de distribución", false),
            ("Dirección",          "Caja de dirección, terminales, barra estabilizadora y columna de dirección", false),
            ("Escape",             "Catalizador, silenciador, tubos de escape y juntas",                        false),
            ("Filtros",            "Filtro de aire, aceite, combustible y habitáculo",                          false),
            ("Frenos",             "Pastillas, discos, tambores, cilindros de freno y líquido de frenos",       false),
            ("Iluminación",        "Faros, luces traseras, bombillas, LEDs y faros antiniebla",                 false),
            ("Motor",              "Piezas y componentes del motor: pistones, cigüeñal, válvulas, juntas y filtros de aceite", false),
            ("Neumáticos",         "Neumáticos, llantas, válvulas y accesorios de rueda",                       true),
            ("Refrigeración",      "Radiador, termostato, bomba de agua, mangueras y líquido refrigerante",     false),
            ("Sistema Eléctrico",  "Batería, alternador, arranque, fusibles, sensores y cableado",              false),
            ("Suspensión",         "Amortiguadores, resortes, rótulas, bujes y brazos de suspensión",           false),
            ("Transmisión",        "Caja de cambios, embrague, diferencial y componentes de la transmisión",    false),
        ];

        private async Task SeedCategoriasRepuestoAsync(int idEmpresa, string creadoPor, DateTime ahora)
        {
            var existentes = await _context.CategoriasRepuesto
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado)
                .Select(c => c.Nombre)
                .ToListAsync();

            var nuevas = _categoriasDefault
                .Where(c => !existentes.Contains(c.Nombre))
                .Select(c => new CategoriaRepuesto
                {
                    IdEmpresa     = idEmpresa,
                    Nombre        = c.Nombre,
                    Descripcion   = c.Descripcion,
                    EsLlanta      = c.EsLlanta,
                    Activo        = true,
                    Eliminado     = false,
                    CreadoPor     = creadoPor,
                    FechaCreacion = ahora,
                });

            _context.CategoriasRepuesto.AddRange(nuevas);
            await _context.SaveChangesAsync();
        }

        private void CargarPaises()
        {
            var paises = _context.Paises
                .Where(p => p.Activo)
                .OrderBy(p => p.Nombre)
                .Select(p => new SelectListItem { Value = p.CodigoIso, Text = $"{p.CodigoIso} - {p.Nombre}" })
                .ToList();
            ViewData["Paises"] = new SelectList(paises, "Value", "Text");

            var monedas = _context.Monedas
                .Where(m => m.Activo)
                .OrderBy(m => m.Nombre)
                .Select(m => new SelectListItem { Value = m.CodigoIso, Text = $"{m.CodigoIso} - {m.Nombre} ({m.Simbolo})" })
                .ToList();
            ViewData["Monedas"] = new SelectList(monedas, "Value", "Text");
        }
    }
}
