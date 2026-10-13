namespace eGestion360Web.Models.Plataforma
{
    /// <summary>
    /// Entidad que guarda datos de un tenant: la fila lleva <c>id_tenant</c> y nunca cambia de tenant (ADR-001). Lo asigna
    /// <see cref="Services.Tenancy.TenantAlGuardarInterceptor"/> al insertar. Desde F1.3 los filtros globales de EF y desde
    /// F1.4 Row-Level Security se apoyan en esta columna.
    /// </summary>
    public interface ITenantEntity
    {
        int IdTenant { get; set; }
    }

    /// <summary>
    /// Entidad de una tabla mixta (ADR-015): <c>id_tenant</c> nulo es una fila global (por ejemplo, una tasa de cambio oficial
    /// del BCH) y con valor es una fila propia de un tenant, deducido de su empresa.
    /// </summary>
    public interface ITenantOpcionalEntity
    {
        int? IdTenant { get; set; }
    }
}
