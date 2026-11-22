using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.AuditoriaPlataforma
{
    /// <summary>
    /// «Auditoría de la plataforma» (F1.8c, ADR-030 D74): entradas, fallos, bloqueos, segundo factor, claves, invitaciones aceptadas,
    /// soporte y acciones del operador sobre todos los tenants (<c>saas.auditoria_plataforma</c>, sin RLS), de solo lectura. Solo para el
    /// personal de la plataforma. Exporta a Excel; el PDF sale de <c>Imprimir</c>, que el navegador guarda como PDF.
    /// </summary>
    [RequierePermiso("plataforma.auditoria.ver")]
    public class IndexModel : PageModel
    {
        private readonly LectorDeAuditoria _lector;
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public IndexModel(LectorDeAuditoria lector, IAuditoriaDeSeguridad? auditoria = null)
        {
            _lector = lector;
            _auditoria = auditoria;
        }

        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
        [BindProperty(SupportsGet = true)] public string? Resultado { get; set; }
        [BindProperty(SupportsGet = true)] public string? Usuario { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdTenant { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public PaginaDeAuditoria Resultados { get; private set; } = new(Array.Empty<FilaDeAuditoria>(), 0, 1, 0);
        public IReadOnlyDictionary<int, string> Tenants { get; private set; } = new Dictionary<int, string>();
        public IReadOnlyList<string> Tipos { get; } = LectorDeAuditoria.TiposDe(plataforma: true);

        private FiltroDeAuditoria Normalizado()
        {
            var filtro = new FiltroDeAuditoria { Desde = Desde, Hasta = Hasta, Tipo = Tipo, Resultado = Resultado, Usuario = Usuario, IdTenant = IdTenant };
            ConsultaDeAuditoria.Normalizar(filtro, LectorDeAuditoria.Hoy());
            (Desde, Hasta, Tipo, Resultado, Usuario, IdTenant) = (filtro.Desde, filtro.Hasta, filtro.Tipo, filtro.Resultado, filtro.Usuario, filtro.IdTenant);
            return filtro;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var filtro = Normalizado();
            Tenants = await _lector.TenantsAsync(ct);
            Resultados = await _lector.PaginaDeLaPlataformaAsync(filtro, Pagina, ct);
            Pagina = Resultados.Pagina;
            return Page();
        }

        public async Task<IActionResult> OnGetExcelAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var filtro = Normalizado();
            var exportacion = await _lector.ExportarDeLaPlataformaAsync(filtro, ct);
            await _auditoria.RegistrarAccionDeLaPlataformaAsync(TiposDeEvento.AuditoriaPlataformaExportada, "auditoria_plataforma", null,
                ConsultaDeAuditoria.DetalleDeExportacion("excel", exportacion.Filas.Count, exportacion.Recortada, filtro), ct: ct);

            var hoy = LectorDeAuditoria.Hoy();
            var libro = ConsultaDeAuditoria.Excel(exportacion.Filas, $"Auditoría de la plataforma · {filtro.Desde ?? hoy:yyyy-MM-dd} a {filtro.Hasta ?? hoy:yyyy-MM-dd}", conTenant: true);
            return File(libro, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ConsultaDeAuditoria.NombreDeArchivo("auditoria_plataforma", filtro, hoy));
        }
    }
}
