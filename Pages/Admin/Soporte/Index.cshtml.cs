using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.Soporte
{
    /// <summary>
    /// Tenant de soporte del administrador general (F1.4c, ADR-025). Con la política RLS encendida la base solo entrega las filas
    /// del tenant del contexto, así que el administrador general (el operador de la plataforma) elige aquí con cuál tenant
    /// trabaja: desde ese momento las pantallas de datos de un tenant (módulos y roles de sus empresas, personas, usuarios) ven y
    /// guardan solo lo de ese tenant. La elección vive en su sesión; salir de soporte la borra. Cada entrada y salida queda en el
    /// log con quién, a cuál tenant y cuándo. La auditoría persistente, el motivo y el aviso al tenant llegan con la consola del
    /// operador (F1.9).
    /// </summary>
    [RequierePermiso("plataforma.soporte.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ApplicationDbContext context, ILogger<IndexModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>Pantalla a la que volver después de elegir (la que mandó al administrador aquí); solo rutas locales.</summary>
        [BindProperty(SupportsGet = true)]
        public string? Volver { get; set; }

        public IReadOnlyList<FilaTenant> Tenants { get; private set; } = Array.Empty<FilaTenant>();

        public int? TenantDeSoporte { get; private set; }

        public string? CodigoDeSoporte { get; private set; }

        /// <param name="Plan">Código del plan vigente (F1.7); nulo si el tenant no tiene plan.</param>
        /// <param name="ModoSuspension">Qué hace su suspensión (solo importa si está suspendido).</param>
        /// <param name="Derechos">Cuántos derechos concede su plan.</param>
        public sealed record FilaTenant(int IdTenant, string Codigo, string Nombre, string Tipo, string Estado, int Empresas,
            string? Plan = null, string ModoSuspension = ModoDeSuspension.Lectura, int Derechos = 0);

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var bloqueo = Bloqueo();
            if (bloqueo != null) return bloqueo;

            await CargarAsync(ct);
            return Page();
        }

        [RequierePermiso("plataforma.soporte.entrar")]
        public async Task<IActionResult> OnPostEntrarAsync(int idTenant, CancellationToken ct)
        {
            var bloqueo = Bloqueo();
            if (bloqueo != null) return bloqueo;

            var tenant = await _context.Tenants.AsNoTracking()
                .Where(t => t.IdTenant == idTenant && t.Estado != EstadoTenant.Cancelado)
                .Select(t => new { t.IdTenant, t.Codigo, t.Estado })
                .FirstOrDefaultAsync(ct);
            if (tenant == null)
            {
                TempData["SoporteError"] = "El tenant elegido no existe o está cancelado.";
                return RedirectToPage(new { Volver });
            }

            Entrar(tenant.IdTenant, tenant.Codigo, tenant.Estado);
            return RedirigirDespuesDeElegir();
        }

        /// <summary>Desde la lista de empresas: entra al tenant de esa empresa y abre la pantalla pedida para ella.</summary>
        [RequierePermiso("plataforma.soporte.entrar")]
        public async Task<IActionResult> OnPostEntrarEnEmpresaAsync(int idEmpresa, string destino, CancellationToken ct)
        {
            var bloqueo = Bloqueo();
            if (bloqueo != null) return bloqueo;

            var empresa = await _context.Empresas.AsNoTracking()
                .Where(e => e.IdEmpresa == idEmpresa && !e.Eliminado)
                .Select(e => new { e.IdEmpresa, e.IdTenant })
                .FirstOrDefaultAsync(ct);
            var tenant = empresa == null ? null : await _context.Tenants.AsNoTracking()
                .Where(t => t.IdTenant == empresa.IdTenant && t.Estado != EstadoTenant.Cancelado)
                .Select(t => new { t.IdTenant, t.Codigo, t.Estado })
                .FirstOrDefaultAsync(ct);
            if (empresa == null || tenant == null)
            {
                TempData["SoporteError"] = "La empresa no existe o su tenant está cancelado.";
                return RedirectToPage();
            }

            Entrar(tenant.IdTenant, tenant.Codigo, tenant.Estado);

            return destino switch
            {
                "modulos" => RedirectToPage("/Admin/EmpresaModulos/Index", new { empresaId = empresa.IdEmpresa }),
                "roles" => RedirectToPage("/Admin/EmpresaRoles/Index", new { empresaId = empresa.IdEmpresa }),
                _ => RedirectToPage()
            };
        }

        public IActionResult OnPostSalir()
        {
            var bloqueo = Bloqueo();
            if (bloqueo != null) return bloqueo;

            if (AuthHelper.GetTenantDeSoporte(HttpContext) is { } actual)
            {
                _logger.LogInformation("Soporte: el usuario {Usuario} sale del tenant {IdTenant} ({Codigo}).",
                    HttpContext.Session.GetString("Username"), actual, AuthHelper.GetCodigoTenantDeSoporte(HttpContext));
                AuthHelper.SalirDeSoporte(HttpContext);
            }

            TempData["SoporteMensaje"] = "Saliste del tenant de soporte.";
            return RedirectToPage();
        }

        private void Entrar(int idTenant, string codigo, string estado)
        {
            AuthHelper.EntrarEnSoporte(HttpContext, idTenant, codigo);
            _logger.LogInformation("Soporte: el usuario {Usuario} entra al tenant {IdTenant} ({Codigo}, {Estado}).",
                HttpContext.Session.GetString("Username"), idTenant, codigo, estado);
        }

        private IActionResult RedirigirDespuesDeElegir()
        {
            TempData["SoporteMensaje"] = "Trabajas en el tenant elegido.";
            return !string.IsNullOrEmpty(Volver) && Url.IsLocalUrl(Volver) ? LocalRedirect(Volver) : RedirectToPage();
        }

        /// <summary>Solo el administrador general sin empresa en la sesión usa el soporte.</summary>
        private IActionResult? Bloqueo()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext) || AuthHelper.GetEmpresaId(HttpContext) is > 0) return RedirectToPage("/MainMenu");
            return null;
        }

        private async Task CargarAsync(CancellationToken ct)
        {
            TenantDeSoporte = AuthHelper.GetTenantDeSoporte(HttpContext);
            CodigoDeSoporte = AuthHelper.GetCodigoTenantDeSoporte(HttpContext);

            var empresasPorTenant = await _context.Empresas.AsNoTracking()
                .Where(e => !e.Eliminado)
                .GroupBy(e => e.IdTenant)
                .Select(g => new { IdTenant = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(g => g.IdTenant, g => g.Cantidad, ct);

            var tenants = await _context.Tenants.AsNoTracking()
                .Where(t => t.Estado != EstadoTenant.Cancelado)
                .OrderBy(t => t.Codigo)
                .Select(t => new { t.IdTenant, t.Codigo, t.Nombre, t.Tipo, t.Estado, t.ModoSuspension })
                .ToListAsync(ct);

            // Plan vigente de cada tenant y cuántos derechos concede (F1.7; solo lectura: cambiarlos llega con la consola del operador).
            var planes = await (from tp in _context.TenantPlanes.AsNoTracking()
                                where tp.VigenteHasta == null
                                join pv in _context.PlanVersiones.AsNoTracking() on tp.IdPlanVersion equals pv.IdPlanVersion
                                join p in _context.Planes.AsNoTracking() on pv.IdPlan equals p.IdPlan
                                select new { tp.IdTenant, p.Codigo, tp.IdPlanVersion }).ToListAsync(ct);
            var derechosPorVersion = await _context.PlanVersionDerechos.AsNoTracking()
                .GroupBy(d => d.IdPlanVersion)
                .Select(g => new { IdPlanVersion = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(g => g.IdPlanVersion, g => g.Cantidad, ct);

            Tenants = tenants
                .Select(t =>
                {
                    var plan = planes.FirstOrDefault(p => p.IdTenant == t.IdTenant);
                    return new FilaTenant(t.IdTenant, t.Codigo, t.Nombre, t.Tipo, t.Estado,
                        empresasPorTenant.TryGetValue(t.IdTenant, out var n) ? n : 0,
                        plan?.Codigo, t.ModoSuspension,
                        plan != null && derechosPorVersion.TryGetValue(plan.IdPlanVersion, out var d) ? d : 0);
                })
                .ToList();
        }
    }
}
