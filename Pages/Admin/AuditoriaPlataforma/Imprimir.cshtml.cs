using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.AuditoriaPlataforma
{
    /// <summary>
    /// La auditoría de la plataforma con los mismos filtros de <c>Index</c>, en una hoja para imprimir o guardar como PDF desde el
    /// navegador. Deja su fila <c>auditoria.plataforma_exportada</c>.
    /// </summary>
    [RequierePermiso("plataforma.auditoria.ver")]
    public class ImprimirModel : PageModel
    {
        private readonly LectorDeAuditoria _lector;
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public ImprimirModel(LectorDeAuditoria lector, IAuditoriaDeSeguridad? auditoria = null)
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

        public FiltroDeAuditoria Filtro { get; private set; } = new();
        public ExportacionDeAuditoria Exportacion { get; private set; } = new(Array.Empty<FilaDeAuditoria>(), 0);
        public string? CodigoDelTenant { get; private set; }
        public DateTime GeneradoLocal { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            Filtro = new FiltroDeAuditoria { Desde = Desde, Hasta = Hasta, Tipo = Tipo, Resultado = Resultado, Usuario = Usuario, IdTenant = IdTenant };
            ConsultaDeAuditoria.Normalizar(Filtro, LectorDeAuditoria.Hoy());

            Exportacion = await _lector.ExportarDeLaPlataformaAsync(Filtro, ct);
            if (Filtro.IdTenant is { } id && (await _lector.TenantsAsync(ct)).TryGetValue(id, out var codigo)) CodigoDelTenant = codigo;
            GeneradoLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LectorDeAuditoria.Zona);

            await _auditoria.RegistrarAccionDeLaPlataformaAsync(TiposDeEvento.AuditoriaPlataformaExportada, "auditoria_plataforma", null,
                ConsultaDeAuditoria.DetalleDeExportacion("pdf", Exportacion.Filas.Count, Exportacion.Recortada, Filtro), ct: ct);

            return Page();
        }
    }
}
