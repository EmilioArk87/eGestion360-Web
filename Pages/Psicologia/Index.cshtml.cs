using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Psicologia
{
    [PaginaSinPermiso("Centro de navegación del módulo: cada opción pide su propio permiso")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public int Pacientes { get; private set; }
        public int Jornadas { get; private set; }
        public int AtencionesEnBorrador { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Pacientes = await _db.Pacientes.CountAsync(p => p.IdEmpresa == idEmpresa && !p.Eliminado);
            Jornadas = await _db.JornadasPsicologia.CountAsync(j => j.IdEmpresa == idEmpresa && j.Estado != EstadosJornada.Anulada);
            AtencionesEnBorrador = await _db.AtencionesPsicologia.CountAsync(a => a.IdEmpresa == idEmpresa && a.Estado == EstadosAtencion.Borrador);
            return Page();
        }
    }
}
