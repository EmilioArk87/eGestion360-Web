namespace eGestion360Web.Services.Personas
{
    /// <summary>
    /// La razón social de un cliente natural es el nombre de la persona (nombres y apellidos). La arman con la
    /// misma regla el alta del cliente y la sincronización que corre cuando la persona cambia de nombre.
    /// </summary>
    public static class RazonSocialCliente
    {
        /// <summary>Largo de clientes.razon_social.</summary>
        public const int LargoMaximo = 200;

        public static string De(string? nombres, string? apellidos)
        {
            var texto = $"{NombresPersona.Limpiar(nombres)} {NombresPersona.Limpiar(apellidos)}".Trim();
            return texto.Length > LargoMaximo ? texto[..LargoMaximo].TrimEnd() : texto;
        }
    }
}
