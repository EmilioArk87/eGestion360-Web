namespace eGestion360Web.Models.Personas
{
    /// <summary>
    /// Roles que una persona puede tener en una empresa (columna tipo_vinculo de persona_empresa).
    /// Coinciden con CK_persona_empresa_tipo del script 014.
    /// </summary>
    public static class TiposVinculo
    {
        public const string Empleado = "empleado";
        public const string Cliente = "cliente";
        public const string Proveedor = "proveedor";
        public const string Usuario = "usuario";
        public const string Contacto = "contacto";
        public const string Otro = "otro";

        public static readonly IReadOnlyList<string> Todos = new[]
        {
            Empleado, Cliente, Proveedor, Usuario, Contacto, Otro
        };
    }

    /// <summary>
    /// Estado de la identidad de una persona (columna estado_identidad de personas).
    /// 'verificada' = tiene un documento de identidad registrado; 'pendiente' = todavía no.
    /// </summary>
    public static class EstadosIdentidad
    {
        public const string Pendiente = "pendiente";
        public const string Verificada = "verificada";
    }
}
