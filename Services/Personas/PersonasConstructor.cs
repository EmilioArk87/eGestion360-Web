using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Arma una persona nueva a partir de datos ya validados. Lo comparten el alta de personal y el alta de clientes:
    /// ambos guardan los mismos datos personales y el mismo documento; lo que cambia es el rol (el vínculo) y las
    /// columnas LEGADO que describen ese rol, que pone quien llama.
    /// </summary>
    internal static class PersonasConstructor
    {
        /// <summary>
        /// Persona con su nombre, datos personales, contacto, licencia, estado de identidad y documento. NO trae
        /// vínculo ni las columnas LEGADO de rol (empresa, tipo de documento, documento, cargo, tarifa, ingreso y
        /// baja): quien llama las completa o las deja vacías.
        /// </summary>
        public static Persona Nueva(PersonaDatosNormalizados n, string usuario, DateTime ahora)
        {
            var persona = new Persona
            {
                Nombres = n.Nombres,        // LEGADO (compuesto)
                Apellidos = n.Apellidos,    // LEGADO (compuesto)
                Activo = true,

                Telefono = n.Telefono,
                Email = n.Email,

                PrimerNombre = n.PrimerNombre,
                SegundoNombre = n.SegundoNombre,
                PrimerApellido = n.PrimerApellido,
                SegundoApellido = n.SegundoApellido,
                NombreNormalizado = n.NombreNormalizado,
                Sexo = n.Sexo,
                EstadoCivil = n.EstadoCivil,
                FechaNacimiento = n.FechaNacimiento,
                PaisNacionalidad = n.PaisNacionalidad,
                TipoSangre = n.TipoSangre,
                IdMunicipioNacimiento = n.IdMunicipioNacimiento,
                IdMunicipioResidencia = n.IdMunicipioResidencia,
                DireccionResidencia = n.DireccionResidencia,
                TelefonoSecundario = n.TelefonoSecundario,
                ContactoEmergenciaNombre = n.ContactoEmergenciaNombre,
                ContactoEmergenciaTelefono = n.ContactoEmergenciaTelefono,
                ContactoEmergenciaParentesco = n.ContactoEmergenciaParentesco,
                LicenciaTipo = n.LicenciaTipo,
                LicenciaNumero = n.LicenciaNumero,
                LicenciaVencimiento = n.LicenciaVencimiento,
                EstadoIdentidad = n.TieneDocumentoDeIdentidad ? EstadosIdentidad.Verificada : EstadosIdentidad.Pendiente,

                CreadoPor = usuario,
                FechaCreacion = ahora
            };

            if (!string.IsNullOrEmpty(n.Documento))
            {
                persona.Documentos.Add(new PersonaDocumento
                {
                    TipoDocumento = n.TipoDocumento!,
                    PaisEmisor = n.PaisEmisor!,
                    Numero = n.Documento,
                    EsPrincipal = true,
                    CreadoPor = usuario,
                    FechaCreacion = ahora
                });
            }

            return persona;
        }
    }
}
