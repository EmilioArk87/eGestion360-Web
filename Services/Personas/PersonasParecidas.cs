using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// Busca, entre las personas que la empresa ya tiene (con cualquier rol), a quienes podrían ser la misma que
    /// se está registrando. Es lo que comparten el alta de personal y el alta de clientes para avisar de un posible
    /// duplicado antes de crear a alguien por segunda vez.
    /// </summary>
    internal static class PersonasParecidas
    {
        /// <summary>
        /// Personas de la empresa con el mismo nombre normalizado o, si se conoce la fecha de nacimiento, con el
        /// mismo primer nombre, el mismo primer apellido y esa misma fecha (así se detecta a la misma persona
        /// escrita con y sin segundo apellido). Si ambas tienen fecha de nacimiento, deben coincidir. Solo mira
        /// a las personas de esta empresa: no se revela a quienes están en otras.
        /// </summary>
        public static async Task<IReadOnlyList<PersonaParecida>> BuscarAsync(
            ApplicationDbContext db, int idEmpresa, string nombreNormalizado, string primerNombre, string primerApellido,
            DateOnly? fechaNacimiento, int? excluirIdPersona, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(nombreNormalizado)) return Array.Empty<PersonaParecida>();

            var inicio = NombresPersona.Normalizar(primerNombre) + " ";
            var apellido = NombresPersona.Normalizar(primerApellido);
            var conApellidoEnMedio = " " + apellido + " ";
            var conApellidoAlFinal = " " + apellido;
            var puedeCompararPartes = fechaNacimiento != null && inicio.Trim().Length > 0 && apellido.Length > 0;

            var consulta = db.Personas.AsNoTracking()
                .Where(p => !p.Eliminado && p.IdPersonaPrincipal == null
                            && p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado)
                            && (p.NombreNormalizado == nombreNormalizado
                                || (puedeCompararPartes && p.FechaNacimiento == fechaNacimiento
                                    && p.NombreNormalizado != null
                                    && p.NombreNormalizado.StartsWith(inicio)
                                    && (p.NombreNormalizado.Contains(conApellidoEnMedio) || p.NombreNormalizado.EndsWith(conApellidoAlFinal)))));

            if (excluirIdPersona != null)
                consulta = consulta.Where(p => p.IdPersona != excluirIdPersona);

            if (fechaNacimiento != null)
                consulta = consulta.Where(p => p.FechaNacimiento == null || p.FechaNacimiento == fechaNacimiento);

            return await consulta
                .OrderBy(p => p.IdPersona)
                .Take(10)
                .Select(p => new PersonaParecida(
                    p.IdPersona, p.Nombres + " " + p.Apellidos, p.FechaNacimiento, p.Activo,
                    p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado && v.TipoVinculo == TiposVinculo.Empleado)))
                .ToListAsync(ct);
        }
    }
}
