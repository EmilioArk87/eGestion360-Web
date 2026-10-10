using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Data.Configuracion
{
    // Persona maestra (scripts 014 a 019): persona, documentos, vínculos con las empresas y ficha de empleado.

    public sealed class PersonaConfiguracion : IEntityTypeConfiguration<Persona>
    {
        public void Configure(EntityTypeBuilder<Persona> entity)
        {
            // Persona maestra (script 014). Todas las referencias son opcionales.
            entity.HasOne(p => p.Nacionalidad)
                  .WithMany()
                  .HasForeignKey(p => p.PaisNacionalidad)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(p => p.MunicipioNacimiento)
                  .WithMany()
                  .HasForeignKey(p => p.IdMunicipioNacimiento)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(p => p.MunicipioResidencia)
                  .WithMany()
                  .HasForeignKey(p => p.IdMunicipioResidencia)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(p => p.TipoLicencia)
                  .WithMany()
                  .HasForeignKey(p => p.LicenciaTipo)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(p => p.PersonaPrincipal)
                  .WithMany()
                  .HasForeignKey(p => p.IdPersonaPrincipal)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        }
    }

    public sealed class PersonaDocumentoConfiguracion : IEntityTypeConfiguration<PersonaDocumento>
    {
        public void Configure(EntityTypeBuilder<PersonaDocumento> entity)
        {
            entity.HasOne(d => d.Persona)
                  .WithMany(p => p.Documentos)
                  .HasForeignKey(d => d.IdPersona)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Tipo)
                  .WithMany()
                  .HasForeignKey(d => d.TipoDocumento)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class PersonaEmpresaConfiguracion : IEntityTypeConfiguration<PersonaEmpresa>
    {
        public void Configure(EntityTypeBuilder<PersonaEmpresa> entity)
        {
            entity.HasOne(v => v.Empresa)
                  .WithMany()
                  .HasForeignKey(v => v.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(v => v.Persona)
                  .WithMany(p => p.Vinculos)
                  .HasForeignKey(v => v.IdPersona)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public sealed class EmpleadoConfiguracion : IEntityTypeConfiguration<Empleado>
    {
        public void Configure(EntityTypeBuilder<Empleado> entity)
        {
            entity.Property(e => e.TarifaDiaria).HasPrecision(18, 2);

            // La BD tiene una clave foránea compuesta (id_persona_empresa, id_empresa, tipo_vinculo)
            // que garantiza la misma empresa y el tipo 'empleado'. A EF le basta la parte simple.
            entity.HasOne(e => e.Vinculo)
                  .WithOne(v => v.Empleado)
                  .HasForeignKey<Empleado>(e => e.IdPersonaEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
