namespace eGestion360Web.Models.Catalogos
{
    /// <summary>
    /// Valores permitidos en las columnas de texto de tasas_cambio y su bitácora. Deben coincidir con los
    /// CHECK del script 020.
    /// </summary>
    public static class TasasCambioCatalogo
    {
        public const string Job = "TASAS_CAMBIO";
        public const string MonedaLocal = "HNL";

        public static class TipoTasa
        {
            public const string Compra = "COMPRA";
            public const string Venta = "VENTA";
            public const string Referencia = "REFERENCIA";
            public static readonly string[] Todos = { Compra, Venta, Referencia };
        }

        public static class EstadoTasa
        {
            public const string Vigente = "VIGENTE";
            public const string Reemplazada = "REEMPLAZADA";
            public const string EnRevision = "EN_REVISION";
            public const string Rechazada = "RECHAZADA";
            public static readonly string[] Todos = { Vigente, Reemplazada, EnRevision, Rechazada };
        }

        public static class Fuente
        {
            public const string BchApi = "BCH_API";
            public const string BchXlsx = "BCH_XLSX";
            public const string Bce = "BCE";
            public const string Derivada = "DERIVADA";
            public const string Manual = "MANUAL";
            public const string LegadoWinForms = "LEGADO_WINFORMS";
        }

        public static class Disparador
        {
            public const string Programado = "PROGRAMADO";
            public const string Externo = "EXTERNO";
            public const string Manual = "MANUAL";
            public const string Reintento = "REINTENTO";
            public static readonly string[] Todos = { Programado, Externo, Manual, Reintento };
        }

        public static class EstadoEjecucion
        {
            public const string EnCurso = "EN_CURSO";
            public const string Exitosa = "EXITOSA";
            public const string Parcial = "PARCIAL";
            public const string Reintentada = "REINTENTADA";
            public const string Fallida = "FALLIDA";
            public const string OmitidaDuplicada = "OMITIDA_DUPLICADA";
            public const string OmitidaInvalida = "OMITIDA_INVALIDA";
            public const string OmitidaSinDatos = "OMITIDA_SIN_DATOS";
            public static readonly string[] Todos =
                { EnCurso, Exitosa, Parcial, Reintentada, Fallida, OmitidaDuplicada, OmitidaInvalida, OmitidaSinDatos };
        }

        public static class ResultadoDetalle
        {
            public const string Insertada = "INSERTADA";
            public const string Reemplazo = "REEMPLAZO";
            public const string Duplicada = "DUPLICADA";
            public const string Invalida = "INVALIDA";
            public const string EnRevision = "EN_REVISION";
            public const string SinDatos = "SIN_DATOS";
            public const string Error = "ERROR";
        }
    }
}
