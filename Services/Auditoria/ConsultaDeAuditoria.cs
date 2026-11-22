using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using eGestion360Web.Models.Auditoria;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>
    /// Los filtros de las pantallas de auditoría (F1.8c, ADR-030 D74). Las fechas son días de Honduras; sin ninguna, se muestran los
    /// últimos <see cref="ConsultaDeAuditoria.DiasPorOmision"/> días.
    /// </summary>
    public sealed class FiltroDeAuditoria
    {
        public DateOnly? Desde { get; set; }
        public DateOnly? Hasta { get; set; }
        public string? Tipo { get; set; }
        public string? Resultado { get; set; }
        public string? Usuario { get; set; }

        /// <summary>Solo en la auditoría de la plataforma: el tenant sobre el que se actuó.</summary>
        public int? IdTenant { get; set; }
    }

    /// <summary>Una fila tal como se muestra y se exporta: la hora en Honduras y el correo enmascarado.</summary>
    public sealed record FilaDeAuditoria(
        long IdEvento,
        DateTime FechaHoraLocal,
        string Tipo,
        string Resultado,
        string? Usuario,
        bool EsOperador,
        int? IdTenant,
        string? Tenant,
        int? IdEmpresa,
        string? ObjetoTipo,
        string? ObjetoId,
        string? Detalle,
        string? Ip,
        string IdCorrelacion);

    /// <summary>Lo que recibe la tabla compartida de las dos pantallas (<c>Pages/Admin/_TablaDeAuditoria.cshtml</c>).</summary>
    public sealed record TablaDeAuditoria(IReadOnlyList<FilaDeAuditoria> Filas, bool ConTenant, bool ParaImprimir);

    /// <summary>
    /// Lo que comparten «Auditoría» (la del tenant) y «Auditoría de la plataforma»: filtrar, ordenar, enmascarar y exportar a Excel.
    /// Solo lee: las dos tablas son inmutables y estas pantallas no escriben en ellas, salvo la fila que deja cada exportación.
    /// </summary>
    public static class ConsultaDeAuditoria
    {
        public const int PorPagina = 50;
        public const int DiasPorOmision = 7;

        /// <summary>Tope de filas de una exportación (Excel o impresión). Si hay más, se exportan las más recientes y la pantalla lo avisa.</summary>
        public const int MaximoParaExportar = 5000;

        public static readonly IReadOnlyList<string> Resultados = new[] { ResultadosDeEvento.Ok, ResultadosDeEvento.Fallo, ResultadosDeEvento.Denegado };

        // Un correo completo dentro del detalle o del usuario. Uno ya enmascarado («ma***@e***.hn») no coincide: lleva asteriscos junto a la arroba.
        private static readonly Regex Correo = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Completa las fechas: sin ninguna, los últimos <see cref="DiasPorOmision"/> días hasta hoy; y si vienen al revés, las ordena.</summary>
        public static void Normalizar(FiltroDeAuditoria filtro, DateOnly hoy)
        {
            if (filtro.Desde == null && filtro.Hasta == null)
                filtro.Desde = hoy.AddDays(-(DiasPorOmision - 1));
            if (filtro.Desde is { } d && filtro.Hasta is { } h && d > h)
                (filtro.Desde, filtro.Hasta) = (h, d);
            filtro.Tipo = string.IsNullOrWhiteSpace(filtro.Tipo) ? null : filtro.Tipo.Trim();
            filtro.Resultado = Resultados.Contains(filtro.Resultado) ? filtro.Resultado : null;
            filtro.Usuario = string.IsNullOrWhiteSpace(filtro.Usuario) ? null : filtro.Usuario.Trim();
            if (filtro.IdTenant is not > 0) filtro.IdTenant = null;
        }

        /// <summary>Aplica los filtros (fechas de Honduras convertidas a UTC) a cualquiera de las dos tablas. No ordena.</summary>
        public static IQueryable<T> Filtrar<T>(IQueryable<T> consulta, FiltroDeAuditoria filtro, TimeZoneInfo zona) where T : EventoDeAuditoria
        {
            if (filtro.Desde is { } desde)
            {
                var desdeUtc = AUtc(desde, zona);
                consulta = consulta.Where(e => e.FechaHora >= desdeUtc);
            }
            if (filtro.Hasta is { } hasta)
            {
                var hastaUtc = AUtc(hasta.AddDays(1), zona);
                consulta = consulta.Where(e => e.FechaHora < hastaUtc);
            }
            if (filtro.Tipo is { } tipo)
                consulta = tipo.EndsWith('.') ? consulta.Where(e => e.Tipo.StartsWith(tipo)) : consulta.Where(e => e.Tipo == tipo);
            if (filtro.Resultado is { } resultado)
                consulta = consulta.Where(e => e.Resultado == resultado);
            if (filtro.Usuario is { } usuario)
                consulta = consulta.Where(e => e.Usuario != null && e.Usuario.Contains(usuario));
            return consulta;
        }

        /// <summary>Lo más reciente primero (<c>id_evento</c> crece en orden).</summary>
        public static IOrderedQueryable<T> Ordenar<T>(IQueryable<T> consulta) where T : EventoDeAuditoria
            => consulta.OrderByDescending(e => e.IdEvento);

        private static DateTime AUtc(DateOnly dia, TimeZoneInfo zona)
            => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zona);

        /// <summary>La fila lista para mostrar: hora de Honduras, correo enmascarado en el usuario y en el detalle.</summary>
        public static FilaDeAuditoria AFila(EventoDeAuditoria e, int? idTenant, string? tenant, TimeZoneInfo zona)
            => new(e.IdEvento,
                   TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.FechaHora, DateTimeKind.Utc), zona),
                   e.Tipo, e.Resultado, EnmascararCorreos(e.Usuario), e.EsOperador, idTenant, tenant, e.IdEmpresa,
                   e.ObjetoTipo, e.ObjetoId, EnmascararCorreos(e.Detalle), e.Ip, e.IdCorrelacion);

        /// <summary>Cambia cada correo completo del texto por su forma enmascarada («maria.lopez@empresa.hn» → «ma***@e***.hn»).</summary>
        public static string? EnmascararCorreos(string? texto)
            => string.IsNullOrEmpty(texto) || !texto.Contains('@')
                ? texto
                : Correo.Replace(texto, m => EnmascaradoDeAuditoria.Identificador(m.Value) ?? string.Empty);

        /// <summary>Lo que se anota en la fila de una exportación: el formato, cuántas filas y los filtros (el usuario, enmascarado).</summary>
        public static object DetalleDeExportacion(string formato, int filas, bool recortada, FiltroDeAuditoria filtro) => new
        {
            formato,
            filas,
            recortada,
            desde = filtro.Desde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            hasta = filtro.Hasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tipo = filtro.Tipo,
            resultado = filtro.Resultado,
            usuario = EnmascaradoDeAuditoria.Identificador(filtro.Usuario),
            tenant = filtro.IdTenant,
        };

        /// <summary>Nombre del archivo exportado: <c>auditoria_2026-11-15_a_2026-11-21.xlsx</c>.</summary>
        public static string NombreDeArchivo(string prefijo, FiltroDeAuditoria filtro, DateOnly hoy)
            => $"{prefijo}_{(filtro.Desde ?? hoy):yyyy-MM-dd}_a_{(filtro.Hasta ?? hoy):yyyy-MM-dd}.xlsx";

        /// <summary>Las filas en un libro de Excel de una hoja, con encabezado fijo y filtro automático.</summary>
        public static byte[] Excel(IReadOnlyList<FilaDeAuditoria> filas, string titulo, bool conTenant)
        {
            using var libro = new XLWorkbook();
            var hoja = libro.Worksheets.Add("Auditoría");

            var columnas = new List<string> { "Fecha y hora (Honduras)", "Tipo", "Resultado", "Usuario", "Operador" };
            if (conTenant) columnas.Add("Tenant");
            columnas.AddRange(new[] { "Empresa", "Objeto", "Detalle", "Dirección IP", "Correlación", "Id" });

            hoja.Cell(1, 1).Value = titulo;
            hoja.Cell(1, 1).Style.Font.Bold = true;
            for (var c = 0; c < columnas.Count; c++)
            {
                var celda = hoja.Cell(3, c + 1);
                celda.Value = columnas[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
            }

            var fila = 4;
            foreach (var f in filas)
            {
                var c = 1;
                hoja.Cell(fila, c).Value = f.FechaHoraLocal;
                hoja.Cell(fila, c++).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                hoja.Cell(fila, c++).Value = $"{NombreDeTipo(f.Tipo)} ({f.Tipo})";
                hoja.Cell(fila, c++).Value = NombreDeResultado(f.Resultado);
                hoja.Cell(fila, c++).Value = f.Usuario ?? string.Empty;
                hoja.Cell(fila, c++).Value = f.EsOperador ? "Sí" : "No";
                if (conTenant) hoja.Cell(fila, c++).Value = f.Tenant ?? (f.IdTenant?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                hoja.Cell(fila, c++).Value = f.IdEmpresa?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                hoja.Cell(fila, c++).Value = Objeto(f);
                hoja.Cell(fila, c++).Value = f.Detalle ?? string.Empty;
                hoja.Cell(fila, c++).Value = f.Ip ?? string.Empty;
                hoja.Cell(fila, c++).Value = f.IdCorrelacion;
                hoja.Cell(fila, c).Value = f.IdEvento;
                fila++;
            }

            var ultima = Math.Max(fila - 1, 3);
            hoja.Range(3, 1, ultima, columnas.Count).SetAutoFilter();
            hoja.SheetView.FreezeRows(3);
            hoja.Columns().AdjustToContents(3, Math.Min(ultima, 200));
            foreach (var columna in hoja.ColumnsUsed())
                if (columna.Width > 80) columna.Width = 80;

            using var memoria = new MemoryStream();
            libro.SaveAs(memoria);
            return memoria.ToArray();
        }

        private static readonly IReadOnlyDictionary<string, string> NombresDeTipo = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TiposDeEvento.SesionEntrada] = "Entrada al sistema",
            [TiposDeEvento.SesionSalida] = "Salida del sistema",
            [TiposDeEvento.SesionBloqueo] = "Cuenta bloqueada",
            [TiposDeEvento.MfaVerificada] = "Segundo factor verificado",
            [TiposDeEvento.MfaAlta] = "Segundo factor activado",
            [TiposDeEvento.MfaRecuperacion] = "Código de recuperación usado",
            [TiposDeEvento.MfaDesactivada] = "Segundo factor desactivado",
            [TiposDeEvento.ClaveCambiada] = "Clave cambiada",
            [TiposDeEvento.ClaveRestablecida] = "Clave restablecida",
            [TiposDeEvento.InvitacionAceptada] = "Invitación aceptada",
            [TiposDeEvento.ContextoElegido] = "Entrada a un sitio",
            [TiposDeEvento.UsuarioCreado] = "Usuario creado",
            [TiposDeEvento.UsuarioEditado] = "Usuario editado",
            [TiposDeEvento.UsuarioEliminado] = "Usuario eliminado",
            [TiposDeEvento.RolCreado] = "Rol creado",
            [TiposDeEvento.RolEditado] = "Rol editado",
            [TiposDeEvento.RolEliminado] = "Rol eliminado",
            [TiposDeEvento.RolPermisos] = "Permisos de un rol",
            [TiposDeEvento.ModuloCambiado] = "Módulo cambiado",
            [TiposDeEvento.AccesoCambiado] = "Acceso a una empresa cambiado",
            [TiposDeEvento.EmpresaCreada] = "Empresa creada",
            [TiposDeEvento.EmpresaEditada] = "Empresa editada",
            [TiposDeEvento.EmpresaEliminada] = "Empresa eliminada",
            [TiposDeEvento.InvitacionEnviada] = "Invitación enviada",
            [TiposDeEvento.InvitacionAnulada] = "Invitación anulada",
            [TiposDeEvento.AccesoDenegado] = "Acceso denegado",
            [TiposDeEvento.SoporteEntrada] = "Entrada de soporte",
            [TiposDeEvento.SoporteSalida] = "Salida de soporte",
            [TiposDeEvento.CorreoConfigurado] = "Correo configurado",
            [TiposDeEvento.TasasConfigurada] = "Proceso de tasas configurado",
            [TiposDeEvento.TasasEjecutada] = "Proceso de tasas ejecutado",
            [TiposDeEvento.TasasOficialAprobada] = "Tasa oficial aprobada",
            [TiposDeEvento.TasasOficialRechazada] = "Tasa oficial rechazada",
            [TiposDeEvento.ColaReintento] = "Cola de eventos: reintento",
            [TiposDeEvento.ColaProcesadoManual] = "Cola de eventos: procesado a mano",
            [TiposDeEvento.AuditoriaExportada] = "Auditoría exportada",
            [TiposDeEvento.AuditoriaPlataformaExportada] = "Auditoría de la plataforma exportada",
        };

        /// <summary>El nombre en español de un tipo de evento; si no lo tiene, el propio tipo.</summary>
        public static string NombreDeTipo(string tipo) => NombresDeTipo.TryGetValue(tipo, out var nombre) ? nombre : tipo;

        /// <summary>«Correcto», «Fallido» o «Denegado».</summary>
        public static string NombreDeResultado(string resultado) => resultado switch
        {
            ResultadosDeEvento.Ok => "Correcto",
            ResultadosDeEvento.Fallo => "Fallido",
            ResultadosDeEvento.Denegado => "Denegado",
            _ => resultado,
        };

        /// <summary>«usuario 15», o vacío si el evento no es sobre un objeto.</summary>
        public static string Objeto(FilaDeAuditoria f)
            => f.ObjetoTipo == null ? string.Empty : f.ObjetoId == null ? f.ObjetoTipo : $"{f.ObjetoTipo} {f.ObjetoId}";
    }
}
