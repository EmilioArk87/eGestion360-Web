using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Pages.Admin.Auditoria
{
    /// <summary>
    /// «Auditoría» de la empresa (F1.8c, ADR-030 D74): lo que pasó dentro del tenant de la petición (cambios de administración,
    /// invitaciones, accesos denegados, entradas a un sitio y el soporte recibido), de solo lectura. La ve solo el administrador de la
    /// empresa (el permiso no se puede dar a un rol) y el operador dentro del tenant que eligió en Soporte. Exporta a Excel; el PDF sale de
    /// <c>Imprimir</c>, que el navegador guarda como PDF.
    /// </summary>
    [RequierePermiso("administracion.auditoria.ver")]
    public class IndexModel : PageModel
    {
        private readonly LectorDeAuditoria _lector;
        private readonly ITenantContext _tenant;
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public IndexModel(LectorDeAuditoria lector, ITenantContext tenant, IAuditoriaDeSeguridad? auditoria = null)
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
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        /// <summary>La petición no tiene tenant (el operador aún no eligió uno en Soporte): no se muestra nada.</summary>
        public bool SinTenant { get; private set; }

        public PaginaDeAuditoria Resultados { get; private set; } = new(Array.Empty<FilaDeAuditoria>(), 0, 1, 0);

        public IReadOnlyList<string> Tipos { get; } = LectorDeAuditoria.TiposDe(plataforma: false);

        public FiltroDeAuditoria Filtro() => new() { Desde = Desde, Hasta = Hasta, Tipo = Tipo, Resultado = Resultado, Usuario = Usuario };

        /// <summary>Los filtros normalizados, de vuelta en las propiedades (así el formulario y los enlaces muestran lo que se aplicó).</summary>
        private FiltroDeAuditoria Normalizado()
        {
            var filtro = Filtro();
            ConsultaDeAuditoria.Normalizar(filtro, LectorDeAuditoria.Hoy());
            (Desde, Hasta, Tipo, Resultado, Usuario) = (filtro.Desde, filtro.Hasta, filtro.Tipo, filtro.Resultado, filtro.Usuario);
            return filtro;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var filtro = Normalizado();
            if (_tenant.IdTenant is not > 0)
            {
                SinTenant = true;
                return Page();
            }

            Resultados = await _lector.PaginaDelTenantAsync(_tenant.IdTenant.Value, filtro, Pagina, ct);
            Pagina = Resultados.Pagina;
            return Page();
        }

        public async Task<IActionResult> OnGetExcelAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var filtro = Normalizado();
            if (_tenant.IdTenant is not > 0) return RedirectToPage("Index");

            var idTenant = _tenant.IdTenant.Value;
            var exportacion = await _lector.ExportarDelTenantAsync(idTenant, filtro, ct);
            if (_auditoria != null)
                await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.AuditoriaExportada)
                {
                    IdTenant = idTenant,
                    ObjetoTipo = "auditoria_seguridad",
                    Detalle = ConsultaDeAuditoria.DetalleDeExportacion("excel", exportacion.Filas.Count, exportacion.Recortada, filtro),
                }, ct);

            var hoy = LectorDeAuditoria.Hoy();
            var libro = ConsultaDeAuditoria.Excel(exportacion.Filas, $"Auditoría de la empresa · {filtro.Desde ?? hoy:yyyy-MM-dd} a {filtro.Hasta ?? hoy:yyyy-MM-dd}", conTenant: false);
            return File(libro, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ConsultaDeAuditoria.NombreDeArchivo("auditoria", filtro, hoy));
        }
    }
}
