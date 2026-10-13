using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.TasasCambio;
using Cat = eGestion360Web.Models.Catalogos.TasasCambioCatalogo;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>
    /// Pantalla de las tasas de cambio (script 020): las vigentes de hoy, las pendientes de revisión, la captura de
    /// tasas propias de la empresa, el histórico y la bitácora del job. Ver 1 - Documetacion/TASAS_CAMBIO.md.
    ///
    /// Permisos, revisados en CADA handler (ocultar un botón no basta):
    ///   * Ver: sesión iniciada y módulo "catalogos" (igual que Clientes).
    ///   * Registrar una tasa propia: permiso de crear en "catalogos" y empresa en la sesión. La empresa sale SIEMPRE
    ///     de la sesión: el formulario no tiene ningún campo de empresa.
    ///   * Aprobar o rechazar una tasa OFICIAL (id_empresa nulo) y "Ejecutar ahora": solo el administrador del sistema.
    ///   * Aprobar o rechazar una tasa PROPIA: un administrador (del sistema o de empresa) y solo si la tasa es de la
    ///     empresa de la sesión. AprobarAsync/RechazarAsync no reciben empresa, así que antes se comprueba que el id
    ///     esté entre las pendientes que la empresa de la sesión puede ver: un id de otra empresa se rechaza.
    /// </summary>
    public class IndexModel : TasasCambioPaginaBase
    {
        public const string Modulo = "catalogos";
        public const int LargoMaximoMotivo = 300;

        public const string AvisoNoInstalado =
            "El módulo de tasas de cambio no está instalado en la base de datos: falta aplicar el script 020.";
        public const string AvisoErrorBaseDatos =
            "No se pudo consultar las tasas de cambio en la base de datos. Intente de nuevo en unos minutos; si el problema sigue, avise al administrador.";

        public const string ClaveMensaje = "TiposCambioMessage";
        public const string ClaveError = "TiposCambioError";

        /// <summary>Tipos que se capturan en pantalla (el job tampoco guarda REFERENCIA).</summary>
        public static readonly string[] TiposCaptura = { Cat.TipoTasa.Compra, Cat.TipoTasa.Venta };

        private readonly ITasaCambioConsultaService _consultas;
        private readonly ITasaCambioSyncService _sync;
        private readonly ColaEjecucionTasasCambio _cola;
        private readonly ILogger<IndexModel> _log;

        public IndexModel(
            ITasaCambioConsultaService consultas,
            ITasaCambioSyncService sync,
            ColaEjecucionTasasCambio cola,
            IOptions<TasasCambioOptions> opciones,
            TimeProvider reloj,
            ILogger<IndexModel> log) : base(opciones, reloj)
        {
            _consultas = consultas;
            _sync = sync;
            _cola = cola;
            _log = log;
        }

        // ── Datos de la pantalla ──────────────────────────────────────────────
        // El histórico y la bitácora del job tienen su propia pantalla (HistoricoModel y BitacoraModel).

        public IReadOnlyList<TasaVigenteActual> Vigentes { get; private set; } = Array.Empty<TasaVigenteActual>();
        public IReadOnlyList<TasaPendienteRevision> Pendientes { get; private set; } = Array.Empty<TasaPendienteRevision>();

        /// <summary>La última ejecución del job (para el estado de la cabecera), sin endpoint ni detalle del error.</summary>
        public EjecucionTasasResumen? UltimaEjecucion { get; private set; }

        /// <summary>Formulario de la tasa propia (para volver a mostrarlo con sus errores).</summary>
        public RegistroTasaInput Registro { get; set; } = new();

        // ── Contexto y permisos (los usan la vista y los handlers) ────────────

        public bool JobHabilitado => Opciones.Habilitado;
        public int SolicitudesEnCola => _cola.Pendientes;

        public bool TienePermisoCrear => AuthHelper.PuedeCrear(HttpContext, Modulo);
        public bool PuedeRegistrar => TienePermisoCrear && IdEmpresaSesion != null;
        public bool PuedeEjecutar => EsAdminSistema && JobHabilitado;

        /// <summary>¿La sesión puede aprobar o rechazar una tasa de esta empresa (nulo = oficial)?</summary>
        public bool PuedeRevisar(int? idEmpresaTasa) => idEmpresaTasa == null
            ? EsAdminSistema
            : AuthHelper.IsAnyAdmin(HttpContext) && idEmpresaTasa == IdEmpresaSesion;

        // ──────────────────────────────────────────────────────────────────
        //  GET
        // ──────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnGetAsync()
        {
            if (Acceso() is { } desvio) return desvio;

            Registro = new RegistroTasaInput { Fecha = Hoy };
            await CargarAsync();
            return Page();
        }

        // ──────────────────────────────────────────────────────────────────
        //  REGISTRAR TASA PROPIA
        // ──────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnPostRegistrarAsync(RegistroTasaInput? registro)
        {
            if (Acceso() is { } desvio) return desvio;

            Registro = registro ?? new RegistroTasaInput();

            if (!TienePermisoCrear)
                return Denegado("registrar una tasa", null, "No tiene permiso para registrar tasas de cambio.", "registrar");

            // La empresa sale de la sesión, nunca del formulario.
            if (IdEmpresaSesion is not { } idEmpresa)
                return Volver(error: "Su sesión no tiene empresa: las tasas propias se registran para la empresa con la que inició sesión.",
                    seccion: "registrar");

            if (ValidarRegistro(Registro) is not { } datos)
            {
                await CargarAsync();
                return Page();
            }

            ResultadoOperacionTasa resultado;
            try
            {
                resultado = await _sync.RegistrarManualAsync(idEmpresa, datos.Moneda, datos.Tipo, datos.Fecha, datos.Tasa, Usuario, Ct);
            }
            catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
            {
                return ErrorBaseDatos(ex, "registrar una tasa propia", "registrar");
            }

            if (!resultado.Exito)
            {
                // No cambió nada: se vuelve a mostrar el formulario con lo que escribió y el motivo.
                ModelState.AddModelError(string.Empty, resultado.Mensaje);
                await CargarAsync();
                return Page();
            }

            return Volver(mensaje: $"{resultado.Mensaje} {datos.Moneda} {FormatoTasas.NombreTipo(datos.Tipo).ToLowerInvariant()} " +
                                   $"del {FormatoTasas.Fecha(datos.Fecha)}: {FormatoTasas.Tasa(datos.Tasa)}.",
                seccion: "vigentes");
        }

        // ──────────────────────────────────────────────────────────────────
        //  APROBAR / RECHAZAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<IActionResult> OnPostAprobarAsync(int idTasaCambio)
        {
            if (Acceso() is { } desvio) return desvio;

            try
            {
                var (pendiente, rechazo) = await PendienteAutorizadaAsync(idTasaCambio, "aprobar");
                if (pendiente == null) return rechazo!;

                var resultado = await _sync.AprobarAsync(pendiente.IdTasaCambio, Usuario, Ct);
                return Resultado(resultado, pendiente);
            }
            catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
            {
                return ErrorBaseDatos(ex, "aprobar una tasa", "pendientes");
            }
        }

        public async Task<IActionResult> OnPostRechazarAsync(int idTasaCambio, string? motivo)
        {
            if (Acceso() is { } desvio) return desvio;

            try
            {
                var (pendiente, rechazo) = await PendienteAutorizadaAsync(idTasaCambio, "rechazar");
                if (pendiente == null) return rechazo!;

                motivo = motivo?.Trim();
                if (string.IsNullOrEmpty(motivo))
                    return Volver(error: "Indique el motivo del rechazo.", seccion: "pendientes");
                if (motivo.Length > LargoMaximoMotivo)
                    return Volver(error: $"El motivo no puede pasar de {LargoMaximoMotivo} caracteres.", seccion: "pendientes");

                var resultado = await _sync.RechazarAsync(pendiente.IdTasaCambio, Usuario, motivo, Ct);
                return Resultado(resultado, pendiente);
            }
            catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
            {
                return ErrorBaseDatos(ex, "rechazar una tasa", "pendientes");
            }
        }

        /// <summary>
        /// La pendiente <paramref name="idTasaCambio"/> si la sesión puede revisarla. Solo se busca entre las que ve la
        /// empresa de la sesión (las oficiales y las suyas), así un id de otra empresa no aparece y se rechaza.
        /// </summary>
        private async Task<(TasaPendienteRevision? Pendiente, IActionResult? Rechazo)> PendienteAutorizadaAsync(int idTasaCambio, string accion)
        {
            if (!AuthHelper.IsAnyAdmin(HttpContext))
                return (null, Denegado(accion, idTasaCambio, "Solo un administrador puede aprobar o rechazar tasas en revisión.", "pendientes"));

            var empresa = IdEmpresaSesion;
            var pendiente = (await _consultas.PendientesRevisionAsync(empresa, Ct)).FirstOrDefault(p => p.IdTasaCambio == idTasaCambio);

            if (pendiente == null)
                return (null, Denegado(accion, idTasaCambio,
                    "La tasa indicada no está pendiente de revisión o no pertenece a su empresa. Recargue la pantalla.", "pendientes"));

            if (pendiente.IdEmpresa == null && !EsAdminSistema)
                return (null, Denegado(accion, idTasaCambio,
                    "Solo el administrador del sistema puede aprobar o rechazar una tasa oficial.", "pendientes"));

            // Segunda barrera: la consulta ya filtra por empresa, pero se comprueba igual.
            if (pendiente.IdEmpresa != null && (empresa == null || pendiente.IdEmpresa != empresa))
                return (null, Denegado(accion, idTasaCambio, "La tasa indicada no pertenece a su empresa.", "pendientes"));

            return (pendiente, null);
        }

        private IActionResult Resultado(ResultadoOperacionTasa resultado, TasaPendienteRevision tasa)
        {
            var descripcion = $"Tasa #{tasa.IdTasaCambio} ({tasa.MonedaOrigen} {FormatoTasas.NombreTipo(tasa.TipoTasa).ToLowerInvariant()} " +
                              $"del {FormatoTasas.Fecha(tasa.FechaVigencia)}): ";
            return resultado.Exito
                ? Volver(mensaje: descripcion + resultado.Mensaje, seccion: "pendientes")
                : Volver(error: descripcion + resultado.Mensaje, seccion: "pendientes");
        }

        // ──────────────────────────────────────────────────────────────────
        //  EJECUTAR AHORA
        // ──────────────────────────────────────────────────────────────────

        public IActionResult OnPostEjecutar()
        {
            if (Acceso() is { } desvio) return desvio;

            if (!EsAdminSistema)
                return Denegado("ejecutar el job", null, "Solo el administrador del sistema puede ejecutar el job de tasas de cambio.", "ejecutar");

            if (!JobHabilitado)
                return Volver(error: "El job de tasas de cambio está deshabilitado (TasasCambio:Habilitado = false): no se puede ejecutar.",
                    seccion: "ejecutar");

            if (!_cola.Solicitar(Cat.Disparador.Manual, Usuario))
                return Volver(error: "Ya hay varias ejecuciones esperando turno. Intente de nuevo en unos minutos.", seccion: "ejecutar");

            _log.LogInformation("Tasas de cambio: {Usuario} pidió ejecutar el job desde la pantalla.", Usuario);
            // El resultado se ve en la bitácora, que es su propia pantalla: se lleva a quien lo pidió hacia allá.
            TempData[ClaveMensaje] = "Ejecución solicitada. Corre en segundo plano: el resultado aparecerá en la bitácora en unos minutos " +
                                     "(puede tardar si las fuentes no responden).";
            return RedirectToPage("Bitacora");
        }

        // ──────────────────────────────────────────────────────────────────
        //  CARGA DE DATOS
        // ──────────────────────────────────────────────────────────────────

        private async Task CargarAsync()
        {
            var empresa = IdEmpresaSesion;

            // Una consulta tras otra: comparten el DbContext de la petición.
            try
            {
                Vigentes = await _consultas.VigentesActualesAsync(empresa, Ct);
                Pendientes = await _consultas.PendientesRevisionAsync(empresa, Ct);

                // Solo para el estado de la cabecera: nada de endpoint ni detalle del error.
                var ultimas = await _consultas.UltimasEjecucionesAsync(1, Ct);
                UltimaEjecucion = ultimas.Select(e => e with { Endpoint = null, DetalleError = null }).FirstOrDefault();
            }
            catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
            {
                AvisarErrorBaseDatos(ex, _log, "la pantalla");

                Vigentes = Array.Empty<TasaVigenteActual>();
                Pendientes = Array.Empty<TasaPendienteRevision>();
                UltimaEjecucion = null;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  VALIDACIÓN DEL FORMULARIO
        // ──────────────────────────────────────────────────────────────────

        private sealed record DatosRegistro(string Moneda, string Tipo, DateOnly Fecha, decimal Tasa);

        /// <summary>Valida el formulario; los errores quedan en ModelState junto a cada campo.</summary>
        private DatosRegistro? ValidarRegistro(RegistroTasaInput registro)
        {
            var valido = true;

            var moneda = Elegir(registro.Moneda, Monedas);
            if (moneda == null)
            {
                ModelState.AddModelError("Registro.Moneda", $"Seleccione la moneda ({string.Join(" o ", Monedas)}).");
                valido = false;
            }

            var tipo = Elegir(registro.Tipo, TiposCaptura);
            if (tipo == null)
            {
                ModelState.AddModelError("Registro.Tipo", "Seleccione el tipo: compra o venta.");
                valido = false;
            }

            if (registro.Fecha == null)
            {
                ModelState.AddModelError("Registro.Fecha", "Indique la fecha de vigencia.");
                valido = false;
            }

            if (LeerTasa(registro.Tasa, out var tasa) is { } errorTasa)
            {
                ModelState.AddModelError("Registro.Tasa", errorTasa);
                valido = false;
            }

            return valido ? new DatosRegistro(moneda!, tipo!, registro.Fecha!.Value, tasa) : null;
        }

        /// <summary>
        /// Lee la tasa escrita en el formulario sin depender de la cultura del servidor: punto decimal (lo que envía
        /// un campo numérico) o coma. Devuelve el mensaje de error, o nulo si es válida.
        /// </summary>
        public static string? LeerTasa(string? texto, out decimal tasa)
        {
            tasa = 0m;
            var limpio = texto?.Trim() ?? string.Empty;
            if (limpio.Length == 0) return "Indique la tasa.";

            if (!limpio.Contains('.')) limpio = limpio.Replace(',', '.');
            if (!decimal.TryParse(limpio, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out tasa))
                return "La tasa debe ser un número, por ejemplo 26.8925.";
            if (tasa <= 0m) return "La tasa debe ser mayor que cero.";
            if (decimal.Round(tasa, 4) != tasa) return "La tasa admite hasta 4 decimales.";
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        //  ACCESO, RESULTADOS Y ERRORES
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Patrón PRG: el resultado va en TempData y se vuelve a la pantalla (a la sección indicada).</summary>
        private IActionResult Volver(string? mensaje = null, string? error = null, string? seccion = null)
        {
            if (mensaje != null) TempData[ClaveMensaje] = mensaje;
            if (error != null) TempData[ClaveError] = error;
            return RedirectToPage("Index", null, null, seccion);
        }

        private IActionResult Denegado(string accion, int? idTasa, string mensaje, string seccion)
        {
            _log.LogWarning("Tasas de cambio: {Usuario} (rol {Rol}, empresa {Empresa}) intentó {Accion} {IdTasa} sin permiso.",
                Usuario, HttpContext.Session.GetString("Role"), IdEmpresaSesion, accion, idTasa);
            return Volver(error: mensaje, seccion: seccion);
        }

        private IActionResult ErrorBaseDatos(Exception ex, string accion, string seccion)
        {
            var faltan = FaltanTablas(ex);
            _log.LogError(ex, "Tasas de cambio: error de base de datos al {Accion}.", accion);
            return Volver(error: faltan
                    ? AvisoNoInstalado
                    : "No se pudo completar la operación por un error de la base de datos. Intente de nuevo; si el problema sigue, avise al administrador.",
                seccion: seccion);
        }

        private static IEnumerable<Exception> Cadena(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException) yield return e;
        }

        /// <summary>Un error de la base (o de la conexión), directo o envuelto por EF.</summary>
        public static bool EsErrorDeBaseDeDatos(Exception ex) => Cadena(ex).Any(e => e is DbException);

        /// <summary>
        /// ¿El error es porque no existen las tablas o columnas del script 020? SQL Server: errores 208 (objeto
        /// inexistente) y 207 (columna inexistente). Otros motores (SQLite en las pruebas): "no such table".
        /// </summary>
        public static bool FaltanTablas(Exception ex) => Cadena(ex).Any(e => e switch
        {
            SqlException sql => sql.Errors.Cast<SqlError>().Any(x => x.Number is 208 or 207),
            DbException db => db.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    /// <summary>
    /// Formulario de la tasa propia. No tiene campo de empresa a propósito: la empresa es la de la sesión.
    /// La tasa llega como texto para leerla sin depender de la cultura del servidor.
    /// </summary>
    public sealed class RegistroTasaInput
    {
        public string? Moneda { get; set; }
        public string? Tipo { get; set; }
        public DateOnly? Fecha { get; set; }
        public string? Tasa { get; set; }
    }

    /// <summary>Textos y clases de Bootstrap de la pantalla de tasas.</summary>
    public static class FormatoTasas
    {
        public static string Tasa(decimal valor) => "L " + valor.ToString("#,##0.0000", CultureInfo.InvariantCulture);

        public static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public static string Fecha(DateOnly? fecha) => fecha is { } f ? Fecha(f) : "—";

        public static string NombreMoneda(string codigo) => codigo switch
        {
            "USD" => "Dólar (USD)",
            "EUR" => "Euro (EUR)",
            _ => codigo
        };

        public static string NombreTipo(string tipo) => tipo switch
        {
            Cat.TipoTasa.Compra => "Compra",
            Cat.TipoTasa.Venta => "Venta",
            Cat.TipoTasa.Referencia => "Referencia",
            _ => tipo
        };

        public static string NombreFuente(string? fuente) => fuente switch
        {
            Cat.Fuente.BchApi => "BCH (API)",
            Cat.Fuente.BchXlsx => "BCH (Excel)",
            Cat.Fuente.Bce => "BCE",
            Cat.Fuente.Derivada => "Derivada (BCE × BCH)",
            Cat.Fuente.Manual => "Captura manual",
            Cat.Fuente.LegadoWinForms => "Legado WinForms",
            null or "" => "—",
            _ => fuente
        };

        public static string NombreDisparador(string disparador) => disparador switch
        {
            Cat.Disparador.Programado => "Programado",
            Cat.Disparador.Externo => "Externo",
            Cat.Disparador.Manual => "Manual",
            Cat.Disparador.Reintento => "Reintento",
            _ => disparador
        };

        public static string NombreEstadoTasa(string estado) => estado switch
        {
            Cat.EstadoTasa.Vigente => "Vigente",
            Cat.EstadoTasa.Reemplazada => "Reemplazada",
            Cat.EstadoTasa.EnRevision => "En revisión",
            Cat.EstadoTasa.Rechazada => "Rechazada",
            _ => estado
        };

        public static string ClaseEstadoTasa(string estado) => estado switch
        {
            Cat.EstadoTasa.Vigente => "bg-success",
            Cat.EstadoTasa.Reemplazada => "bg-secondary",
            Cat.EstadoTasa.EnRevision => "bg-warning text-dark",
            Cat.EstadoTasa.Rechazada => "bg-danger",
            _ => "bg-light text-dark"
        };

        public static string NombreEstadoEjecucion(string estado) => estado switch
        {
            Cat.EstadoEjecucion.EnCurso => "En curso",
            Cat.EstadoEjecucion.Exitosa => "Exitosa",
            Cat.EstadoEjecucion.Parcial => "Parcial",
            Cat.EstadoEjecucion.Reintentada => "Reintentada",
            Cat.EstadoEjecucion.Fallida => "Fallida",
            Cat.EstadoEjecucion.OmitidaDuplicada => "Omitida (duplicada)",
            Cat.EstadoEjecucion.OmitidaInvalida => "Omitida (inválida)",
            Cat.EstadoEjecucion.OmitidaSinDatos => "Omitida (sin datos)",
            _ => estado
        };

        /// <summary>EXITOSA verde; PARCIAL y REINTENTADA ámbar; OMITIDA_* gris; FALLIDA rojo; EN_CURSO azul.</summary>
        public static string ClaseEstadoEjecucion(string estado) => estado switch
        {
            Cat.EstadoEjecucion.Exitosa => "bg-success",
            Cat.EstadoEjecucion.Parcial or Cat.EstadoEjecucion.Reintentada => "bg-warning text-dark",
            Cat.EstadoEjecucion.OmitidaDuplicada or Cat.EstadoEjecucion.OmitidaInvalida or Cat.EstadoEjecucion.OmitidaSinDatos => "bg-secondary",
            Cat.EstadoEjecucion.Fallida => "bg-danger",
            Cat.EstadoEjecucion.EnCurso => "bg-info text-dark",
            _ => "bg-light text-dark"
        };

        public static string ClaseResultadoDetalle(string resultado) => resultado switch
        {
            Cat.ResultadoDetalle.Insertada => "bg-success",
            Cat.ResultadoDetalle.Reemplazo => "bg-primary",
            Cat.ResultadoDetalle.Duplicada => "bg-secondary",
            Cat.ResultadoDetalle.EnRevision => "bg-warning text-dark",
            Cat.ResultadoDetalle.SinDatos => "bg-light text-dark border",
            Cat.ResultadoDetalle.Invalida or Cat.ResultadoDetalle.Error => "bg-danger",
            _ => "bg-light text-dark"
        };

        /// <summary>Variación porcentual de <paramref name="nueva"/> contra <paramref name="actual"/>, con signo.</summary>
        public static string Variacion(decimal nueva, decimal? actual)
        {
            if (actual is not > 0m) return string.Empty;
            var pct = (nueva - actual.Value) / actual.Value * 100m;
            return (pct >= 0 ? "+" : "") + pct.ToString("0.00", CultureInfo.InvariantCulture) + " %";
        }
    }
}
