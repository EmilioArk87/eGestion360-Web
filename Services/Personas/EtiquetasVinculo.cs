using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    /// <summary>Cómo se llama y se pinta cada rol de una persona en una empresa (<see cref="TiposVinculo"/>) en las pantallas.</summary>
    public static class EtiquetasVinculo
    {
        public static string Rol(string tipoVinculo) => tipoVinculo switch
        {
            TiposVinculo.Empleado => "Empleado",
            TiposVinculo.Cliente => "Cliente",
            TiposVinculo.Proveedor => "Proveedor",
            TiposVinculo.Usuario => "Usuario",
            TiposVinculo.Contacto => "Contacto",
            TiposVinculo.Otro => "Otro",
            _ => tipoVinculo
        };

        /// <summary>Clases de Bootstrap 5.1 de la insignia: de color si el vínculo está vigente, apagada si terminó o está inactivo.</summary>
        public static string Clase(string tipoVinculo, bool vigente)
        {
            if (!vigente) return "bg-light text-muted border";

            return tipoVinculo switch
            {
                TiposVinculo.Empleado => "bg-primary",
                TiposVinculo.Cliente => "bg-info text-dark",
                TiposVinculo.Proveedor => "bg-warning text-dark",
                TiposVinculo.Usuario => "bg-dark",
                _ => "bg-secondary"
            };
        }

        /// <summary>«Vigente», «Terminado el 31/01/2026» o «Inactivo»: el estado de un vínculo en palabras.</summary>
        public static string Estado(bool activo, DateOnly? fechaFin)
        {
            if (fechaFin != null) return $"Terminado el {fechaFin.Value:dd/MM/yyyy}";
            return activo ? "Vigente" : "Inactivo";
        }
    }
}
