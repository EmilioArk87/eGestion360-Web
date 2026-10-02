using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>Crea personas completas (persona + vínculo + empleado + documento) directamente en la base de prueba.</summary>
    public static class PersonasDePrueba
    {
        public static Persona Insertar(
            ApplicationDbContext db,
            int idEmpresa,
            string primerNombre,
            string primerApellido,
            string? dni = null,
            string? codigoInterno = null,
            string cargo = "MECANICO",
            DateOnly? nacimiento = null,
            bool vigente = true)
        {
            var ahora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

            var persona = new Persona
            {
                IdEmpresa = idEmpresa,
                Nombres = primerNombre,
                Apellidos = primerApellido,
                PrimerNombre = primerNombre,
                PrimerApellido = primerApellido,
                NombreNormalizado = NombresPersona.Normalizar(primerNombre, null, primerApellido, null),
                FechaNacimiento = nacimiento,
                Cargo = cargo,
                TipoDocumento = dni != null ? "DNI" : "INTERNO",
                Documento = dni ?? codigoInterno ?? "SIN-DOC",
                EstadoIdentidad = dni != null ? EstadosIdentidad.Verificada : EstadosIdentidad.Pendiente,
                CreadoPor = "pruebas",
                FechaCreacion = ahora
            };

            var vinculo = new PersonaEmpresa
            {
                IdEmpresa = idEmpresa,
                TipoVinculo = TiposVinculo.Empleado,
                FechaFin = vigente ? null : new DateOnly(2026, 1, 31),
                CreadoPor = "pruebas",
                FechaCreacion = ahora,
                Empleado = new Empleado
                {
                    IdEmpresa = idEmpresa,
                    CodigoInterno = codigoInterno,
                    Cargo = cargo,
                    CreadoPor = "pruebas",
                    FechaCreacion = ahora
                }
            };
            persona.Vinculos.Add(vinculo);

            if (dni != null)
            {
                persona.Documentos.Add(new PersonaDocumento
                {
                    TipoDocumento = "DNI",
                    PaisEmisor = "HN",
                    Numero = dni,
                    EsPrincipal = true,
                    CreadoPor = "pruebas",
                    FechaCreacion = ahora
                });
            }

            db.Personas.Add(persona);
            db.SaveChanges();
            return persona;
        }
    }
}
