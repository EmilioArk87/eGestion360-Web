namespace eGestion360Web.Models.Flota
{
    public class KpiResumenVehiculo
    {
        public int IdVehiculo { get; set; }
        public string Placa { get; set; } = string.Empty;
        public string? NombreRuta { get; set; }
        public decimal KmTotal { get; set; }
        public decimal CostoCombustible { get; set; }
        public decimal LitrosCombustible { get; set; }

        public decimal LitrosPorKm =>
            KmTotal > 0 ? Math.Round(LitrosCombustible / KmTotal, 4) : 0;

        public decimal CostoRepuestos { get; set; }
        public decimal CostoLlantas { get; set; }
        public decimal CostoSalarios { get; set; }
        public decimal CostoSeguros { get; set; }
        public decimal CostoMantenimiento { get; set; }

        public decimal CostoTotal =>
            CostoCombustible + CostoRepuestos + CostoLlantas + CostoSalarios + CostoSeguros + CostoMantenimiento;

        public decimal LempirasPorKm =>
            KmTotal > 0 ? Math.Round(CostoTotal / KmTotal, 4) : 0;

        public bool TieneActividad => KmTotal > 0 || CostoTotal > 0;
    }
}
