using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Auditoria;

namespace eGestion360Web.Data.Configuracion
{
    // Bitácora de cambios por campo (script 014).

    public sealed class BitacoraCambioConfiguracion : IEntityTypeConfiguration<BitacoraCambio>
    {
        public void Configure(EntityTypeBuilder<BitacoraCambio> entity)
        {
            // La tabla tiene un disparador INSTEAD OF UPDATE, DELETE. Declararlo hace que EF recupere
            // el id generado sin la cláusula OUTPUT directa, que SQL Server no admite con disparadores.
            entity.ToTable(t => t.HasTrigger("TR_bitacora_cambios_inmutable"));
        }
    }
}
