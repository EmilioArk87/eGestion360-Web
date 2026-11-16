namespace eGestion360Web.Models.Psicologia
{
    /// <summary>Estados de una jornada o brigada (CK_jornadas_estado del script 042).</summary>
    public static class EstadosJornada
    {
        public const string Abierta = "abierta";
        public const string Cerrada = "cerrada";
        public const string Anulada = "anulada";

        public static readonly IReadOnlyList<string> Todos = new[] { Abierta, Cerrada, Anulada };
    }

    /// <summary>
    /// Estados de una atención (CK_atenciones_estado del script 042). Una atención cerrada ya no se edita: el documento impreso
    /// siempre coincide con lo guardado.
    /// </summary>
    public static class EstadosAtencion
    {
        public const string Borrador = "borrador";
        public const string Cerrada = "cerrada";
        public const string Anulada = "anulada";

        public static readonly IReadOnlyList<string> Todos = new[] { Borrador, Cerrada, Anulada };
    }

    /// <summary>Estados de un lote de importación (CK_importaciones_estado del script 042).</summary>
    public static class EstadosImportacion
    {
        public const string Borrador = "borrador";
        public const string Confirmada = "confirmada";
        public const string Descartada = "descartada";

        public static readonly IReadOnlyList<string> Todos = new[] { Borrador, Confirmada, Descartada };
    }

    /// <summary>Resultado de cada fila de un archivo importado (CK_importacion_filas_resultado del script 042).</summary>
    public static class ResultadosFilaImportacion
    {
        /// <summary>Todavía sin revisar.</summary>
        public const string Pendiente = "pendiente";
        /// <summary>La persona no existía en la cuenta: se crea.</summary>
        public const string Nueva = "nueva";
        /// <summary>La persona ya existía (mismo DNI en la cuenta): se reutiliza y se le agrega el vínculo de paciente.</summary>
        public const string Reutilizada = "reutilizada";
        /// <summary>Hay que confirmar a mano el reparto de nombres y apellidos o una diferencia con la persona existente.</summary>
        public const string Revisar = "revisar";
        /// <summary>La fila no se puede importar (DNI inválido, repetido en el archivo, datos que faltan…).</summary>
        public const string Error = "error";
        /// <summary>Quien importa decidió no incluirla.</summary>
        public const string Omitida = "omitida";

        public static readonly IReadOnlyList<string> Todos = new[] { Pendiente, Nueva, Reutilizada, Revisar, Error, Omitida };
    }

    /// <summary>Tamaños de papel del documento impreso (CK_impresiones_papel del script 042).</summary>
    public static class TamanosPapel
    {
        /// <summary>Carta: 8.5 × 11 pulgadas.</summary>
        public const string Carta = "carta";
        /// <summary>Oficio: 8.5 × 13 pulgadas.</summary>
        public const string Oficio = "oficio";
        /// <summary>Media carta: 5.5 × 8.5 pulgadas.</summary>
        public const string MediaCarta = "media_carta";

        public static readonly IReadOnlyList<string> Todos = new[] { Carta, Oficio, MediaCarta };
    }
}
