namespace eGestion360Web.Models.Flota
{
    public class KpiResumenVehiculo
    {
        public int IdVehiculo { get; set; }
        public string Placa { get; set; } = string.Empty;
        public string? NombreRuta { get; set; }
        public decimal KmTotal { get; set; }
        public decimal CostoCombustible { get; set; }
        public decimal CostoRepuestos { get; set; }
        public decimal CostoSalarios { get; set; }
        public decimal CostoSeguros { get; set; }
        public decimal CostoMantenimiento { get; set; }

        public decimal CostoTotal =>
            CostoCombustible + CostoRepuestos + CostoSalarios + CostoSeguros + CostoMantenimiento;

        public decimal LempirasPorKm =>
            KmTotal > 0 ? Math.Round(CostoTotal / KmTotal, 4) : 0;

        public bool TieneActividad => KmTotal > 0 || CostoTotal > 0;
    }
}
