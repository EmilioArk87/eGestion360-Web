using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models.Psicologia;

namespace eGestion360Web.Data.Configuracion
{
    // Módulo de Psicología (script 042, esquema psi). En la BD las claves foráneas son compuestas por tenant y por empresa
    // (una atención no puede apuntar a un paciente de otra empresa); a EF le basta la parte simple, como en Empleado.
    // Los índices únicos repiten los de la BD para que las pruebas con SQLite se comporten igual.

    public sealed class JornadaConfiguracion : IEntityTypeConfiguration<Jornada>
    {
        public void Configure(EntityTypeBuilder<Jornada> entity)
        {
            entity.HasOne(j => j.Empresa)
                  .WithMany()
                  .HasForeignKey(j => j.IdEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(j => j.Responsable)
                  .WithMany()
                  .HasForeignKey(j => j.IdPersonaResponsable)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasIndex(j => new { j.IdEmpresa, j.FechaInicio }).HasDatabaseName("IX_jornadas_empresa_fecha");
        }
    }

    public sealed class PacienteConfiguracion : IEntityTypeConfiguration<Paciente>
    {
        public void Configure(EntityTypeBuilder<Paciente> entity)
        {
            // Un vínculo puede tener fichas eliminadas en su historial, pero una sola vigente (UX_pacientes_vinculo).
            entity.HasOne(p => p.Vinculo)
                  .WithMany()
                  .HasForeignKey(p => p.IdPersonaEmpresa)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => new { p.IdEmpresa, p.NumeroExpediente }).IsUnique().HasDatabaseName("UX_pacientes_expediente");
            entity.HasIndex(p => p.IdPersonaEmpresa).IsUnique().HasFilter("[eliminado] = 0").HasDatabaseName("UX_pacientes_vinculo");
        }
    }

