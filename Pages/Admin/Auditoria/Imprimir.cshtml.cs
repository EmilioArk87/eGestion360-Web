using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Pages.Admin.Auditoria
{
    /// <summary>
    /// La auditoría de la empresa con los mismos filtros de <c>Index</c>, en una hoja para imprimir o guardar como PDF desde el navegador
    /// (decisión del 2026-11-21: sin librería de PDF, como las constancias de Psicología). Deja su fila <c>auditoria.exportada</c>.
    /// </summary>
    [RequierePermiso("administracion.auditoria.ver")]
    public class ImprimirModel : PageModel
    {
        private readonly LectorDeAuditoria _lector;
        private readonly ITenantContext _tenant;
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public ImprimirModel(LectorDeAuditoria lector, ITenantContext tenant, IAuditoriaDeSeguridad? auditoria = null)
        {
            _lector = lector;
            _tenant = tenant;
            _auditoria = auditoria;
        }

        [BindProperty(SupportsGet = true)] public DateOnly? Desde { get; set; }
        [BindProperty(SupportsGet = true)] public DateOnly? Hasta { get; set; }
        [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
        [BindProperty(SupportsGet = true)] public string? Resultado { get; set; }
        [BindProperty(SupportsGet = true)] public string? Usuario { get; set; }

        public FiltroDeAuditoria Filtro { get; private set; } = new();
        public ExportacionDeAuditoria Exportacion { get; private set; } = new(Array.Empty<FilaDeAuditoria>(), 0);
        public DateTime GeneradoLocal { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (_tenant.IdTenant is not > 0) return RedirectToPage("Index");

            Filtro = new FiltroDeAuditoria { Desde = Desde, Hasta = Hasta, Tipo = Tipo, Resultado = Resultado, Usuario = Usuario };
            ConsultaDeAuditoria.Normalizar(Filtro, LectorDeAuditoria.Hoy());

            var idTenant = _tenant.IdTenant.Value;
            Exportacion = await _lector.ExportarDelTenantAsync(idTenant, Filtro, ct);
            GeneradoLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LectorDeAuditoria.Zona);

            if (_auditoria != null)
                await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.AuditoriaExportada)
                {
                    IdTenant = idTenant,
                    ObjetoTipo = "auditoria_seguridad",
                    Detalle = ConsultaDeAuditoria.DetalleDeExportacion("pdf", Exportacion.Filas.Count, Exportacion.Recortada, Filtro),
                }, ct);

            return Page();
        }
    }
}
