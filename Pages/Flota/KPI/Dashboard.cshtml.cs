using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Flota.KPI
{
    public class DashboardModel : PageModel
    {
        private readonly KpiService _kpi;

        public DashboardModel(KpiService kpi) => _kpi = kpi;

        [BindProperty(SupportsGet = true)]
        public DateOnly FechaDesde { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateOnly FechaHasta { get; set; }

        public List<KpiResumenVehiculo> Resultados { get; set; } = new();

        public decimal KmTotalFlota       => Resultados.Sum(r => r.KmTotal);
        public decimal CostoTotalFlota    => Resultados.Sum(r => r.CostoTotal);
        public decimal LkmPromedio        => KmTotalFlota > 0 ? Math.Round(CostoTotalFlota / KmTotalFlota, 4) : 0;
        public decimal TotalCombustible   => Resultados.Sum(r => r.CostoCombustible);
        public decimal TotalRepuestos     => Resultados.Sum(r => r.CostoRepuestos);
        public decimal TotalSalarios      => Resultados.Sum(r => r.CostoSalarios);
        public decimal TotalSeguros       => Resultados.Sum(r => r.CostoSeguros);
        public decimal TotalMantenimiento => Resultados.Sum(r => r.CostoMantenimiento);

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            if (FechaDesde == default)
            {
                var hoy = DateOnly.FromDateTime(DateTime.Today);
                FechaDesde = new DateOnly(hoy.Year, hoy.Month, 1);
                FechaHasta = hoy;
            }

            if (FechaHasta < FechaDesde) FechaHasta = FechaDesde;

            int idEmpresa = GetIdEmpresa();
            Resultados = await _kpi.ObtenerKpiAsync(idEmpresa, FechaDesde, FechaHasta);
            return Page();
        }

        private int GetIdEmpresa()
        {
            if (int.TryParse(HttpContext.Session.GetString("EmpresaId"), out int id) && id > 0) return id;
            return 1;
        }
    }
}