    public sealed class AtencionConfiguracion : IEntityTypeConfiguration<Atencion>
    {
        public void Configure(EntityTypeBuilder<Atencion> entity)
        {
            entity.HasOne(a => a.Paciente)
                  .WithMany(p => p.Atenciones)
                  .HasForeignKey(a => a.IdPaciente)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Jornada)
                  .WithMany()
                  .HasForeignKey(a => a.IdJornada)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(a => a.Profesional)
                  .WithMany()
                  .HasForeignKey(a => a.IdPersonaProfesional)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            // Una sola atención vigente por paciente en cada jornada: reimportar la brigada no la duplica (decisión D7).
            entity.HasIndex(a => new { a.IdJornada, a.IdPaciente }).IsUnique()
                  .HasFilter("[id_jornada] IS NOT NULL AND [estado] <> 'anulada'")
                  .HasDatabaseName("UX_atenciones_jornada_paciente");
            entity.HasIndex(a => new { a.IdPaciente, a.FechaAtencion }).HasDatabaseName("IX_atenciones_paciente_fecha");
        }
    }

    public sealed class ImpresionConfiguracion : IEntityTypeConfiguration<Impresion>
    {
        public void Configure(EntityTypeBuilder<Impresion> entity)
        {
            // Lo impreso es una atención o una constancia (CK_impresiones_documento exige exactamente una).
            entity.HasOne(i => i.Atencion)
                  .WithMany()
                  .HasForeignKey(i => i.IdAtencion)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(i => i.Constancia)
                  .WithMany()
                  .HasForeignKey(i => i.IdConstancia)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasIndex(i => new { i.IdAtencion, i.FechaCreacion }).HasDatabaseName("IX_impresiones_atencion");
            entity.HasIndex(i => new { i.IdConstancia, i.FechaCreacion }).HasDatabaseName("IX_impresiones_constancia");
        }
    }

    public sealed class PruebaConfiguracion : IEntityTypeConfiguration<Prueba>
    {
        public void Configure(EntityTypeBuilder<Prueba> entity)
        {
            entity.HasIndex(p => new { p.IdEmpresa, p.Codigo }).IsUnique().HasDatabaseName("UX_pruebas_codigo");
        }
    }

    public sealed class AtencionPruebaConfiguracion : IEntityTypeConfiguration<AtencionPrueba>
    {
        public void Configure(EntityTypeBuilder<AtencionPrueba> entity)
        {
            entity.HasOne(x => x.Atencion)
                  .WithMany(a => a.Pruebas)
                  .HasForeignKey(x => x.IdAtencion)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Prueba)
                  .WithMany()
                  .HasForeignKey(x => x.IdPrueba)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.IdAtencion, x.IdPrueba }).IsUnique().HasDatabaseName("UX_atencion_pruebas_atencion_prueba");
        }
    }

    public sealed class TipoConstanciaConfiguracion : IEntityTypeConfiguration<TipoConstancia>
    {
        public void Configure(EntityTypeBuilder<TipoConstancia> entity)
        {
            entity.HasIndex(t => new { t.IdEmpresa, t.Codigo }).IsUnique().HasDatabaseName("UX_tipos_constancia_codigo");
        }
    }

    public sealed class ConstanciaConfiguracion : IEntityTypeConfiguration<Constancia>
    {
        public void Configure(EntityTypeBuilder<Constancia> entity)
        {
            entity.HasOne(c => c.Paciente)
                  .WithMany()
                  .HasForeignKey(c => c.IdPaciente)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Atencion)
                  .WithMany()
                  .HasForeignKey(c => c.IdAtencion)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasOne(c => c.Tipo)
                  .WithMany()
                  .HasForeignKey(c => c.IdTipoConstancia)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Profesional)
                  .WithMany()
                  .HasForeignKey(c => c.IdPersonaProfesional)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            // El correlativo no se repite en la empresa, tampoco el de una constancia anulada (decisión D13).
            entity.HasIndex(c => new { c.IdEmpresa, c.NumeroConstancia }).IsUnique().HasDatabaseName("UX_constancias_numero");
            entity.HasIndex(c => new { c.IdPaciente, c.FechaEmision }).HasDatabaseName("IX_constancias_paciente_fecha");
        }
    }

    public sealed class ConstanciaPruebaConfiguracion : IEntityTypeConfiguration<ConstanciaPrueba>
    {
        public void Configure(EntityTypeBuilder<ConstanciaPrueba> entity)
        {
            entity.HasOne(x => x.Constancia)
                  .WithMany(c => c.Pruebas)
                  .HasForeignKey(x => x.IdConstancia)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Prueba)
                  .WithMany()
                  .HasForeignKey(x => x.IdPrueba)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.IdConstancia, x.IdPrueba }).IsUnique().HasDatabaseName("UX_constancia_pruebas_constancia_prueba");
        }
    }

    public sealed class ImportacionConfiguracion : IEntityTypeConfiguration<Importacion>
    {
        public void Configure(EntityTypeBuilder<Importacion> entity)
        {
            entity.HasOne(i => i.Jornada)
                  .WithMany()
                  .HasForeignKey(i => i.IdJornada)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            // El mismo archivo no se confirma dos veces en la misma empresa.
            entity.HasIndex(i => new { i.IdEmpresa, i.HashSha256 }).IsUnique()
                  .HasFilter("[estado] = 'confirmada'")
                  .HasDatabaseName("UX_importaciones_hash");
        }
    }

    public sealed class ImportacionFilaConfiguracion : IEntityTypeConfiguration<ImportacionFila>
    {
        public void Configure(EntityTypeBuilder<ImportacionFila> entity)
        {
            entity.HasOne(f => f.Importacion)
                  .WithMany(i => i.Filas)
                  .HasForeignKey(f => f.IdImportacion)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Paciente)
                  .WithMany()
                  .HasForeignKey(f => f.IdPaciente)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);

            entity.HasIndex(f => new { f.IdImportacion, f.NumeroFila }).IsUnique().HasDatabaseName("UX_importacion_filas_fila");
        }
    }

    public sealed class ProfesionalConfiguracion : IEntityTypeConfiguration<Profesional>
    {
        public void Configure(EntityTypeBuilder<Profesional> entity)
        {
            entity.HasOne(p => p.Persona)
                  .WithMany()
                  .HasForeignKey(p => p.IdPersona)
                  .OnDelete(DeleteBehavior.Restrict);

            // Una persona es profesional una sola vez por empresa; la colegiación no se repite (los administradores pueden no tenerla).
            entity.HasIndex(p => new { p.IdEmpresa, p.IdPersona }).IsUnique().HasDatabaseName("UX_profesionales_persona");
            entity.HasIndex(p => new { p.IdEmpresa, p.NumeroColegiacion }).IsUnique()
                  .HasFilter("[numero_colegiacion] IS NOT NULL").HasDatabaseName("UX_profesionales_colegiacion");
        }
    }
}
